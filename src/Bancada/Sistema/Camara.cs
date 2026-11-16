using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkiaSharp;

namespace Bancada.Sistema;

/// <summary>Cámara abierta que entrega fotogramas en BGRA. Windows: Media Foundation. Linux: V4L2.</summary>
internal abstract class Camara : IDisposable
{
    public int Ancho { get; protected set; }
    public int Alto { get; protected set; }
    public string Formato { get; protected set; } = "";

    /// <summary>Espera el siguiente fotograma y lo copia en BGRA (Ancho × Alto × 4). False si no ha llegado.</summary>
    public abstract bool Leer(ref byte[] destino);

    public abstract void Dispose();

    public sealed record Info(string Nombre, object Id);

    public static List<Info> Listar()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return CamaraWindows.Listar();
            if (OperatingSystem.IsLinux()) return CamaraLinux.Listar();
        }
        catch { }
        return new();
    }

    /// <summary>Abre la cámara. Lanza una excepción con un mensaje legible si no se puede.</summary>
    public static Camara Abrir(Info info)
    {
        if (OperatingSystem.IsWindows()) return new CamaraWindows(info);
        if (OperatingSystem.IsLinux()) return new CamaraLinux((string)info.Id);
        throw new PlatformNotSupportedException();
    }

    protected void Reservar(ref byte[] destino)
    {
        var n = Ancho * Alto * 4;
        if (destino.Length != n) destino = new byte[n];
    }
}

// =====================================================================================
//  Windows: Media Foundation (Source Reader con conversión a RGB32)
// =====================================================================================

[SupportedOSPlatform("windows")]
internal sealed class CamaraWindows : Camara
{
    static readonly Guid SourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    static readonly Guid SourceTypeVidcap = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    static readonly Guid FriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    static readonly Guid Video = new("73646976-0000-0010-8000-00AA00389B71");
    static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    static readonly Guid IidMediaSource = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");
    const int PrimerVideo = unchecked((int)0xFFFFFFFC), TodosLosFlujos = unchecked((int)0xFFFFFFFE);

    static bool iniciado;
    readonly IMFActivate activador;
    readonly IMFSourceReader lector;
    int paso;

    static void Iniciar()
    {
        if (iniciado) return;
        Marshal.ThrowExceptionForHR(MFStartup(0x00020070, 0));
        iniciado = true;
    }

