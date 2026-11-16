using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Bancada.Sistema;

/// <summary>Una gráfica que se puede poner a trabajar (vía OpenCL, que traen los drivers de NVIDIA, AMD e Intel).</summary>
public sealed record Gpu(string Nombre, string Fabricante, bool Integrada, long Memoria, long MaxBloque, int Unidades, int Mhz,
    string Driver, IntPtr Dispositivo);

public sealed record LecturaGpu(double? TempC, double? Mhz, double? UsoPct, double? Vatios, bool LimitadaPorTemperatura);

/// <summary>Busca las gráficas y resuelve las librerías nativas de OpenCL y NVML en cada sistema.</summary>
internal static class Gpus
{
    static int resolverPuesto;

    /// <summary>OpenCL y NVML se llaman distinto en Windows y en Linux: aquí se busca la librería correcta.</summary>
    public static void PrepararLibrerias()
    {
        if (Interlocked.Exchange(ref resolverPuesto, 1) == 1) return;
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (nombre, ensamblado, ruta) =>
        {
            string[] candidatos = nombre switch
            {
                "OpenCL" when OperatingSystem.IsLinux() => new[] { "libOpenCL.so.1", "libOpenCL.so" },
                "nvml" when OperatingSystem.IsLinux() => new[] { "libnvidia-ml.so.1" },
                "nvml" => new[] { "nvml.dll", @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll" },
                _ => Array.Empty<string>()
            };
            foreach (var c in candidatos)
                if (NativeLibrary.TryLoad(c, out var h)) return h;
            return IntPtr.Zero; // el resto, como siempre
        });
    }

    /// <summary>Versión del controlador de una plataforma OpenCL (la de su primer dispositivo, p. ej. «3683.0 (PAL,LC)»).</summary>
    static Version VersionControlador(IntPtr plataforma)
    {
        var uno = new IntPtr[1];
        if (OpenCl.clGetDeviceIDs(plataforma, OpenCl.TipoTodos, 1, uno, out _) != 0) return new Version(0, 0);
        var m = System.Text.RegularExpressions.Regex.Match(OpenCl.Texto(uno[0], OpenCl.VersionDriver), @"\d+(\.\d+){0,3}");
        if (!m.Success) return new Version(0, 0);
        return Version.TryParse(m.Value.Contains('.') ? m.Value : m.Value + ".0", out var v) ? v : new Version(0, 0);
    }

