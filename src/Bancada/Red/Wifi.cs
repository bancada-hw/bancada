using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Bancada.Sistema;

namespace Bancada.Red;

public sealed record RedWifi(string Ssid, int SenalPct, int? Dbm, int Canal, string Banda, bool Conectada);

public sealed record ConexionWifi(string Ssid, int SenalPct, int? VelocidadMbps, string Banda);

public sealed record EstadoWifi(bool HayAdaptador, string? Error, ConexionWifi? Actual, List<RedWifi> Redes);

public static class Wifi
{
    public static async Task<EstadoWifi> EstadoAsync(bool escanear)
    {
        var e = OperatingSystem.IsWindows() ? await WifiWindows.EstadoAsync(escanear)
              : OperatingSystem.IsLinux() ? await WifiLinux.EstadoAsync(escanear)
              : new EstadoWifi(false, Tx("Sistema no compatible"), null, new());
        return Opciones.Anonimo ? Anonimizar(e) : e;
    }

    /// <summary>--anonimo: cambia los nombres de las redes por otros inventados (capturas para la documentación).</summary>
    static EstadoWifi Anonimizar(EstadoWifi e)
    {
        var nombres = new Dictionary<string, string>();
        string Nombre(string ssid) => ssid.Length == 0 ? ssid
            : nombres.TryGetValue(ssid, out var n) ? n
            : nombres[ssid] = nombres.Count == 0 ? "LAB-5G" : $"WIFI-{nombres.Count:00}";
        if (e.Actual != null) Nombre(e.Actual.Ssid);
        foreach (var r in e.Redes.Where(r => r.Conectada)) Nombre(r.Ssid);
        return e with
        {
            Actual = e.Actual == null ? null : e.Actual with { Ssid = Nombre(e.Actual.Ssid) },
            Redes = e.Redes.Select(r => r with { Ssid = Nombre(r.Ssid) }).ToList()
        };
    }

    public static int Canal(int mhz) =>
        mhz == 2484 ? 14 :
        mhz is >= 2412 and <= 2472 ? (mhz - 2407) / 5 :
        mhz is >= 5000 and < 5925 ? (mhz - 5000) / 5 :
        mhz is >= 5925 and <= 7125 ? (mhz - 5950) / 5 : 0;

    public static string Banda(int mhz) => mhz <= 0 ? "" : mhz < 3000 ? Tx("2,4 GHz") : mhz < 5925 ? "5 GHz" : "6 GHz";

    /// <summary>Una fila por nombre de red (con la señal del punto de acceso más fuerte).</summary>
    public static List<RedWifi> Agrupar(IEnumerable<RedWifi> redes) =>
        redes.GroupBy(r => r.Ssid)
             .Select(g => g.OrderByDescending(r => r.SenalPct).First() with { Conectada = g.Any(r => r.Conectada) })
             .OrderByDescending(r => r.Conectada).ThenByDescending(r => r.SenalPct)
             .ToList();
}

