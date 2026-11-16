using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Bancada.Modelo;

namespace Bancada.Sistema;

[SupportedOSPlatform("linux")]
internal static class DetectorLinux
{
    const string Dmi = "/sys/class/dmi/id/";

    static string D(string campo) => Plataforma.Leer(Dmi + campo) ?? "";

    public static InfoEquipo Detectar()
    {
        var i = new InfoEquipo();
        bool admin = Plataforma.EsAdministrador;

        i.Marca = TextoEquipo.Marca(D("sys_vendor"));
        var producto = TextoEquipo.Limpio(D("product_name"));
        var version = TextoEquipo.Limpio(D("product_version"));
        // En Lenovo, product_version es el nombre comercial ("ThinkPad T14 Gen 3").
        i.Modelo = i.Marca == "Lenovo" && version.Length > 0 && !version.Equals(producto, StringComparison.OrdinalIgnoreCase)
            ? $"{version} ({producto})"
            : producto;

        var serie = Plataforma.Leer(Dmi + "product_serial");
        if (serie != null) i.NumeroSerie = TextoEquipo.Limpio(serie);
        else if (File.Exists(Dmi + "product_serial")) i.NumeroSerie = Tx("Visible solo con sudo");

        var fecha = D("bios_date"); // MM/DD/AAAA
        var partes = fecha.Split('/');
        i.Bios = D("bios_version") + (partes.Length == 3 ? $" · {partes[1]}/{partes[0]}/{partes[2]}" : "");
        i.Placa = $"{TextoEquipo.Marca(D("board_vendor"))} {TextoEquipo.Limpio(D("board_name"))}".Trim();

        LeerProcesador(i);
        LeerMemoria(i, admin);
        LeerGraficas(i);
        LeerDiscos(i);
        LeerBateria(i);

        var osRelease = Plataforma.Leer("/etc/os-release") ?? "";
        var nombre = Regex.Match(osRelease, "^PRETTY_NAME=\"?([^\"\\n]+)\"?", RegexOptions.Multiline).Groups[1].Value;
        var kernel = Plataforma.Leer("/proc/sys/kernel/osrelease") ?? "";
        i.SistemaOperativo = Tf("{0} · núcleo {1}", (nombre.Length > 0 ? nombre : "Linux"), kernel);

        int.TryParse(D("chassis_type"), out var chasis);
        i.Tipo = TextoEquipo.DesdeChasis(new[] { chasis }, i.Bateria != null);

        i.EsVirtual = Plataforma.EsWsl ||
                      Regex.IsMatch($"{i.Marca} {producto}", "virtual|vmware|kvm|qemu|virtualbox|hyper-v", RegexOptions.IgnoreCase);
        if (Plataforma.EsWsl)
            i.Notas.Add(Tx("Bancada se está ejecutando dentro de WSL: los datos son los de la máquina virtual, no los del equipo real."));
        if (!admin)
            i.Notas.Add(Tx("Ejecuta Bancada con sudo para ver el número de serie y los módulos de RAM."));
        return i;
    }