    /// <summary>Gráficas con OpenCL. Si no hay ninguna, <paramref name="problema"/> explica por qué.</summary>
    public static List<Gpu> Listar(out string? problema)
    {
        problema = null;
        var lista = new List<Gpu>();
        PrepararLibrerias();
        try
        {
            if (OpenCl.clGetPlatformIDs(0, null, out var n) != 0 || n == 0)
            {
                problema = Tx("No hay ningún controlador OpenCL instalado.");
                return lista;
            }
            var plataformas = new IntPtr[n];
            OpenCl.clGetPlatformIDs(n, plataformas, out _);
            ulong tipos = OpenCl.TipoGpu | OpenCl.TipoAcelerador;
            if (Environment.GetEnvironmentVariable("BANCADA_OPENCL_CPU") == "1") tipos |= OpenCl.TipoCpu; // pruebas sin gráfica real
            // La misma tarjeta puede salir varias veces: en Windows el cargador de OpenCL registra el controlador
            // una vez por adaptador y cada copia vuelve a ofrecer todas las tarjetas (2 tarjetas AMD → 4 entradas).
            // Se cuenta cada tarjeta física una sola vez, por su identificador (UUID o posición en el bus PCI).
            // Si dos copias del controlador ofrecen la misma tarjeta, gana la de versión más alta: se recorren primero.
            var vistas = new HashSet<string>();
            foreach (var p in plataformas.OrderByDescending(VersionControlador))
            {
                if (OpenCl.clGetDeviceIDs(p, tipos, 0, null, out var nd) != 0 || nd == 0) continue;
                var dispositivos = new IntPtr[nd];
                OpenCl.clGetDeviceIDs(p, tipos, nd, dispositivos, out _);
                var repetidasAqui = new Dictionary<string, int>();
                foreach (var d in dispositivos)
                {
                    if (!vistas.Add(OpenCl.Identidad(d, repetidasAqui))) continue;
                    var fabricante = OpenCl.Texto(d, OpenCl.Fabricante);
                    var nombre = OpenCl.Texto(d, OpenCl.Nombre);
                    // AMD da un nombre en clave («gfx1032»); el comercial va aparte.
                    if (fabricante.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
                    {
                        var placa = OpenCl.Texto(d, OpenCl.NombrePlacaAmd);
                        if (placa.Length > 0) nombre = placa;
                    }
                    lista.Add(new Gpu(
                        Modelo.TextoEquipo.Hardware(nombre),
                        Modelo.TextoEquipo.Marca(fabricante.Replace("Corporation", "").Trim()),
                        OpenCl.Entero(d, OpenCl.MemoriaUnificada) != 0,
                        (long)OpenCl.Largo(d, OpenCl.MemoriaGlobal),
                        (long)OpenCl.Largo(d, OpenCl.BloqueMaximo),
                        (int)OpenCl.Entero(d, OpenCl.Unidades),
                        (int)OpenCl.Entero(d, OpenCl.Frecuencia),
                        OpenCl.Texto(d, OpenCl.VersionDriver),
                        d));
                }
            }
            if (lista.Count == 0) problema = Tx("Los controladores OpenCL instalados no ofrecen ninguna gráfica.");
        }
        catch (DllNotFoundException)
        {
            problema = Tx("No hay ningún controlador OpenCL instalado.");
        }
        catch (Exception ex)
        {
            problema = ex.Message;
        }
        // Las dedicadas primero: suelen ser las que interesa probar.
        return lista.OrderBy(g => g.Integrada).ThenByDescending(g => g.Memoria).ToList();
    }
}

/// <summary>
/// Trabajo para una gráfica: cálculo intensivo con resultados comprobables, una imagen animada (para ver que
/// la gráfica pinta bien) y la prueba de su memoria de vídeo.
/// </summary>
internal sealed class TrabajoGpu : IDisposable
{
    const string Codigo = @"
__kernel void estres(__global uint* salida, uint iteraciones, uint semilla, float objetivo)
{
    uint id = get_global_id(0);
    uint h = id * 2654435761u ^ semilla;
    float a = (float)(id & 1023u) * 0.0009765625f;
    for (uint i = 0; i < iteraciones; i++)
    {
        h ^= h << 13; h ^= h >> 17; h ^= h << 5;
        a = mad(a, 1.000001f, 0.0000001f);
        a = native_sin(a) * 0.5f + a * 0.5f;
        a = a > 2.0f ? a - 1.0f : a;
    }
    salida[id] = h ^ (a == objetivo ? 1u : 0u);
}

__kernel void fractal(__global uchar4* imagen, uint ancho, uint alto, float t, uint maxIter)
{
    uint x = get_global_id(0), y = get_global_id(1);
    if (x >= ancho || y >= alto) return;
    float zx = 1.6f * ((float)x - ancho * 0.5f) / (alto * 0.5f);
    float zy = 1.6f * ((float)y - alto * 0.5f) / (alto * 0.5f);
    float cx = 0.7885f * cos(t), cy = 0.7885f * sin(t);
    uint i = 0;
    while (i < maxIter && zx * zx + zy * zy < 16.0f)
    {
        float nx = zx * zx - zy * zy + cx;
        zy = 2.0f * zx * zy + cy;
        zx = nx;
        i++;
    }
    float v = i >= maxIter ? 0.0f : ((float)i - log2(log2(zx * zx + zy * zy)) + 4.0f) / 64.0f;
    v = clamp(v, 0.0f, 1.0f);
    // Colores de Bancada: verde mascara, cobre y blanco serigrafia (solo ASCII: el codigo va al driver).
    float r = v < 0.5f ? mix(15.0f, 217.0f, v * 2.0f) : mix(217.0f, 238.0f, (v - 0.5f) * 2.0f);
    float g = v < 0.5f ? mix(61.0f, 144.0f, v * 2.0f) : mix(144.0f, 235.0f, (v - 0.5f) * 2.0f);
    float b = v < 0.5f ? mix(46.0f, 85.0f, v * 2.0f) : mix(85.0f, 226.0f, (v - 0.5f) * 2.0f);
    imagen[y * ancho + x] = (uchar4)((uchar)b, (uchar)g, (uchar)r, (uchar)255);
}

__kernel void escribir(__global uint* m, uint base, uint semilla, uint invertir)
{
    uint i = get_global_id(0);
    uint v = (base + i) * 2654435761u ^ semilla;
    m[i] = invertir ? ~v : v;
}

__kernel void comprobar(__global const uint* m, uint base, uint semilla, uint invertir, __global uint* errores)
{
    uint i = get_global_id(0);
    uint v = (base + i) * 2654435761u ^ semilla;
    if (invertir) v = ~v;
    if (m[i] != v) atomic_inc(errores);
}
";

    public const int AnchoImagen = 640, AltoImagen = 360;

    readonly Gpu gpu;
    readonly IntPtr contexto, cola, programa, kEstres, kFractal, kEscribir, kComprobar;
    readonly IntPtr bufSalida, bufImagen;
    readonly nuint tamGlobal;
    uint iteraciones = 512;
    uint semilla = 0x1234567;

    public long Operaciones { get; private set; }

    /// <summary>Pasadas que hizo la última prueba de memoria de vídeo.</summary>
    public int Pasadas { get; private set; }