    public static new List<Info> Listar()
    {
        Iniciar();
        var lista = new List<Info>();
        MFCreateAttributes(out var attrs, 1);
        attrs.SetGUID(SourceType, SourceTypeVidcap);
        Marshal.ThrowExceptionForHR(MFEnumDeviceSources(attrs, out var array, out var n));
        for (int i = 0; i < n; i++)
        {
            var p = Marshal.ReadIntPtr(array, i * IntPtr.Size);
            var act = (IMFActivate)Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);
            string nombre = Tx("Cámara");
            try
            {
                act.GetAllocatedString(FriendlyName, out var s, out var largo);
                nombre = Marshal.PtrToStringUni(s, largo);
                Marshal.FreeCoTaskMem(s);
            }
            catch { }
            lista.Add(new Info(nombre, act));
        }
        Marshal.FreeCoTaskMem(array);
        Marshal.ReleaseComObject(attrs);
        return lista;
    }

    public CamaraWindows(Info info)
    {
        Iniciar();
        activador = (IMFActivate)info.Id;
        IntPtr fuente;
        try
        {
            activador.ActivateObject(IidMediaSource, out fuente);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070005)
        {
            throw new InvalidOperationException(Tx("Windows bloquea la cámara. Actívala en Configuración › Privacidad y seguridad › Cámara, incluido «Permitir que las aplicaciones de escritorio accedan a la cámara»."));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(Tf("No se puede encender la cámara (código 0x{0:X8}). Puede que otra aplicación la esté usando.", ex.HResult));
        }

        MFCreateAttributes(out var attrs, 1);
        attrs.SetUINT32(EnableVideoProcessing, 1);
        Marshal.ThrowExceptionForHR(MFCreateSourceReaderFromMediaSource(fuente, attrs, out lector));
        Marshal.Release(fuente);
        Marshal.ReleaseComObject(attrs);

        lector.SetStreamSelection(TodosLosFlujos, false);
        lector.SetStreamSelection(PrimerVideo, true);

        // Formato nativo: fluido (24 fotogramas/s o más) y con la mayor resolución sin pasar de 1280 de ancho.
        IMFMediaType? mejor = null;
        long mejorPuntos = -1;
        for (int i = 0; ; i++)
        {
            IMFMediaType t;
            try { lector.GetNativeMediaType(PrimerVideo, i, out t); }
            catch { break; }
            t.GetUINT64(FrameSize, out var tam);
            long w = tam >> 32, h = tam & 0xFFFFFFFF;
            double fps = 0;
            try
            {
                t.GetUINT64(FrameRate, out var fr);
                long num = fr >> 32, den = fr & 0xFFFFFFFF;
                if (den > 0) fps = (double)num / den;
            }
            catch { }
            long puntos = w > 1280 ? -1 : (fps >= 24 ? 1L << 40 : 0) + w * h * 64 + (long)Math.Min(fps, 60);
            if (puntos > mejorPuntos)
            {
                if (mejor != null) Marshal.ReleaseComObject(mejor);
                mejor = t;
                mejorPuntos = puntos;
            }
            else Marshal.ReleaseComObject(t);
        }
        if (mejor != null)
        {
            try { lector.SetCurrentMediaType(PrimerVideo, IntPtr.Zero, mejor); } catch { }
            Marshal.ReleaseComObject(mejor);
        }

        MFCreateMediaType(out var salida);
        salida.SetGUID(MajorType, Video);
        salida.SetGUID(Subtype, Rgb32);
        try { lector.SetCurrentMediaType(PrimerVideo, IntPtr.Zero, salida); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(Tf("La cámara no da imagen en un formato compatible (código 0x{0:X8}).", ex.HResult));
        }
        finally { Marshal.ReleaseComObject(salida); }
        LeerTamano();
        Formato = "RGB32";
    }

    void LeerTamano()
    {
        lector.GetCurrentMediaType(PrimerVideo, out var actual);
        actual.GetUINT64(FrameSize, out var tam);
        Ancho = (int)(tam >> 32);
        Alto = (int)(tam & 0xFFFFFFFF);
        try { actual.GetUINT32(DefaultStride, out paso); } catch { paso = Ancho * 4; }
        if (paso == 0) paso = Ancho * 4;
        Marshal.ReleaseComObject(actual);
    }

    public override bool Leer(ref byte[] destino)
    {
        lector.ReadSample(PrimerVideo, 0, out _, out var flags, out _, out var muestra);
        if ((flags & 0x20) != 0) LeerTamano(); // cambio de formato
        if ((flags & 0x1) != 0) throw new InvalidOperationException(Tx("La cámara ha dejado de enviar imagen."));
        if (muestra == null) return false;
        try
        {
            muestra.ConvertToContiguousBuffer(out var buffer);
            try
            {
                Reservar(ref destino);
                int fila = Ancho * 4;
                if (buffer is IMF2DBuffer b2)
                {
                    b2.Lock2D(out var linea0, out var pitch);
                    try { Copiar(linea0, pitch, destino, fila); }
                    finally { b2.Unlock2D(); }
                }
                else
                {
                    buffer.Lock(out var p, out _, out _);
                    try { Copiar(paso < 0 ? p + (Alto - 1) * -paso : p, paso, destino, fila); }
                    finally { buffer.Unlock(); }
                }
            }
            finally { Marshal.ReleaseComObject(buffer); }
        }
        finally { Marshal.ReleaseComObject(muestra); }
        return true;
    }

    void Copiar(IntPtr linea0, int pitch, byte[] destino, int fila)
    {
        for (int y = 0; y < Alto; y++)
            Marshal.Copy(linea0 + y * pitch, destino, y * fila, fila);
        // RGB32 deja el cuarto byte a cero: se marca opaco.
        for (int i = 3; i < destino.Length; i += 4) destino[i] = 255;
    }

    public override void Dispose()
    {
        try { Marshal.ReleaseComObject(lector); } catch { }
        try { activador.ShutdownObject(); } catch { }
    }

    [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IMFAttributes attrs, int tam);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType tipo);
    [DllImport("mf.dll")] static extern int MFEnumDeviceSources(IMFAttributes attrs, out IntPtr activadores, out int n);
    [DllImport("mfreadwrite.dll")] static extern int MFCreateSourceReaderFromMediaSource(IntPtr fuente, IMFAttributes attrs, out IMFSourceReader lector);

    // Interfaces COM con solo los métodos que se usan; los huecos (_xx) mantienen el orden de la tabla virtual.
    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09(); void _10();
        void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17();
        void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid clave, int valor);
        void _19(); void _20();
        void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid clave, [MarshalAs(UnmanagedType.LPStruct)] Guid valor);
    }

    [ComImport, Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFActivate
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid clave, out IntPtr texto, out int largo);
        void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19(); void _20();
        void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
        void ActivateObject([MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr objeto);
        void ShutdownObject();
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void _00(); void _01(); void _02(); void _03();
        void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid clave, out int valor);
        void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid clave, out long valor);
        void _06(); void _07(); void _08(); void _09(); void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19(); void _20();
        void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid clave, [MarshalAs(UnmanagedType.LPStruct)] Guid valor);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19();
        void _20(); void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
        void _30(); void _31(); void _32(); void _33(); void _34(); void _35(); void _36(); void _37();
        void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        void Lock(out IntPtr datos, out int maximo, out int actual);
        void Unlock();
    }

    [ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMF2DBuffer
    {
        void Lock2D(out IntPtr linea0, out int pitch);
        void Unlock2D();
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        void _00();
        void SetStreamSelection(int flujo, [MarshalAs(UnmanagedType.Bool)] bool elegido);
        void GetNativeMediaType(int flujo, int indice, out IMFMediaType tipo);
        void GetCurrentMediaType(int flujo, out IMFMediaType tipo);
        void SetCurrentMediaType(int flujo, IntPtr reservado, IMFMediaType tipo);
        void _05();
        void ReadSample(int flujo, int control, out int flujoReal, out int flags, out long tiempo, out IMFSample? muestra);
    }
}

