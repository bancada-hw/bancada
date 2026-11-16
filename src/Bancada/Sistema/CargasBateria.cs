using System.Net;
using System.Text.RegularExpressions;
using Bancada.Red;

namespace Bancada.Sistema;

/// <summary>Qué hace el equipo durante la prueba de descarga.</summary>
public enum ModoDescarga { Reposo, Navegacion, Video, Maxima }

/// <summary>
/// Navegación: abre páginas web reales cada 15 segundos, con sus imágenes, estilos y scripts, como alguien
/// que va leyendo. Así trabajan a la vez el Wi‑Fi, el procesador y el disco, igual que en un uso de oficina.
/// </summary>
internal sealed class CargaNavegacion : IDisposable
{
    static readonly string[] Paginas =
    {
        "https://es.wikipedia.org/wiki/Especial:Aleatoria",
        "https://www.rtve.es/noticias/",
        "https://en.wikipedia.org/wiki/Special:Random",
        "https://www.bbc.com/news",
        "https://github.com/explore",
        "https://www.elmundo.es/",
        "https://stackoverflow.com/questions",
        "https://www.lemonde.fr/",
    };

    static readonly Regex Recurso = new("(?:src|href)=\"([^\"]+?\\.(?:png|jpe?g|webp|gif|svg|css|js)(?:\\?[^\"]*)?)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    readonly CancellationTokenSource cts = new();
    int cargadas, fallidas;
    long bytes;

    public int Cargadas => Volatile.Read(ref cargadas);
    public int Fallidas => Volatile.Read(ref fallidas);
    public long Bytes => Interlocked.Read(ref bytes);

    public void Iniciar() => _ = Task.Run(() => Bucle(cts.Token));

    async Task Bucle(CancellationToken ct)
    {
        using var http = Conectividad.NuevoHttp(20);
        for (int i = 0; !ct.IsCancellationRequested; i++)
        {
            try
            {
                using var r = await http.GetAsync(Paginas[i % Paginas.Length], ct);
                var html = await r.Content.ReadAsStringAsync(ct);
                Interlocked.Add(ref bytes, html.Length);
                var origen = r.RequestMessage?.RequestUri ?? new Uri(Paginas[i % Paginas.Length]);
                foreach (var u in Recursos(html, origen).Take(20))
                {
                    try { Interlocked.Add(ref bytes, (await http.GetByteArrayAsync(u, ct)).Length); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch { }
                }
                Interlocked.Increment(ref cargadas);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch { Interlocked.Increment(ref fallidas); }

            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch { return; }
        }
    }

    static IEnumerable<Uri> Recursos(string html, Uri origen) =>
        Recurso.Matches(html)
            .Select(m => Uri.TryCreate(origen, WebUtility.HtmlDecode(m.Groups[1].Value), out var u) ? u : null)
            .Where(u => u is { Scheme: "https" or "http" })
            .Distinct()!;

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}

/// <summary>Carga máxima: el procesador entero y todas las gráficas a la vez. Es el consumo más alto que puede pedir el equipo.</summary>
internal sealed class CargaMaxima : IDisposable
{
    readonly Estres cpu = new();
    readonly List<Thread> hilos = new();
    readonly List<string> graficas = new();
    volatile bool activo;

    /// <summary>Gráficas que están trabajando (vacía si no hay OpenCL: entonces solo trabaja el procesador).</summary>
    public IReadOnlyList<string> Graficas { get { lock (graficas) return graficas.ToList(); } }
    public string? Problema { get; private set; }

    public void Iniciar()
    {
        activo = true;
        cpu.Iniciar();
        _ = Task.Run(() =>
        {
            var lista = Gpus.Listar(out var problema);
            if (lista.Count == 0) Problema = problema;
            foreach (var g in lista)
            {
                var t = new Thread(() => Trabajar(g)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Batería " + g.Nombre };
                lock (hilos) hilos.Add(t);
                t.Start();
            }
        });
    }

    void Trabajar(Gpu g)
    {
        try
        {
            using var trabajo = new TrabajoGpu(g);
            lock (graficas) graficas.Add(g.Nombre);
            while (activo) trabajo.Lanzar();
        }
        catch (Exception ex)
        {
            Problema = $"{g.Nombre}: {ex.Message}";
            lock (graficas) graficas.Remove(g.Nombre);
        }
    }

    public void Dispose()
    {
        activo = false;
        cpu.Detener();
        lock (hilos)
        {
            foreach (var t in hilos) t.Join(2000);
            hilos.Clear();
        }
    }
}
