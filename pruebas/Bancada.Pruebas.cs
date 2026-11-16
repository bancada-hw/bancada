#:project ../src/Bancada/Bancada.csproj
#:property AllowUnsafeBlocks=true
#:property PublishAot=false

// Banco de pruebas del hardware real, sin interfaz. Uso:
//   dotnet run pruebas/Bancada.Pruebas.cs -- [camara] [micro] [energia] [usb] [sensores] [estres] [memoria] [disco] [placa]
// No guarda imágenes ni audio: solo imprime medidas.

using System.Diagnostics;
using Bancada.Sistema;

var pedidas = args.Length > 0 ? args.ToHashSet() : new HashSet<string> { "energia", "usb", "sensores" };
bool Pide(string p) => pedidas.Contains(p) || pedidas.Contains("todo");

if (Pide("energia"))
{
    var e = Energia.Leer();
    Console.WriteLine($"[energia] baterias={e.Baterias.Count} carga={e.CargaPct} enchufado={e.Enchufado} cargando={e.Cargando} W={e.PotenciaW} V={e.VoltajeV}");
    foreach (var b in e.Baterias) Console.WriteLine($"  {b.Fabricante} {b.Nombre} {b.Quimica} diseño={b.DisenoWh} llena={b.LlenaWh} resto={b.RestanteWh} ciclos={b.Ciclos} salud={b.SaludPct}");
}

if (Pide("suspension"))
{
    // Lee lo que registró Windows de las suspensiones de los últimos 15 minutos (no suspende nada).
    var c = Suspension.Capacidad();
    Console.WriteLine($"[suspension] {c.Tipo} puede={c.Puede} moderna={c.Moderna} despiertaSolo={c.DespiertaSolo}");
    var reloj = Stopwatch.StartNew();
    // BANCADA_DESDE=HH:mm:ss para mirar desde una hora concreta de hoy.
    var desde = TimeSpan.TryParse(Environment.GetEnvironmentVariable("BANCADA_DESDE"), out var h) ? DateTime.Today + h : DateTime.Now.AddMinutes(-15);
    var i = Suspension.Analizar(desde);
    Console.WriteLine(i == null
        ? $"  registro: nada ({reloj.ElapsedMilliseconds} ms)"
        : $"  registro: modo={i.Modo} duración={i.Duracion} despertador={i.Despertador} temporizador={i.DespertoTemporizador} frenos={string.Join(",", i.Frenos)} ({reloj.ElapsedMilliseconds} ms)");
}

if (Pide("gpu"))
{
    var lista = Gpus.Listar(out var problema);
    Console.WriteLine($"[gpu] {lista.Count} gráficas{(problema != null ? " · " + problema : "")}");
    foreach (var g in lista)
    {
        Console.WriteLine($"[gpu] {g.Nombre} | {g.Fabricante} | integrada={g.Integrada} | memoria={g.Memoria >> 20} MB | bloque máx={g.MaxBloque >> 20} MB | unidades={g.Unidades} | {g.Mhz} MHz | driver {g.Driver}");
        try
        {
            var sensor = SensorGpu.Para(g);
            Console.WriteLine($"      sensor: {(sensor == null ? "ninguno" : sensor.GetType().Name)} · reposo: {sensor?.Leer()}");
            using var t = new TrabajoGpu(g);
            var sw = Stopwatch.StartNew();
            int tandas = 0;
            double maxMs = 0;
            while (sw.Elapsed.TotalSeconds < 8) { maxMs = Math.Max(maxMs, t.Lanzar()); tandas++; }
            var gops = t.Operaciones / sw.Elapsed.TotalSeconds / 1e9;
            var fallos = t.Verificar();
            var img = new byte[TrabajoGpu.AnchoImagen * TrabajoGpu.AltoImagen * 4];
            t.Fractal(1.2f, img);
            long brillo = 0;
            for (int i = 0; i < img.Length; i += 4) brillo += img[i + 1];
            Console.WriteLine($"      estrés 8 s: {tandas} tandas (máx {maxMs:0} ms) · {gops:0.0} miles de millones de op/s · fallos de cálculo {fallos} · imagen verde medio {brillo / (img.Length / 4.0):0}");
            Console.WriteLine($"      bajo carga: {sensor?.Leer()}");
            var m = t.ProbarMemoria(new Progress<(double, string)>(_ => { }), CancellationToken.None);
            Console.WriteLine($"      memoria de vídeo: {m.probados >> 20} MB probados · {m.errores} errores · {sw.Elapsed.TotalSeconds - 8:0.0} s");
        }
        catch (Exception ex) { Console.WriteLine($"      ERROR: {ex.Message}"); }
    }
}