// =====================================================================================
//  Linux: Video4Linux2 con búferes mapeados (YUYV o MJPEG)
// =====================================================================================

[SupportedOSPlatform("linux")]
internal sealed unsafe class CamaraLinux : Camara
{
    const nuint QueryCap = 0x80685600, SetFmt = 0xC0D05605, ReqBufs = 0xC0145608, QueryBuf = 0xC0585609,
        QBuf = 0xC058560F, DqBuf = 0xC0585611, StreamOn = 0x40045612, StreamOff = 0x40045613;
    const uint Yuyv = 0x56595559, Mjpg = 0x47504A4D;
    const int TipoCaptura = 1, MemoriaMmap = 1, NumBuffers = 4;

    readonly int fd;
    readonly IntPtr[] mapas = new IntPtr[NumBuffers];
    readonly nuint[] largos = new nuint[NumBuffers];
    readonly int buffers;
    readonly uint formato;
    readonly int bytesPorLinea;
    bool emitiendo;

    public static new List<Info> Listar()
    {
        var lista = new List<Info>();
        const string clase = "/sys/class/video4linux";
        if (!Directory.Exists(clase)) return lista;
        foreach (var dir in Directory.GetDirectories(clase, "video*").OrderBy(d => int.TryParse(Path.GetFileName(d)[5..], out var n) ? n : 99))
        {
            // Cada cámara crea varios nodos; el de imagen es el de índice 0 (los demás son metadatos).
            if ((Plataforma.Leer(Path.Combine(dir, "index")) ?? "0") != "0") continue;
            var ruta = "/dev/" + Path.GetFileName(dir);
            var nombre = Plataforma.Leer(Path.Combine(dir, "name")) ?? ruta;
            if (nombre.Contains(':')) nombre = nombre[..nombre.IndexOf(':')].Trim();
            if (!EsCaptura(ruta)) continue;
            lista.Add(new Info(nombre, ruta));
        }
        return lista;
    }

