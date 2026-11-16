using System.Numerics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Bancada.Sistema;

public sealed record Sensor(string Nombre, double Valor, string Unidad);

public sealed record LecturaSensores(double? TempCpu, string FuenteTemp, double? Mhz, double? UsoPct, List<Sensor> Otros);

/// <summary>Temperaturas, ventiladores, frecuencia y uso del procesador con lo que el sistema expone sin controladores extra.</summary>
internal sealed class Sensores
{
    long[]? statAnterior;

    public LecturaSensores Leer()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return LeerWindows();
            if (OperatingSystem.IsLinux()) return LeerLinux();
        }
        catch { }
        return new LecturaSensores(null, "", null, null, new());
    }

    // ---------- Windows ----------

    [SupportedOSPlatform("windows")]
    static LecturaSensores LeerWindows()
    {
        var otros = new List<Sensor>();
        double? temp = null;
        string fuente = "";
        foreach (var o in DetectorWindows.Wmi("SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
        {
            double k = DetectorWindows.L(o["HighPrecisionTemperature"]) / 10.0;
            if (k <= 0) k = DetectorWindows.L(o["Temperature"]);
            if (k < 200) continue;
            var c = k - 273.15;
            var nombre = DetectorWindows.T(o["Name"]).Replace(@"\_TZ.", "").Trim('\\', '_');
            otros.Add(new Sensor(Tf("Zona térmica {0}", nombre), Math.Round(c, 1), "°C"));
            if (temp == null || c > temp) { temp = c; fuente = Tf("zona térmica {0}", nombre); }
        }

        double? mhz = null, uso = null;
        foreach (var o in DetectorWindows.Wmi("SELECT PercentProcessorPerformance, PercentProcessorUtility, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name = '_Total'"))
        {
            var base_ = DetectorWindows.L(o["ProcessorFrequency"]);
            var rend = DetectorWindows.L(o["PercentProcessorPerformance"]);
            if (base_ > 0 && rend > 0) mhz = base_ * rend / 100.0;
            uso = Math.Min(100, DetectorWindows.L(o["PercentProcessorUtility"]));
        }
        return new LecturaSensores(temp, fuente, mhz, uso, otros);
    }

    // ---------- Linux ----------

    [SupportedOSPlatform("linux")]
    LecturaSensores LeerLinux()
    {
        var otros = new List<Sensor>();
        double? temp = null;
        string fuente = "";
        int prioridad = 99;

        if (Directory.Exists("/sys/class/hwmon"))
        {
            foreach (var h in Directory.GetDirectories("/sys/class/hwmon").Order())
            {
                var chip = Plataforma.Leer(Path.Combine(h, "name")) ?? "";
                foreach (var f in Directory.GetFiles(h, "temp*_input").Order())
                {
                    if (!double.TryParse(Plataforma.Leer(f), out var mc) || mc <= 0) continue;
                    var c = mc / 1000;
                    var etiqueta = Plataforma.Leer(f.Replace("_input", "_label")) ?? "";
                    otros.Add(new Sensor(NombreSensor(chip, etiqueta), Math.Round(c, 1), "°C"));

                    // Elegir la temperatura más representativa del procesador.
                    int p = (chip, etiqueta) switch
                    {
                        ("coretemp", var e) when e.StartsWith("Package") => 0,
                        ("k10temp" or "zenpower", "Tctl" or "Tdie") => 0,
                        ("cpu_thermal" or "cpu-thermal" or "soc_thermal", _) => 1,
                        ("coretemp" or "k10temp" or "zenpower", _) => 2,
                        ("thinkpad" or "dell_smm" or "asus" or "hp", var e) when e.Contains("CPU", StringComparison.OrdinalIgnoreCase) => 3,
                        ("acpitz", _) => 5,
                        _ => 99
                    };
                    if (p < prioridad || (p == prioridad && p < 99 && c > temp))
                    {
                        prioridad = p;
                        temp = c;
                        fuente = NombreSensor(chip, etiqueta).ToLowerInvariant();
                    }
                }
                foreach (var f in Directory.GetFiles(h, "fan*_input").Order())
                    if (double.TryParse(Plataforma.Leer(f), out var rpm))
                        otros.Add(new Sensor($"Ventilador {Regex.Match(Path.GetFileName(f), @"\d+").Value} ({chip})", rpm, "rpm"));
            }
        }
        if (temp == null && Directory.Exists("/sys/class/thermal"))
        {
            foreach (var z in Directory.GetDirectories("/sys/class/thermal", "thermal_zone*"))
            {
                var tipo = Plataforma.Leer(Path.Combine(z, "type")) ?? "";
                if (!double.TryParse(Plataforma.Leer(Path.Combine(z, "temp")), out var mc) || mc <= 0) continue;
                if (temp == null || tipo.Contains("pkg") || tipo.Contains("cpu", StringComparison.OrdinalIgnoreCase))
                {
                    temp = mc / 1000;
                    fuente = tipo;
                }
            }
        }

        // Frecuencia media de todos los hilos.
        double? mhz = null;
        var frecs = new List<double>();
        if (Directory.Exists("/sys/devices/system/cpu"))
            foreach (var cpu in Directory.GetDirectories("/sys/devices/system/cpu", "cpu*"))
                if (double.TryParse(Plataforma.Leer(Path.Combine(cpu, "cpufreq", "scaling_cur_freq")), out var khz)) frecs.Add(khz / 1000);
        if (frecs.Count == 0)
            foreach (Match m in Regex.Matches(Plataforma.Leer("/proc/cpuinfo") ?? "", @"^cpu MHz\s*:\s*([\d.]+)", RegexOptions.Multiline))
                if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) frecs.Add(v);
        if (frecs.Count > 0) mhz = frecs.Average();

        // Uso a partir de /proc/stat entre dos lecturas.
        double? uso = null;
        var linea = (Plataforma.Leer("/proc/stat") ?? "").Split('\n')[0];
        var nums = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(x => long.TryParse(x, out var n) ? n : 0).ToArray();
        if (nums.Length >= 4)
        {
            if (statAnterior is { } a && a.Length == nums.Length)
            {
                long total = nums.Sum() - a.Sum();
                long ocioso = nums[3] + (nums.Length > 4 ? nums[4] : 0) - a[3] - (a.Length > 4 ? a[4] : 0);
                if (total > 0) uso = Math.Clamp(100.0 * (total - ocioso) / total, 0, 100);
            }
            statAnterior = nums;
        }
        return new LecturaSensores(temp, fuente, mhz, uso, otros);
    }

    static string NombreSensor(string chip, string etiqueta) => (chip, etiqueta) switch
    {
        ("coretemp", var e) when e.StartsWith("Package") => Tx("Procesador (paquete)"),
        ("coretemp", var e) when e.StartsWith("Core") => Tx("Procesador, núcleo ") + e[5..],
        ("k10temp" or "zenpower", var e) => Tx("Procesador ") + e,
        ("nvme", var e) => Tx("Disco NVMe") + (e.Length > 0 && e != "Composite" ? " " + e : ""),
        ("amdgpu", var e) => Tx("Gráfica AMD ") + e,
        ("nouveau" or "nvidia", _) => Tx("Gráfica NVIDIA"),
        ("acpitz", _) => "Placa (ACPI)",
        ("pch_skylake" or "pch_cannonlake" or "pch_cometlake" or "pch_tigerlake", _) => "Chipset",
        (var c, var e) when c.StartsWith("iwlwifi") => "Tarjeta Wi-Fi",
        (var c, "") => c,
        (var c, var e) => $"{e} ({c})"
    };
}

