using System.Management;
using System.Runtime.Versioning;

namespace Bancada.Sistema;

/// <summary>
/// Brillo de la pantalla interna, de 0 a 100. Null o false cuando el equipo no deja leerlo o cambiarlo
/// (monitores externos, máquinas virtuales). No hacen falta permisos de administrador.
/// </summary>
internal static class Brillo
{
    public static int? Leer()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return LeerWindows();
            if (OperatingSystem.IsLinux()) return LeerLinux();
        }
        catch { }
        return null;
    }

    public static bool Fijar(int pct)
    {
        pct = Math.Clamp(pct, 1, 100);
        try
        {
            if (OperatingSystem.IsWindows()) return FijarWindows(pct);
            if (OperatingSystem.IsLinux()) return FijarLinux(pct);
        }
        catch { }
        return false;
    }

    // ---------- Windows: WMI (solo pantallas internas) ----------

    [SupportedOSPlatform("windows")]
    static int? LeerWindows()
    {
        using var buscador = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
        foreach (var o in buscador.Get())
            using (o)
                return Convert.ToInt32(o["CurrentBrightness"]);
        return null;
    }

    [SupportedOSPlatform("windows")]
    static bool FijarWindows(int pct)
    {
        bool hecho = false;
        using var buscador = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
        foreach (ManagementObject o in buscador.Get())
            using (o)
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct });
                hecho = true;
            }
        return hecho;
    }

    // ---------- Linux: /sys/class/backlight; se cambia a través de logind (sin root) ----------

    static string? Dispositivo()
    {
        const string raiz = "/sys/class/backlight";
        if (!Directory.Exists(raiz)) return null;
        // El mismo orden que usan los escritorios: firmware (ACPI) > platform > raw.
        int Orden(string d) => Plataforma.Leer(Path.Combine(d, "type")) switch { "firmware" => 0, "platform" => 1, _ => 2 };
        return Directory.GetDirectories(raiz).OrderBy(Orden).FirstOrDefault();
    }

    static int? LeerLinux()
    {
        var d = Dispositivo();
        if (d == null) return null;
        if (!double.TryParse(Plataforma.Leer(Path.Combine(d, "max_brightness")), out var max) || max <= 0) return null;
        if (!double.TryParse(Plataforma.Leer(Path.Combine(d, "brightness")), out var actual)) return null;
        return (int)Math.Round(actual * 100 / max);
    }

    static bool FijarLinux(int pct)
    {
        var d = Dispositivo();
        if (d == null || !double.TryParse(Plataforma.Leer(Path.Combine(d, "max_brightness")), out var max) || max <= 0) return false;
        var nombre = Path.GetFileName(d);
        var valor = Math.Max(1, (int)Math.Round(max * pct / 100));

        if (Plataforma.Buscar("busctl") is string busctl &&
            Plataforma.Ejecutar(busctl, $"call org.freedesktop.login1 /org/freedesktop/login1/session/auto org.freedesktop.login1.Session SetBrightness ssu backlight {nombre} {valor}", 5000) != null)
            return true;
        if (Plataforma.Buscar("brightnessctl") is string bc && Plataforma.Ejecutar(bc, $"-d {nombre} set {valor}", 5000) != null)
            return true;
        try
        {
            File.WriteAllText(Path.Combine(d, "brightness"), valor.ToString());
            return true;
        }
        catch { return false; }
    }
}
