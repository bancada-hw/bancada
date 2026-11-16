using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bancada.Modelo;
using Microsoft.Win32.SafeHandles;

namespace Bancada.Sistema;

public sealed class SaludDisco
{
    public string Modelo { get; init; } = "";
    public string Tipo { get; init; } = "";
    public string Conexion { get; init; } = "";
    public long Bytes { get; init; }
    public string Firmware { get; set; } = "";
    public Veredicto Estado { get; set; } = Veredicto.Pendiente;
    public string EstadoTexto { get; set; } = Tx("Sin datos de salud");
    public int? TemperaturaC { get; set; }
    public int? VidaUsadaPct { get; set; }
    public int? HorasEncendido { get; set; }
    public long? EscritoBytes { get; set; }
    public long? Reasignados { get; set; }
    public long? Pendientes { get; set; }
    public long? ErroresMedio { get; set; }
    public long? ErroresCable { get; set; }
    public long? ApagadosBruscos { get; set; }
    public bool SmartFallando { get; set; }
    public bool HaySmart { get; set; }
    /// <summary>Errores de lectura/escritura que el sistema ha registrado para este disco en los últimos 30 días.</summary>
    public int ErroresSistema { get; set; }
    /// <summary>Lecturas de sectores dañados que el sistema ha registrado (esto sí indica un disco que se estropea).</summary>
    public int SectoresDanadosSistema { get; set; }
    public int ReintentosSistema { get; set; }
    bool Extraible => Conexion is "USB" or "SD" or "eMMC/SD";
    public List<string> Motivos { get; } = new();
    public List<string> Notas { get; } = new();

    /// <summary>Decide el estado a partir de lo leído.</summary>
    public void Evaluar(string? saludSistema = null)
    {
        var e = Veredicto.Ok;
        void Peor(Veredicto v, string motivo)
        {
            Motivos.Add(motivo);
            if (v > e) e = v;
        }
        if (SmartFallando) Peor(Veredicto.Fallo, Tx("El propio disco avisa de que va a fallar (SMART)."));
        if (saludSistema == "Mala") Peor(Veredicto.Fallo, Tx("El sistema marca el disco como averiado."));
        if (saludSistema == "Aviso") Peor(Veredicto.Aviso, Tx("El sistema marca el disco con advertencias."));
        if (Pendientes > 0) Peor(Veredicto.Fallo, Tf("{0} sectores dañados pendientes de reasignar.", Pendientes));
        if (ErroresMedio > 0) Peor(Veredicto.Fallo, Tf("{0} errores de datos irrecuperables.", ErroresMedio));
        if (Reasignados > 50) Peor(Veredicto.Fallo, Tf("{0} sectores reasignados: el disco se está degradando.", Reasignados));
        else if (Reasignados > 0) Peor(Veredicto.Aviso, Tf("{0} sectores reasignados.", Reasignados));
        if (VidaUsadaPct >= 100) Peor(Veredicto.Fallo, Tx("Ha agotado la vida útil prevista por el fabricante."));
        else if (VidaUsadaPct >= 80) Peor(Veredicto.Aviso, Tf("Ha consumido el {0} % de su vida útil.", VidaUsadaPct));
        if (TemperaturaC >= 70) Peor(Veredicto.Aviso, Tf("Temperatura alta ({0} °C).", TemperaturaC));
        if (ErroresCable > 0) Peor(Veredicto.Aviso, Tf("{0} errores de transmisión: revisa el cable o el conector.", ErroresCable));
        if (SectoresDanadosSistema > 0)
            Peor(Veredicto.Aviso, Tf("El sistema ha encontrado sectores dañados en este disco {0} veces en los últimos 30 días.", SectoresDanadosSistema));
        // Un disco USB que se quita sin expulsar deja errores de lectura/escritura en el registro: no es avería.
        if (ErroresSistema > 0 && Extraible)
            Notas.Add(Tf("{0} errores por desconectarlo mientras se usaba (últimos 30 días). No es una avería, pero quitarlo sin expulsar puede estropear los archivos que se estén copiando.", ErroresSistema));
        else if (ErroresSistema > 0)
            Peor(Veredicto.Aviso, Tf("El sistema ha registrado {0} errores de lectura o escritura en este disco en los últimos 30 días.", ErroresSistema));
        if (ReintentosSistema > 0)
            Notas.Add(Tf("{0} lecturas reintentadas en los últimos 30 días. Es habitual en discos USB; en un disco interno, vigílalo.", ReintentosSistema));

        if (!HaySmart && saludSistema is null or "" && ErroresSistema == 0 && SectoresDanadosSistema == 0)
        {
            Estado = Veredicto.Pendiente;
            EstadoTexto = Tx("Sin datos de salud");
            return;
        }
        Estado = e;
        EstadoTexto = e switch { Veredicto.Ok => Tx("Salud correcta"), Veredicto.Aviso => Tx("Con advertencias"), _ => Tx("Averiado o a punto de fallar") };
    }
}

