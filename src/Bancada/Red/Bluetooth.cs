using System.Runtime.Versioning;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Red;

public sealed record EstadoBluetooth(bool Hay, string Nombre, string Estado, Veredicto Resultado);

public static class Bluetooth
{
    public static Task<EstadoBluetooth> DetectarAsync() => Task.Run(() =>
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Windows();
            if (OperatingSystem.IsLinux()) return Linux();
        }
        catch { }
        return new EstadoBluetooth(false, "", Tx("No se ha podido comprobar"), Veredicto.Pendiente);
    });

    [SupportedOSPlatform("windows")]
    static EstadoBluetooth Windows()
    {
        foreach (var o in DetectorWindows.Wmi("SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'"))
        {
            var pnp = DetectorWindows.T(o["PNPDeviceID"]);
            // Solo la radio física; el resto son servicios y dispositivos emparejados.
            if (!pnp.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
            var nombre = TextoEquipo.Hardware(DetectorWindows.T(o["Name"]));
            var codigo = (int)DetectorWindows.L(o["ConfigManagerErrorCode"]);
            return codigo switch
            {
                0 => new EstadoBluetooth(true, nombre, Tx("Detectado y funcionando"), Veredicto.Ok),
                22 => new EstadoBluetooth(true, nombre, Tx("Desactivado en el Administrador de dispositivos"), Veredicto.Aviso),
                28 => new EstadoBluetooth(true, nombre, Tx("Sin controlador instalado"), Veredicto.Aviso),
                _ => new EstadoBluetooth(true, nombre, Tf("Con error (código {0})", codigo), Veredicto.Fallo)
            };
        }
        return new EstadoBluetooth(false, "", Tx("No se ha encontrado adaptador Bluetooth"), Veredicto.Pendiente);
    }

    [SupportedOSPlatform("linux")]
    static EstadoBluetooth Linux()
    {
        const string bt = "/sys/class/bluetooth";
        var hci = Directory.Exists(bt) ? Directory.GetDirectories(bt, "hci*").Order().FirstOrDefault() : null;
        if (hci == null) return new EstadoBluetooth(false, "", Tx("No se ha encontrado adaptador Bluetooth"), Veredicto.Pendiente);

        var producto = Plataforma.Leer(Path.Combine(hci, "device", "..", "product"));
        var fabricante = Plataforma.Leer(Path.Combine(hci, "device", "..", "manufacturer"));
        var nombre = producto != null ? $"{fabricante} {producto}".Trim() : Path.GetFileName(hci);

        if (Directory.Exists("/sys/class/rfkill"))
        {
            foreach (var rf in Directory.GetDirectories("/sys/class/rfkill"))
            {
                if (Plataforma.Leer(Path.Combine(rf, "type")) != "bluetooth") continue;
                if (Plataforma.Leer(Path.Combine(rf, "hard")) == "1")
                    return new EstadoBluetooth(true, nombre, Tx("Bloqueado por el interruptor o la BIOS"), Veredicto.Aviso);
                if (Plataforma.Leer(Path.Combine(rf, "soft")) == "1")
                    return new EstadoBluetooth(true, nombre, Tx("Desactivado en el sistema (rfkill)"), Veredicto.Aviso);
            }
        }
        return new EstadoBluetooth(true, nombre, Tx("Detectado y funcionando"), Veredicto.Ok);
    }
}
