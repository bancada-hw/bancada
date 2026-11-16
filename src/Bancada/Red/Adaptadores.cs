using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Bancada.Sistema;

namespace Bancada.Red;

public sealed record Adaptador(
    string Nombre, string Modelo, string Tipo, bool Conectado, long VelocidadBps,
    string Mac, string Ipv4, string PuertaEnlace, bool Fisico);

public static class Adaptadores
{
    public static List<Adaptador> Listar()
    {
        var fisicasWindows = OperatingSystem.IsWindows() ? FisicasWindows() : null;
        var lista = new List<Adaptador>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            // Interfaces de software en Linux (Docker, puentes, VPN…): nunca son una tarjeta.
            if (OperatingSystem.IsLinux() && System.Text.RegularExpressions.Regex.IsMatch(ni.Name,
                    "^(docker|veth|br-|virbr|vmnet|vboxnet|tailscale|tun|tap|wg|zt|lxc|lxd|cni|flannel|podman)")) continue;

            var mac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                        (OperatingSystem.IsLinux() && (Directory.Exists($"/sys/class/net/{ni.Name}/wireless") ||
                                                       Directory.Exists($"/sys/class/net/{ni.Name}/phy80211")));
            bool ethernet = !wifi && ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;

            bool fisico;
            string modelo = ni.Description;
            if (fisicasWindows != null)
            {
                // Se compara por GUID: los filtros de software (Npcap, QoS, WFP) repiten la MAC pero no el GUID.
                fisico = fisicasWindows.Contains(ni.Id);
            }
            else if (OperatingSystem.IsLinux())
            {
                var dispositivo = $"/sys/class/net/{ni.Name}/device";
                // Física = cuelga de un bus real; vmbus (Hyper-V/WSL) y virtio son virtuales.
                string bus = "";
                try { bus = Path.GetFileName(new DirectoryInfo(dispositivo + "/subsystem").ResolveLinkTarget(true)?.FullName ?? ""); }
                catch { }
                fisico = bus is "pci" or "usb" or "sdio" or "mmc";
                var vendor = Plataforma.Leer(dispositivo + "/vendor");
                if (vendor != null) modelo = PciIds.Nombre(vendor, Plataforma.Leer(dispositivo + "/device"));
            }
            else fisico = true;

            long velocidad = -1;
            try { velocidad = ni.Speed; } catch { }

            string ip = "", puerta = "";
            try
            {
                var props = ni.GetIPProperties();
                ip = string.Join(", ", props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()));
                puerta = props.GatewayAddresses.Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString() ?? "";
            }
            catch { }

            lista.Add(new Adaptador(ni.Name, Modelo.TextoEquipo.Hardware(modelo), wifi ? "Wi-Fi" : ethernet ? "Ethernet" : Tx("Otro"),
                ni.OperationalStatus == OperationalStatus.Up, velocidad, mac, ip, puerta, fisico));
        }

        // Si hay tarjetas reales, se ocultan las virtuales (Hyper-V, VPN, máquinas virtuales…).
        if (lista.Any(a => a.Fisico)) lista = lista.Where(a => a.Fisico).ToList();
        return lista.OrderByDescending(a => a.Conectado).ThenBy(a => a.Tipo).ToList();
    }

    /// <summary>GUID de las tarjetas de red físicas (PCI, USB, SDIO), según Windows.</summary>
    [SupportedOSPlatform("windows")]
    static HashSet<string> FisicasWindows()
    {
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in DetectorWindows.Wmi("SELECT GUID, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
        {
            var pnp = DetectorWindows.T(o["PNPDeviceID"]);
            var guid = DetectorWindows.T(o["GUID"]);
            if (guid.Length == 0) continue;
            if (pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
                pnp.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                pnp.StartsWith("SD\\", StringComparison.OrdinalIgnoreCase))
                guids.Add(guid);
        }
        return guids;
    }
}
