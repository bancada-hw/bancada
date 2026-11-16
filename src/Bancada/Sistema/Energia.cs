using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Bancada.Sistema;

public sealed record InfoBateria(
    string Nombre, string Fabricante, string Quimica,
    double? DisenoWh, double? LlenaWh, double? RestanteWh, int? Ciclos)
{
    public int? SaludPct => DisenoWh > 0 && LlenaWh > 0 ? (int)Math.Round(LlenaWh.Value * 100 / DisenoWh.Value) : null;
}

/// <summary>Estado de la alimentación. PotenciaW es positiva al cargar y negativa al descargar.</summary>
public sealed record EstadoEnergia(
    List<InfoBateria> Baterias, int? CargaPct, bool Enchufado, bool Cargando, bool Descargando,
    double? PotenciaW, double? VoltajeV, TimeSpan? Restante)
{
    public bool HayBateria => Baterias.Count > 0;
    public double? RestanteWh => Baterias.Any(b => b.RestanteWh != null) ? Baterias.Sum(b => b.RestanteWh ?? 0) : null;
    public double? LlenaWh => Baterias.Any(b => b.LlenaWh != null) ? Baterias.Sum(b => b.LlenaWh ?? 0) : null;
    public double? DisenoWh => Baterias.Any(b => b.DisenoWh != null) ? Baterias.Sum(b => b.DisenoWh ?? 0) : null;
}

internal static class Energia
{
    public static EstadoEnergia Leer()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return EnergiaWindows.Leer();
            if (OperatingSystem.IsLinux()) return LeerLinux();
        }
        catch { }
        return new EstadoEnergia(new(), null, true, false, false, null, null, null);
    }

    /// <summary>Si el modo de ahorro de batería del sistema está activo (cambia el consumo durante la prueba). Null si no se sabe.</summary>
    public static bool? AhorroActivo()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return EnergiaWindows.Ahorro();
            if (!OperatingSystem.IsLinux()) return null;
            if (Plataforma.Buscar("powerprofilesctl") is string ppd && Plataforma.Ejecutar(ppd, "get", 5000) is string perfil)
                return perfil.Trim() == "power-saver";
            if (Plataforma.Leer("/sys/firmware/acpi/platform_profile") is string fw)
                return fw is "low-power" or "quiet";
        }
        catch { }
        return null;
    }

    // ---------- Linux: /sys/class/power_supply ----------

    static EstadoEnergia LeerLinux()
    {
        const string raiz = "/sys/class/power_supply";
        var baterias = new List<InfoBateria>();
        bool enchufado = false, cargando = false, descargando = false;
        double potencia = 0, voltaje = 0;
        bool hayPotencia = false;
        int cargaSuma = 0, cargaN = 0;
        if (!Directory.Exists(raiz)) return new EstadoEnergia(baterias, null, true, false, false, null, null, null);

        foreach (var dir in Directory.GetDirectories(raiz).Order())
        {
            string? L(string f) => Plataforma.Leer(Path.Combine(dir, f));
            double? N(string f) => double.TryParse(L(f), out var v) ? v : null;
            if (L("scope") == "Device") continue; // ratones, mandos…
            var tipo = L("type");
            if (tipo is "Mains" or "USB" or "USB_C" or "USB_PD")
            {
                if (L("online") == "1") enchufado = true;
                continue;
            }
            if (tipo != "Battery" || L("present") == "0") continue;

            var estado = L("status") ?? "";
            if (estado == "Charging") cargando = true;
            if (estado == "Discharging") descargando = true;
            if (estado is "Charging" or "Full" or "Not charging") enchufado = true;

            // Energía en µWh; si solo hay carga (µAh), se pasa a energía con el voltaje de diseño.
            double vDiseno = (N("voltage_min_design") ?? N("voltage_now") ?? 0) / 1e6;
            double? Wh(string energia, string carga) =>
                N(energia) is double e ? e / 1e6 : N(carga) is double c && vDiseno > 0 ? c / 1e6 * vDiseno : null;
            var info = new InfoBateria(
                L("model_name") ?? Path.GetFileName(dir),
                L("manufacturer") ?? "",
                L("technology") ?? "",
                Wh("energy_full_design", "charge_full_design"),
                Wh("energy_full", "charge_full"),
                Wh("energy_now", "charge_now"),
                int.TryParse(L("cycle_count"), out var ciclos) && ciclos > 0 ? ciclos : null);
            baterias.Add(info);

            if (int.TryParse(L("capacity"), out var pct)) { cargaSuma += pct; cargaN++; }
            var v = N("voltage_now") / 1e6;
            if (v > 0) voltaje = v.Value;
            double? w = N("power_now") is double pw ? pw / 1e6 : N("current_now") is double ia && v > 0 ? ia / 1e6 * v.Value : null;
            if (w != null)
            {
                hayPotencia = true;
                potencia += estado == "Discharging" ? -Math.Abs(w.Value) : estado == "Charging" ? Math.Abs(w.Value) : 0;
            }
        }

        int? carga = cargaN > 0 ? cargaSuma / cargaN : null;
        TimeSpan? restante = null;
        var restanteWh = baterias.Sum(b => b.RestanteWh ?? 0);
        if (descargando && potencia < -0.5 && restanteWh > 0)
            restante = TimeSpan.FromHours(restanteWh / -potencia);
        return new EstadoEnergia(baterias, carga, enchufado || baterias.Count == 0, cargando, descargando,
            hayPotencia ? potencia : null, voltaje > 0 ? voltaje : null, restante);
    }
}