    static bool EsCaptura(string ruta)
    {
        int f = open(ruta, 2 | 0x800);
        if (f < 0) return true; // sin permiso para mirar: se lista y ya se verá al abrirla
        try
        {
            var cap = stackalloc byte[104];
            if (ioctl(f, QueryCap, cap) < 0) return false;
            uint caps = *(uint*)(cap + 84), dev = *(uint*)(cap + 88);
            if ((caps & 0x80000000) != 0) caps = dev;
            return (caps & 0x1) != 0;
        }
        finally { close(f); }
    }

    public CamaraLinux(string ruta)
    {
        fd = open(ruta, 2 | 0x800); // O_RDWR | O_NONBLOCK
        if (fd < 0)
        {
            var err = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(err switch
            {
                13 => "No hay permiso para usar la cámara. Añade tu usuario al grupo «video» (sudo usermod -aG video $USER) y vuelve a iniciar sesión.",
                16 => Tx("La cámara está ocupada por otra aplicación."),
                _ => Tf("No se puede abrir la cámara (error {0}).", err)
            });
        }

        // Formato: YUYV 640×480 (lo admite casi cualquier webcam) y, si no, MJPEG.
        var fmt = stackalloc byte[208];
        bool ok = false;
        foreach (var pf in new[] { Yuyv, Mjpg })
        {
            new Span<byte>(fmt, 208).Clear();
            *(uint*)fmt = TipoCaptura;
            *(uint*)(fmt + 8) = 640;
            *(uint*)(fmt + 12) = 480;
            *(uint*)(fmt + 16) = pf;
            if (ioctl(fd, SetFmt, fmt) == 0 && *(uint*)(fmt + 16) == pf) { ok = true; break; }
        }
        if (!ok) { close(fd); throw new InvalidOperationException(Tx("La cámara no ofrece imagen en un formato compatible (YUYV o MJPEG).")); }
        Ancho = (int)*(uint*)(fmt + 8);
        Alto = (int)*(uint*)(fmt + 12);
        formato = *(uint*)(fmt + 16);
        bytesPorLinea = (int)*(uint*)(fmt + 24);
        if (bytesPorLinea == 0) bytesPorLinea = Ancho * 2;
        Formato = formato == Yuyv ? "YUYV" : "MJPEG";

        var req = stackalloc byte[20];
        new Span<byte>(req, 20).Clear();
        *(uint*)req = NumBuffers;
        *(uint*)(req + 4) = TipoCaptura;
        *(uint*)(req + 8) = MemoriaMmap;
        if (ioctl(fd, ReqBufs, req) < 0) { close(fd); throw new InvalidOperationException(Tx("La cámara no admite captura por búferes.")); }
        buffers = Math.Min(NumBuffers, (int)*(uint*)req);

        var buf = stackalloc byte[88];
        for (int i = 0; i < buffers; i++)
        {
            PrepararBuffer(buf, i);
            if (ioctl(fd, QueryBuf, buf) < 0) { Dispose(); throw new InvalidOperationException(Tx("Error preparando la cámara.")); }
            largos[i] = *(uint*)(buf + 72);
            mapas[i] = mmap(IntPtr.Zero, largos[i], 3, 1, fd, *(uint*)(buf + 64));
            if (mapas[i] == new IntPtr(-1)) { mapas[i] = IntPtr.Zero; Dispose(); throw new InvalidOperationException(Tx("Error preparando la cámara (mmap).")); }
            PrepararBuffer(buf, i);
            ioctl(fd, QBuf, buf);
        }
        int tipo = TipoCaptura;
        if (ioctl(fd, StreamOn, &tipo) < 0) { Dispose(); throw new InvalidOperationException(Tx("La cámara no ha empezado a enviar imagen.")); }
        emitiendo = true;
    }