/// <summary>Carga todos los hilos del procesador con cálculo vectorial y comprueba que los resultados no cambian.</summary>
internal sealed class Estres
{
    const int Largo = 1024;
    volatile bool activo;
    long iteraciones, errores;
    readonly List<Thread> hilos = new();
    double referencia;

    public long Errores => Interlocked.Read(ref errores);
    public long Iteraciones => Interlocked.Read(ref iteraciones);
    public bool Activo => activo;

    public void Iniciar()
    {
        if (activo) return;
        activo = true;
        iteraciones = errores = 0;
        referencia = Calcular();
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            // Prioridad baja: la carga es total, pero la ventana sigue respondiendo.
            var t = new Thread(Trabajar) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = Tf("Estrés {0}", i) };
            hilos.Add(t);
            t.Start();
        }
    }

    public void Detener()
    {
        activo = false;
        foreach (var t in hilos) t.Join(2000);
        hilos.Clear();
    }

    void Trabajar()
    {
        while (activo)
        {
            if (Calcular() != referencia) Interlocked.Increment(ref errores);
            Interlocked.Increment(ref iteraciones);
        }
    }

    /// <summary>Cálculo determinista: el mismo resultado exacto en cualquier núcleo sano.</summary>
    static double Calcular()
    {
        Span<double> a = stackalloc double[Largo];
        Span<double> b = stackalloc double[Largo];
        for (int i = 0; i < Largo; i++)
        {
            a[i] = 1.0 + i * 1e-3;
            b[i] = 2.0 - i * 1e-3;
        }
        var ka = new Vector<double>(1.0000001);
        var kb = new Vector<double>(0.9999999);
        var kc = new Vector<double>(1e-7);
        int w = Vector<double>.Count;
        for (int r = 0; r < 400; r++)
        {
            for (int i = 0; i + w <= Largo; i += w)
            {
                var va = new Vector<double>(a.Slice(i, w));
                var vb = new Vector<double>(b.Slice(i, w));
                va = va * ka + vb * kc;
                vb = vb * kb + va * kc;
                va.CopyTo(a.Slice(i, w));
                vb.CopyTo(b.Slice(i, w));
            }
        }
        double s = 0;
        for (int i = 0; i < Largo; i++) s += a[i] * 0.5 + b[i] * 0.25;
        return s;
    }
}
