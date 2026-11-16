using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Bancada.Sistema;

public sealed record CapacidadSuspension(bool Puede, string Tipo, bool DespiertaSolo, string? Nota, bool Moderna = false);

/// <summary>Lo que Windows registró de una suspensión: de qué tipo fue de verdad, cuánto duró, qué la despertó y qué la frenó.</summary>
public sealed record InformeSuspension(string Modo, TimeSpan? Duracion, string? Despertador, bool DespertoTemporizador, List<string> Frenos)
{
    public const string Moderna = "moderna", Clasica = "s3", Hibernacion = "hibernacion";
}

/// <summary>
/// Prueba de suspensión: se suspende el equipo, se despierta solo (o lo despierta el técnico) y se comprueba que vuelve bien.
/// Antes de suspender se deja una marca en disco: si el equipo no vuelve y hay que reiniciarlo, al abrir Bancada se sabe.
/// </summary>
internal static class Suspension
{
    static string RutaMarca => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bancada", "suspension-en-curso");

    /// <summary>
    /// Si la última prueba no terminó porque el equipo no volvió, cuándo empezó. Solo cuenta si el equipo ha
    /// arrancado de cero después (hubo que forzar el apagado); si no, la prueba se quedó a medias porque se cerró
    /// Bancada y la marca se borra sin más.
    /// </summary>
    public static DateTime? PruebaSinTerminar()
    {
        try
        {
            if (!File.Exists(RutaMarca)) return null;
            var cuando = File.GetLastWriteTime(RutaMarca);
            var arranque = UltimoArranque();
            if (DateTime.Now - cuando > TimeSpan.FromDays(2) || (arranque is DateTime a && a < cuando))
            {
                Desmarcar();
                return null;
            }
            return cuando;
        }
        catch { return null; }
    }