    public TrabajoGpu(Gpu g)
    {
        gpu = g;
        Gpus.PrepararLibrerias();
        var dispositivos = new[] { g.Dispositivo };
        contexto = OpenCl.clCreateContext(IntPtr.Zero, 1, dispositivos, IntPtr.Zero, IntPtr.Zero, out var err);
        OpenCl.Comprobar(err, "clCreateContext");
        cola = OpenCl.clCreateCommandQueue(contexto, g.Dispositivo, 0, out err);
        OpenCl.Comprobar(err, "clCreateCommandQueue");
        programa = OpenCl.clCreateProgramWithSource(contexto, 1, new[] { Codigo }, null, out err);
        OpenCl.Comprobar(err, "clCreateProgramWithSource");
        err = OpenCl.clBuildProgram(programa, 1, dispositivos, "-cl-mad-enable", IntPtr.Zero, IntPtr.Zero);
        if (err != 0)
        {
            var registro = new byte[16384];
            OpenCl.clGetProgramBuildInfo(programa, g.Dispositivo, 0x1183, (nuint)registro.Length, registro, out var largo);
            throw new InvalidOperationException(Tx("El controlador de la gráfica no ha podido preparar la prueba.") + " " +
                                                Encoding.UTF8.GetString(registro, 0, (int)Math.Min((long)largo, registro.Length)).Trim('\0').Trim());
        }
        kEstres = OpenCl.clCreateKernel(programa, "estres", out err); OpenCl.Comprobar(err, "estres");
        kFractal = OpenCl.clCreateKernel(programa, "fractal", out err); OpenCl.Comprobar(err, "fractal");
        kEscribir = OpenCl.clCreateKernel(programa, "escribir", out err); OpenCl.Comprobar(err, "escribir");
        kComprobar = OpenCl.clCreateKernel(programa, "comprobar", out err); OpenCl.Comprobar(err, "comprobar");

        // Suficientes hilos para llenar la gráfica entera.
        tamGlobal = (nuint)(Math.Max(1, g.Unidades) * 4096);
        bufSalida = OpenCl.clCreateBuffer(contexto, 1, tamGlobal * 4, IntPtr.Zero, out err);
        OpenCl.Comprobar(err, "clCreateBuffer");
        bufImagen = OpenCl.clCreateBuffer(contexto, 1, (nuint)(AnchoImagen * AltoImagen * 4), IntPtr.Zero, out err);
        OpenCl.Comprobar(err, "clCreateBuffer");
    }

    /// <summary>Una tanda de cálculo. Se ajusta sola para durar unos 40 ms (las tandas largas hacen que Windows reinicie el driver).</summary>
    public double Lanzar()
    {
        semilla = semilla * 1664525u + 1013904223u;
        var reloj = Stopwatch.StartNew();
        Estres(iteraciones, semilla);
        OpenCl.Comprobar(OpenCl.clFinish(cola), "clFinish");
        var ms = reloj.Elapsed.TotalMilliseconds;
        Operaciones += (long)tamGlobal * iteraciones;
        if (ms < 25 && iteraciones < 1 << 22) iteraciones = (uint)(iteraciones * 1.5);
        else if (ms > 60 && iteraciones > 64) iteraciones = (uint)(iteraciones / 1.5);
        return ms;
    }

    void Estres(uint iter, uint sem)
    {
        var buf = bufSalida;
        OpenCl.clSetKernelArg(kEstres, 0, (nuint)IntPtr.Size, ref buf);
        OpenCl.clSetKernelArg(kEstres, 1, 4, ref iter);
        OpenCl.clSetKernelArg(kEstres, 2, 4, ref sem);
        float objetivo = 1234.5f;
        OpenCl.clSetKernelArg(kEstres, 3, 4, ref objetivo);
        var global = new[] { tamGlobal };
        OpenCl.Comprobar(OpenCl.clEnqueueNDRangeKernel(cola, kEstres, 1, null, global, null, 0, null, IntPtr.Zero), "estres");
    }

    /// <summary>Repite una tanda y compara una muestra de resultados con el cálculo hecho en el procesador. Devuelve los fallos.</summary>
    public int Verificar()
    {
        uint iter = Math.Min(iteraciones, 20000);
        uint sem = semilla ^ 0x9E3779B9u;
        Estres(iter, sem);
        var salida = new uint[(int)tamGlobal];
        var h = GCHandle.Alloc(salida, GCHandleType.Pinned);
        try
        {
            OpenCl.Comprobar(OpenCl.clEnqueueReadBuffer(cola, bufSalida, 1, 0, tamGlobal * 4, h.AddrOfPinnedObject(), 0, null, IntPtr.Zero), "lectura");
        }
        finally { h.Free(); }
        int fallos = 0;
        var rnd = new Random((int)sem);
        for (int k = 0; k < 128; k++)
        {
            uint id = (uint)rnd.Next((int)tamGlobal);
            uint e = id * 2654435761u ^ sem;
            for (uint i = 0; i < iter; i++) { e ^= e << 13; e ^= e >> 17; e ^= e << 5; }
            if (salida[id] != e) fallos++;
        }
        return fallos;
    }