    static void PrepararBuffer(byte* b, int indice)
    {
        new Span<byte>(b, 88).Clear();
        *(uint*)b = (uint)indice;
        *(uint*)(b + 4) = TipoCaptura;
        *(uint*)(b + 60) = MemoriaMmap;
    }

    public override bool Leer(ref byte[] destino)
    {
        var pfd = stackalloc int[2];
        pfd[0] = fd;
        pfd[1] = 1; // events = POLLIN (short) y revents = 0
        if (poll(pfd, 1, 1000) <= 0) return false;

        var buf = stackalloc byte[88];
        PrepararBuffer(buf, 0);
        if (ioctl(fd, DqBuf, buf) < 0) return false;
        int i = (int)*(uint*)buf;
        int usados = (int)*(uint*)(buf + 8);
        try
        {
            Reservar(ref destino);
            var origen = (byte*)mapas[i];
            if (formato == Yuyv) Yuyv2Bgra(origen, destino);
            else return Jpeg2Bgra(origen, usados, ref destino);
            return true;
        }
        finally
        {
            ioctl(fd, QBuf, buf);
        }
    }

    void Yuyv2Bgra(byte* src, byte[] dst)
    {
        fixed (byte* d0 = dst)
        {
            for (int y = 0; y < Alto; y++)
            {
                byte* s = src + y * bytesPorLinea;
                byte* d = d0 + y * Ancho * 4;
                for (int x = 0; x < Ancho; x += 2, s += 4, d += 8)
                {
                    int u = s[1] - 128, v = s[3] - 128;
                    int ru = 409 * v + 128, gu = -100 * u - 208 * v + 128, bu = 516 * u + 128;
                    int c0 = 298 * (s[0] - 16), c1 = 298 * (s[2] - 16);
                    d[0] = Sat((c0 + bu) >> 8); d[1] = Sat((c0 + gu) >> 8); d[2] = Sat((c0 + ru) >> 8); d[3] = 255;
                    d[4] = Sat((c1 + bu) >> 8); d[5] = Sat((c1 + gu) >> 8); d[6] = Sat((c1 + ru) >> 8); d[7] = 255;
                }
            }
        }
    }

    static byte Sat(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    bool Jpeg2Bgra(byte* jpeg, int largo, ref byte[] destino)
    {
        using var datos = SKData.CreateCopy((IntPtr)jpeg, (ulong)largo);
        using var codec = SKCodec.Create(datos);
        if (codec == null) return false;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        Ancho = info.Width;
        Alto = info.Height;
        Reservar(ref destino);
        fixed (byte* p = destino)
            return codec.GetPixels(info, (IntPtr)p) is SKCodecResult.Success or SKCodecResult.IncompleteInput;
    }

    public override void Dispose()
    {
        if (emitiendo)
        {
            int tipo = TipoCaptura;
            ioctl(fd, StreamOff, &tipo);
            emitiendo = false;
        }
        for (int i = 0; i < mapas.Length; i++)
        {
            if (mapas[i] != IntPtr.Zero) munmap(mapas[i], largos[i]);
            mapas[i] = IntPtr.Zero;
        }
        if (fd >= 0) close(fd);
    }

    [DllImport("libc", SetLastError = true)] static extern int open([MarshalAs(UnmanagedType.LPStr)] string ruta, int flags);
    [DllImport("libc")] static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] static extern int ioctl(int fd, nuint peticion, void* arg);
    [DllImport("libc", SetLastError = true)] static extern IntPtr mmap(IntPtr dir, nuint largo, int prot, int flags, int fd, long desplazamiento);
    [DllImport("libc")] static extern int munmap(IntPtr dir, nuint largo);
    [DllImport("libc")] static extern int poll(int* fds, nuint n, int espera);
}