public sealed record Volumen(string Nombre, string Carpeta, string Disco, long Libre, long Total);

public sealed record ResultadoDisco(double LecturaMBs, double EscrituraMBs, double Aleatoria4kMBs, double Iops, long BytesProbados);

internal static class Almacenamiento
{
    public static (List<SaludDisco> discos, string? nota) Salud()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return AlmacenamientoWindows.Salud();
            if (OperatingSystem.IsLinux()) return SaludLinux();
        }
        catch { }
        return (new(), null);
    }

    public static List<Volumen> Volumenes()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return AlmacenamientoWindows.Volumenes();
            if (OperatingSystem.IsLinux()) return VolumenesLinux();
        }
        catch { }
        return new();
    }

    // ---------- Velocidad (común) ----------

    const int Bloque = 4 << 20;

    public static unsafe ResultadoDisco Medir(Volumen v, IProgress<string> progreso, CancellationToken ct)
    {
        long libre = new DriveInfo(v.Carpeta).AvailableFreeSpace;
        long tam = libre > (6L << 30) ? 1L << 30 : libre > (2L << 30) ? 512L << 20 : libre > (600L << 20) ? 256L << 20 : 0;
        if (tam == 0) throw new InvalidOperationException(Tx("No hay espacio libre suficiente para la prueba (hacen falta unos 600 MB)."));

        var ruta = Path.Combine(v.Carpeta, $".bancada-prueba-{Environment.ProcessId}.tmp");
        var buf = (byte*)NativeMemory.AlignedAlloc(Bloque, 4096);
        var span = new Span<byte>(buf, Bloque);
        Random.Shared.NextBytes(span); // datos aleatorios: que la compresión de algunos SSD no infle el resultado
        var directo = OperatingSystem.IsWindows() ? FileOptions.WriteThrough | (FileOptions)0x20000000 : FileOptions.None;
        try
        {
            // Escritura secuencial
            progreso.Report(Tx("Escribiendo…"));
            long escritos = 0;
            var sw = Stopwatch.StartNew();
            using (var h = File.OpenHandle(ruta, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, directo, tam))
            {
                while (escritos < tam && sw.Elapsed.TotalSeconds < 15)
                {
                    ct.ThrowIfCancellationRequested();
                    RandomAccess.Write(h, span, escritos);
                    escritos += Bloque;
                    if ((escritos & ((64L << 20) - 1)) == 0) progreso.Report(Tf("Escribiendo… {0} MB", (escritos >> 20)));
                }
                if (OperatingSystem.IsLinux())
                {
                    fsync((int)h.DangerousGetHandle());
                    posix_fadvise((int)h.DangerousGetHandle(), 0, 0, 4); // POSIX_FADV_DONTNEED: fuera de la caché
                }
            }
            var escritura = escritos / 1e6 / sw.Elapsed.TotalSeconds;

            // Lectura secuencial
            progreso.Report(Tx("Leyendo…"));
            long leidos = 0;
            sw.Restart();
            using (var h = File.OpenHandle(ruta, FileMode.Open, FileAccess.Read, FileShare.None, directo))
            {
                if (OperatingSystem.IsLinux()) posix_fadvise((int)h.DangerousGetHandle(), 0, 0, 4);
                while (leidos < escritos && sw.Elapsed.TotalSeconds < 15)
                {
                    ct.ThrowIfCancellationRequested();
                    var n = RandomAccess.Read(h, span, leidos);
                    if (n <= 0) break;
                    leidos += n;
                }
            }
            var lectura = leidos / 1e6 / sw.Elapsed.TotalSeconds;

            // Lectura aleatoria de bloques de 4 KB (lo que más se nota al usar el equipo)
            progreso.Report(Tx("Lectura aleatoria de bloques pequeños…"));
            long operaciones = 0;
            var pequeno = new Span<byte>(buf, 4096);
            long bloques4k = escritos / 4096;
            sw.Restart();
            using (var h = File.OpenHandle(ruta, FileMode.Open, FileAccess.Read, FileShare.None, directo | FileOptions.RandomAccess))
            {
                if (OperatingSystem.IsLinux())
                {
                    posix_fadvise((int)h.DangerousGetHandle(), 0, 0, 4);
                    posix_fadvise((int)h.DangerousGetHandle(), 0, 0, 1); // POSIX_FADV_RANDOM: sin lectura anticipada
                }
                var rnd = new Random(1234);
                while (sw.Elapsed.TotalSeconds < 6)
                {
                    ct.ThrowIfCancellationRequested();
                    RandomAccess.Read(h, pequeno, rnd.NextInt64(bloques4k) * 4096);
                    operaciones++;
                }
            }
            var iops = operaciones / sw.Elapsed.TotalSeconds;
            return new ResultadoDisco(lectura, escritura, iops * 4096 / 1e6, iops, escritos);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(Tx("No hay permiso para escribir en esta unidad."));
        }
        finally
        {
            NativeMemory.AlignedFree(buf);
            try { File.Delete(ruta); } catch { }
        }
    }

    /// <summary>Qué velocidad de lectura secuencial es normal según el tipo de disco.</summary>
    public static (double minimo, string texto) Esperado(string tipo, string conexion) => (tipo, conexion) switch
    {
        (_, "NVMe") => (900, Tx("Un SSD NVMe suele leer entre 1.500 y 7.000 MB/s.")),
        ("HDD", _) => (60, Tx("Un disco duro mecánico suele leer entre 80 y 200 MB/s.")),
        (_, "USB") => (15, Tx("Por USB depende del dispositivo: de 20 MB/s (USB 2) a más de 400 MB/s (USB 3).")),
        (_, "SD" or "eMMC" or "eMMC/SD") => (40, Tx("Una memoria eMMC o tarjeta SD suele leer entre 80 y 300 MB/s.")),
        ("SSD", _) => (250, Tx("Un SSD SATA suele leer entre 400 y 560 MB/s.")),
        _ => (0, "")
    };

    [DllImport("libc")] static extern int fsync(int fd);
    [DllImport("libc")] static extern int posix_fadvise(int fd, long desde, long largo, int consejo);

    // ---------- Linux ----------

    [SupportedOSPlatform("linux")]
    static (List<SaludDisco>, string?) SaludLinux()
    {
        var lista = new List<SaludDisco>();
        const string bloques = "/sys/block";
        if (!Directory.Exists(bloques)) return (lista, null);
        bool admin = Plataforma.EsAdministrador;
        var smartctl = admin ? Plataforma.Buscar("smartctl") : null;
        var udisks = Plataforma.Buscar("udisksctl");
        var hwmon = SensoresHwmonDisco();
        bool algunoSinDatos = false;

        foreach (var dir in Directory.GetDirectories(bloques).Order())
        {
            var nombre = Path.GetFileName(dir);
            if (Regex.IsMatch(nombre, @"^(loop|ram|zram|dm-|sr|md|fd|nbd)")) continue;
            if (!long.TryParse(Plataforma.Leer(Path.Combine(dir, "size")), out var sectores) || sectores == 0) continue;
            string destino = "";
            try { destino = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName ?? ""; } catch { }
            var modelo = Plataforma.Leer(Path.Combine(dir, "device", "model")) ?? nombre;
            var conexion = nombre.StartsWith("nvme") ? "NVMe" : nombre.StartsWith("mmcblk") ? "eMMC/SD" :
                           destino.Contains("/usb") ? "USB" :
                           nombre.StartsWith("vd") || destino.Contains("/virtio") || Regex.IsMatch(modelo, "virtual|qemu|vbox|vmware", RegexOptions.IgnoreCase) ? "Virtual" : "SATA";
            var d = new SaludDisco
            {
                Modelo = modelo,
                Tipo = Plataforma.Leer(Path.Combine(dir, "queue", "rotational")) == "1" ? "HDD" : "SSD",
                Conexion = conexion,
                Bytes = sectores * 512,
                Firmware = Plataforma.Leer(Path.Combine(dir, "device", "firmware_rev")) ?? Plataforma.Leer(Path.Combine(dir, "device", "rev")) ?? ""
            };

            string? dispositivo = null;
            try { dispositivo = new DirectoryInfo(Path.Combine(dir, "device")).ResolveLinkTarget(true)?.FullName; } catch { }
            if (dispositivo != null && hwmon.TryGetValue(dispositivo, out var temp)) d.TemperaturaC = temp;

            if (smartctl != null) LeerSmartctl(d, smartctl, "/dev/" + nombre);
            else if (udisks != null) LeerUdisks(d, udisks, "/dev/" + nombre);
            var nucleo = Placa.Eventos.NucleoLinux() ?? "";
            var dispositivoRe = $@"\b{Regex.Escape(nombre)}(p?\d+)?\b";
            d.SectoresDanadosSistema = Regex.Matches(nucleo, $@"(critical medium error|Unrecovered read error).*{dispositivoRe}").Count;
            d.ErroresSistema = Regex.Matches(nucleo, $@"I/O error.*{dispositivoRe}").Count;
            d.Evaluar();
            if (!d.HaySmart && conexion != "Virtual") algunoSinDatos = true;
            lista.Add(d);
        }
        string? nota = null;
        if (algunoSinDatos)
            nota = admin
                ? Tx("Para ver la salud SMART completa, instala smartmontools (sudo apt install smartmontools).")
                : Tx("Para ver la salud SMART completa de todos los discos, instala smartmontools y abre Bancada con sudo.");
        return (lista, nota);
    }

    /// <summary>Temperaturas de disco que da el núcleo (nvme, drivetemp), por dispositivo.</summary>
    static Dictionary<string, int> SensoresHwmonDisco()
    {
        var r = new Dictionary<string, int>();
        if (!Directory.Exists("/sys/class/hwmon")) return r;
        foreach (var h in Directory.GetDirectories("/sys/class/hwmon"))
        {
            var nombre = Plataforma.Leer(Path.Combine(h, "name"));
            if (nombre is not ("nvme" or "drivetemp")) continue;
            if (!int.TryParse(Plataforma.Leer(Path.Combine(h, "temp1_input")), out var mc)) continue;
            try
            {
                var dev = new DirectoryInfo(Path.Combine(h, "device")).ResolveLinkTarget(true)?.FullName;
                if (dev != null) r[dev] = mc / 1000;
            }
            catch { }
        }
        return r;
    }

    static void LeerSmartctl(SaludDisco d, string smartctl, string dispositivo)
    {
        var json = Plataforma.Ejecutar(smartctl, $"-j -a {dispositivo}", 20000);
        // smartctl devuelve códigos distintos de 0 aunque haya leído bien (bits de aviso): se reintenta sin exigir 0.
        json ??= Plataforma.EjecutarSalida(smartctl, $"-j -a {dispositivo}", 20000);
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("smart_status", out var st) && st.TryGetProperty("passed", out var ok))
            {
                d.HaySmart = true;
                d.SmartFallando = !ok.GetBoolean();
            }
            if (raiz.TryGetProperty("temperature", out var t) && t.TryGetProperty("current", out var tc)) d.TemperaturaC = tc.GetInt32();
            if (raiz.TryGetProperty("power_on_time", out var po) && po.TryGetProperty("hours", out var ph)) d.HorasEncendido = ph.GetInt32();
            if (raiz.TryGetProperty("firmware_version", out var fw)) d.Firmware = fw.GetString() ?? d.Firmware;
            if (raiz.TryGetProperty("nvme_smart_health_information_log", out var n))
            {
                d.HaySmart = true;
                long L(string k) => n.TryGetProperty(k, out var x) && x.TryGetInt64(out var v) ? v : 0;
                if (L("critical_warning") != 0) d.SmartFallando = true;
                d.VidaUsadaPct = (int)L("percentage_used");
                d.ErroresMedio = L("media_errors");
                d.ApagadosBruscos = L("unsafe_shutdowns");
                d.EscritoBytes = L("data_units_written") * 512000;
            }
            if (raiz.TryGetProperty("ata_smart_attributes", out var ata) && ata.TryGetProperty("table", out var tabla))
            {
                d.HaySmart = true;
                foreach (var a in tabla.EnumerateArray())
                {
                    var id = a.GetProperty("id").GetInt32();
                    long raw = a.TryGetProperty("raw", out var r) && r.TryGetProperty("value", out var rv) && rv.TryGetInt64(out var x) ? x : 0;
                    switch (id)
                    {
                        case 5: d.Reasignados = raw; break;
                        case 197: d.Pendientes = raw; break;
                        case 198: d.ErroresMedio = raw; break;
                        case 199: d.ErroresCable = raw; break;
                        case 241: d.EscritoBytes ??= raw * 512; break;
                        case 231 or 233 when a.TryGetProperty("value", out var norm): d.VidaUsadaPct ??= 100 - norm.GetInt32(); break;
                    }
                }
            }
        }
        catch { }
    }

    static void LeerUdisks(SaludDisco d, string udisksctl, string dispositivo)
    {
        var bloque = Plataforma.Ejecutar(udisksctl, $"info -b {dispositivo}", 5000) ?? "";
        var unidad = Regex.Match(bloque, @"^\s*Drive:\s*'?/org/freedesktop/UDisks2/drives/([^'\s]+)", RegexOptions.Multiline).Groups[1].Value;
        if (unidad.Length == 0) return;
        var info = Plataforma.Ejecutar(udisksctl, $"info -d {unidad}", 5000) ?? "";
        string V(string clave) => Regex.Match(info, $@"^\s*{clave}:\s*(.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();

        if (V("SmartSupported") == "true" || V("SmartEnabled") == "true")
        {
            d.HaySmart = true;
            d.SmartFallando = V("SmartFailing") == "true";
            if (long.TryParse(V("SmartNumBadSectors"), out var malos)) d.Reasignados = malos;
            if (long.TryParse(V("SmartPowerOnSeconds"), out var seg) && seg > 0) d.HorasEncendido = (int)(seg / 3600);
            if (double.TryParse(V("SmartTemperature"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var k) && k > 200)
                d.TemperaturaC = (int)Math.Round(k - 273.15);
        }
        // NVMe (udisks 2.10 o posterior)
        if (int.TryParse(V("SmartPowerOnHours"), out var horas) && horas > 0)
        {
            d.HaySmart = true;
            d.HorasEncendido = horas;
            var aviso = V("SmartCriticalWarning");
            if (aviso.Length > 0 && aviso != "[]") d.SmartFallando = true;
            if (int.TryParse(V("SmartTemperature"), out var tk) && tk > 200) d.TemperaturaC = tk - 273;
        }
    }

    [SupportedOSPlatform("linux")]
    static List<Volumen> VolumenesLinux()
    {
        var lista = new List<Volumen>();
        var tipos = new HashSet<string> { "ext2", "ext3", "ext4", "btrfs", "xfs", "f2fs", "vfat", "exfat", "ntfs", "ntfs3", "fuseblk", "jfs", "bcachefs" };
        var casa = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var montajes = (Plataforma.Leer("/proc/self/mounts") ?? "").Split('\n')
            .Select(l => l.Split(' '))
            .Where(p => p.Length >= 3 && p[0].StartsWith("/dev/") && tipos.Contains(p[2]))
            .Select(p => (dev: p[0], punto: p[1].Replace("\\040", " "), tipo: p[2]))
            .ToList();
        // La carpeta personal se mide en su propio volumen (donde siempre se puede escribir).
        var deCasa = montajes.Where(m => casa == m.punto || casa.StartsWith(m.punto.TrimEnd('/') + "/")).OrderByDescending(m => m.punto.Length).FirstOrDefault();
        var vistos = new HashSet<string>();
        foreach (var m in montajes.OrderBy(m => m.punto == deCasa.punto ? 0 : 1))
        {
            if (!vistos.Add(m.dev)) continue;
            if (m.punto.StartsWith("/boot") || m.punto.StartsWith("/efi") || m.punto.StartsWith("/snap")) continue;
            var carpeta = m.punto == deCasa.punto ? casa : m.punto;
            if (carpeta != casa && !SePuedeEscribir(carpeta)) continue;
            try
            {
                var di = new DriveInfo(m.punto);
                var nombre = m.punto == "/" ? Tx("Sistema (/)") : m.punto;
                lista.Add(new Volumen(nombre, carpeta, DiscoDeParticion(m.dev), di.AvailableFreeSpace, di.TotalSize));
            }
            catch { }
        }
        return lista;
    }

    static bool SePuedeEscribir(string carpeta)
    {
        try
        {
            var p = Path.Combine(carpeta, $".bancada-{Environment.ProcessId}");
            File.WriteAllBytes(p, Array.Empty<byte>());
            File.Delete(p);
            return true;
        }
        catch { return false; }
    }

    static string DiscoDeParticion(string dev)
    {
        try
        {
            var real = new FileInfo(dev).ResolveLinkTarget(true)?.FullName ?? dev;
            var nombre = Path.GetFileName(real);
            for (int i = 0; i < 4 && nombre.StartsWith("dm-"); i++) // cifrado o LVM: se baja al disco de debajo
            {
                var esclavos = Path.Combine("/sys/class/block", nombre, "slaves");
                var primero = Directory.Exists(esclavos) ? Directory.GetDirectories(esclavos).FirstOrDefault() : null;
                if (primero == null) break;
                nombre = Path.GetFileName(primero);
            }
            var sys = Path.Combine("/sys/class/block", nombre);
            if (File.Exists(Path.Combine(sys, "partition")))
            {
                var dirReal = new DirectoryInfo(sys).ResolveLinkTarget(true)?.FullName;
                var padre = dirReal != null ? Path.GetFileName(Path.GetDirectoryName(dirReal)) : null;
                if (!string.IsNullOrEmpty(padre)) nombre = padre;
            }
            return Plataforma.Leer($"/sys/block/{nombre}/device/model") ?? nombre;
        }
        catch { return ""; }
    }
}

[SupportedOSPlatform("windows")]
internal static class AlmacenamientoWindows
{
    const string Ambito = @"root\Microsoft\Windows\Storage";

    public static (List<SaludDisco>, string?) Salud()
    {
        var lista = new List<SaludDisco>();
        bool admin = Plataforma.EsAdministrador;
        bool faltanDatos = false;

        var fiabilidad = new Dictionary<string, System.Management.ManagementBaseObject>();
        foreach (var o in DetectorWindows.Wmi("SELECT DeviceId, Temperature, Wear, PowerOnHours, ReadErrorsUncorrected FROM MSFT_StorageReliabilityCounter", Ambito))
            fiabilidad[DetectorWindows.T(o["DeviceId"])] = o;

        var ata = admin ? SmartAta() : new();
        var sectores = PorDisco(Placa.Eventos.Consultar("disk", "7"));  // bloque dañado
        var errores = PorDisco(Placa.Eventos.Consultar("disk", "51")); // error durante una operación (típico al desconectar)
        var reintentos = PorDisco(Placa.Eventos.Consultar("disk", "153"));

        foreach (var o in DetectorWindows.Wmi("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus, FirmwareVersion FROM MSFT_PhysicalDisk", Ambito))
        {
            var id = DetectorWindows.T(o["DeviceId"]);
            var conexion = DetectorWindows.L(o["BusType"]) switch
            {
                17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 12 => "SD", 13 => "eMMC", 3 => "ATA", 1 => "SCSI", _ => ""
            };
            var tipo = DetectorWindows.L(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => conexion == "NVMe" ? "SSD" : "" };
            var saludSistema = DetectorWindows.L(o["HealthStatus"]) switch { 0 => "Correcta", 1 => "Aviso", 2 => "Mala", _ => "" };
            var modelo = DetectorWindows.T(o["FriendlyName"]);
            var d = new SaludDisco
            {
                Modelo = modelo,
                Tipo = tipo,
                Conexion = conexion,
                Bytes = DetectorWindows.L(o["Size"]),
                Firmware = DetectorWindows.T(o["FirmwareVersion"])
            };

            if (conexion == "NVMe" && int.TryParse(id, out var num) && NvmeSalud(num) is { } log) AplicarNvme(d, log);

            if (fiabilidad.TryGetValue(id, out var f))
            {
                if (d.TemperaturaC == null && DetectorWindows.L(f["Temperature"]) is > 0 and < 150 and var t) d.TemperaturaC = (int)t;
                if (d.VidaUsadaPct == null && f["Wear"] != null) d.VidaUsadaPct = (int)DetectorWindows.L(f["Wear"]);
                if (d.HorasEncendido == null && DetectorWindows.L(f["PowerOnHours"]) is > 0 and var h) d.HorasEncendido = (int)h;
                if (d.ErroresMedio == null && f["ReadErrorsUncorrected"] != null) d.ErroresMedio = DetectorWindows.L(f["ReadErrorsUncorrected"]);
            }

            var clave = Normalizar(modelo);
            var smart = ata.FirstOrDefault(a => clave.Length > 3 && a.instancia.Contains(clave));
            if (smart.atributos != null) AplicarAta(d, smart.atributos, smart.fallo);
            d.ErroresSistema = errores.GetValueOrDefault(id);
            d.SectoresDanadosSistema = sectores.GetValueOrDefault(id);
            d.ReintentosSistema = reintentos.GetValueOrDefault(id);

            d.Evaluar(saludSistema);
            if (!d.HaySmart && conexion is "SATA" or "ATA") faltanDatos = true;
            lista.Add(d);
        }
        string? nota = faltanDatos && !admin
            ? Tx("Para ver los datos SMART de los discos SATA (sectores dañados, horas de uso), abre Bancada como administrador.")
            : null;
        return (lista, nota);
    }

    static string Normalizar(string s) => Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9]", "");

    /// <summary>Cuántos eventos menciona cada número de disco (\Device\HarddiskN\…).</summary>
    static Dictionary<string, int> PorDisco(string xml) =>
        xml.Split("<Event ", StringSplitOptions.RemoveEmptyEntries)
            .Select(e => Regex.Match(e, @"\\Device\\Harddisk(\d+)\\"))
            .Where(m => m.Success)
            .GroupBy(m => m.Groups[1].Value)
            .ToDictionary(g => g.Key, g => g.Count());

    static void AplicarNvme(SaludDisco d, byte[] log)
    {
        d.HaySmart = true;
        if (log[0] != 0) d.SmartFallando = true;
        var k = BitConverter.ToUInt16(log, 1);
        if (k > 200) d.TemperaturaC = k - 273;
        d.VidaUsadaPct = log[5];
        d.EscritoBytes = (long)Math.Min(long.MaxValue, BitConverter.ToUInt64(log, 48) * 512000.0);
        d.HorasEncendido = (int)Math.Min(int.MaxValue, BitConverter.ToUInt64(log, 128));
        d.ApagadosBruscos = (long)BitConverter.ToUInt64(log, 144);
        d.ErroresMedio = (long)BitConverter.ToUInt64(log, 160);
    }

    static void AplicarAta(SaludDisco d, byte[] v, bool fallo)
    {
        d.HaySmart = true;
        d.SmartFallando |= fallo;
        for (int i = 0; i < 30; i++)
        {
            int o = 2 + i * 12;
            if (o + 12 > v.Length) break;
            int id = v[o];
            if (id == 0) continue;
            long raw = v[o + 5] | (long)v[o + 6] << 8 | (long)v[o + 7] << 16 | (long)v[o + 8] << 24;
            switch (id)
            {
                case 5: d.Reasignados = raw; break;
                case 9: d.HorasEncendido = (int)(raw & 0xFFFFFFFF); break;
                case 194: d.TemperaturaC ??= (int)(raw & 0xFF); break;
                case 197: d.Pendientes = raw; break;
                case 198: d.ErroresMedio = raw; break;
                case 199: d.ErroresCable = raw; break;
                case 231 or 233: d.VidaUsadaPct ??= 100 - v[o + 3]; break;
            }
        }
    }

    /// <summary>Atributos SMART de los discos ATA/SATA (solo con permisos de administrador).</summary>
    static List<(string instancia, byte[] atributos, bool fallo)> SmartAta()
    {
        var r = new List<(string, byte[], bool)>();
        var fallos = new Dictionary<string, bool>();
        foreach (var o in DetectorWindows.Wmi("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", @"root\wmi"))
            fallos[DetectorWindows.T(o["InstanceName"])] = o["PredictFailure"] is true;
        foreach (var o in DetectorWindows.Wmi("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData", @"root\wmi"))
        {
            var inst = DetectorWindows.T(o["InstanceName"]);
            if (o["VendorSpecific"] is byte[] datos)
                r.Add((Normalizar(inst), datos, fallos.TryGetValue(inst, out var f) && f));
        }
        return r;
    }

    /// <summary>Registro de salud NVMe (página 2) pedido directamente al disco.</summary>
    static byte[]? NvmeSalud(int disco)
    {
        foreach (uint acceso in new uint[] { 0, 0xC0000000 })
        {
            using var h = CreateFile($@"\\.\PhysicalDrive{disco}", acceso, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            const int tam = 8 + 40 + 512;
            var buf = new byte[tam];
            void E(int o, int v) => BitConverter.TryWriteBytes(buf.AsSpan(o), v);
            E(0, 50);   // StorageDeviceProtocolSpecificProperty
            E(4, 0);    // PropertyStandardQuery
            E(8, 3);    // ProtocolTypeNvme
            E(12, 2);   // NVMeDataTypeLogPage
            E(16, 2);   // página de salud
            E(24, 40);  // datos tras la cabecera
            E(28, 512);
            if (!DeviceIoControl(h, 0x2D1400, buf, tam, buf, tam, out _, IntPtr.Zero)) continue;
            int desp = BitConverter.ToInt32(buf, 24);
            if (desp <= 0 || 8 + desp + 512 > tam) continue;
            return buf.AsSpan(8 + desp, 512).ToArray();
        }
        return null;
    }

    public static List<Volumen> Volumenes()
    {
        var discoDeLetra = new Dictionary<char, string>();
        var nombres = DetectorWindows.Wmi("SELECT DeviceId, FriendlyName FROM MSFT_PhysicalDisk", Ambito)
            .ToDictionary(o => DetectorWindows.T(o["DeviceId"]), o => DetectorWindows.T(o["FriendlyName"]));
        foreach (var o in DetectorWindows.Wmi("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", Ambito))
        {
            var letra = o["DriveLetter"] switch { char c => c, ushort u => (char)u, _ => '\0' };
            if (letra != '\0' && nombres.TryGetValue(DetectorWindows.T(o["DiskNumber"]), out var n)) discoDeLetra[char.ToUpperInvariant(letra)] = n;
        }

        var sistema = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var lista = new List<Volumen>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable) || !d.IsReady) continue;
                bool esSistema = d.Name.Equals(sistema, StringComparison.OrdinalIgnoreCase);
                var carpeta = esSistema ? Path.GetTempPath() : d.RootDirectory.FullName;
                var etiqueta = d.VolumeLabel.Length > 0 ? $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}" : d.Name.TrimEnd('\\');
                if (esSistema) etiqueta += Tx(" (sistema)");
                discoDeLetra.TryGetValue(char.ToUpperInvariant(d.Name[0]), out var disco);
                lista.Add(new Volumen(etiqueta, carpeta, disco ?? "", d.AvailableFreeSpace, d.TotalSize));
            }
            catch { }
        }
        return lista;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string ruta, uint acceso, uint compartir, IntPtr seg, uint modo, uint flags, IntPtr plantilla);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, byte[] entrada, int tamEntrada, byte[] salida, int tamSalida, out uint bytes, IntPtr ov);
}
