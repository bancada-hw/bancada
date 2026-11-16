using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Bancada.Sistema;

public enum Canal { Izquierdo, Derecho, Ambos }

/// <summary>Sonidos de prueba generados al vuelo y reproducción con lo que trae el sistema (sin librerías).</summary>
internal static class Sonido
{
    public const int Frecuencia = 44100;

    // ---------- Generación ----------

    /// <summary>Arpegio corto tipo campana (do-mi-sol-do) por el canal pedido.</summary>
    public static byte[] Campana(Canal canal)
    {
        double[] notas = { 523.25, 659.25, 783.99, 1046.5 };
        const double paso = 0.2, ultima = 0.7;
        int total = (int)((paso * (notas.Length - 1) + ultima) * Frecuencia);
        var mono = new double[total];
        for (int n = 0; n < notas.Length; n++)
        {
            int inicio = (int)(n * paso * Frecuencia);
            double dur = n == notas.Length - 1 ? ultima : paso + 0.25;
            int fin = Math.Min(total, inicio + (int)(dur * Frecuencia));
            for (int i = inicio; i < fin; i++)
            {
                double t = (double)(i - inicio) / Frecuencia;
                double env = Math.Min(1, t * 200) * Math.Exp(-t * 5.5);
                mono[i] += env * (Math.Sin(2 * Math.PI * notas[n] * t) + 0.22 * Math.Sin(4 * Math.PI * notas[n] * t));
            }
        }
        return Wav(Estereo(mono, canal, 0.42), 2);
    }

    /// <summary>Barrido de 60 Hz a 12 kHz en 6 segundos: deja oír vibraciones, zumbidos o cortes del altavoz.</summary>
    public static byte[] Barrido()
    {
        const double f0 = 60, f1 = 12000, dur = 6;
        int total = (int)(dur * Frecuencia);
        var mono = new double[total];
        double k = Math.Log(f1 / f0);
        for (int i = 0; i < total; i++)
        {
            double t = (double)i / Frecuencia;
            double fase = 2 * Math.PI * f0 * dur / k * (Math.Exp(t / dur * k) - 1);
            double env = Math.Min(1, Math.Min(t / 0.15, (dur - t) / 0.15));
            mono[i] = env * Math.Sin(fase);
        }
        return Wav(Estereo(mono, Canal.Ambos, 0.35), 2);
    }

    static short[] Estereo(double[] mono, Canal canal, double volumen)
    {
        var s = new short[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++)
        {
            var v = (short)Math.Clamp(mono[i] * volumen * 32767, -32767, 32767);
            if (canal != Canal.Derecho) s[2 * i] = v;
            if (canal != Canal.Izquierdo) s[2 * i + 1] = v;
        }
        return s;
    }