if (Pide("velocidad"))
{
    // Velocidad de Internet: cuándo empieza cada fase y el resultado final.
    var sw = Stopwatch.StartNew();
    var fases = new Dictionary<string, (double desde, double hasta, string ultimo)>();
    var prog = new Progress<string>(t =>
    {
        var f = t.Split('…')[0];
        var ahora = sw.Elapsed.TotalSeconds;
        lock (fases) fases[f] = fases.TryGetValue(f, out var x) ? (x.desde, ahora, t) : (ahora, ahora, t);
    });
    var r = await Bancada.Red.Conectividad.MedirVelocidadAsync(prog, CancellationToken.None);
    Console.WriteLine($"[velocidad] bajada={r.BajadaMbps:0.0} Mbps subida={r.SubidaMbps:0.0} Mbps latencia={r.LatenciaMs:0} ms total={sw.Elapsed.TotalSeconds:0.0} s");
    await Task.Delay(300);
    lock (fases) foreach (var (f, x) in fases.OrderBy(p => p.Value.desde))
        Console.WriteLine($"    {x.desde,5:0.0} → {x.hasta,5:0.0} s  {f}  (última lectura en directo: {x.ultimo})");
}

if (Pide("descarga"))
{
    // Datos simulados: 30 minutos, una lectura cada 10 s, batería de 74,9 Wh.
    List<MuestraDescarga> Simular(Func<int, (int pct, double wh)> f) =>
        Enumerable.Range(0, 181).Select(i => { var (p, wh) = f(i); return new MuestraDescarga(TimeSpan.FromSeconds(i * 10), p, wh, -22.4); }).ToList();
    var casos = new (string nombre, List<MuestraDescarga> m)[]
    {
        ("normal", Simular(i => (100 - (int)(i * 15 / 180.0), 74.9 - i * 11.2 / 180))),
        ("caida brusca", Simular(i => { var p = 100 - (int)(i * 15 / 180.0); return (i >= 90 ? p - 12 : p, 74.9 - i * 11.2 / 180); })),
        ("descalibrada", Simular(i => (100 - (int)(i * 25 / 180.0), 74.9 - i * 7.5 / 180))),
    };
    foreach (var (nombre, m) in casos)
    {
        var r = Descarga.Analizar(m, 74.9, 75.0, conCarga: false);
        Console.WriteLine($"[descarga] {nombre}: {r.PctInicio}→{r.PctFin} % gastados={r.WhGastados:0.0} Wh media={r.PotenciaMedia:0.0} W autonomia={r.Autonomia?.TotalHours:0.00} h caidas={r.CaidasBruscas}");
        foreach (var (v, t) in r.Avisos) Console.WriteLine($"    {v}: {t}");
    }
}

if (Pide("usb"))
    foreach (var d in Usb.Listar())
        Console.WriteLine($"[usb] {(d.Interno ? "int" : "ext")} {d.Puerto} | {d.Nombre} | {d.Velocidad} | p3={d.PuertoUsb3} d3={d.DispositivoUsb3}");

if (Pide("sensores"))
{
    var s = new Sensores();
    s.Leer();
    Thread.Sleep(1000);
    var l = s.Leer();
    Console.WriteLine($"[sensores] temp={l.TempCpu} ({l.FuenteTemp}) mhz={l.Mhz} uso={l.UsoPct} otros={l.Otros.Count}");
}

