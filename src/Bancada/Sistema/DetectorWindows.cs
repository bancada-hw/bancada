using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Bancada.Modelo;

namespace Bancada.Sistema;

[SupportedOSPlatform("windows")]
internal static class DetectorWindows
{
    public static List<ManagementBaseObject> Wmi(string consulta, string ambito = @"root\cimv2")
    {
        var lista = new List<ManagementBaseObject>();
        try
        {
            using var buscador = new ManagementObjectSearcher(ambito, consulta);
            foreach (var o in buscador.Get()) lista.Add(o);
        }
        catch { /* clase no disponible o sin permisos: se deja vacío */ }
        return lista;
    }

    public static string T(object? v) => v?.ToString()?.Trim() ?? "";

    public static long L(object? v)
    {
        try { return v == null ? 0 : Convert.ToInt64(v); }
        catch { return 0; }
    }

    public static InfoEquipo Detectar()
    {
        var i = new InfoEquipo();

        foreach (var o in Wmi("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem"))
        {
            i.Marca = TextoEquipo.Marca(T(o["Manufacturer"]));
            i.Modelo = TextoEquipo.Limpio(T(o["Model"]));
            i.RamTotalBytes = L(o["TotalPhysicalMemory"]);
        }

        // Lenovo guarda el nombre comercial (p. ej. "ThinkPad T14 Gen 3") aparte del tipo de máquina.
        foreach (var o in Wmi("SELECT Version FROM Win32_ComputerSystemProduct"))
        {
            var comercial = TextoEquipo.Limpio(T(o["Version"]));
            if (i.Marca == "Lenovo" && comercial.Length > 0 && !comercial.Equals(i.Modelo, StringComparison.OrdinalIgnoreCase))
                i.Modelo = i.Modelo.Length > 0 ? $"{comercial} ({i.Modelo})" : comercial;
        }

        foreach (var o in Wmi("SELECT SerialNumber, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            i.NumeroSerie = TextoEquipo.Limpio(T(o["SerialNumber"]));
            var fecha = T(o["ReleaseDate"]);
            i.Bios = T(o["SMBIOSBIOSVersion"]) + (fecha.Length >= 8 ? $" · {fecha[6..8]}/{fecha[4..6]}/{fecha[..4]}" : "");
        }

        foreach (var o in Wmi("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            i.Placa = $"{TextoEquipo.Marca(T(o["Manufacturer"]))} {TextoEquipo.Limpio(T(o["Product"]))}".Trim();

        var chasis = new List<int>();
        foreach (var o in Wmi("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            if (o["ChassisTypes"] is ushort[] tipos) chasis.AddRange(tipos.Select(t => (int)t));

        foreach (var o in Wmi("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
        {
            i.Procesador = TextoEquipo.Procesador(T(o["Name"]));
            i.NucleosFisicos += (int)L(o["NumberOfCores"]);
            i.HilosLogicos += (int)L(o["NumberOfLogicalProcessors"]);
        }

        foreach (var o in Wmi("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
        {
            var n = (int)L(o["MemoryDevices"]);
            if (n > 0) i.RanurasRam = (i.RanurasRam ?? 0) + n;
        }

        foreach (var o in Wmi("SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory"))
        {
            var configurada = L(o["ConfiguredClockSpeed"]);
            i.ModulosRam.Add(new ModuloRam(
                T(o["DeviceLocator"]),
                L(o["Capacity"]),
                (int)(configurada > 0 ? configurada : L(o["Speed"])),
                TextoEquipo.Limpio(T(o["Manufacturer"])),
                TextoEquipo.Limpio(T(o["PartNumber"]))));
        }

        foreach (var o in Wmi("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
        {
            var pnp = T(o["PNPDeviceID"]);
            // Fuera los adaptadores virtuales (escritorio remoto, Hyper-V…), que no son hardware.
            if (pnp.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) || pnp.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase)) continue;
            var nombre = TextoEquipo.Hardware(T(o["Name"]));
            if (nombre.Length > 0) i.Graficas.Add(nombre);
        }

        foreach (var o in Wmi("SELECT FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
        {
            var conexion = L(o["BusType"]) switch
            {
                17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS",
                12 => "SD", 13 => "eMMC", 3 => "ATA", 1 => "SCSI", _ => ""
            };
            var tipo = L(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => conexion == "NVMe" ? "SSD" : "" };
            var salud = L(o["HealthStatus"]) switch { 0 => "Correcta", 1 => "Aviso", 2 => "Mala", _ => "" };
            i.Discos.Add(new Disco(T(o["FriendlyName"]), L(o["Size"]), tipo, conexion, salud));
        }

        var baterias = Wmi("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        if (baterias.Count > 0)
        {
            var b = baterias[0];
            var estado = L(b["BatteryStatus"]) switch
            {
                1 => Tx("Descargando"),
                2 => Tx("Enchufado"),
                3 => Tx("Carga completa"),
                4 => Tx("Carga baja"),
                5 => Tx("Carga crítica"),
                6 or 7 or 8 or 9 => Tx("Cargando"),
                _ => ""
            };
            long diseno = 0, llena = 0;
            int? ciclos = null;
            foreach (var o in Wmi("SELECT DesignedCapacity FROM BatteryStaticData", @"root\wmi")) diseno = L(o["DesignedCapacity"]);
            foreach (var o in Wmi("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\wmi")) llena = L(o["FullChargedCapacity"]);
            foreach (var o in Wmi("SELECT CycleCount FROM BatteryCycleCount", @"root\wmi"))
            {
                var c = (int)L(o["CycleCount"]);
                if (c > 0) ciclos = c;
            }
            int? salud = diseno > 0 && llena > 0 ? (int)Math.Round(llena * 100.0 / diseno) : null;
            // Sin permisos de administrador, WMI no da la capacidad: se pregunta directamente al controlador de la batería.
            if (salud == null || ciclos == null)
            {
                var directa = EnergiaWindows.Leer().Baterias.FirstOrDefault();
                salud ??= directa?.SaludPct;
                ciclos ??= directa?.Ciclos;
            }
            i.Bateria = new Bateria((int)L(b["EstimatedChargeRemaining"]), estado, salud, ciclos);
        }

        foreach (var o in Wmi("SELECT Caption, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
            i.SistemaOperativo = Tf("{0} · compilación {1}", T(o["Caption"]).Replace("Microsoft ", ""), T(o["BuildNumber"]));

        // Windows descuenta la memoria reservada (gráficos integrados, firmware): con los módulos se sabe la instalada.
        var instalada = i.ModulosRam.Sum(m => m.Bytes);
        if (instalada > 0) i.RamTotalBytes = instalada;

        i.EsVirtual = Regex.IsMatch($"{i.Marca} {i.Modelo}", "virtual|vmware|kvm|qemu|hyper-v|virtualbox", RegexOptions.IgnoreCase);
        if (i.ModulosRam.Count == 0)
            i.Notas.Add(Tx("Windows no muestra los módulos de RAM sin permisos de administrador."));
        i.Tipo = TextoEquipo.DesdeChasis(chasis, i.Bateria != null);
        return i;
    }
}