/// <summary>Windows: consultas directas al controlador de batería. No hacen falta permisos de administrador.</summary>
[SupportedOSPlatform("windows")]
internal static class EnergiaWindows
{
    static readonly Guid ClaseBateria = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    const uint ConsultarEtiqueta = 0x294040, ConsultarInfo = 0x294044, ConsultarEstado = 0x29404C;

    public static EstadoEnergia Leer()
    {
        var baterias = new List<InfoBateria>();
        bool cargando = false, descargando = false, enchufadoBat = false;
        double potencia = 0, voltaje = 0;
        bool hayPotencia = false;

        foreach (var ruta in RutasBaterias())
        {
            using var h = CreateFile(ruta, 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (h.IsInvalid) continue;
            uint espera = 0;
            if (!DeviceIoControl(h, ConsultarEtiqueta, ref espera, 4, out uint etiqueta, 4, out _, IntPtr.Zero) || etiqueta == 0) continue;

            var info = new InfoBat();
            var q = new Consulta { Etiqueta = etiqueta, Nivel = 0 };
            if (!DeviceIoControl(h, ConsultarInfo, ref q, Marshal.SizeOf<Consulta>(), out info, Marshal.SizeOf<InfoBat>(), out _, IntPtr.Zero)) continue;
            bool relativa = (info.Capacidades & 0x40000000) != 0;

            var esperaEstado = new EsperaEstado { Etiqueta = etiqueta };
            DeviceIoControl(h, ConsultarEstado, ref esperaEstado, Marshal.SizeOf<EsperaEstado>(), out EstadoBat est, Marshal.SizeOf<EstadoBat>(), out _, IntPtr.Zero);

            double? Wh(uint mwh) => relativa || mwh == 0 || mwh == 0xFFFFFFFF ? null : mwh / 1000.0;
            var quimica = System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(info.Quimica)).TrimEnd('\0', ' ');
            baterias.Add(new InfoBateria(
                Texto(h, etiqueta, 4) ?? Tx("Batería"),
                Texto(h, etiqueta, 6) ?? "",
                quimica switch { "LION" or "Li-I" => Tx("Ion de litio"), "LiP" or "LIPO" => Tx("Polímero de litio"), "PbAc" => Tx("Plomo-ácido"), "NiMH" => "NiMH", "NiCd" => "NiCd", var x => x },
                Wh(info.CapacidadDiseno), Wh(info.CapacidadLlena),
                Wh(est.Capacidad),
                info.Ciclos > 0 ? (int)info.Ciclos : null));

            if ((est.Estado & 1) != 0) enchufadoBat = true;
            if ((est.Estado & 2) != 0) descargando = true;
            if ((est.Estado & 4) != 0) cargando = true;
            if (est.Voltaje is > 0 and not 0xFFFFFFFF) voltaje = est.Voltaje / 1000.0;
            if (est.Potencia != unchecked((int)0x80000000) && !relativa)
            {
                hayPotencia = true;
                potencia += est.Potencia / 1000.0;
            }
        }

        GetSystemPowerStatus(out var sps);
        bool enchufado = sps.Red == 1 || enchufadoBat || baterias.Count == 0;
        int? carga = sps.Porcentaje <= 100 ? sps.Porcentaje : null;
        TimeSpan? restante = sps.SegundosRestantes > 0 && !enchufado ? TimeSpan.FromSeconds(sps.SegundosRestantes) : null;
        var restanteWh = baterias.Sum(b => b.RestanteWh ?? 0);
        if (restante == null && descargando && potencia < -0.5 && restanteWh > 0) restante = TimeSpan.FromHours(restanteWh / -potencia);
        return new EstadoEnergia(baterias, carga, enchufado, cargando, descargando && !enchufado,
            hayPotencia ? potencia : null, voltaje > 0 ? voltaje : null, restante);
    }

