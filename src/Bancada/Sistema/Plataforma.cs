using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Bancada.Sistema;

internal static class Plataforma
{
    /// <summary>Lee un archivo de texto (típicamente de /sys o /proc). Null si no existe o no se puede leer.</summary>
    public static string? Leer(string ruta)
    {
        try { return File.Exists(ruta) ? File.ReadAllText(ruta).Trim() : null; }
        catch { return null; }
    }

    /// <summary>Ejecuta un programa y devuelve su salida estándar, o null si falla o tarda demasiado.</summary>
    public static async Task<string?> EjecutarAsync(string programa, string argumentos, int limiteMs = 15000, bool exigirCero = true)
    {
        Process? p = null;
        try
        {
            var psi = new ProcessStartInfo(programa, argumentos)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Salida siempre en inglés: no depende del idioma del sistema al interpretarla.
            psi.Environment["LC_ALL"] = "C";
            psi.Environment["LANG"] = "C";
            p = Process.Start(psi);
            if (p == null) return null;
            var salida = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(limiteMs);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0 || !exigirCero ? await salida : null;
        }
        catch
        {
            try { p?.Kill(true); } catch { }
            return null;
        }
        finally
        {
            p?.Dispose();
        }
    }

    // Las versiones síncronas esperan en un hilo aparte: llamadas desde la interfaz no se bloquean entre sí.
    public static string? Ejecutar(string programa, string argumentos, int limiteMs = 15000) =>
        Task.Run(() => EjecutarAsync(programa, argumentos, limiteMs)).GetAwaiter().GetResult();

    /// <summary>Como Ejecutar, pero devuelve la salida aunque el programa termine con un código distinto de 0.</summary>
    public static string? EjecutarSalida(string programa, string argumentos, int limiteMs = 15000) =>
        Task.Run(() => EjecutarAsync(programa, argumentos, limiteMs, exigirCero: false)).GetAwaiter().GetResult();

    public static bool EsAdministrador
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows()) return EsAdminWindows();
                if (OperatingSystem.IsLinux()) return geteuid() == 0;
            }
            catch { }
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    static bool EsAdminWindows() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [DllImport("libc")]
    static extern uint geteuid();

    public static bool EsWsl =>
        OperatingSystem.IsLinux() && (Leer("/proc/version") ?? "").Contains("microsoft", StringComparison.OrdinalIgnoreCase);

    /// <summary>Vuelve a abrir Bancada pidiendo permisos de administrador (Windows) y cierra esta instancia.</summary>
    public static void ReiniciarComoAdministrador()
    {
        var ruta = Environment.ProcessPath;
        if (ruta == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true, Verb = "runas" });
            Environment.Exit(0);
        }
        catch { /* el usuario ha cancelado el aviso de Windows */ }
    }

    /// <summary>Abre un archivo o una carpeta con el programa predeterminado del sistema.</summary>
    public static void Abrir(string ruta)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open", $"\"{ruta}\"") { UseShellExecute = false });
        }
        catch { }
    }

    /// <summary>Carpeta Documentos del usuario (en Linux, la de xdg-user-dirs), o la carpeta personal.</summary>
    public static string CarpetaDocumentos()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(docs) ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>Primer programa de la lista que exista en el PATH.</summary>
    public static string? Buscar(params string[] programas)
    {
        var rutas = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in programas)
            foreach (var r in rutas)
            {
                var c = Path.Combine(r, p);
                if (File.Exists(c)) return c;
                if (OperatingSystem.IsWindows() && File.Exists(c + ".exe")) return c + ".exe";
            }
        return null;
    }
}

/// <summary>Nombres de dispositivos PCI a partir de la base de datos pci.ids que traen las distribuciones Linux.</summary>
internal static class PciIds
{
    static readonly string[] Rutas = { "/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids" };

    public static string Nombre(string? vendor, string? device)
    {
        var v = Normalizar(vendor);
        var d = Normalizar(device);
        if (v.Length == 0) return Tx("Dispositivo desconocido");
        var ruta = Rutas.FirstOrDefault(File.Exists);
        if (ruta != null)
        {
            try
            {
                string? fabricante = null;
                foreach (var linea in File.ReadLines(ruta))
                {
                    if (linea.Length == 0 || linea[0] == '#') continue;
                    if (fabricante == null)
                    {
                        if (linea[0] != '\t' && linea.StartsWith(v + " ")) fabricante = linea[4..].Trim();
                        continue;
                    }
                    if (linea[0] != '\t') break;
                    if (linea.Length > 6 && linea[1] != '\t' && linea.AsSpan(1).StartsWith(d + " "))
                        return $"{Corto(fabricante)} {linea[5..].Trim()}";
                }
            }
            catch { }
        }
        return Tf("{0} (dispositivo {1})", Fabricante(v), d);
    }

    static string Normalizar(string? id) => (id ?? "").Trim().Replace("0x", "").ToLowerInvariant();

    static string Corto(string fabricante) =>
        fabricante.StartsWith("Intel") ? "Intel" :
        fabricante.Contains("NVIDIA") ? "NVIDIA" :
        fabricante.Contains("AMD") || fabricante.Contains("ATI") ? "AMD" :
        fabricante.StartsWith("Realtek") ? "Realtek" : fabricante;

    public static string Fabricante(string vendor) => Normalizar(vendor) switch
    {
        "8086" => "Intel",
        "10de" => "NVIDIA",
        "1002" => "AMD",
        "14e4" => "Broadcom",
        "10ec" => "Realtek",
        "168c" => "Qualcomm Atheros",
        "17cb" => "Qualcomm",
        "1414" => "Microsoft",
        "15ad" => "VMware",
        "1af4" => "Red Hat (virtio)",
        var x => Tx("Fabricante ") + x
    };
}