    /// <summary>Pinta un fotograma de la animación en BGRA.</summary>
    public void Fractal(float t, byte[] destino)
    {
        var buf = bufImagen;
        uint ancho = AnchoImagen, alto = AltoImagen, maxIter = 700;
        OpenCl.clSetKernelArg(kFractal, 0, (nuint)IntPtr.Size, ref buf);
        OpenCl.clSetKernelArg(kFractal, 1, 4, ref ancho);
        OpenCl.clSetKernelArg(kFractal, 2, 4, ref alto);
        OpenCl.clSetKernelArg(kFractal, 3, 4, ref t);
        OpenCl.clSetKernelArg(kFractal, 4, 4, ref maxIter);
        var global = new nuint[] { AnchoImagen, AltoImagen };
        OpenCl.Comprobar(OpenCl.clEnqueueNDRangeKernel(cola, kFractal, 2, null, global, null, 0, null, IntPtr.Zero), "fractal");
        var h = GCHandle.Alloc(destino, GCHandleType.Pinned);
        try
        {
            OpenCl.Comprobar(OpenCl.clEnqueueReadBuffer(cola, bufImagen, 1, 0, (nuint)destino.Length, h.AddrOfPinnedObject(), 0, null, IntPtr.Zero), "lectura");
        }
        finally { h.Free(); }
    }

    /// <summary>
    /// Llena la memoria de vídeo con patrones y comprueba cada palabra en la propia gráfica.
    /// En gráficas integradas (memoria compartida con el sistema) se prueba como mucho 1 GB.
    /// </summary>
    public (long probados, long errores) ProbarMemoria(IProgress<(double pct, string texto)> progreso, CancellationToken ct)
    {
        long objetivo = gpu.Integrada
            ? Math.Min(1L << 30, gpu.Memoria / 4)
            : Math.Max(256L << 20, Math.Min(gpu.Memoria - (512L << 20), (long)(gpu.Memoria * 0.85)));
        long trozo = Math.Min(Math.Max(64L << 20, gpu.MaxBloque), 512L << 20) / 4 * 4;
        var bloques = new List<(IntPtr buf, long bytes)>();
        var bufErrores = OpenCl.clCreateBuffer(contexto, 1, 4, IntPtr.Zero, out var err);
        OpenCl.Comprobar(err, "clCreateBuffer");
        try
        {
            progreso.Report((0, Tx("Reservando memoria de vídeo…")));
            for (long reservado = 0; reservado < objetivo; reservado += trozo)
            {
                var bytes = Math.Min(trozo, objetivo - reservado);
                var b = OpenCl.clCreateBuffer(contexto, 1, (nuint)bytes, IntPtr.Zero, out err);
                if (err != 0) break;
                // La memoria se reserva de verdad al usarla: si esta escritura falla, no cabe más.
                if (Escribir(b, bytes, 0, 1, 0) != 0) { OpenCl.clReleaseMemObject(b); break; }
                if (OpenCl.clFinish(cola) != 0) { OpenCl.clReleaseMemObject(b); break; }
                bloques.Add((b, bytes));
            }
            if (bloques.Count == 0) throw new InvalidOperationException(Tx("No se ha podido reservar memoria de vídeo."));

            // Pasadas con patrones distintos (y su inverso) durante unos 30 segundos, con un mínimo de 8:
            // hay fallos que solo aparecen cuando la memoria lleva un rato trabajando y se calienta.
            long errores = 0;
            var reloj = Stopwatch.StartNew();
            const double Segundos = 30;
            int p = 0;
            while (p < 8 || reloj.Elapsed.TotalSeconds < Segundos)
            {
                ct.ThrowIfCancellationRequested();
                uint semillaPasada = (uint)(p / 2) * 0x9E3779B9u ^ 0xA5A5A5A5u;
                uint invertirPasada = (uint)(p % 2);
                double pct = Math.Min(99, Math.Max(p / 8.0, reloj.Elapsed.TotalSeconds / Segundos) * 100);
                progreso.Report((pct, Tf("Pasada {0}: escribiendo", p + 1)));
                uint baseIdx = 0;
                foreach (var (b, bytes) in bloques)
                {
                    OpenCl.Comprobar(Escribir(b, bytes, baseIdx, semillaPasada, invertirPasada), "escribir");
                    baseIdx += (uint)(bytes / 4);
                }
                OpenCl.Comprobar(OpenCl.clFinish(cola), "clFinish");
                progreso.Report((pct, Tf("Pasada {0}: comprobando", p + 1)));
                uint cero = 0;
                OpenCl.clEnqueueWriteBuffer(cola, bufErrores, 1, 0, 4, ref cero, 0, null, IntPtr.Zero);
                baseIdx = 0;
                foreach (var (b, bytes) in bloques)
                {
                    var buf = b;
                    var be = bufErrores;
                    uint sem = semillaPasada, inv = invertirPasada, bi = baseIdx;
                    OpenCl.clSetKernelArg(kComprobar, 0, (nuint)IntPtr.Size, ref buf);
                    OpenCl.clSetKernelArg(kComprobar, 1, 4, ref bi);
                    OpenCl.clSetKernelArg(kComprobar, 2, 4, ref sem);
                    OpenCl.clSetKernelArg(kComprobar, 3, 4, ref inv);
                    OpenCl.clSetKernelArg(kComprobar, 4, (nuint)IntPtr.Size, ref be);
                    var global = new[] { (nuint)(bytes / 4) };
                    OpenCl.Comprobar(OpenCl.clEnqueueNDRangeKernel(cola, kComprobar, 1, null, global, null, 0, null, IntPtr.Zero), "comprobar");
                    baseIdx += (uint)(bytes / 4);
                }
                uint malos = 0;
                OpenCl.Comprobar(OpenCl.clEnqueueReadBuffer(cola, bufErrores, 1, 0, 4, ref malos, 0, null, IntPtr.Zero), "lectura");
                errores += malos;
                p++;
            }
            Pasadas = p;
            return (bloques.Sum(b => b.bytes), errores);
        }
        finally
        {
            foreach (var (b, _) in bloques) OpenCl.clReleaseMemObject(b);
            OpenCl.clReleaseMemObject(bufErrores);
        }
    }