/// <summary>Wi-Fi en Windows mediante la API nativa (wlanapi.dll): no depende del idioma de Windows.</summary>
[SupportedOSPlatform("windows")]
internal static class WifiWindows
{
    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(int version, IntPtr reservado, out int negociada, out IntPtr cliente);
    [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr cliente, IntPtr reservado);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr cliente, IntPtr reservado, out IntPtr lista);
    [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr cliente, ref Guid interfaz, IntPtr ssid, IntPtr ie, IntPtr reservado);
    [DllImport("wlanapi.dll")] static extern int WlanGetNetworkBssList(IntPtr cliente, ref Guid interfaz, IntPtr ssid, int tipoBss, int seguridad, IntPtr reservado, out IntPtr lista);
    [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr cliente, ref Guid interfaz, int opcode, IntPtr reservado, out int tam, out IntPtr datos, IntPtr tipoValor);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr memoria);

    const int ErrorAccesoDenegado = 5;
    const int ErrorServicioParado = 1062;
    const int TamEntradaBss = 360;

    public static Task<EstadoWifi> EstadoAsync(bool escanear) => Task.Run(async () =>
    {
        int err;
        IntPtr cliente;
        try { err = WlanOpenHandle(2, IntPtr.Zero, out _, out cliente); }
        catch (DllNotFoundException) { return new EstadoWifi(false, null, null, new()); }

        // Sin servicio WLAN (habitual en sobremesas sin Wi-Fi): no hay adaptador.
        if (err == ErrorServicioParado) return new EstadoWifi(false, null, null, new());
        if (err != 0) return new EstadoWifi(false, Tf("No se pudo acceder al Wi-Fi (error {0}).", err), null, new());

        try
        {
            if (WlanEnumInterfaces(cliente, IntPtr.Zero, out var lista) != 0) return new EstadoWifi(false, null, null, new());
            int interfaces = Marshal.ReadInt32(lista);
            if (interfaces == 0)
            {
                WlanFreeMemory(lista);
                return new EstadoWifi(false, null, null, new());
            }
            // WLAN_INTERFACE_INFO: GUID (16) + descripción (512) + estado (4)
            var guid = Marshal.PtrToStructure<Guid>(lista + 8);
            int estadoInterfaz = Marshal.ReadInt32(lista + 8 + 16 + 512);
            WlanFreeMemory(lista);

            ConexionWifi? actual = null;
            var bssidActual = "";
            if (estadoInterfaz == 1 && WlanQueryInterface(cliente, ref guid, 7, IntPtr.Zero, out _, out var conexion, IntPtr.Zero) == 0)
            {
                // WLAN_CONNECTION_ATTRIBUTES: la asociación empieza en el byte 520.
                var ssid = LeerSsid(conexion + 520);
                bssidActual = LeerMac(conexion + 560);
                int calidad = Marshal.ReadInt32(conexion + 576);
                int rxKbps = Marshal.ReadInt32(conexion + 580);
                WlanFreeMemory(conexion);
                actual = new ConexionWifi(ssid, calidad, rxKbps > 0 ? rxKbps / 1000 : null, "");
            }

            if (escanear)
            {
                WlanScan(cliente, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(4500);
            }

            var redes = new List<RedWifi>();
            err = WlanGetNetworkBssList(cliente, ref guid, IntPtr.Zero, 3, 0, IntPtr.Zero, out var bss);
            if (err == ErrorAccesoDenegado)
                return new EstadoWifi(true,
                    Tx("Windows no deja ver las redes cercanas. Actívalo en Configuración › Privacidad y seguridad › Ubicación › «Permitir que las aplicaciones de escritorio accedan a tu ubicación»."),
                    actual, redes);
            if (err == 0)
            {
                int n = Marshal.ReadInt32(bss + 4);
                for (int k = 0; k < n; k++)
                {
                    var e = bss + 8 + k * TamEntradaBss;
                    var ssid = LeerSsid(e);
                    var bssid = LeerMac(e + 40);
                    int rssi = Marshal.ReadInt32(e + 56);
                    int calidad = Marshal.ReadInt32(e + 60);
                    int mhz = Marshal.ReadInt32(e + 92) / 1000;
                    redes.Add(new RedWifi(ssid.Length == 0 ? Tx("(red oculta)") : ssid, calidad, rssi, Wifi.Canal(mhz), Wifi.Banda(mhz),
                        bssidActual.Length > 0 && bssid == bssidActual));
                }
                WlanFreeMemory(bss);
            }

            if (actual != null)
            {
                var propia = redes.FirstOrDefault(r => r.Conectada) ?? redes.Where(r => r.Ssid == actual.Ssid).MaxBy(r => r.SenalPct);
                if (propia != null) actual = actual with { Banda = propia.Banda };
            }
            return new EstadoWifi(true, null, actual, Wifi.Agrupar(redes));
        }
        finally
        {
            WlanCloseHandle(cliente, IntPtr.Zero);
        }
    });

    static string LeerSsid(IntPtr p)
    {
        int largo = Math.Clamp(Marshal.ReadInt32(p), 0, 32);
        var bytes = new byte[largo];
        Marshal.Copy(p + 4, bytes, 0, largo);
        return Encoding.UTF8.GetString(bytes);
    }

    static string LeerMac(IntPtr p)
    {
        var b = new byte[6];
        Marshal.Copy(p, b, 0, 6);
        return string.Join(":", b.Select(x => x.ToString("X2")));
    }
}

/// <summary>Wi-Fi en Linux mediante NetworkManager (nmcli), presente en casi todas las distribuciones de escritorio.</summary>
[SupportedOSPlatform("linux")]
internal static class WifiLinux
{
    public static async Task<EstadoWifi> EstadoAsync(bool escanear)
    {
        bool hay = Directory.Exists("/sys/class/net") &&
                   Directory.GetDirectories("/sys/class/net").Any(d =>
                       Directory.Exists(Path.Combine(d, "wireless")) || Directory.Exists(Path.Combine(d, "phy80211")));
        if (!hay) return new EstadoWifi(false, null, null, new());

        var salida = await Plataforma.EjecutarAsync("nmcli",
            $"-t -f IN-USE,SSID,SIGNAL,FREQ,RATE device wifi list --rescan {(escanear ? "yes" : "no")}", 25000);
        if (salida == null)
            return new EstadoWifi(true, Tx("Para ver las redes Wi-Fi hace falta NetworkManager (la orden nmcli)."), null, new());

        var redes = new List<RedWifi>();
        ConexionWifi? actual = null;
        foreach (var linea in salida.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = DividirTerse(linea);
            if (c.Count < 5) continue;
            bool enUso = c[0] == "*";
            var ssid = c[1].Length == 0 ? Tx("(red oculta)") : c[1];
            int.TryParse(c[2], out var senal);
            int.TryParse(c[3].Split(' ')[0], out var mhz);
            int.TryParse(c[4].Split(' ')[0], out var velocidad);
            redes.Add(new RedWifi(ssid, senal, null, Wifi.Canal(mhz), Wifi.Banda(mhz), enUso));
            if (enUso) actual = new ConexionWifi(ssid, senal, velocidad > 0 ? velocidad : null, Wifi.Banda(mhz));
        }
        return new EstadoWifi(true, null, actual, Wifi.Agrupar(redes));
    }

    /// <summary>Divide una línea de "nmcli -t" por ':' respetando los '\:' escapados.</summary>
    static List<string> DividirTerse(string linea)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        for (int i = 0; i < linea.Length; i++)
        {
            if (linea[i] == '\\' && i + 1 < linea.Length) actual.Append(linea[++i]);
            else if (linea[i] == ':') { campos.Add(actual.ToString()); actual.Clear(); }
            else actual.Append(linea[i]);
        }
        campos.Add(actual.ToString());
        return campos;
    }
}