    /// <summary>Ahorro de batería de Windows (por defecto se activa solo al 20 %).</summary>
    public static bool Ahorro() => GetSystemPowerStatus(out var s) && s.Bandera == 1;

    static string? Texto(SafeFileHandle h, uint etiqueta, int nivel)
    {
        var q = new Consulta { Etiqueta = etiqueta, Nivel = nivel };
        var buf = new char[128];
        if (!DeviceIoControl(h, ConsultarInfo, ref q, Marshal.SizeOf<Consulta>(), buf, buf.Length * 2, out var bytes, IntPtr.Zero)) return null;
        var s = new string(buf, 0, (int)bytes / 2).TrimEnd('\0').Trim();
        return s.Length > 0 ? s : null;
    }

    static List<string> RutasBaterias()
    {
        var rutas = new List<string>();
        var clase = ClaseBateria;
        var lista = SetupDiGetClassDevs(ref clase, IntPtr.Zero, IntPtr.Zero, 0x2 | 0x10);
        if (lista == new IntPtr(-1)) return rutas;
        try
        {
            for (uint i = 0; i < 8; i++)
            {
                var datos = new DatosInterfaz { Tam = Marshal.SizeOf<DatosInterfaz>() };
                if (!SetupDiEnumDeviceInterfaces(lista, IntPtr.Zero, ref clase, i, ref datos)) break;
                SetupDiGetDeviceInterfaceDetail(lista, ref datos, IntPtr.Zero, 0, out var necesario, IntPtr.Zero);
                if (necesario == 0) continue;
                var detalle = Marshal.AllocHGlobal((int)necesario);
                try
                {
                    Marshal.WriteInt32(detalle, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(lista, ref datos, detalle, necesario, out _, IntPtr.Zero))
                        rutas.Add(Marshal.PtrToStringUni(detalle + 4) ?? "");
                }
                finally { Marshal.FreeHGlobal(detalle); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(lista); }
        return rutas.Where(r => r.Length > 0).ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DatosInterfaz { public int Tam; public Guid Clase; public int Flags; public IntPtr Reservado; }

    [StructLayout(LayoutKind.Sequential)]
    struct Consulta { public uint Etiqueta; public int Nivel; public int ARitmo; }

    [StructLayout(LayoutKind.Sequential)]
    struct InfoBat
    {
        public uint Capacidades;
        public byte Tecnologia, R1, R2, R3;
        public uint Quimica;
        public uint CapacidadDiseno, CapacidadLlena, Alerta1, Alerta2, Sesgo, Ciclos;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EsperaEstado { public uint Etiqueta, Espera, Estado, Baja, Alta; }

    [StructLayout(LayoutKind.Sequential)]
    struct EstadoBat { public uint Estado, Capacidad, Voltaje; public int Potencia; }

    [StructLayout(LayoutKind.Sequential)]
    struct EstadoSistema { public byte Red, Bandera, Porcentaje, Ahorro; public int SegundosRestantes, SegundosTotales; }

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid clase, IntPtr enumerador, IntPtr ventana, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr lista, IntPtr info, ref Guid clase, uint indice, ref DatosInterfaz datos);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr lista, ref DatosInterfaz datos, IntPtr detalle, uint tam, out uint necesario, IntPtr info);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr lista);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string ruta, uint acceso, uint compartir, IntPtr seg, uint modo, uint flags, IntPtr plantilla);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, ref uint entrada, int tamEntrada, out uint salida, int tamSalida, out uint bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, ref Consulta entrada, int tamEntrada, out InfoBat salida, int tamSalida, out uint bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, ref Consulta entrada, int tamEntrada, [Out] char[] salida, int tamSalida, out uint bytes, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, ref EsperaEstado entrada, int tamEntrada, out EstadoBat salida, int tamSalida, out uint bytes, IntPtr ov);
    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out EstadoSistema estado);
}