    int Escribir(IntPtr buf, long bytes, uint baseIdx, uint sem, uint inv)
    {
        OpenCl.clSetKernelArg(kEscribir, 0, (nuint)IntPtr.Size, ref buf);
        OpenCl.clSetKernelArg(kEscribir, 1, 4, ref baseIdx);
        OpenCl.clSetKernelArg(kEscribir, 2, 4, ref sem);
        OpenCl.clSetKernelArg(kEscribir, 3, 4, ref inv);
        var global = new[] { (nuint)(bytes / 4) };
        return OpenCl.clEnqueueNDRangeKernel(cola, kEscribir, 1, null, global, null, 0, null, IntPtr.Zero);
    }

    public void Dispose()
    {
        OpenCl.clFinish(cola);
        OpenCl.clReleaseMemObject(bufSalida);
        OpenCl.clReleaseMemObject(bufImagen);
        foreach (var k in new[] { kEstres, kFractal, kEscribir, kComprobar }) OpenCl.clReleaseKernel(k);
        OpenCl.clReleaseProgram(programa);
        OpenCl.clReleaseCommandQueue(cola);
        OpenCl.clReleaseContext(contexto);
    }
}

/// <summary>Temperatura, frecuencia, uso y consumo de una gráfica: NVML (NVIDIA), contadores de Windows o hwmon (Linux).</summary>
internal abstract class SensorGpu
{
    public abstract LecturaGpu Leer();

    public static SensorGpu? Para(Gpu g)
    {
        Gpus.PrepararLibrerias();
        if (g.Fabricante == "NVIDIA" && SensorNvml.Crear(g.Nombre) is { } n) return n;
        if (OperatingSystem.IsWindows() && SensorWindows.Crear(g.Nombre) is { } w) return w;
        if (OperatingSystem.IsLinux() && SensorHwmon.Crear(g.Fabricante) is { } h) return h;
        return null;
    }

