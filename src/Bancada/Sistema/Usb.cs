using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Bancada.Sistema;

/// <summary>Un dispositivo USB conectado y el puerto donde está.</summary>
public sealed record DispositivoUsb(
    string Clave, string Nombre, string Puerto, string IdPuerto,
    double? Mbps, bool PuertoUsb3, bool DispositivoUsb3, bool Interno, bool PorConcentrador = false)
{
    public string Velocidad => Mbps switch
    {
        null => "",
        <= 2 => Tx("USB 1 (1,5 Mbps)"),
        <= 12 => "USB 1 (12 Mbps)",
        <= 480 => "USB 2 (480 Mbps)",
        <= 5000 => "USB 3 (5 Gbps)",
        <= 10000 => "USB 3 (10 Gbps)",
        _ => "USB 3 (20 Gbps)"
    };

    /// <summary>Un dispositivo USB 3 en un puerto USB 3 que va a velocidad USB 2: cable, conector o puerto con problemas.</summary>
    public bool VaLento => DispositivoUsb3 && PuertoUsb3 && Mbps is <= 480;
}

internal static class Usb
{
    public static List<DispositivoUsb> Listar()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return UsbWindows.Listar();
            if (OperatingSystem.IsLinux()) return ListarLinux();
        }
        catch { }
        return new();
    }

    // ---------- Linux: /sys/bus/usb/devices ----------

    static List<DispositivoUsb> ListarLinux()
    {
        var lista = new List<DispositivoUsb>();
        const string raiz = "/sys/bus/usb/devices";
        if (!Directory.Exists(raiz)) return lista;
        foreach (var dir in Directory.GetDirectories(raiz))
        {
            var nombre = Path.GetFileName(dir);
            var m = Regex.Match(nombre, @"^(\d+)-([\d.]+)$");
            if (!m.Success) continue;
            string? L(string f) => Plataforma.Leer(Path.Combine(dir, f));
            if (L("bDeviceClass") == "09") continue; // concentradores

            var producto = L("product");
            var fabricante = L("manufacturer");
            var vid = L("idVendor") ?? "";
            var pid = L("idProduct") ?? "";
            var texto = string.Join(" ", new[] { fabricante, producto }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            if (texto.Length == 0) texto = Tf("Dispositivo {0}:{1}", vid, pid);
            double? mbps = double.TryParse(L("speed"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
            bool disp3 = double.TryParse((L("version") ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ver) && ver >= 3;

            // Directorio del puerto en el concentrador padre.
            var bus = m.Groups[1].Value;
            var ruta = m.Groups[2].Value;
            var ultimo = ruta.Contains('.') ? ruta[(ruta.LastIndexOf('.') + 1)..] : ruta;
            var padre = ruta.Contains('.') ? $"{bus}-{ruta[..ruta.LastIndexOf('.')]}" : $"usb{bus}";
            var dirPadre = Path.Combine(raiz, padre);
            var interfazPadre = padre.StartsWith("usb") ? $"{bus}-0:1.0" : $"{padre}:1.0";
            var puertoDir = Path.Combine(dirPadre, interfazPadre, padre.StartsWith("usb") ? $"usb{bus}-port{ultimo}" : $"{padre}-port{ultimo}");

            string idPuerto = nombre;
            bool puerto3 = mbps >= 5000;
            try
            {
                var real = new DirectoryInfo(puertoDir).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(puertoDir);
                idPuerto = real;
                var peer = Path.Combine(puertoDir, "peer");
                if (Directory.Exists(peer))
                {
                    puerto3 = true; // el puerto tiene pareja USB 3
                    var realPeer = new DirectoryInfo(peer).ResolveLinkTarget(true)?.FullName ?? peer;
                    if (string.CompareOrdinal(realPeer, idPuerto) < 0) idPuerto = realPeer;
                }
            }
            catch { }
            var conexion = Plataforma.Leer(Path.Combine(puertoDir, "connect_type"));
            bool interno = L("removable") == "fixed" || conexion == "hardwired";

            // Lo que cuenta es el puerto del propio equipo: con un concentrador o una base, el primero de la cadena.
            var primero = ruta.Contains('.') ? ruta[..ruta.IndexOf('.')] : ruta;
            if (ruta.Contains('.'))
            {
                var dirRaiz = Path.Combine(raiz, $"usb{bus}", $"{bus}-0:1.0", $"usb{bus}-port{primero}");
                try
                {
                    idPuerto = new DirectoryInfo(dirRaiz).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(dirRaiz);
                    var peer = Path.Combine(dirRaiz, "peer");
                    if (Directory.Exists(peer))
                    {
                        var realPeer = new DirectoryInfo(peer).ResolveLinkTarget(true)?.FullName ?? peer;
                        if (string.CompareOrdinal(realPeer, idPuerto) < 0) idPuerto = realPeer;
                    }
                }
                catch { idPuerto = $"{bus}-{primero}"; }
            }
            var puerto = ruta.Contains('.') ? Tf("Puerto {0}-{1} (a través de un concentrador o base)", bus, primero) : Tf("Puerto {0}", nombre);
            lista.Add(new DispositivoUsb(nombre, texto, puerto, idPuerto, mbps, puerto3, disp3, interno, ruta.Contains('.')));
        }
        return lista.OrderBy(d => d.Interno).ThenBy(d => d.Puerto).ToList();
    }
}

/// <summary>Windows: árbol de dispositivos (CfgMgr32) y consulta de velocidad al concentrador de cada puerto.</summary>
[SupportedOSPlatform("windows")]
internal static class UsbWindows
{
    static readonly Guid InterfazHub = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");
    static readonly Guid Base = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly (Guid, uint) PropDescripcionBus = (new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    static readonly (Guid, uint) PropNombre = (Base, 14);
    static readonly (Guid, uint) PropDescripcion = (Base, 2);
    static readonly (Guid, uint) PropServicio = (Base, 6);
    static readonly (Guid, uint) PropDireccion = (Base, 30);
    static readonly (Guid, uint) PropContenedor = (new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    static readonly Guid ContenedorEquipo = new("00000000-0000-0000-ffff-ffffffffffff");

    public static List<DispositivoUsb> Listar()
    {
        var lista = new List<DispositivoUsb>();
        var raices = new List<string>();
        var brutos = new List<(string id, string nombre, uint? puerto, string idHub, double? mbps, bool puerto3, bool disp3, bool interno, bool concentrador)>();
        foreach (var id in ListaIds("USB"))
        {
            if (id.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase)) { raices.Add(id); continue; }
            if (id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) continue; // interfaces de un mismo aparato
            if (CM_Locate_DevNodeW(out var nodo, id, 0) != 0) continue;
            var servicio = Texto(nodo, PropServicio) ?? "";
            if (servicio.StartsWith("usbhub", StringComparison.OrdinalIgnoreCase)) continue;

            var nombre = Texto(nodo, PropDescripcionBus) ?? Texto(nodo, PropNombre) ?? Texto(nodo, PropDescripcion) ?? id;
            var puerto = Numero(nodo, PropDireccion);
            bool interno = GuidDe(nodo, PropContenedor) == ContenedorEquipo;
            if (CM_Get_Parent(out var padre, nodo, 0) != 0) continue;
            var idPadre = IdDe(padre) ?? "";

            double? mbps = null;
            bool puerto3 = false, disp3 = false;
            if (puerto is uint p && RutaHub(idPadre) is string hub)
                (mbps, puerto3, disp3) = Velocidad(hub, p);

            // Lo que cuenta es el puerto del propio equipo: con un concentrador o una base, se sube por la cadena.
            bool deRaiz = EsRaiz(idPadre);
            var idHub = idPadre;
            var puertoEquipo = puerto;
            for (int salto = 0; !EsRaiz(idHub) && salto < 8; salto++)
            {
                if (CM_Locate_DevNodeW(out var nodoHub, idHub, 0) != 0 || CM_Get_Parent(out var arriba, nodoHub, 0) != 0) break;
                puertoEquipo = Numero(nodoHub, PropDireccion);
                idHub = IdDe(arriba) ?? "";
            }
            brutos.Add((id, Modelo.TextoEquipo.Hardware(nombre), puertoEquipo, idHub, mbps, puerto3, disp3, interno, !deRaiz));
        }
        // Con varios controladores, el número de puerto se repite: se añade cuál es.
        foreach (var b in brutos)
        {
            var raiz = raices.Count > 1 ? raices.FindIndex(r => r.Equals(b.idHub, StringComparison.OrdinalIgnoreCase)) : -1;
            string texto;
            if (b.puerto is not uint n) texto = Tx("Puerto desconocido");
            else if (raiz >= 0)
                texto = b.concentrador
                    ? Tf("Puerto {0} del controlador {1} (a través de un concentrador o base)", n, raiz + 1)
                    : Tf("Puerto {0} del controlador {1}", n, raiz + 1);
            else
                texto = b.concentrador ? Tf("Puerto {0} (a través de un concentrador o base)", n) : Tf("Puerto {0}", n);
            lista.Add(new DispositivoUsb(b.id, b.nombre, texto, $"{b.idHub}#{b.puerto}", b.mbps, b.puerto3, b.disp3, b.interno, b.concentrador));
        }
        return lista.OrderBy(d => d.Interno).ThenBy(d => d.Puerto).ToList();
    }

    static bool EsRaiz(string id) => id.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase);

    static List<string> ListaIds(string enumerador)
    {
        const uint flags = 0x1 | 0x100; // filtro por enumerador + solo presentes
        if (CM_Get_Device_ID_List_SizeW(out var largo, enumerador, flags) != 0 || largo == 0) return new();
        var buf = new char[largo];
        if (CM_Get_Device_ID_ListW(enumerador, buf, largo, flags) != 0) return new();
        return new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    static string? IdDe(uint nodo)
    {
        var buf = new char[400];
        return CM_Get_Device_IDW(nodo, buf, buf.Length, 0) == 0 ? new string(buf).TrimEnd('\0') : null;
    }

    static readonly Dictionary<string, string?> rutasHub = new();

    static string? RutaHub(string idHub)
    {
        if (rutasHub.TryGetValue(idHub, out var r)) return r;
        var guid = InterfazHub;
        string? ruta = null;
        if (CM_Get_Device_Interface_List_SizeW(out var largo, ref guid, idHub, 0) == 0 && largo > 1)
        {
            var buf = new char[largo];
            if (CM_Get_Device_Interface_ListW(ref guid, idHub, buf, largo, 0) == 0)
                ruta = new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        return rutasHub[idHub] = ruta;
    }

    static (double? mbps, bool puerto3, bool disp3) Velocidad(string hub, uint puerto)
    {
        using var h = CreateFile(hub, 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return (null, false, false);
        var info = new byte[512];
        BitConverter.TryWriteBytes(info.AsSpan(0), puerto);
        if (!DeviceIoControl(h, 0x220448, info, info.Length, info, info.Length, out _, IntPtr.Zero)) return (null, false, false);
        int bcd = BitConverter.ToUInt16(info, 6);
        double? mbps = info[23] switch { 0 => 1.5, 1 => 12, 2 => 480, 3 => 5000, _ => null };
        bool disp3 = bcd >= 0x0300;

        var v2 = new byte[16];
        BitConverter.TryWriteBytes(v2.AsSpan(0), puerto);
        BitConverter.TryWriteBytes(v2.AsSpan(4), 16);
        BitConverter.TryWriteBytes(v2.AsSpan(8), 7);
        bool puerto3 = false;
        if (DeviceIoControl(h, 0x22045C, v2, 16, v2, 16, out _, IntPtr.Zero))
        {
            var protocolos = BitConverter.ToUInt32(v2, 8);
            var flags = BitConverter.ToUInt32(v2, 12);
            puerto3 = (protocolos & 4) != 0;
            if ((flags & 1) != 0 && mbps < 5000) mbps = 5000;
            if ((flags & 4) != 0) mbps = 10000;
            if ((flags & 2) != 0) disp3 = true;
        }
        return (mbps, puerto3, disp3);
    }

    // ---------- Propiedades de dispositivo ----------

    static byte[]? Propiedad(uint nodo, (Guid g, uint pid) clave, out uint tipo)
    {
        var k = new ClaveProp { Fmtid = clave.g, Pid = clave.pid };
        uint tam = 0;
        CM_Get_DevNode_PropertyW(nodo, ref k, out tipo, null, ref tam, 0);
        if (tam == 0) return null;
        var buf = new byte[tam];
        return CM_Get_DevNode_PropertyW(nodo, ref k, out tipo, buf, ref tam, 0) == 0 ? buf : null;
    }

    public static string? Texto(uint nodo, (Guid, uint) clave)
    {
        var b = Propiedad(nodo, clave, out var tipo);
        if (b == null || tipo != 0x12) return null; // DEVPROP_TYPE_STRING
        var s = Encoding.Unicode.GetString(b).TrimEnd('\0').Trim();
        return s.Length > 0 ? s : null;
    }

    public static uint? Numero(uint nodo, (Guid, uint) clave)
    {
        var b = Propiedad(nodo, clave, out var tipo);
        return b is { Length: >= 4 } && tipo is 0x07 or 0x06 ? BitConverter.ToUInt32(b, 0) : null;
    }

    static Guid? GuidDe(uint nodo, (Guid, uint) clave)
    {
        var b = Propiedad(nodo, clave, out var tipo);
        return b is { Length: 16 } && tipo == 0x0D ? new Guid(b) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ClaveProp { public Guid Fmtid; public uint Pid; }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int largo, string filtro, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string filtro, char[] buf, int largo, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint nodo, string id, uint flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint padre, uint nodo, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_IDW(uint nodo, char[] buf, int largo, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_DevNode_PropertyW(uint nodo, ref ClaveProp clave, out uint tipo, byte[]? buf, ref uint tam, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_List_SizeW(out int largo, ref Guid clase, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_ListW(ref Guid clase, string id, char[] buf, int largo, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string ruta, uint acceso, uint compartir, IntPtr seg, uint modo, uint flags, IntPtr plantilla);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, byte[] entrada, int tamEntrada, byte[] salida, int tamSalida, out uint bytes, IntPtr ov);

    /// <summary>Lista genérica de dispositivos presentes de un enumerador (PCI, USB…), para otras pruebas.</summary>
    public static List<(string id, uint nodo)> Dispositivos(string enumerador)
    {
        var r = new List<(string, uint)>();
        foreach (var id in ListaIds(enumerador))
            if (CM_Locate_DevNodeW(out var nodo, id, 0) == 0) r.Add((id, nodo));
        return r;
    }

    public static string NombreDe(uint nodo, string id) => Texto(nodo, PropNombre) ?? Texto(nodo, PropDescripcion) ?? id;
}
