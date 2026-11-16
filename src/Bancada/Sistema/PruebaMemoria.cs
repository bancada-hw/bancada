using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Bancada.Sistema;

public sealed record ResultadoMemoria(long BytesProbados, long Errores, List<string> Detalles, TimeSpan Duracion, bool Cancelada);

/// <summary>
/// Prueba de la RAM libre: llena la memoria con patrones (ceros, unos, damero, bits que caminan,
/// la propia dirección y datos aleatorios) y comprueba que se leen igual que se escribieron.
/// </summary>
internal static unsafe class PruebaMemoria
{
    const long Trozo = 256L << 20;

    static string[] Patrones => new string[]
    {
        Tx("Todo ceros"), Tx("Todo unos"), Tx("Damero"), Tx("Damero invertido"), Tx("Bits que caminan"), Tx("Dirección propia"), Tx("Aleatorio"), Tx("Aleatorio invertido")
    };

    public static (long total, long disponible) Estado()
    {
        if (OperatingSystem.IsWindows())
        {
            var m = new EstadoMemoria { Tam = (uint)Marshal.SizeOf<EstadoMemoria>() };
            if (GlobalMemoryStatusEx(ref m)) return ((long)m.Total, (long)m.Disponible);
        }
        else
        {
            var info = Plataforma.Leer("/proc/meminfo") ?? "";
            long Kb(string clave) => long.TryParse(Regex.Match(info, $@"^{clave}:\s*(\d+)", RegexOptions.Multiline).Groups[1].Value, out var v) ? v * 1024 : 0;
            return (Kb("MemTotal"), Kb("MemAvailable"));
        }
        return (0, 0);
    }

    /// <summary>Cuánto probar: la mayor parte de la memoria libre, dejando margen para que el sistema no se ahogue.</summary>
    public static long TamanoRecomendado()
    {
        var (_, libre) = Estado();
        long t = Math.Min((long)(libre * 0.75), libre - (1536L << 20));
        t = Math.Min(t, 48L << 30);
        return Math.Max(t, 0) / Trozo * Trozo;
    }

    public static ResultadoMemoria Ejecutar(long bytes, IProgress<(double pct, string texto)> progreso, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        var trozos = new List<IntPtr>();
        var detalles = new List<string>();
        long errores = 0;
        bool cancelada = false;
        try
        {
            progreso.Report((0, Tx("Reservando memoria…")));
            for (long r = 0; r < bytes; r += Trozo)
            {
                ct.ThrowIfCancellationRequested();
                try { trozos.Add((IntPtr)NativeMemory.Alloc((nuint)Trozo)); }
                catch (OutOfMemoryException) { break; }
            }
            long probados = trozos.Count * Trozo;
            int pasos = Patrones.Length * 2, hechos = 0;
            var opciones = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };

            for (int patron = 0; patron < Patrones.Length; patron++)
            {
                var nombre = Patrones[patron];
                progreso.Report((hechos * 100.0 / pasos, Tf("{0}: escribiendo", nombre)));
                Parallel.For(0, trozos.Count, opciones, i => Escribir(patron, (ulong*)trozos[i], i));
                hechos++;
                progreso.Report((hechos * 100.0 / pasos, Tf("{0}: comprobando", nombre)));
                Parallel.For(0, trozos.Count, opciones, i =>
                {
                    var malos = Comprobar(patron, (ulong*)trozos[i], i, out var primero);
                    if (malos == 0) return;
                    Interlocked.Add(ref errores, malos);
                    lock (detalles)
                        if (detalles.Count < 12)
                            detalles.Add(Tf("{0}: {1} errores en el bloque {2} (primero en +0x{3:X}, bits cambiados 0x{4:X16})", nombre, malos, (i + 1), primero.desplazamiento, primero.bits));
                });
                hechos++;
            }
            return new ResultadoMemoria(probados, errores, detalles, reloj.Elapsed, false);
        }
        catch (OperationCanceledException)
        {
            cancelada = true;
            return new ResultadoMemoria(trozos.Count * Trozo, errores, detalles, reloj.Elapsed, cancelada);
        }
        finally
        {
            foreach (var t in trozos) NativeMemory.Free((void*)t);
        }
    }

    static ulong Semilla(int trozo, int patron) => (ulong)(trozo + 1) * 0x9E3779B97F4A7C15UL ^ (ulong)(patron + 7) * 0xD1B54A32D192ED03UL;

    static void Escribir(int patron, ulong* p, int trozo)
    {
        long n = Trozo / 8;
        switch (patron)
        {
            case 0: new Span<ulong>(p, (int)Math.Min(n, int.MaxValue)).Fill(0); break;
            case 1: new Span<ulong>(p, (int)Math.Min(n, int.MaxValue)).Fill(ulong.MaxValue); break;
            case 2: for (long i = 0; i < n; i++) p[i] = (i & 1) == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL; break;
            case 3: for (long i = 0; i < n; i++) p[i] = (i & 1) == 0 ? 0x5555555555555555UL : 0xAAAAAAAAAAAAAAAAUL; break;
            case 4: for (long i = 0; i < n; i++) p[i] = 1UL << (int)(i & 63); break;
            case 5: for (long i = 0; i < n; i++) p[i] = (ulong)(p + i) ^ 0x9E3779B97F4A7C15UL; break;
            default:
            {
                var s = Semilla(trozo, patron);
                bool inv = patron == 7;
                for (long i = 0; i < n; i++)
                {
                    s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
                    var v = s * 0x2545F4914F6CDD1DUL;
                    p[i] = inv ? ~v : v;
                }
                break;
            }
        }
    }

    static long Comprobar(int patron, ulong* p, int trozo, out (long desplazamiento, ulong bits) primero)
    {
        long n = Trozo / 8, malos = 0;
        primero = default;
        var s = Semilla(trozo, patron);
        for (long i = 0; i < n; i++)
        {
            ulong esperado;
            switch (patron)
            {
                case 0: esperado = 0; break;
                case 1: esperado = ulong.MaxValue; break;
                case 2: esperado = (i & 1) == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL; break;
                case 3: esperado = (i & 1) == 0 ? 0x5555555555555555UL : 0xAAAAAAAAAAAAAAAAUL; break;
                case 4: esperado = 1UL << (int)(i & 63); break;
                case 5: esperado = (ulong)(p + i) ^ 0x9E3779B97F4A7C15UL; break;
                default:
                    s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
                    esperado = s * 0x2545F4914F6CDD1DUL;
                    if (patron == 7) esperado = ~esperado;
                    break;
            }
            var leido = p[i];
            if (leido != esperado)
            {
                if (malos == 0) primero = (i * 8, leido ^ esperado);
                malos++;
            }
        }
        return malos;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EstadoMemoria
    {
        public uint Tam, Carga;
        public ulong Total, Disponible, TotalPaginacion, DisponiblePaginacion, TotalVirtual, DisponibleVirtual, Extendida;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref EstadoMemoria m);
}