    /// <summary>Archivo WAV PCM de 16 bits en memoria.</summary>
    public static byte[] Wav(short[] muestras, int canales)
    {
        int datos = muestras.Length * 2;
        var b = new byte[44 + datos];
        using var w = new BinaryWriter(new MemoryStream(b));
        w.Write("RIFF"u8);
        w.Write(36 + datos);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)canales);
        w.Write(Frecuencia);
        w.Write(Frecuencia * canales * 2);
        w.Write((short)(canales * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(datos);
        Buffer.BlockCopy(muestras, 0, b, 44, datos);
        return b;
    }

    // ---------- Reproducción ----------

    static IntPtr memoriaWindows;
    static Process? reproductorLinux;

    /// <summary>Reproduce un WAV sin bloquear. Devuelve un error legible o null.</summary>
    public static string? Reproducir(byte[] wav)
    {
        Detener();
        if (OperatingSystem.IsWindows())
        {
            // PlaySound asíncrono lee de la memoria mientras suena: debe seguir viva hasta el siguiente sonido.
            memoriaWindows = Marshal.AllocHGlobal(wav.Length);
            Marshal.Copy(wav, 0, memoriaWindows, wav.Length);
            return PlaySound(memoriaWindows, IntPtr.Zero, 0x1 | 0x2 | 0x4) ? null : Tx("Windows no ha podido reproducir el sonido.");
        }

        var programa = Plataforma.Buscar("pw-play", "paplay", "aplay");
        if (programa == null) return Tx("No se ha encontrado ningún reproductor (pw-play, paplay o aplay).");
        var ruta = Path.Combine(Path.GetTempPath(), "bancada-sonido.wav");
        File.WriteAllBytes(ruta, wav);
        try
        {
            var args = Path.GetFileName(programa) == "aplay" ? $"-q \"{ruta}\"" : $"\"{ruta}\"";
            reproductorLinux = Process.Start(new ProcessStartInfo(programa, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static void Detener()
    {
        if (OperatingSystem.IsWindows())
        {
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
            if (memoriaWindows != IntPtr.Zero) Marshal.FreeHGlobal(memoriaWindows);
            memoriaWindows = IntPtr.Zero;
            return;
        }
        try { if (reproductorLinux is { HasExited: false }) reproductorLinux.Kill(); } catch { }
        reproductorLinux?.Dispose();
        reproductorLinux = null;
    }

    [DllImport("winmm.dll", SetLastError = true)]
    static extern bool PlaySound(IntPtr sonido, IntPtr modulo, uint opciones);

    // ---------- Dispositivos ----------

    public sealed record Dispositivos(List<string> Tarjetas, List<string> Salidas, List<string> Entradas);

    public static Dispositivos Listar()
    {
        var r = new Dispositivos(new(), new(), new());
        if (OperatingSystem.IsWindows()) ListarWindows(r);
        else if (OperatingSystem.IsLinux()) ListarLinux(r);
        return r;
    }

    [SupportedOSPlatform("windows")]
    static void ListarWindows(Dispositivos r)
    {
        foreach (var o in DetectorWindows.Wmi("SELECT Name, PNPDeviceID FROM Win32_SoundDevice"))
        {
            var pnp = DetectorWindows.T(o["PNPDeviceID"]);
            if (pnp.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) || pnp.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase)) continue;
            r.Tarjetas.Add(Modelo.TextoEquipo.Hardware(DetectorWindows.T(o["Name"])));
        }
        // Nombres completos de los puntos de audio activos (el registro los guarda sin recortar).
        Endpoints(@"Render", r.Salidas);
        Endpoints(@"Capture", r.Entradas);
        if (r.Salidas.Count == 0)
        {
            int salidas = waveOutGetNumDevs();
            for (int i = 0; i < salidas; i++)
            {
                var c = new CapsSalida();
                if (waveOutGetDevCapsW((UIntPtr)i, ref c, Marshal.SizeOf<CapsSalida>()) == 0) r.Salidas.Add(Modelo.TextoEquipo.Hardware(c.Nombre));
            }
        }
        if (r.Entradas.Count == 0)
        {
            int entradas = waveInGetNumDevs();
            for (int i = 0; i < entradas; i++)
            {
                var c = new CapsEntrada();
                if (waveInGetDevCapsW((UIntPtr)i, ref c, Marshal.SizeOf<CapsEntrada>()) == 0) r.Entradas.Add(Modelo.TextoEquipo.Hardware(c.Nombre));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    static void Endpoints(string tipo, List<string> lista)
    {
        try
        {
            using var raiz = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{tipo}");
            if (raiz == null) return;
            foreach (var id in raiz.GetSubKeyNames())
            {
                using var k = raiz.OpenSubKey(id);
                if (k?.GetValue("DeviceState") is not int estado || estado != 1) continue; // solo activos
                using var props = k.OpenSubKey("Properties");
                var descripcion = props?.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
                var interfaz = props?.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
                if (string.IsNullOrWhiteSpace(descripcion)) continue;
                lista.Add(Modelo.TextoEquipo.Hardware(string.IsNullOrWhiteSpace(interfaz) ? descripcion : $"{descripcion} ({interfaz})"));
            }
        }
        catch { }
    }

    static void ListarLinux(Dispositivos r)
    {
        var tarjetas = Plataforma.Leer("/proc/asound/cards") ?? "";
        foreach (Match m in Regex.Matches(tarjetas, @"^\s*\d+\s+\[[^\]]*\]:\s*[^-]+-\s*(.+)$", RegexOptions.Multiline))
            r.Tarjetas.Add(m.Groups[1].Value.Trim());
        var pactl = Plataforma.Buscar("pactl");
        if (pactl == null) return;
        foreach (var (tipo, lista) in new[] { ("sinks", r.Salidas), ("sources", r.Entradas) })
        {
            var salida = Plataforma.Ejecutar(pactl, $"list {tipo}", 4000) ?? "";
            foreach (Match m in Regex.Matches(salida, @"^\s*Description:\s*(.+)$", RegexOptions.Multiline))
            {
                var d = m.Groups[1].Value.Trim();
                if (!d.StartsWith("Monitor of", StringComparison.OrdinalIgnoreCase)) lista.Add(d);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CapsSalida
    {
        public ushort Mid, Pid;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Nombre;
        public uint Formatos;
        public ushort Canales, Reservado;
        public uint Soporte;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CapsEntrada
    {
        public ushort Mid, Pid;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Nombre;
        public uint Formatos;
        public ushort Canales, Reservado;
    }

    [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveOutGetDevCapsW(UIntPtr id, ref CapsSalida caps, int tam);
    [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(UIntPtr id, ref CapsEntrada caps, int tam);
}

/// <summary>Captura del micrófono predeterminado en mono, 16 bits, 44,1 kHz.</summary>
internal abstract class Grabadora : IDisposable
{
    /// <summary>Bloques de muestras según llegan (en un hilo de fondo).</summary>
    public event Action<short[]>? Muestras;

    protected void Entregar(short[] bloque) => Muestras?.Invoke(bloque);

    /// <summary>Empieza a grabar. Devuelve un error legible o null.</summary>
    public abstract string? Iniciar();

    public abstract void Dispose();

    public static Grabadora Crear() => OperatingSystem.IsWindows() ? new GrabadoraWindows() : new GrabadoraLinux();
}

[SupportedOSPlatform("windows")]
internal sealed class GrabadoraWindows : Grabadora
{
    const int Bloques = 4, MuestrasPorBloque = 2205; // 50 ms
    static readonly int TamCabecera = IntPtr.Size * 4 + 16;
    static readonly int DesplGrabado = IntPtr.Size + 4;
    static readonly int DesplFlags = IntPtr.Size * 2 + 8;

    IntPtr dispositivo;
    readonly IntPtr[] cabeceras = new IntPtr[Bloques];
    readonly IntPtr[] datos = new IntPtr[Bloques];
    Thread? hilo;
    volatile bool activa;

    public override string? Iniciar()
    {
        var formato = new Formato { Tag = 1, Canales = 1, Muestras = Sonido.Frecuencia, BytesSeg = Sonido.Frecuencia * 2, Bloque = 2, Bits = 16 };
        int r = waveInOpen(out dispositivo, 0xFFFFFFFF, ref formato, IntPtr.Zero, IntPtr.Zero, 0);
        if (r != 0)
            return r == 2 ? Tx("No hay ningún micrófono activo en Windows.") : Tf("Windows no deja abrir el micrófono (código {0}).", r);

        for (int i = 0; i < Bloques; i++)
        {
            datos[i] = Marshal.AllocHGlobal(MuestrasPorBloque * 2);
            cabeceras[i] = Marshal.AllocHGlobal(TamCabecera);
            for (int b = 0; b < TamCabecera; b++) Marshal.WriteByte(cabeceras[i], b, 0);
            Marshal.WriteIntPtr(cabeceras[i], 0, datos[i]);
            Marshal.WriteInt32(cabeceras[i], IntPtr.Size, MuestrasPorBloque * 2);
            waveInPrepareHeader(dispositivo, cabeceras[i], TamCabecera);
            waveInAddBuffer(dispositivo, cabeceras[i], TamCabecera);
        }
        activa = true;
        waveInStart(dispositivo);
        hilo = new Thread(Bucle) { IsBackground = true, Name = Tx("Micrófono") };
        hilo.Start();
        return null;
    }

    void Bucle()
    {
        while (activa)
        {
            for (int i = 0; i < Bloques && activa; i++)
            {
                var flags = Marshal.ReadInt32(cabeceras[i], DesplFlags);
                if ((flags & 1) == 0) continue; // WHDR_DONE
                int bytes = Marshal.ReadInt32(cabeceras[i], DesplGrabado);
                var bloque = new short[bytes / 2];
                Marshal.Copy(datos[i], bloque, 0, bloque.Length);
                Marshal.WriteInt32(cabeceras[i], DesplFlags, flags & ~1);
                waveInAddBuffer(dispositivo, cabeceras[i], TamCabecera);
                if (bloque.Length > 0) Entregar(bloque);
            }
            Thread.Sleep(10);
        }
    }

    public override void Dispose()
    {
        if (dispositivo == IntPtr.Zero) return;
        activa = false;
        hilo?.Join(500);
        waveInReset(dispositivo);
        for (int i = 0; i < Bloques; i++)
        {
            if (cabeceras[i] == IntPtr.Zero) continue;
            waveInUnprepareHeader(dispositivo, cabeceras[i], TamCabecera);
            Marshal.FreeHGlobal(cabeceras[i]);
            Marshal.FreeHGlobal(datos[i]);
            cabeceras[i] = IntPtr.Zero;
        }
        waveInClose(dispositivo);
        dispositivo = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct Formato
    {
        public ushort Tag, Canales;
        public uint Muestras, BytesSeg;
        public ushort Bloque, Bits, Extra;
    }

    [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, uint dispositivo, ref Formato f, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr cabecera, int tam);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr cabecera, int tam);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr cabecera, int tam);
    [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
}

/// <summary>En Linux se lee el audio en bruto de parecord (PulseAudio/PipeWire), arecord (ALSA) o pw-record.</summary>
internal sealed class GrabadoraLinux : Grabadora
{
    Process? proceso;
    Thread? hilo;
    volatile bool activa;

    public override string? Iniciar()
    {
        var programa = Plataforma.Buscar("parecord", "arecord", "pw-record");
        if (programa == null)
            return Tx("No se ha encontrado ningún programa para grabar (parecord, arecord o pw-record). Instala pulseaudio-utils o alsa-utils.");
        var args = Path.GetFileName(programa) switch
        {
            "parecord" => "--raw --format=s16le --rate=44100 --channels=1 --latency-msec=50",
            "arecord" => "-q -t raw -f S16_LE -r 44100 -c 1",
            _ => "--rate 44100 --channels 1 --format s16 -"
        };
        try
        {
            proceso = Process.Start(new ProcessStartInfo(programa, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        if (proceso == null) return Tx("No se ha podido empezar a grabar.");
        _ = proceso.StandardError.ReadToEndAsync();
        activa = true;
        hilo = new Thread(Bucle) { IsBackground = true, Name = Tx("Micrófono") };
        hilo.Start();
        return null;
    }

    void Bucle()
    {
        var flujo = proceso!.StandardOutput.BaseStream;
        var buf = new byte[4410];
        int pendiente = 0;
        try
        {
            while (activa)
            {
                int n = flujo.Read(buf, pendiente, buf.Length - pendiente);
                if (n <= 0) break;
                pendiente += n;
                int utiles = pendiente & ~1;
                if (utiles < 882) continue; // al menos 10 ms
                var bloque = new short[utiles / 2];
                Buffer.BlockCopy(buf, 0, bloque, 0, utiles);
                pendiente -= utiles;
                if (pendiente > 0) buf[0] = buf[utiles];
                Entregar(bloque);
            }
        }
        catch { }
    }

    public override void Dispose()
    {
        activa = false;
        try { if (proceso is { HasExited: false }) proceso.Kill(); } catch { }
        hilo?.Join(500);
        proceso?.Dispose();
        proceso = null;
    }
}
