using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Bancada.Modelo;
using Microsoft.Win32;

namespace Bancada.Sistema;

public sealed record Comprobacion(string Nombre, Veredicto Resultado, string Resumen, List<string> Lineas);

/// <summary>
/// Comprobaciones de la placa base: firmware, TPM, dispositivos con fallos, enlaces PCIe,
/// errores de hardware registrados por el sistema, reloj (pila CMOS) y cálculo en cada núcleo.
/// </summary>
internal static class Placa
{
    public static async Task<List<Comprobacion>> ComprobarAsync(IProgress<string> progreso)
    {
        var r = new List<Comprobacion>();
        async Task Paso(string texto, Func<Comprobacion> f)
        {
            progreso.Report(texto);
            try { r.Add(await Task.Run(f)); }
            catch (Exception ex) { r.Add(new Comprobacion(texto.TrimEnd('…'), Veredicto.Pendiente, Tx("No se ha podido comprobar"), new() { ex.Message })); }
        }
        await Paso(Tx("Firmware y arranque…"), Firmware);
        await Paso(Tx("Chip de seguridad (TPM)…"), Tpm);
        await Paso(Tx("Dispositivos…"), Dispositivos);
        await Paso(Tx("Enlaces PCI Express…"), EnlacesPcie);
        await Paso(Tx("Errores de hardware registrados…"), ErroresRegistrados);
        progreso.Report(Tx("Reloj de la placa…"));
        r.Add(await Reloj());
        await Paso(Tx("Cálculo en cada núcleo del procesador…"), Nucleos);
        return r;
    }

    // ---------- Firmware ----------

    static Comprobacion Firmware()
    {
        bool? uefi = null, seguro = null;
        if (OperatingSystem.IsWindows())
        {
            if (GetFirmwareType(out var tipo)) uefi = tipo == 2;
            seguro = SecureBootWindows();
        }
        else if (OperatingSystem.IsLinux() && !Plataforma.EsWsl) // en WSL no se ve el firmware real
        {
            uefi = Directory.Exists("/sys/firmware/efi");
            try
            {
                var f = Directory.Exists("/sys/firmware/efi/efivars")
                    ? Directory.GetFiles("/sys/firmware/efi/efivars", "SecureBoot-*").FirstOrDefault() : null;
                if (f != null)
                {
                    var b = File.ReadAllBytes(f);
                    if (b.Length >= 5) seguro = b[4] == 1;
                }
            }
            catch { }
        }
        var lineas = new List<string>();
        var bios = Sesion.Equipo?.Bios ?? "";
        if (bios.Length > 0) lineas.Add($"BIOS {bios}");
        var anio = Regex.Match(bios, @"(\d{4})\s*$").Groups[1].Value;
        if (int.TryParse(anio, out var a) && DateTime.Now.Year - a >= 4)
            lineas.Add(Tf("La BIOS es de {0}. Conviene buscar una versión más reciente en la web del fabricante: corrigen fallos y problemas de seguridad.", a));
        var partes = new List<string>
        {
            uefi switch { true => "UEFI", false => Tx("BIOS heredada (Legacy)"), _ => Tx("Tipo de firmware desconocido") },
            seguro switch { true => Tx("arranque seguro activado"), false => Tx("arranque seguro desactivado"), _ => "" }
        };
        return new Comprobacion("Firmware (BIOS/UEFI)", Veredicto.Ok, string.Join(" · ", partes.Where(p => p.Length > 0)), lineas);
    }

