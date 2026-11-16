using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bancada.Sistema;

/// <summary>
/// Reproduce en bucle y sin sonido el vídeo de la prueba de batería (1080p H.264, generado para Bancada)
/// dentro de una ventana nativa, con el decodificador del sistema: Media Foundation en Windows y GStreamer
/// en Linux. Los dos usan la gráfica para decodificar cuando el controlador lo permite, como un reproductor normal.
/// </summary>
internal abstract class ReproductorVideo : IDisposable
{
    /// <summary>Qué se está usando para reproducir, para mostrarlo al técnico.</summary>
    public abstract string Motor { get; }

    /// <summary>Por qué no se reproduce, o null si va bien.</summary>
    public string? Problema { get; protected set; }

    /// <summary>Veces que el vídeo ha llegado al final y ha vuelto a empezar.</summary>
    public int Vueltas { get; protected set; }

    /// <summary>Empieza a reproducir dentro de la ventana nativa <paramref name="ventana"/> (HWND en Windows, XID en Linux).</summary>
    public abstract void Empezar(IntPtr ventana, string archivo);

    /// <summary>Se llama cada medio segundo desde la interfaz: vuelve a empezar el vídeo cuando termina.</summary>
    public abstract void Vigilar();

    /// <summary>La ventana ha cambiado de tamaño.</summary>
    public virtual void Redimensionar() { }

    public abstract void Dispose();

    public static ReproductorVideo Crear()
    {
        if (OperatingSystem.IsWindows()) return new VideoWindows();
        if (VideoGstreamer.Disponible()) return new VideoGstreamer();
        return new VideoExterno();
    }

    /// <summary>Saca el vídeo de dentro del programa a la carpeta temporal (solo la primera vez). Va en trozos: se unen aquí.</summary>
    public static string Archivo()
    {
        var ensamblado = Assembly.GetExecutingAssembly();
        var trozos = ensamblado.GetManifestResourceNames().Where(n => n.StartsWith("Video.bateria.mp4."))
            .OrderBy(n => int.Parse(n[(n.LastIndexOf('.') + 1)..])).ToList();
        if (trozos.Count == 0) throw new FileNotFoundException("Video.bateria.mp4");
        long total = trozos.Sum(n => { using var s = ensamblado.GetManifestResourceStream(n)!; return s.Length; });
        var ruta = Path.Combine(Path.GetTempPath(), "Bancada", "bateria-1080p.mp4");
        if (File.Exists(ruta) && new FileInfo(ruta).Length == total) return ruta;
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var temporal = ruta + ".tmp";
        using (var destino = File.Create(temporal))
            foreach (var n in trozos)
            {
                using var origen = ensamblado.GetManifestResourceStream(n)!;
                origen.CopyTo(destino);
            }
        File.Move(temporal, ruta, true);
        return ruta;
    }
}

/// <summary>Windows: MFPlay (Media Foundation). Pinta directamente en la ventana y usa DXVA para decodificar.</summary>
[SupportedOSPlatform("windows")]
internal sealed class VideoWindows : ReproductorVideo
{
    const int Vacio = 0, Parado = 1, Reproduciendo = 2;
    IMFPMediaPlayer? jugador;
    bool haEmpezado;

    public override string Motor => "Media Foundation";

    public override void Empezar(IntPtr ventana, string archivo)
    {
        try
        {
            MFStartup(0x00020070, 0);
            Marshal.ThrowExceptionForHR(MFPCreateMediaPlayer(archivo, true, 0, IntPtr.Zero, ventana, out jugador));
            jugador.SetMute(true);
        }
        catch (Exception ex)
        {
            Problema = Tx("Windows no ha podido reproducir el vídeo.") + " " + ex.Message;
        }
    }

    public override void Vigilar()
    {
        if (jugador == null) return;
        if (jugador.GetState(out var estado) != 0) return;
        if (estado == Reproduciendo) haEmpezado = true;
        else if (estado == Parado && haEmpezado)
        {
            // Ha llegado al final: al principio otra vez.
            var cero = new PropVariant { Tipo = 20 }; // VT_I8, en unidades de 100 ns
            var en100ns = Guid.Empty;
            jugador.SetPosition(ref en100ns, ref cero);
            jugador.Play();
            Vueltas++;
        }
    }

    public override void Redimensionar() => jugador?.UpdateVideo();

    public override void Dispose()
    {
        if (jugador == null) return;
        try { jugador.Shutdown(); } catch { }
        Marshal.ReleaseComObject(jugador);
        jugador = null;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Tipo;
        [FieldOffset(8)] public long Valor;
    }