    /// <summary>Cuándo arrancó el sistema. En Windows el contador incluye el tiempo suspendido o hibernado, así que solo se reinicia al arrancar de cero.</summary>
    static DateTime? UltimoArranque()
    {
        if (OperatingSystem.IsWindows()) return DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var linea = (Plataforma.Leer("/proc/stat") ?? "").Split('\n').FirstOrDefault(l => l.StartsWith("btime "));
        return linea != null && long.TryParse(linea[6..].Trim(), out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime : null;
    }

    public static void Marcar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaMarca)!);
            File.WriteAllText(RutaMarca, DateTime.Now.ToString("O"));
        }
        catch { }
    }

    public static void Desmarcar()
    {
        try { File.Delete(RutaMarca); } catch { }
    }

    public static CapacidadSuspension Capacidad()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return CapacidadWindows();
            if (OperatingSystem.IsLinux()) return CapacidadLinux();
        }
        catch { }
        return new CapacidadSuspension(false, Tx("No se puede saber"), false, null);
    }

    [SupportedOSPlatform("windows")]
    static CapacidadSuspension CapacidadWindows()
    {
        var c = new byte[128];
        if (!GetPwrCapabilities(c)) return new CapacidadSuspension(true, Tx("Suspensión"), true, null);
        bool s3 = c[5] != 0, modernStandby = c[20] != 0;
        // En la suspensión moderna Windows no deja que un programa despierte el equipo (comprobado: su temporizador
        // se ignora), así que lo despierta el técnico, como en el uso real.
        if (modernStandby)
            return new CapacidadSuspension(true, Tx("Modern Standby (suspensión moderna, S0)"), false,
                Tx("En la suspensión moderna la pantalla se apaga pero el equipo sigue medio despierto. Es justo donde más fallan muchos portátiles al volver."),
                Moderna: true);
        if (s3) return new CapacidadSuspension(true, Tx("Suspensión clásica (S3)"), true, null);
        return new CapacidadSuspension(false, Tx("Este equipo no tiene suspensión"), false,
            Tx("Puede estar desactivada en la BIOS o faltar el controlador del chipset."));
    }

    static CapacidadSuspension CapacidadLinux()
    {
        if (Plataforma.EsWsl)
            return new CapacidadSuspension(false, Tx("No disponible dentro de WSL"), false, Tx("WSL es una máquina virtual: no puede suspender el equipo."));
        var estados = Plataforma.Leer("/sys/power/state") ?? "";
        var modos = Plataforma.Leer("/sys/power/mem_sleep") ?? "";
        if (!estados.Contains("mem") && !estados.Contains("freeze"))
            return new CapacidadSuspension(false, Tx("Este equipo no tiene suspensión"), false, null);
        var activo = Regex.Match(modos, @"\[(\w+)\]").Groups[1].Value;
        var tipo = activo switch
        {
            "deep" => Tx("Suspensión clásica (S3)"),
            "s2idle" => Tx("Suspensión moderna (s2idle)"),
            _ => Tx("Suspensión")
        };
        bool solo = Plataforma.EsAdministrador && Plataforma.Buscar("rtcwake") != null;
        return new CapacidadSuspension(true, tipo, solo,
            solo ? null : Tx("Sin sudo, Bancada no puede programar que el equipo se despierte solo: despiértalo tú con una tecla o el botón de encendido."));
    }

    /// <summary>Motivo por el que Windows rechazó la última suspensión (lo rellena el hilo que la pide), o null.</summary>
    public static volatile string? ErrorAlSuspender;

    /// <summary>Suspende el equipo y pide que se despierte a los <paramref name="segundos"/>. Devuelve un error legible o null.</summary>
    public static string? Suspender(int segundos, bool moderna)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return SuspenderWindows(segundos, moderna);
            if (OperatingSystem.IsLinux()) return SuspenderLinux(segundos);
        }
        catch (Exception ex) { return ex.Message; }
        return Tx("Sistema no compatible");
    }

    static string? SuspenderLinux(int segundos)
    {
        if (Plataforma.EsAdministrador && Plataforma.Buscar("rtcwake") is string rtcwake)
        {
            var modo = (Plataforma.Leer("/sys/power/state") ?? "").Contains("mem") ? "mem" : "freeze";
            Process.Start(new ProcessStartInfo(rtcwake, $"-m {modo} -s {segundos}") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            return null;
        }
        var systemctl = Plataforma.Buscar("systemctl");
        if (systemctl == null) return Tx("No se encuentra systemctl para suspender el equipo.");
        Process.Start(new ProcessStartInfo(systemctl, "suspend") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
        return null;
    }

    [SupportedOSPlatform("windows")]
    static SafeWaitHandle? temporizador;

    [SupportedOSPlatform("windows")]
    static string? SuspenderWindows(int segundos, bool moderna)
    {
        // Temporizador que despierta el equipo (si la opción «Permitir temporizadores de reactivación» lo deja).
        temporizador?.Dispose();
        temporizador = CreateWaitableTimerW(IntPtr.Zero, true, null);
        long cuando = -segundos * 10_000_000L; // relativo, en unidades de 100 ns
        SetWaitableTimer(temporizador, ref cuando, 0, IntPtr.Zero, IntPtr.Zero, true);
        ErrorAlSuspender = null;

        if (moderna)
        {
            // En la suspensión moderna no hay S3: SetSuspendState acaba hibernando el equipo (comprobado).
            // Windows entra en suspensión moderna cuando se apaga la pantalla, igual que con el botón de encendido.
            PostMessageW((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2); // HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, apagar
            return null;
        }

        HabilitarPrivilegioApagado();
        // SetSuspendState vuelve cuando el equipo se despierta: se llama en un hilo aparte.
        // Si Windows la rechaza en el acto, se guarda el motivo para enseñarlo.
        var hilo = new Thread(() =>
        {
            if (!SetSuspendState(false, false, false))
                ErrorAlSuspender = Tf("Windows no ha querido suspender el equipo: {0}", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
        }) { IsBackground = true, Name = "Suspensión" };
        hilo.Start();
        return null;
    }

    /// <summary>Tras volver, se enciende la pantalla (en la suspensión moderna puede despertar «a oscuras»).</summary>
    public static void EncenderPantalla()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SetThreadExecutionState(0x2 | 0x1); // ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED (una vez)
            mouse_event(0x0001, 0, 0, 0, IntPtr.Zero); // movimiento nulo del ratón: despierta la pantalla
        }
        catch { }
    }

    /// <summary>
    /// Lo que Windows registró desde <paramref name="desde"/> en el registro del sistema: si hubo suspensión moderna
    /// (eventos 506/507), clásica o hibernación (42, Power-Troubleshooter 1, arranque tipo 2), cuánto duró, qué la
    /// despertó y qué controladores frenaron la entrada (40). Null si no hay nada.
    /// </summary>
    public static InformeSuspension? Analizar(DateTime desde) => OperatingSystem.IsWindows() ? AnalizarWindows(desde) : null;

    [SupportedOSPlatform("windows")]
    static InformeSuspension? AnalizarWindows(DateTime desde)
    {
        var ms = (long)(DateTime.Now - desde).TotalMilliseconds + 20_000;
        var q = "*[System[((Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1) or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=40 or EventID=42 or EventID=506 or EventID=507)) or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27)) " +
                $"and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        var xml = Plataforma.Ejecutar("wevtutil", $"qe System /q:\"{q}\" /c:60 /f:xml", 15000) ?? "";
        var eventos = Regex.Split(xml, "(?=<Event xmlns)")
            .Select(e => (id: int.TryParse(Regex.Match(e, @"<EventID[^>]*>(\d+)</EventID>").Groups[1].Value, out var n) ? n : 0,
                          datos: Regex.Matches(e, @"<Data Name='([^']+)'>([^<]*)</Data>").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim())))
            .Where(e => e.id > 0).ToList();
        if (eventos.Count == 0) return null;

        string? Dato(int id, string nombre) => eventos.LastOrDefault(e => e.id == id && e.datos.ContainsKey(nombre)).datos?.GetValueOrDefault(nombre);
        var resumen = eventos.LastOrDefault(e => e.id == 1).datos; // Power-Troubleshooter: hora de entrada y salida, y qué despertó
        var estado = resumen?.GetValueOrDefault("TargetState") ?? Dato(42, "TargetState");
        bool hiberno = estado == "5" || Dato(27, "BootType") == "2";
        // Suspensión moderna: el 507 trae lo que duró (los de menos de 2 s son de paso, p. ej. al volver de hibernar).
        var moderna = eventos.Where(e => e.id == 507 && long.TryParse(e.datos.GetValueOrDefault("DurationInUs"), out var us) && us >= 2_000_000)
            .Select(e => TimeSpan.FromMilliseconds(long.Parse(e.datos["DurationInUs"]) / 1000.0)).LastOrDefault();

        string modo = hiberno ? InformeSuspension.Hibernacion
            : estado == "4" ? InformeSuspension.Clasica
            : moderna > TimeSpan.Zero ? InformeSuspension.Moderna
            : "";
        TimeSpan? duracion = null;
        if (resumen != null && DateTime.TryParse(resumen.GetValueOrDefault("SleepTime"), out var entra) && DateTime.TryParse(resumen.GetValueOrDefault("WakeTime"), out var sale) && sale > entra)
            duracion = sale - entra;
        if (modo == InformeSuspension.Moderna) duracion = moderna;

        var despertador = resumen?.GetValueOrDefault("WakeSourceText");
        var dueno = resumen?.GetValueOrDefault("WakeTimerOwner") ?? "";
        bool temporizador = (despertador ?? "").Contains("Bancada", StringComparison.OrdinalIgnoreCase) || dueno.Contains("Bancada", StringComparison.OrdinalIgnoreCase);
        var frenos = eventos.Where(e => e.id == 40)
            .Select(e => (e.datos.GetValueOrDefault("DriverName") ?? "").Replace(@"\Driver\", ""))
            .Where(n => n.Length > 0).Distinct().ToList();
        if (modo == "" && frenos.Count == 0) return null;
        return new InformeSuspension(modo, duracion, string.IsNullOrWhiteSpace(despertador) ? null : despertador, temporizador, frenos);
    }

    /// <summary>Espera a que alguna tarjeta de red vuelva a tener dirección IP. Devuelve los segundos, o null si no vuelve.</summary>
    public static async Task<double?> EsperarRedAsync(int maxSegundos)
    {
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed.TotalSeconds < maxSegundos)
        {
            if (HayRed()) return reloj.Elapsed.TotalSeconds;
            await Task.Delay(1000);
        }
        return null;
    }

    public static bool HayRed()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) &&
                n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)));
        }
        catch { return false; }
    }

    /// <summary>Dispositivos con fallos ahora mismo (para comparar antes y después de suspender).</summary>
    public static int DispositivosConFallos()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return DispositivosWindows();
    }

    [SupportedOSPlatform("windows")]
    static int DispositivosWindows() =>
        DetectorWindows.Wmi("SELECT PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22 AND ConfigManagerErrorCode <> 45")
            .Count(o => !DetectorWindows.T(o["PNPDeviceID"]).StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows")]
    static void HabilitarPrivilegioApagado()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out var token)) return;
        try
        {
            if (!LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid)) return;
            var tp = new Privilegios { Cuantos = 1, Luid = luid, Atributos = 0x2 };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct Privilegios { public uint Cuantos; public long Luid; public uint Atributos; }

    [DllImport("powrprof.dll")] static extern bool GetPwrCapabilities(byte[] capacidades);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr ventana, uint mensaje, IntPtr w, IntPtr l);
    [DllImport("powrprof.dll", SetLastError = true)] static extern bool SetSuspendState(bool hibernar, bool forzar, bool sinEventosDeReactivacion);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern SafeWaitHandle CreateWaitableTimerW(IntPtr atributos, bool manual, string? nombre);
    [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(SafeWaitHandle h, ref long cuando, int periodo, IntPtr rutina, IntPtr arg, bool reactivar);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint estado);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int datos, IntPtr extra);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr proceso, uint acceso, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValueW(string? sistema, string nombre, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool quitarTodos, ref Privilegios nuevos, uint tam, IntPtr previos, IntPtr devuelto);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}

/// <summary>
/// Detecta que el equipo ha estado suspendido: el reloj de pared avanza durante la suspensión,
/// pero el contador del programa no (o el programa se queda congelado sin poder avanzar).
/// </summary>
internal sealed class VigiaSuspension
{
    readonly Stopwatch monotono = Stopwatch.StartNew();
    DateTime paredPrevia = DateTime.UtcNow;
    double monoPrevio;

    /// <summary>Llamar cada segundo. Devuelve cuánto tiempo estuvo suspendido el equipo desde la última llamada, si lo estuvo.</summary>
    public TimeSpan? Comprobar()
    {
        var pared = DateTime.UtcNow;
        var mono = monotono.Elapsed.TotalSeconds;
        var dPared = (pared - paredPrevia).TotalSeconds;
        var dMono = mono - monoPrevio;
        paredPrevia = pared;
        monoPrevio = mono;
        // El reloj de pared ha avanzado mucho más que el contador, o el programa ha estado parado más de 8 s.
        var hueco = Math.Max(dPared - dMono, dPared > 8 ? dPared - 1 : 0);
        return hueco > 5 ? TimeSpan.FromSeconds(hueco) : null;
    }
}