    static string Clave(string s) => new(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Si dos nombres de gráfica son la misma (cada API los escribe un poco distinto).</summary>
    protected static bool Coinciden(string a, string b)
    {
        var x = Clave(Modelo.TextoEquipo.Hardware(a));
        var y = Clave(Modelo.TextoEquipo.Hardware(b));
        return x.Length > 3 && y.Length > 3 && (x.Contains(y) || y.Contains(x));
    }

    sealed class SensorNvml(IntPtr dispositivo) : SensorGpu
    {
        public static SensorNvml? Crear(string nombre)
        {
            try
            {
                if (nvmlInit_v2() != 0 || nvmlDeviceGetCount_v2(out var n) != 0) return null;
                for (uint i = 0; i < n; i++)
                {
                    if (nvmlDeviceGetHandleByIndex_v2(i, out var d) != 0) continue;
                    var buf = new byte[96];
                    nvmlDeviceGetName(d, buf, (uint)buf.Length);
                    var nombreNvml = Encoding.ASCII.GetString(buf).TrimEnd('\0');
                    if (n == 1 || Coinciden(nombreNvml, nombre)) return new SensorNvml(d);
                }
            }
            catch { }
            return null;
        }

        public override LecturaGpu Leer()
        {
            double? t = nvmlDeviceGetTemperature(dispositivo, 0, out var temp) == 0 ? temp : null;
            double? mhz = nvmlDeviceGetClockInfo(dispositivo, 0, out var reloj) == 0 ? reloj : null;
            double? uso = nvmlDeviceGetUtilizationRates(dispositivo, out var u) == 0 ? u.Gpu : null;
            double? w = nvmlDeviceGetPowerUsage(dispositivo, out var mw) == 0 ? mw / 1000.0 : null;
            bool limitada = false;
            try { if (nvmlDeviceGetCurrentClocksThrottleReasons(dispositivo, out var r) == 0) limitada = (r & (0x20 | 0x40)) != 0; } catch { }
            return new LecturaGpu(t, mhz, uso, w, limitada);
        }

        [StructLayout(LayoutKind.Sequential)] struct Uso { public uint Gpu, Memoria; }
        [DllImport("nvml")] static extern int nvmlInit_v2();
        [DllImport("nvml")] static extern int nvmlDeviceGetCount_v2(out uint n);
        [DllImport("nvml")] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr d);
        [DllImport("nvml")] static extern int nvmlDeviceGetName(IntPtr d, byte[] nombre, uint largo);
        [DllImport("nvml")] static extern int nvmlDeviceGetTemperature(IntPtr d, int sensor, out uint temp);
        [DllImport("nvml")] static extern int nvmlDeviceGetClockInfo(IntPtr d, int tipo, out uint mhz);
        [DllImport("nvml")] static extern int nvmlDeviceGetUtilizationRates(IntPtr d, out Uso uso);
        [DllImport("nvml")] static extern int nvmlDeviceGetPowerUsage(IntPtr d, out uint mw);
        [DllImport("nvml")] static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr d, out ulong razones);
    }

    /// <summary>Windows: la temperatura que muestra el Administrador de tareas (datos de rendimiento del adaptador).</summary>
    [SupportedOSPlatform("windows")]
    sealed class SensorWindows(uint adaptador) : SensorGpu
    {
        public static SensorWindows? Crear(string nombre)
        {
            try
            {
                var e = new Enumeracion();
                if (D3DKMTEnumAdapters2(ref e) != 0 || e.Numero == 0) return null;
                var tam = Marshal.SizeOf<InfoAdaptador>();
                var mem = Marshal.AllocHGlobal(tam * (int)e.Numero);
                try
                {
                    e.Adaptadores = mem;
                    if (D3DKMTEnumAdapters2(ref e) != 0) return null;
                    for (int i = 0; i < e.Numero; i++)
                    {
                        var info = Marshal.PtrToStructure<InfoAdaptador>(mem + i * tam);
                        var registro = new byte[2080];
                        if (Consultar(info.Adaptador, 8, registro) != 0) continue;
                        var cadena = Encoding.Unicode.GetString(registro, 0, 520).TrimEnd('\0');
                        if (Coinciden(cadena, nombre)) return new SensorWindows(info.Adaptador);
                    }
                }
                finally { Marshal.FreeHGlobal(mem); }
            }
            catch { }
            return null;
        }

        public override LecturaGpu Leer()
        {
            var datos = new byte[64]; // D3DKMT_ADAPTER_PERFDATA, índice físico 0
            double? t = null;
            if (Consultar(adaptador, 62, datos) == 0)
            {
                var deci = BitConverter.ToUInt32(datos, 56);
                if (deci is > 0 and < 1500) t = deci / 10.0;
            }
            return new LecturaGpu(t, null, null, null, false);
        }

        static int Consultar(uint adaptador, int tipo, byte[] datos)
        {
            var h = GCHandle.Alloc(datos, GCHandleType.Pinned);
            try
            {
                var q = new Consulta { Adaptador = adaptador, Tipo = tipo, Datos = h.AddrOfPinnedObject(), Tam = (uint)datos.Length };
                return D3DKMTQueryAdapterInfo(ref q);
            }
            finally { h.Free(); }
        }