    [SupportedOSPlatform("windows")]
    static bool? SecureBootWindows()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return k?.GetValue("UEFISecureBootEnabled") is int v ? v == 1 : null;
        }
        catch { return null; }
    }

    // ---------- TPM ----------

    static Comprobacion Tpm()
    {
        string? version = null;
        if (OperatingSystem.IsWindows())
        {
            var info = new InfoTpm { Version = 1 };
            if (Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<InfoTpm>(), ref info) == 0)
                version = info.VersionTpm switch { 1 => "1.2", 2 => "2.0", _ => "desconocida" };
        }
        else if (OperatingSystem.IsLinux() && Directory.Exists("/sys/class/tpm/tpm0"))
        {
            var mayor = Plataforma.Leer("/sys/class/tpm/tpm0/tpm_version_major");
            version = mayor == "2" ? "2.0" : mayor == "1" ? "1.2" : Tx("presente");
        }
        if (version != null)
            return new Comprobacion(Tx("Chip de seguridad (TPM)"), version == "1.2" ? Veredicto.Aviso : Veredicto.Ok, Tf("TPM {0} activo", version),
                version == "1.2" ? new() { Tx("TPM 1.2 es antiguo: Windows 11 necesita TPM 2.0.") } : new());
        bool virtual_ = Sesion.Equipo?.EsVirtual == true;
        return new Comprobacion(Tx("Chip de seguridad (TPM)"), virtual_ ? Veredicto.Pendiente : Veredicto.Aviso, Tx("No hay TPM activo"),
            new() { virtual_ ? Tx("En una máquina virtual es normal.") : Tx("Si el equipo lo tiene, actívalo en la BIOS (Intel PTT, AMD fTPM o «Security Chip»). Windows 11 y el cifrado de disco lo necesitan.") });
    }

    // ---------- Dispositivos ----------

    static Comprobacion Dispositivos() =>
        OperatingSystem.IsWindows() ? DispositivosWindows() : DispositivosLinux();

    [SupportedOSPlatform("windows")]
    static Comprobacion DispositivosWindows()
    {
        var lineas = new List<string>();
        var peor = Veredicto.Ok;
        int n = 0;
        foreach (var o in DetectorWindows.Wmi("SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            var id = DetectorWindows.T(o["PNPDeviceID"]);
            if (id.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase)) continue;
            var codigo = (int)DetectorWindows.L(o["ConfigManagerErrorCode"]);
            if (codigo == 45) continue; // desconectado
            var (texto, gravedad) = codigo switch
            {
                22 => (Tx("desactivado"), Veredicto.Pendiente),
                28 => (Tx("sin controlador instalado"), Veredicto.Aviso),
                1 or 18 or 39 or 32 => (Tx("controlador dañado o mal instalado"), Veredicto.Aviso),
                10 => (Tx("no puede iniciarse"), Veredicto.Fallo),
                43 => (Tx("Windows lo ha detenido porque informó de un fallo"), Veredicto.Fallo),
                31 or 37 or 24 => (Tx("no funciona correctamente"), Veredicto.Fallo),
                12 => (Tx("conflicto de recursos"), Veredicto.Aviso),
                14 => (Tx("necesita reiniciar el equipo"), Veredicto.Pendiente),
                _ => (Tf("código de error {0}", codigo), Veredicto.Aviso)
            };
            var nombre = Modelo.TextoEquipo.Hardware(DetectorWindows.T(o["Name"]));
            lineas.Add($"{(nombre.Length > 0 ? nombre : id)}: {texto}");
            if (gravedad != Veredicto.Pendiente) n++;
            if (gravedad > peor && gravedad != Veredicto.NoAplica) peor = gravedad;
        }
        return new Comprobacion(Tx("Dispositivos del equipo"), peor,
            n == 0 ? (lineas.Count == 0 ? Tx("Todos funcionan") : Tx("Todos funcionan (hay alguno desactivado)")) : Tf("{0} con problemas", n), lineas);
    }

    static Comprobacion DispositivosLinux()
    {
        var lineas = new List<string>();
        const string pci = "/sys/bus/pci/devices";
        if (Directory.Exists(pci))
        {
            foreach (var dir in Directory.GetDirectories(pci))
            {
                var clase = (Plataforma.Leer(Path.Combine(dir, "class")) ?? "").Replace("0x", "");
                if (clase.Length < 6) continue;
                var bas = clase[..2];
                var sub = clase[..4];
                // Solo los que siempre necesitan controlador: discos, red, gráfica, sonido, USB, inalámbricos.
                bool importa = bas is "01" or "02" or "03" or "04" or "0d" || sub is "0c03" or "0c0a";
                if (!importa || Directory.Exists(Path.Combine(dir, "driver"))) continue;
                lineas.Add(Tf("{0}: sin controlador", PciIds.Nombre(Plataforma.Leer(Path.Combine(dir, "vendor")), Plataforma.Leer(Path.Combine(dir, "device")))));
            }
        }
        return new Comprobacion(Tx("Dispositivos del equipo"), lineas.Count == 0 ? Veredicto.Ok : Veredicto.Aviso,
            lineas.Count == 0 ? Tx("Todos tienen controlador") : Tf("{0} sin controlador", lineas.Count), lineas);
    }

    // ---------- PCI Express ----------

    static Comprobacion EnlacesPcie()
    {
        var lineas = new List<string>();
        int revisados = 0;
        if (OperatingSystem.IsWindows()) EnlacesWindows(lineas, ref revisados);
        else if (OperatingSystem.IsLinux()) EnlacesLinux(lineas, ref revisados);
        if (revisados == 0)
            return new Comprobacion(Tx("Enlaces PCI Express"), Veredicto.Pendiente, Tx("No se pueden leer en este equipo"), new());
        if (lineas.Count == 0)
            return new Comprobacion(Tx("Enlaces PCI Express"), Veredicto.Ok, Tf("{0} dispositivos con todas sus líneas", revisados), new());
        // En portátiles y equipos compactos todo va soldado: menos líneas suele ser una decisión de diseño.
        if (Sesion.Equipo?.Tipo is not TipoEquipo.Sobremesa)
        {
            lineas.Add(Tx("En un portátil o equipo compacto los componentes van soldados y el fabricante a menudo les da menos líneas a propósito: no indica avería."));
            return new Comprobacion(Tx("Enlaces PCI Express"), Veredicto.Ok, Tf("{0} dispositivos revisados", revisados), lineas);
        }
        var n = lineas.Count;
        lineas.Add(Tx("Un enlace con menos líneas de las posibles suele deberse a suciedad o daño en la ranura o en el conector, o a una tarjeta mal asentada."));
        return new Comprobacion(Tx("Enlaces PCI Express"), Veredicto.Aviso, Tf("{0} funcionando con menos líneas de las posibles", n), lineas);
    }

    static void EnlacesLinux(List<string> lineas, ref int revisados)
    {
        const string pci = "/sys/bus/pci/devices";
        if (!Directory.Exists(pci)) return;
        foreach (var dir in Directory.GetDirectories(pci))
        {
            var clase = (Plataforma.Leer(Path.Combine(dir, "class")) ?? "").Replace("0x", "");
            if (clase.StartsWith("06")) continue; // puentes
            if (!int.TryParse(Plataforma.Leer(Path.Combine(dir, "current_link_width")), out var actual) ||
                !int.TryParse(Plataforma.Leer(Path.Combine(dir, "max_link_width")), out var maximo) ||
                actual <= 0 || maximo <= 0 || maximo > 32) continue;
            revisados++;
            // Si la ranura (el puente de arriba) es más estrecha que la tarjeta, es por diseño, no avería.
            string? padre = null;
            try
            {
                var real = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName;
                if (real != null) padre = Path.GetDirectoryName(real);
            }
            catch { }
            if (padre != null && int.TryParse(Plataforma.Leer(Path.Combine(padre, "max_link_width")), out var ranura) && ranura > 0)
                maximo = Math.Min(maximo, ranura);
            if (actual < maximo)
                lineas.Add(Tf("{0}: x{1} de x{2}", PciIds.Nombre(Plataforma.Leer(Path.Combine(dir, "vendor")), Plataforma.Leer(Path.Combine(dir, "device"))), actual, maximo));
        }
    }

    [SupportedOSPlatform("windows")]
    static void EnlacesWindows(List<string> lineas, ref int revisados)
    {
        var g = new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
        foreach (var (id, nodo) in UsbWindows.Dispositivos("PCI"))
        {
            if (UsbWindows.Numero(nodo, (g, 3)) == 6) continue; // puentes
            var actual = UsbWindows.Numero(nodo, (g, 10));
            var maximo = UsbWindows.Numero(nodo, (g, 12));
            if (actual is not (> 0 and <= 32) || maximo is not (> 0 and <= 32)) continue;
            revisados++;
            uint limite = maximo.Value;
            if (CM_Get_Parent(out var padre, nodo, 0) == 0 && UsbWindows.Numero(padre, (g, 12)) is uint ranura and > 0 and <= 32)
                limite = Math.Min(limite, ranura);
            if (actual < limite)
                lineas.Add(Tf("{0}: x{1} de x{2}", Modelo.TextoEquipo.Hardware(UsbWindows.NombreDe(nodo, id)), actual, limite));
        }
    }

    // ---------- Errores registrados ----------

    static Comprobacion ErroresRegistrados() =>
        OperatingSystem.IsWindows() ? ErroresWindows() : ErroresLinux();

    [SupportedOSPlatform("windows")]
    static Comprobacion ErroresWindows()
    {
        int Contar(string proveedor, string? ids, out List<int> eventos)
        {
            var xml = Eventos.Consultar(proveedor, ids);
            eventos = Regex.Matches(xml, @"<EventID[^>]*>(\d+)</EventID>").Select(m => int.Parse(m.Groups[1].Value)).ToList();
            return eventos.Count;
        }

        var lineas = new List<string>();
        var peor = Veredicto.Ok;
        void Marcar(Veredicto v, string texto)
        {
            lineas.Add(texto);
            if (v > peor) peor = v;
        }

        Contar("Microsoft-Windows-WHEA-Logger", null, out var whea);
        int graves = whea.Count(e => e is 1 or 18 or 20 or 46);
        int corregidos = whea.Count - graves;
        if (graves > 0) Marcar(Veredicto.Fallo, Tf("{0} errores de hardware graves (procesador, memoria o bus) en los últimos 30 días.", graves));
        // Los corregidos los arregla el propio hardware; solo preocupan si son muchísimos (enlace o memoria en mal estado).
        if (corregidos > 0) Marcar(corregidos >= 100 ? Veredicto.Aviso : Veredicto.Ok,
            Tf("{0} errores de hardware corregidos automáticamente en los últimos 30 días{1}.", corregidos, (corregidos >= 100 ? Tx(": son demasiados") : Tx(" (no es preocupante)"))));

        var pantallazos = Contar("Microsoft-Windows-WER-SystemErrorReporting", "1001", out _);
        if (pantallazos > 0) Marcar(Veredicto.Aviso, Tf("{0} pantallazos azules en los últimos 30 días.", pantallazos));

        var apagones = Contar("Microsoft-Windows-Kernel-Power", "41", out _);
        if (apagones >= 3) Marcar(Veredicto.Aviso, Tf("{0} reinicios o apagados inesperados en los últimos 30 días (fallo de alimentación, cuelgues o apagado a la fuerza).", apagones));
        else if (apagones > 0) lineas.Add(Tf("{0} apagado(s) inesperado(s) en los últimos 30 días.", apagones));

        // Los errores de disco se revisan en la prueba de discos, junto al disco al que pertenecen.
        return new Comprobacion(Tx("Errores de hardware registrados"), peor,
            lineas.Count == 0 ? Tx("Ninguno en los últimos 30 días") : peor == Veredicto.Ok ? Tx("Sin errores importantes") : Tx("Hay errores registrados"), lineas);
    }

    static Comprobacion ErroresLinux()
    {
        var registro = Eventos.NucleoLinux();
        string fuente = Tx("registro del núcleo");
        var lineas = new List<string>();
        var peor = Veredicto.Ok;
        if (registro == null)
            return new Comprobacion(Tx("Errores de hardware registrados"), Veredicto.Pendiente,
                Tx("Sin permiso para leer el registro del sistema"), new() { Tx("Abre Bancada con sudo para revisarlo.") });

        int Cuenta(string patron) => Regex.Matches(registro, patron, RegexOptions.IgnoreCase | RegexOptions.Multiline).Count;
        void Marcar(int n, Veredicto v, string texto)
        {
            if (n == 0) return;
            lineas.Add($"{n} {texto}");
            if (v > peor) peor = v;
        }
        Marcar(Cuenta(@"(mce:.*(uncorrected|fatal))|(\[Hardware Error\].*(fatal|uncorrected))"), Veredicto.Fallo, Tx("errores de hardware graves (procesador, memoria o bus)."));
        Marcar(Cuenta(@"(mce: \[Hardware Error\])|(Machine check events logged)|(\[Hardware Error\]: Corrected)"), Veredicto.Aviso, Tx("errores de hardware corregidos."));
        Marcar(Cuenta(@"AER:.*Uncorrect"), Veredicto.Fallo, Tx("errores graves en el bus PCI Express."));
        Marcar(Cuenta(@"AER:.*Corrected"), Veredicto.Aviso, Tx("errores corregidos en el bus PCI Express."));
        Marcar(Cuenta(@"EDAC.*\b(UE|uncorrect)"), Veredicto.Fallo, Tx("errores de memoria sin corregir."));
        Marcar(Cuenta(@"EDAC.*\b(CE|corrected)"), Veredicto.Aviso, Tx("errores de memoria corregidos."));
        Marcar(Cuenta(@"(temperature above threshold)|(critical temperature reached)"), Veredicto.Aviso, Tx("avisos de temperatura excesiva."));

        // Contadores de errores de memoria ECC.
        if (Directory.Exists("/sys/devices/system/edac/mc"))
            foreach (var mc in Directory.GetDirectories("/sys/devices/system/edac/mc", "mc*"))
            {
                if (long.TryParse(Plataforma.Leer(Path.Combine(mc, "ue_count")), out var ue) && ue > 0) Marcar((int)ue, Veredicto.Fallo, Tx("errores de memoria ECC sin corregir."));
                if (long.TryParse(Plataforma.Leer(Path.Combine(mc, "ce_count")), out var ce) && ce > 0) Marcar((int)ce, Veredicto.Aviso, Tx("errores de memoria ECC corregidos."));
            }
        return new Comprobacion(Tx("Errores de hardware registrados"), peor,
            lineas.Count == 0 ? Tf("Ninguno en el {0}", fuente) : Tx("Hay errores registrados"), lineas);
    }

    // ---------- Reloj (pila CMOS) ----------

    static async Task<Comprobacion> Reloj()
    {
        var lineas = new List<string>();
        var peor = Veredicto.Ok;
        string resumen;
        var desfase = await DesfaseNtp();
        if (desfase is double d)
        {
            var abs = Math.Abs(d);
            resumen = abs < 2 ? Tx("La hora del equipo es correcta") : Tf("La hora va {0} {1}", (d > 0 ? Tx("adelantada") : Tx("atrasada")), Ui(abs));
            if (abs >= 120)
            {
                peor = Veredicto.Aviso;
                lineas.Add(Tx("Si la hora se desajusta cada vez que se apaga el equipo, la pila de la placa base (CMOS) está gastada."));
            }
        }
        else resumen = Tx("No se ha podido comparar con la hora de Internet");

        if (OperatingSystem.IsLinux() && long.TryParse(Plataforma.Leer("/sys/class/rtc/rtc0/since_epoch"), out var rtc))
        {
            var dif = rtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var horas = Math.Round(dif / 3600.0);
            if (Math.Abs(dif) >= 60 && Math.Abs(dif - horas * 3600) < 120)
                lineas.Add(Tf("El reloj de la placa está en hora local ({0}{1} h respecto a UTC). Es normal si el equipo también tiene Windows.", (horas > 0 ? "+" : ""), horas));
            else if (Math.Abs(dif) >= 120)
            {
                peor = Veredicto.Aviso;
                lineas.Add(Tf("El reloj de la placa difiere {0} de la hora del sistema: posible pila CMOS agotada.", Ui(Math.Abs(dif))));
            }
        }
        return new Comprobacion(Tx("Reloj de la placa"), peor, resumen, lineas);

        static string Ui(double s) => s >= 3600 ? $"{Formato.N(s / 3600, "0.#")} h" : s >= 60 ? $"{Formato.N(s / 60, "0")} min" : $"{Formato.N(s, "0")} s";
    }

    /// <summary>Diferencia en segundos entre el reloj del equipo y un servidor de hora (SNTP).</summary>
    static async Task<double?> DesfaseNtp()
    {
        foreach (var servidor in new[] { "time.cloudflare.com", "pool.ntp.org" })
        {
            try
            {
                using var udp = new UdpClient();
                var dir = (await Dns.GetHostAddressesAsync(servidor)).First(a => a.AddressFamily == AddressFamily.InterNetwork);
                var pet = new byte[48];
                pet[0] = 0x1B; // versión 3, modo cliente
                var t0 = DateTime.UtcNow;
                await udp.SendAsync(pet, pet.Length, new IPEndPoint(dir, 123));
                using var cts = new CancellationTokenSource(3000);
                var resp = await udp.ReceiveAsync(cts.Token);
                var t3 = DateTime.UtcNow;
                if (resp.Buffer.Length < 48) continue;
                var t1 = Ntp(resp.Buffer, 32);
                var t2 = Ntp(resp.Buffer, 40);
                return ((t1 - t0).TotalSeconds + (t2 - t3).TotalSeconds) / 2;
            }
            catch { }
        }
        return null;
    }

    static DateTime Ntp(byte[] b, int o)
    {
        ulong seg = (ulong)b[o] << 24 | (ulong)b[o + 1] << 16 | (ulong)b[o + 2] << 8 | b[o + 3];
        ulong frac = (ulong)b[o + 4] << 24 | (ulong)b[o + 5] << 16 | (ulong)b[o + 6] << 8 | b[o + 7];
        return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seg + frac / 4294967296.0);
    }

    // ---------- Núcleos ----------

    static Comprobacion Nucleos()
    {
        int n = Math.Min(Environment.ProcessorCount, 64);
        var malos = new List<int>();
        bool fijado = true;
        var hilo = new Thread(() =>
        {
            var referencia = Calculo();
            for (int i = 0; i < n; i++)
            {
                fijado &= Fijar(i);
                for (int k = 0; k < 3; k++)
                    if (Calculo() != referencia) { malos.Add(i); break; }
            }
        }, 1 << 20) { IsBackground = true };
        hilo.Start();
        hilo.Join();

        var lineas = new List<string> { Tx("Instrucciones: ") + Instrucciones() };
        if (!fijado) lineas.Add(Tx("No se ha podido fijar cada prueba a un núcleo concreto; se ha probado igualmente."));
        return malos.Count == 0
            ? new Comprobacion(Tx("Procesador, núcleo a núcleo"), Veredicto.Ok, Tf("Los {0} hilos calculan bien", n), lineas)
            : new Comprobacion(Tx("Procesador, núcleo a núcleo"), Veredicto.Fallo,
                Tf("Errores de cálculo en {0} hilo(s): {1}", malos.Count, string.Join(", ", malos.Select(m => m + 1))),
                lineas.Prepend(Tx("Un procesador que se equivoca al calcular provoca cuelgues y datos corruptos.")).ToList());
    }

    static ulong Calculo()
    {
        // Entero (multiplicaciones y rotaciones) y coma flotante; el resultado exacto es siempre el mismo.
        ulong h = 0xcbf29ce484222325;
        for (int i = 0; i < 2_000_000; i++)
        {
            h ^= (ulong)i;
            h *= 0x100000001b3;
            h = (h << 13) | (h >> 51);
        }
        double x = 1.0;
        for (int i = 1; i < 300_000; i++) x = Math.Sqrt(x * 1.000001 + i) / 1.0000003;
        return h ^ (ulong)BitConverter.DoubleToInt64Bits(x);
    }

    static bool Fijar(int cpu)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << cpu)) != UIntPtr.Zero;
            if (OperatingSystem.IsLinux())
            {
                var mascara = new ulong[16];
                mascara[cpu / 64] = 1UL << (cpu % 64);
                return sched_setaffinity(0, (IntPtr)(mascara.Length * 8), mascara) == 0;
            }
        }
        catch { }
        return false;
    }

    static string Instrucciones()
    {
        var l = new List<string>();
        if (Sse42.IsSupported) l.Add("SSE4.2");
        if (Avx2.IsSupported) l.Add("AVX2");
        if (Fma.IsSupported) l.Add("FMA");
        if (Avx512F.IsSupported) l.Add("AVX-512");
        if (System.Runtime.Intrinsics.X86.Aes.IsSupported) l.Add("AES");
        if (AdvSimd.IsSupported) l.Add("NEON");
        if (System.Runtime.Intrinsics.Arm.Aes.IsSupported) l.Add("AES");
        if (Crc32.IsSupported) l.Add("CRC32");
        return l.Count > 0 ? string.Join(", ", l.Distinct()) : Tx("básicas");
    }

    /// <summary>Registro del sistema de los últimos 30 días (Windows: visor de eventos; Linux: diario del núcleo).</summary>
    internal static class Eventos
    {
        [SupportedOSPlatform("windows")]
        public static string Consultar(string proveedor, string? ids)
        {
            var filtro = ids == null ? "" : " and (" + string.Join(" or ", ids.Split(',').Select(i => $"EventID={i}")) + ")";
            var q = $"*[System[Provider[@Name='{proveedor}']{filtro} and TimeCreated[timediff(@SystemTime) <= 2592000000]]]";
            return Plataforma.Ejecutar("wevtutil", $"qe System /q:\"{q}\" /c:500 /rd:true /f:xml", 20000) ?? "";
        }

        static string? nucleo;

        /// <summary>Errores del núcleo de Linux (de los últimos 30 días si hay permiso para leer el diario).</summary>
        public static string? NucleoLinux()
        {
            if (nucleo != null) return nucleo;
            var r = Plataforma.EjecutarSalida("journalctl", "-k -p err --since -30d --no-pager -o cat -q", 20000);
            if (string.IsNullOrWhiteSpace(r) || r.Contains("insufficient permissions") || r.Contains("No journal files"))
                r = Plataforma.Ejecutar("dmesg", "--level=err,crit,alert,emerg", 10000);
            return nucleo = r;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct InfoTpm { public uint Version, VersionTpm, Interfaz, Revision; }

    [DllImport("tbs.dll")] static extern uint Tbsi_GetDeviceInfo(uint tam, ref InfoTpm info);
    [DllImport("kernel32.dll")] static extern bool GetFirmwareType(out int tipo);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern UIntPtr SetThreadAffinityMask(IntPtr hilo, UIntPtr mascara);
    [DllImport("libc")] static extern int sched_setaffinity(int pid, IntPtr tam, ulong[] mascara);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint padre, uint nodo, uint flags);
}