    // Orden exacto de la tabla de mfplay.h: los métodos que no se usan están para mantener los huecos.
    [ComImport, Guid("A714590A-58AF-430a-85BF-44F5EC838D85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFPMediaPlayer
    {
        [PreserveSig] int Play();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int FrameStep();
        [PreserveSig] int SetPosition(ref Guid tipo, ref PropVariant valor);
        [PreserveSig] int GetPosition(ref Guid tipo, out PropVariant valor);
        [PreserveSig] int GetDuration(ref Guid tipo, out PropVariant valor);
        [PreserveSig] int SetRate(float ritmo);
        [PreserveSig] int GetRate(out float ritmo);
        [PreserveSig] int GetSupportedRates(int adelante, out float minimo, out float maximo);
        [PreserveSig] int GetState(out int estado);
        [PreserveSig] int CreateMediaItemFromURL(IntPtr url, int sincrono, IntPtr usuario, out IntPtr item);
        [PreserveSig] int CreateMediaItemFromObject(IntPtr objeto, int sincrono, IntPtr usuario, out IntPtr item);
        [PreserveSig] int SetMediaItem(IntPtr item);
        [PreserveSig] int ClearMediaItem();
        [PreserveSig] int GetMediaItem(out IntPtr item);
        [PreserveSig] int GetVolume(out float volumen);
        [PreserveSig] int SetVolume(float volumen);
        [PreserveSig] int GetBalance(out float balance);
        [PreserveSig] int SetBalance(float balance);
        [PreserveSig] int GetMute(out int silencio);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool silencio);
        [PreserveSig] int GetNativeVideoSize(IntPtr video, IntPtr proporcion);
        [PreserveSig] int GetIdealVideoSize(IntPtr minimo, IntPtr maximo);
        [PreserveSig] int SetVideoSourceRect(IntPtr rect);
        [PreserveSig] int GetVideoSourceRect(IntPtr rect);
        [PreserveSig] int SetAspectRatioMode(uint modo);
        [PreserveSig] int GetAspectRatioMode(out uint modo);
        [PreserveSig] int GetVideoWindow(out IntPtr ventana);
        [PreserveSig] int UpdateVideo();
        [PreserveSig] int SetBorderColor(uint color);
        [PreserveSig] int InsertEffect(IntPtr efecto, int opcional);
        [PreserveSig] int RemoveEffect(IntPtr efecto);
        [PreserveSig] int RemoveAllEffects();
        [PreserveSig] int Shutdown();
    }

    [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
    [DllImport("mfplay.dll", CharSet = CharSet.Unicode)]
    static extern int MFPCreateMediaPlayer(string url, [MarshalAs(UnmanagedType.Bool)] bool empezar, int opciones,
        IntPtr aviso, IntPtr ventana, out IMFPMediaPlayer jugador);
}

/// <summary>Linux: GStreamer (playbin), pintando dentro de la ventana de Bancada. Usa VA-API si está instalado.</summary>
internal sealed class VideoGstreamer : ReproductorVideo
{
    const string Gst = "libgstreamer-1.0.so.0", GObject = "libgobject-2.0.so.0", GstVideo = "libgstvideo-1.0.so.0";
    const int EstadoNulo = 1, EstadoReproduciendo = 4;
    const int MensajeFin = 1, MensajeError = 2;
    static bool iniciado;
    IntPtr tuberia, bus;
    string motor = "GStreamer";

    public override string Motor => motor;

    public static bool Disponible()
    {
        if (!NativeLibrary.TryLoad(Gst, out _) || !NativeLibrary.TryLoad(GstVideo, out _)) return false;
        try
        {
            if (!iniciado) { gst_init(IntPtr.Zero, IntPtr.Zero); iniciado = true; }
            var prueba = gst_element_factory_make("playbin", null);
            if (prueba == IntPtr.Zero) return false;
            gst_object_unref(prueba);
            return true;
        }
        catch { return false; }
    }

    public override void Empezar(IntPtr ventana, string archivo)
    {
        try
        {
            tuberia = gst_element_factory_make("playbin", "bancada");
            // Salidas que aceptan pintar en una ventana ajena, de la más eficiente a la más sencilla.
            IntPtr salida = IntPtr.Zero;
            foreach (var nombre in new[] { "xvimagesink", "glimagesink", "ximagesink" })
            {
                salida = gst_element_factory_make(nombre, null);
                if (salida == IntPtr.Zero) continue;
                motor = $"GStreamer ({nombre})";
                break;
            }
            if (salida == IntPtr.Zero)
            {
                Problema = Tx("GStreamer no tiene ninguna salida de vídeo para X11 instalada.");
                return;
            }
            gst_video_overlay_set_window_handle(salida, (nuint)ventana);
            g_object_set(tuberia, "video-sink", salida, IntPtr.Zero);
            g_object_set(tuberia, "mute", 1, IntPtr.Zero);
            var uri = gst_filename_to_uri(archivo, IntPtr.Zero);
            g_object_set(tuberia, "uri", uri, IntPtr.Zero);
            g_free(uri);
            bus = gst_element_get_bus(tuberia);
            gst_element_set_state(tuberia, EstadoReproduciendo);
        }
        catch (Exception ex)
        {
            Problema = Tx("GStreamer no ha podido reproducir el vídeo.") + " " + ex.Message;
        }
    }

    public override void Vigilar()
    {
        if (bus == IntPtr.Zero) return;
        IntPtr m;
        // Se piden por separado para no tener que leer el tipo dentro de la estructura del mensaje.
        while ((m = gst_bus_timed_pop_filtered(bus, 0, MensajeError)) != IntPtr.Zero)
        {
            gst_mini_object_unref(m);
            Problema ??= Tx("GStreamer no ha podido decodificar el vídeo (faltan los complementos de H.264, por ejemplo gstreamer1.0-libav o gstreamer1.0-vaapi).");
        }
        while ((m = gst_bus_timed_pop_filtered(bus, 0, MensajeFin)) != IntPtr.Zero)
        {
            gst_mini_object_unref(m);
            // Fin del vídeo: al principio otra vez.
            gst_element_seek_simple(tuberia, 3, 1 | 4, 0); // tiempo, FLUSH | KEY_UNIT
            Vueltas++;
        }
    }

    public override void Dispose()
    {
        if (tuberia == IntPtr.Zero) return;
        gst_element_set_state(tuberia, EstadoNulo);
        if (bus != IntPtr.Zero) gst_object_unref(bus);
        gst_object_unref(tuberia);
        tuberia = bus = IntPtr.Zero;
    }

    [DllImport(Gst)] static extern void gst_init(IntPtr argc, IntPtr argv);
    [DllImport(Gst)] static extern IntPtr gst_element_factory_make(string fabrica, string? nombre);
    [DllImport(Gst)] static extern int gst_element_set_state(IntPtr elemento, int estado);
    [DllImport(Gst)] static extern IntPtr gst_element_get_bus(IntPtr elemento);
    [DllImport(Gst)] static extern IntPtr gst_bus_timed_pop_filtered(IntPtr bus, ulong espera, int tipos);
    [DllImport(Gst)] static extern bool gst_element_seek_simple(IntPtr elemento, int formato, int opciones, long posicion);
    [DllImport(Gst)] static extern void gst_mini_object_unref(IntPtr objeto);
    [DllImport(Gst)] static extern void gst_object_unref(IntPtr objeto);
    [DllImport(Gst)] static extern IntPtr gst_filename_to_uri(string archivo, IntPtr error);
    [DllImport(GstVideo)] static extern void gst_video_overlay_set_window_handle(IntPtr salida, nuint ventana);
    [DllImport(GObject)] static extern void g_object_set(IntPtr objeto, string propiedad, IntPtr valor, IntPtr fin);
    [DllImport(GObject)] static extern void g_object_set(IntPtr objeto, string propiedad, int valor, IntPtr fin);
    [DllImport("libglib-2.0.so.0")] static extern void g_free(IntPtr memoria);
}

/// <summary>Linux sin GStreamer: mpv o ffplay en su propia ventana, si están instalados.</summary>
internal sealed class VideoExterno : ReproductorVideo
{
    Process? proceso;
    string motor = "";

    public override string Motor => motor;

    public override void Empezar(IntPtr ventana, string archivo)
    {
        (string prog, string args)? elegido =
            Plataforma.Buscar("mpv") is string mpv ? (mpv, $"--loop=inf --no-audio --hwdec=auto --wid={ventana} \"{archivo}\"") :
            Plataforma.Buscar("ffplay") is string ff ? (ff, $"-loop 0 -an -loglevel quiet -window_title Bancada \"{archivo}\"") : null;
        if (elegido is not var (programa, argumentos))
        {
            Problema = Tx("No hay con qué reproducir vídeo: instala GStreamer (viene con casi todos los escritorios) o mpv.");
            return;
        }
        motor = Path.GetFileName(programa);
        try { proceso = Process.Start(new ProcessStartInfo(programa, argumentos) { UseShellExecute = false }); }
        catch (Exception ex) { Problema = ex.Message; }
    }

    public override void Vigilar()
    {
        if (proceso is { HasExited: true } && Problema == null) Problema = Tf("{0} se ha cerrado.", motor);
    }

    public override void Dispose()
    {
        try { if (proceso is { HasExited: false }) proceso.Kill(true); } catch { }
        proceso?.Dispose();
        proceso = null;
    }
}
