using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Bancada.Modelo;

namespace Bancada.Red;

public sealed record PasoConexion(string Nombre, Veredicto Resultado, string Detalle);

public sealed record ResultadoVelocidad(double BajadaMbps, double SubidaMbps, double LatenciaMs);

public static class Conectividad
{
    internal static HttpClient NuevoHttp(int segundos = 20)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(segundos) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Bancada/0.1");
        return http;
    }

    /// <summary>Router → DNS → Internet → web, en ese orden: así se ve en qué punto se corta.</summary>
    public static async Task<List<PasoConexion>> ComprobarAsync(string? puertaEnlace)
    {
        var pasos = new List<PasoConexion>();

        pasos.Add(string.IsNullOrEmpty(puertaEnlace)
            ? new PasoConexion(Tx("Router (puerta de enlace)"), Veredicto.Fallo, Tx("El equipo no está conectado a ninguna red"))
            : await PingAsync(Tx("Router (puerta de enlace)"), puertaEnlace));

        var reloj = Stopwatch.StartNew();
        try
        {
            var ips = await Dns.GetHostAddressesAsync("www.wikipedia.org");
            pasos.Add(new PasoConexion(Tx("Resolución de nombres (DNS)"), ips.Length > 0 ? Veredicto.Ok : Veredicto.Fallo,
                $"{reloj.ElapsedMilliseconds} ms"));
        }
        catch
        {
            pasos.Add(new PasoConexion(Tx("Resolución de nombres (DNS)"), Veredicto.Fallo, Tx("No resuelve nombres de dominio")));
        }

        pasos.Add(await PingAsync("Internet (1.1.1.1)", "1.1.1.1"));

        try
        {
            using var http = NuevoHttp(10);
            reloj.Restart();
            using var r = await http.GetAsync("https://www.google.com/generate_204");
            pasos.Add(new PasoConexion(Tx("Navegación web (HTTPS)"), (int)r.StatusCode == 204 ? Veredicto.Ok : Veredicto.Aviso,
                $"{reloj.ElapsedMilliseconds} ms"));
        }
        catch
        {
            pasos.Add(new PasoConexion(Tx("Navegación web (HTTPS)"), Veredicto.Fallo, Tx("No se puede abrir una web segura")));
        }
        return pasos;
    }

    static async Task<PasoConexion> PingAsync(string nombre, string destino)
    {
        using var ping = new Ping();
        var tiempos = new List<long>();
        int perdidos = 0;
        for (int k = 0; k < 4; k++)
        {
            try
            {
                var r = await ping.SendPingAsync(destino, 1500);
                if (r.Status == IPStatus.Success) tiempos.Add(r.RoundtripTime); else perdidos++;
            }
            catch { perdidos++; }
        }
        if (tiempos.Count == 0) return new PasoConexion(nombre, Veredicto.Fallo, Tx("No responde"));
        var media = Formato.N(tiempos.Average(), "0");
        return perdidos == 0
            ? new PasoConexion(nombre, Veredicto.Ok, Tf("{0} ms, sin pérdidas", media))
            : new PasoConexion(nombre, Veredicto.Aviso, Tf("{0} ms, {1} de 4 paquetes perdidos", media, perdidos));
    }

    const string Servidor = "https://speed.cloudflare.com";

    /// <summary>Segundos que se mide cada sentido.</summary>
    public const int SegundosPorSentido = 10;

    /// <summary>Segundos iniciales que no cuentan: la conexión aún está acelerando (arranque lento de TCP).</summary>
    const double Calentamiento = 2;

    /// <summary>Latencia, y bajada y subida durante 10 segundos cada una, contra los servidores de Cloudflare.</summary>
    public static async Task<ResultadoVelocidad> MedirVelocidadAsync(IProgress<string> progreso, CancellationToken ct)
    {
        using var http = NuevoHttp(30);

        progreso.Report(Tx("Midiendo la latencia…"));
        var latencias = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            var reloj = Stopwatch.StartNew();
            using var r = await http.GetAsync($"{Servidor}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct);
            latencias.Add(reloj.Elapsed.TotalMilliseconds);
        }
        latencias.Sort();
        double latencia = latencias[latencias.Count / 2];

        // Bajada: varias conexiones que piden datos una y otra vez hasta que se acaba el tiempo.
        progreso.Report(Tx("Midiendo la velocidad de bajada…"));
        double bajada = await SentidoAsync(6, async (tok, sumar) =>
        {
            var buffer = new byte[128 * 1024];
            while (!tok.IsCancellationRequested)
            {
                // 25 MB por petición (Cloudflare rechaza peticiones mayores); se repiten hasta que acaba el tiempo.
                using var r = await http.GetAsync($"{Servidor}/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, tok);
                r.EnsureSuccessStatusCode();
                await using var s = await r.Content.ReadAsStreamAsync(tok);
                int leidos;
                while ((leidos = await s.ReadAsync(buffer, tok)) > 0) sumar(leidos);
            }
        }, mbps => progreso.Report(Tf("Midiendo la velocidad de bajada… {0} Mbps", Formato.N(mbps, mbps < 100 ? "0.0" : "0"))), ct);

        // Subida: lo mismo enviando datos aleatorios (no se comprimen), contados según salen.
        progreso.Report(Tx("Midiendo la velocidad de subida…"));
        var datos = new byte[2_000_000];
        Random.Shared.NextBytes(datos);
        double subida = await SentidoAsync(4, async (tok, sumar) =>
        {
            while (!tok.IsCancellationRequested)
            {
                using var contenido = new ContenidoContado(datos, sumar);
                using var r = await http.PostAsync($"{Servidor}/__up", contenido, tok);
                r.EnsureSuccessStatusCode();
            }
        }, mbps => progreso.Report(Tf("Midiendo la velocidad de subida… {0} Mbps", Formato.N(mbps, mbps < 100 ? "0.0" : "0"))), ct);

        return new ResultadoVelocidad(bajada, subida, latencia);
    }

    /// <summary>
    /// Un sentido de la prueba: varias conexiones en paralelo durante 10 segundos. Devuelve los Mbps medios
    /// sin contar los 2 primeros segundos, e informa de la velocidad en directo cada medio segundo.
    /// </summary>
    static async Task<double> SentidoAsync(int paralelos, Func<CancellationToken, Action<int>, Task> trabajo,
        Action<double> enDirecto, CancellationToken ct)
    {
        long total = 0;
        Exception? error = null;
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(SegundosPorSentido));
        var reloj = Stopwatch.StartNew();

        int fallos = 0;
        var tareas = Enumerable.Range(0, paralelos).Select(_ => Task.Run(async () =>
        {
            // Si una petición falla (el servidor corta, un error puntual), esa conexión vuelve a intentarlo:
            // así cada sentido dura siempre los 10 segundos.
            while (!limite.IsCancellationRequested)
            {
                try { await trabajo(limite.Token, n => Interlocked.Add(ref total, n)); }
                catch (Exception ex) when (!limite.IsCancellationRequested)
                {
                    error ??= ex;
                    if (Interlocked.Increment(ref fallos) > 30) return; // falla siempre: no insistir más
                    try { await Task.Delay(150, limite.Token); } catch { return; }
                }
                catch { return; } // cortada al acabarse el tiempo
            }
        })).ToList();

        // Muestras cada medio segundo: velocidad en directo y punto de partida tras el calentamiento.
        long bytesInicio = -1;
        double tInicio = 0;
        while (!tareas.All(t => t.IsCompleted))
        {
            await Task.WhenAny(Task.WhenAll(tareas), Task.Delay(500));
            var t = reloj.Elapsed.TotalSeconds;
            var b = Interlocked.Read(ref total);
            if (bytesInicio < 0 && t >= Calentamiento) { bytesInicio = b; tInicio = t; }
            var mbps = bytesInicio >= 0 && t - tInicio > 0.4 ? (b - bytesInicio) * 8 / (t - tInicio) / 1e6 : b * 8 / t / 1e6;
            if (!limite.IsCancellationRequested) enDirecto(mbps);
        }
        ct.ThrowIfCancellationRequested();

        var fin = reloj.Elapsed.TotalSeconds;
        var bytes = Interlocked.Read(ref total);
        if (bytes == 0) throw error ?? new InvalidOperationException(Tx("No ha llegado ningún dato del servidor."));
        return bytesInicio >= 0 && fin - tInicio >= 1
            ? (bytes - bytesInicio) * 8 / (fin - tInicio) / 1e6
            : bytes * 8 / fin / 1e6;
    }

    /// <summary>Datos de subida que se cuentan a medida que se escriben en la conexión.</summary>
    sealed class ContenidoContado(byte[] datos, Action<int> sumar) : HttpContent
    {
        const int Trozo = 64 * 1024;

        protected override async Task SerializeToStreamAsync(Stream destino, TransportContext? contexto, CancellationToken ct)
        {
            for (int o = 0; o < datos.Length; o += Trozo)
            {
                var n = Math.Min(Trozo, datos.Length - o);
                await destino.WriteAsync(datos.AsMemory(o, n), ct);
                sumar(n);
            }
        }

        protected override Task SerializeToStreamAsync(Stream destino, TransportContext? contexto) =>
            SerializeToStreamAsync(destino, contexto, CancellationToken.None);

        protected override bool TryComputeLength(out long largo)
        {
            largo = datos.Length;
            return true;
        }
    }
}