        [StructLayout(LayoutKind.Sequential)] struct Enumeracion { public uint Numero; public IntPtr Adaptadores; }
        [StructLayout(LayoutKind.Sequential)] struct InfoAdaptador { public uint Adaptador; public uint LuidBajo; public int LuidAlto; public uint Salidas; public int Preciso; }
        [StructLayout(LayoutKind.Sequential)] struct Consulta { public uint Adaptador; public int Tipo; public IntPtr Datos; public uint Tam; }
        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref Enumeracion e);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref Consulta q);
    }

    /// <summary>Linux: temperatura del controlador de la gráfica (amdgpu, nouveau, i915, xe).</summary>
    sealed class SensorHwmon(string ruta) : SensorGpu
    {
        public static SensorHwmon? Crear(string fabricante)
        {
            if (!Directory.Exists("/sys/class/hwmon")) return null;
            string[] chips = fabricante switch
            {
                "AMD" => new[] { "amdgpu", "radeon" },
                "NVIDIA" => new[] { "nouveau" },
                "Intel" => new[] { "xe", "i915" },
                _ => Array.Empty<string>()
            };
            foreach (var h in Directory.GetDirectories("/sys/class/hwmon").Order())
                if (chips.Contains(Plataforma.Leer(Path.Combine(h, "name"))) && File.Exists(Path.Combine(h, "temp1_input")))
                    return new SensorHwmon(h);
            return null;
        }

        public override LecturaGpu Leer()
        {
            double? t = double.TryParse(Plataforma.Leer(Path.Combine(ruta, "temp1_input")), out var mc) ? mc / 1000 : null;
            double? w = double.TryParse(Plataforma.Leer(Path.Combine(ruta, "power1_average")), out var uw) ? uw / 1e6 : null;
            double? mhz = null;
            var frec = Plataforma.Leer(Path.Combine(ruta, "freq1_input"));
            if (double.TryParse(frec, out var hz)) mhz = hz / 1e6;
            return new LecturaGpu(t, mhz, null, w, false);
        }
    }
}

/// <summary>Las funciones de OpenCL 1.2 que se usan.</summary>
internal static class OpenCl
{
    public const ulong TipoCpu = 1 << 1, TipoGpu = 1 << 2, TipoAcelerador = 1 << 3, TipoTodos = 0xFFFFFFFF;
    public const uint Unidades = 0x1002, Frecuencia = 0x100C, BloqueMaximo = 0x1010, MemoriaGlobal = 0x101F,
        Nombre = 0x102B, Fabricante = 0x102C, VersionDriver = 0x102D, MemoriaUnificada = 0x1035, NombrePlacaAmd = 0x4038,
        UuidKhr = 0x106A, PciBusKhr = 0x410F, TopologiaAmd = 0x4037, PciBusNv = 0x4008, PciRanuraNv = 0x4009;

    public static string Texto(IntPtr d, uint param)
    {
        var buf = new byte[512];
        return clGetDeviceInfo(d, param, (nuint)buf.Length, buf, out var n) == 0
            ? Encoding.UTF8.GetString(buf, 0, (int)Math.Min((long)n, buf.Length)).TrimEnd('\0').Trim()
            : "";
    }

    /// <summary>Lo que distingue a una tarjeta física, aunque la ofrezcan dos plataformas OpenCL distintas.</summary>
    public static string Identidad(IntPtr d, Dictionary<string, int> repetidasEnPlataforma)
    {
        // 1) UUID del dispositivo (cl_khr_device_uuid). Algunos controladores lo dejan a ceros: no sirve.
        var uuid = Bytes(d, UuidKhr, 16);
        if (uuid != null && uuid.Any(b => b != 0)) return "uuid:" + Convert.ToHexString(uuid);
        // 2) Posición en el bus PCI (cl_khr_pci_bus_info): dominio, bus, dispositivo y función.
        var pci = Bytes(d, PciBusKhr, 16);
        if (pci != null && pci.Any(b => b != 0))
            return $"pci:{BitConverter.ToUInt32(pci, 0):x}:{BitConverter.ToUInt32(pci, 4):x}:{BitConverter.ToUInt32(pci, 8):x}.{BitConverter.ToUInt32(pci, 12):x}";
        // 3) Topología de AMD (cl_amd_device_attribute_query): tipo 1 = PCIe, bus/disp./función al final.
        var amd = Bytes(d, TopologiaAmd, 24);
        if (amd != null && BitConverter.ToUInt32(amd, 0) == 1) return $"pci:?:{amd[21]:x}:{amd[22]:x}.{amd[23]:x}";
        // 4) NVIDIA (cl_nv_device_attribute_query): bus y ranura.
        var bus = Bytes(d, PciBusNv, 4);
        var ranura = Bytes(d, PciRanuraNv, 4);
        if (bus != null && ranura != null)
            return $"pci:?:{BitConverter.ToUInt32(bus, 0):x}:{BitConverter.ToUInt32(ranura, 0) >> 3:x}.{BitConverter.ToUInt32(ranura, 0) & 7:x}";
        // 5) Sin nada de eso, por sus datos. Dos iguales dentro de la misma plataforma sí son dos tarjetas
        //    (por eso el número de orden); entre plataformas distintas se toman por la misma.
        var datos = string.Join("|", Texto(d, Fabricante), Texto(d, Nombre), Texto(d, NombrePlacaAmd), Largo(d, MemoriaGlobal),
            Entero(d, Unidades), Texto(d, VersionDriver));
        var orden = repetidasEnPlataforma.GetValueOrDefault(datos);
        repetidasEnPlataforma[datos] = orden + 1;
        return $"datos:{datos}#{orden}";
    }