if (Pide("camara"))
{
    foreach (var c in Camara.Listar())
    {
        try
        {
            using var cam = Camara.Abrir(c);
            var buf = Array.Empty<byte>();
            var sw = Stopwatch.StartNew();
            int n = 0;
            long brillo = 0;
            while (n < 30 && sw.Elapsed.TotalSeconds < 8)
                if (cam.Leer(ref buf))
                {
                    n++;
                    for (int i = 0; i < buf.Length; i += 4 * 101) brillo += buf[i + 1];
                }
            var muestras = Math.Max(1, n * (buf.Length / (4 * 101)));
            Console.WriteLine($"[camara] {c.Nombre}: {cam.Ancho}x{cam.Alto} {cam.Formato} {n} fotogramas en {sw.Elapsed.TotalSeconds:0.0}s ({n / sw.Elapsed.TotalSeconds:0.0} fps) brillo medio {brillo / (double)muestras:0}");
        }
        catch (Exception ex) { Console.WriteLine($"[camara] {c.Nombre}: ERROR {ex.Message}"); }
    }
}

if (Pide("micro"))
{
    using var g = Grabadora.Crear();
    long total = 0;
    int pico = 0;
    g.Muestras += b => { total += b.Length; foreach (var x in b) pico = Math.Max(pico, Math.Abs((int)x)); };
    var err = g.Iniciar();
    Thread.Sleep(2000);
    Console.WriteLine($"[micro] error={err ?? "ninguno"} muestras={total} (esperadas ~88200) pico={(pico == 0 ? "silencio" : $"{20 * Math.Log10(pico / 32768.0):0} dBFS")}");
    var silencio = Sonido.Wav(new short[4410 * 2], 2);
    Console.WriteLine($"[sonido] reproducir silencio: {Sonido.Reproducir(silencio) ?? "ok"}");
    Thread.Sleep(300);
    Sonido.Detener();
}

if (Pide("estres"))
{
    var e = new Estres();
    e.Iniciar();
    Thread.Sleep(8000);
    var it = e.Iteraciones;
    e.Detener();
    Console.WriteLine($"[estres] hilos={Environment.ProcessorCount} iteraciones={it} ({it / 8.0:0}/s) errores={e.Errores}");
}

if (Pide("memoria"))
{
    var (total, libre) = PruebaMemoria.Estado();
    Console.WriteLine($"[memoria] total={total >> 30} GB libre={libre >> 30} GB recomendado={PruebaMemoria.TamanoRecomendado() >> 30} GB");
    var r = PruebaMemoria.Ejecutar(2L << 30, new Progress<(double, string)>(_ => { }), CancellationToken.None);
    Console.WriteLine($"[memoria] probados={r.BytesProbados >> 20} MB errores={r.Errores} duracion={r.Duracion.TotalSeconds:0.0}s");
}

if (Pide("disco"))
{
    var v = Almacenamiento.Volumenes().First();
    Console.WriteLine($"[disco] midiendo {v.Nombre} en {v.Carpeta} ({v.Disco})…");
    var r = Almacenamiento.Medir(v, new Progress<string>(_ => { }), CancellationToken.None);
    Console.WriteLine($"[disco] lectura={r.LecturaMBs:0} MB/s escritura={r.EscrituraMBs:0} MB/s 4k={r.Iops:0} op/s ({r.Aleatoria4kMBs:0.0} MB/s) bytes={r.BytesProbados >> 20} MB");
    var (discos, nota) = Almacenamiento.Salud();
    foreach (var d in discos) Console.WriteLine($"[disco] {d.Modelo}: {d.EstadoTexto} temp={d.TemperaturaC} horas={d.HorasEncendido} vida={d.VidaUsadaPct} errSis={d.ErroresSistema} reint={d.ReintentosSistema} | {string.Join(" / ", d.Motivos)}");
    if (nota != null) Console.WriteLine($"[disco] nota: {nota}");
}

if (Pide("placa"))
    foreach (var c in await Placa.ComprobarAsync(new Progress<string>(_ => { })))
        Console.WriteLine($"[placa] {c.Nombre}: {c.Resultado} · {c.Resumen} | {string.Join(" / ", c.Lineas)}");