    static void LeerProcesador(InfoEquipo i)
    {
        var cpuinfo = Plataforma.Leer("/proc/cpuinfo") ?? "";
        var modelo = Regex.Match(cpuinfo, @"^model name\s*:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value;
        i.Procesador = TextoEquipo.Procesador(modelo);
        i.HilosLogicos = Regex.Matches(cpuinfo, @"^processor\s*:", RegexOptions.Multiline).Count;

        // Núcleos físicos: pares únicos (physical id, core id).
        var nucleos = new HashSet<string>();
        foreach (var bloque in cpuinfo.Split("\n\n"))
        {
            var fisico = Regex.Match(bloque, @"^physical id\s*:\s*(\d+)", RegexOptions.Multiline).Groups[1].Value;
            var nucleo = Regex.Match(bloque, @"^core id\s*:\s*(\d+)", RegexOptions.Multiline).Groups[1].Value;
            if (nucleo.Length > 0) nucleos.Add(fisico + ":" + nucleo);
        }
        i.NucleosFisicos = nucleos.Count > 0 ? nucleos.Count : i.HilosLogicos;
    }

    static void LeerMemoria(InfoEquipo i, bool admin)
    {
        var meminfo = Plataforma.Leer("/proc/meminfo") ?? "";
        if (long.TryParse(Regex.Match(meminfo, @"^MemTotal:\s*(\d+)", RegexOptions.Multiline).Groups[1].Value, out var kb))
            i.RamTotalBytes = kb * 1024;

        if (!admin) return;
        var dmi = Plataforma.Ejecutar("dmidecode", "-t 17");
        if (dmi == null) return;
        int ranuras = 0;
        foreach (var bloque in dmi.Split("\n\n"))
        {
            if (!bloque.Contains("Memory Device")) continue;
            ranuras++;
            string Campo(string nombre) => Regex.Match(bloque, $@"^\s*{nombre}:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            var tam = Campo("Size");
            var m = Regex.Match(tam, @"(\d+)\s*(GB|MB)");
            if (!m.Success) continue; // ranura vacía
            long bytes = long.Parse(m.Groups[1].Value) * (m.Groups[2].Value == "GB" ? 1073741824L : 1048576L);
            var velocidad = Regex.Match(Campo("Configured Memory Speed") + " " + Campo("Speed"), @"(\d+)").Groups[1].Value;
            i.ModulosRam.Add(new ModuloRam(Campo("Locator"), bytes, int.TryParse(velocidad, out var mhz) ? mhz : 0,
                TextoEquipo.Limpio(Campo("Manufacturer")), TextoEquipo.Limpio(Campo("Part Number"))));
        }
        if (ranuras > 0) i.RanurasRam = ranuras;
        var instalada = i.ModulosRam.Sum(m => m.Bytes);
        if (instalada > 0) i.RamTotalBytes = instalada;
    }

    static void LeerGraficas(InfoEquipo i)
    {
        const string drm = "/sys/class/drm";
        if (!Directory.Exists(drm)) return;
        var vistas = new HashSet<string>();
        foreach (var dir in Directory.GetDirectories(drm, "card*").Order())
        {
            if (Path.GetFileName(dir).Contains('-')) continue; // salidas de vídeo (card0-HDMI-A-1…)
            var vendor = Plataforma.Leer(Path.Combine(dir, "device", "vendor"));
            var device = Plataforma.Leer(Path.Combine(dir, "device", "device"));
            if (vendor == null || !vistas.Add(vendor + device)) continue;
            i.Graficas.Add(PciIds.Nombre(vendor, device));
        }
    }

    static void LeerDiscos(InfoEquipo i)
    {
        const string bloques = "/sys/block";
        if (!Directory.Exists(bloques)) return;
        foreach (var dir in Directory.GetDirectories(bloques).Order())
        {
            var nombre = Path.GetFileName(dir);
            if (Regex.IsMatch(nombre, @"^(loop|ram|zram|dm-|sr|md|fd|nbd)")) continue;
            if (!long.TryParse(Plataforma.Leer(Path.Combine(dir, "size")), out var sectores) || sectores == 0) continue;
            var modelo = Plataforma.Leer(Path.Combine(dir, "device", "model")) ?? nombre;
            var rotacional = Plataforma.Leer(Path.Combine(dir, "queue", "rotational")) == "1";
            string destino = "";
            try { destino = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName ?? ""; } catch { }
            var conexion = nombre.StartsWith("nvme") ? "NVMe" :
                           nombre.StartsWith("mmcblk") ? "eMMC/SD" :
                           destino.Contains("/usb") ? "USB" :
                           nombre.StartsWith("vd") || destino.Contains("/virtio") ? "Virtual" : "SATA";
            i.Discos.Add(new Disco(modelo, sectores * 512, rotacional ? "HDD" : "SSD", conexion, ""));
        }
    }

    static void LeerBateria(InfoEquipo i)
    {
        const string fuentes = "/sys/class/power_supply";
        if (!Directory.Exists(fuentes)) return;
        foreach (var dir in Directory.GetDirectories(fuentes))
        {
            if (Plataforma.Leer(Path.Combine(dir, "type")) != "Battery") continue;
            if (Plataforma.Leer(Path.Combine(dir, "scope")) == "Device") continue; // pilas de ratones, mandos…
            string? L(string f) => Plataforma.Leer(Path.Combine(dir, f));
            int? carga = int.TryParse(L("capacity"), out var c) ? c : null;
            var estado = L("status") switch
            {
                "Charging" => Tx("Cargando"),
                "Discharging" => Tx("Descargando"),
                "Full" => Tx("Carga completa"),
                "Not charging" => Tx("Enchufado, sin cargar"),
                _ => ""
            };
            long.TryParse(L("energy_full") ?? L("charge_full"), out var llena);
            long.TryParse(L("energy_full_design") ?? L("charge_full_design"), out var diseno);
            int? salud = llena > 0 && diseno > 0 ? (int)Math.Round(llena * 100.0 / diseno) : null;
            int? ciclos = int.TryParse(L("cycle_count"), out var cc) && cc > 0 ? cc : null;
            i.Bateria = new Bateria(carga, estado, salud, ciclos);
            return;
        }
    }
}