    /// <summary>Un dato binario de tamaño fijo, o null si el controlador no lo da.</summary>
    static byte[]? Bytes(IntPtr d, uint param, int tam)
    {
        var buf = new byte[tam];
        return clGetDeviceInfo(d, param, (nuint)tam, buf, out var n) == 0 && (int)n == tam ? buf : null;
    }

    public static ulong Largo(IntPtr d, uint param)
    {
        var buf = new byte[8];
        return clGetDeviceInfo(d, param, 8, buf, out _) == 0 ? BitConverter.ToUInt64(buf, 0) : 0;
    }

    public static uint Entero(IntPtr d, uint param)
    {
        var buf = new byte[8];
        return clGetDeviceInfo(d, param, 8, buf, out _) == 0 ? BitConverter.ToUInt32(buf, 0) : 0;
    }

    public static void Comprobar(int err, string donde)
    {
        if (err == 0) return;
        throw new InvalidOperationException(err switch
        {
            -4 or -5 or -6 => Tx("La gráfica se ha quedado sin memoria o sin recursos."),
            -36 or -9999 => Tx("El controlador de la gráfica ha dejado de responder (se ha reiniciado o colgado)."),
            _ => Tf("Error de la gráfica ({0}, código {1}).", donde, err)
        });
    }

    [DllImport("OpenCL")] public static extern int clGetPlatformIDs(uint n, IntPtr[]? plataformas, out uint total);
    [DllImport("OpenCL")] public static extern int clGetDeviceIDs(IntPtr plataforma, ulong tipo, uint n, IntPtr[]? dispositivos, out uint total);
    [DllImport("OpenCL")] public static extern int clGetDeviceInfo(IntPtr d, uint param, nuint tam, byte[] valor, out nuint devuelto);
    [DllImport("OpenCL")] public static extern IntPtr clCreateContext(IntPtr props, uint n, IntPtr[] dispositivos, IntPtr aviso, IntPtr datos, out int err);
    [DllImport("OpenCL")] public static extern IntPtr clCreateCommandQueue(IntPtr contexto, IntPtr d, ulong props, out int err);
    [DllImport("OpenCL")] public static extern IntPtr clCreateProgramWithSource(IntPtr contexto, uint n, string[] fuentes, nuint[]? largos, out int err);
    [DllImport("OpenCL")] public static extern int clBuildProgram(IntPtr programa, uint n, IntPtr[] dispositivos, string opciones, IntPtr aviso, IntPtr datos);
    [DllImport("OpenCL")] public static extern int clGetProgramBuildInfo(IntPtr programa, IntPtr d, uint param, nuint tam, byte[] valor, out nuint devuelto);
    [DllImport("OpenCL")] public static extern IntPtr clCreateKernel(IntPtr programa, string nombre, out int err);
    [DllImport("OpenCL")] public static extern IntPtr clCreateBuffer(IntPtr contexto, ulong flags, nuint tam, IntPtr host, out int err);
    [DllImport("OpenCL")] public static extern int clSetKernelArg(IntPtr kernel, uint i, nuint tam, ref IntPtr valor);
    [DllImport("OpenCL")] public static extern int clSetKernelArg(IntPtr kernel, uint i, nuint tam, ref uint valor);
    [DllImport("OpenCL")] public static extern int clSetKernelArg(IntPtr kernel, uint i, nuint tam, ref float valor);
    [DllImport("OpenCL")] public static extern int clEnqueueNDRangeKernel(IntPtr cola, IntPtr kernel, uint dims, nuint[]? desplazamiento, nuint[] global, nuint[]? local, uint nEspera, IntPtr[]? espera, IntPtr evento);
    [DllImport("OpenCL")] public static extern int clEnqueueReadBuffer(IntPtr cola, IntPtr buf, uint bloqueante, nuint desp, nuint tam, IntPtr destino, uint nEspera, IntPtr[]? espera, IntPtr evento);
    [DllImport("OpenCL")] public static extern int clEnqueueReadBuffer(IntPtr cola, IntPtr buf, uint bloqueante, nuint desp, nuint tam, ref uint destino, uint nEspera, IntPtr[]? espera, IntPtr evento);
    [DllImport("OpenCL")] public static extern int clEnqueueWriteBuffer(IntPtr cola, IntPtr buf, uint bloqueante, nuint desp, nuint tam, ref uint origen, uint nEspera, IntPtr[]? espera, IntPtr evento);
    [DllImport("OpenCL")] public static extern int clFinish(IntPtr cola);
    [DllImport("OpenCL")] public static extern int clReleaseMemObject(IntPtr m);
    [DllImport("OpenCL")] public static extern int clReleaseKernel(IntPtr k);
    [DllImport("OpenCL")] public static extern int clReleaseProgram(IntPtr p);
    [DllImport("OpenCL")] public static extern int clReleaseCommandQueue(IntPtr c);
    [DllImport("OpenCL")] public static extern int clReleaseContext(IntPtr c);
}
