using System.Diagnostics;
using System.Runtime.InteropServices;
using Bancada.Modelo;

namespace Bancada.Sistema;

/// <summary>Una lectura de la batería durante la prueba de descarga. W es negativa al descargar.</summary>
public sealed record MuestraDescarga(TimeSpan T, int? Pct, double? Wh, double? W);

public sealed record ResultadoDescarga(
    TimeSpan Duracion, int? PctInicio, int? PctFin, double? WhGastados, double? PotenciaMedia,
    TimeSpan? Autonomia, TimeSpan? AutonomiaNueva, int CaidasBruscas, List<(Veredicto v, string texto)> Avisos);

/// <summary>Análisis de una prueba de descarga: autonomía real, coherencia del indicador y caídas bruscas.</summary>
internal static class Descarga
{
    public static ResultadoDescarga Analizar(IReadOnlyList<MuestraDescarga> m, double? llenaWh, double? disenoWh, bool ligero)
    {
        var avisos = new List<(Veredicto, string)>();
        if (m.Count < 2) return new ResultadoDescarga(TimeSpan.Zero, null, null, null, null, null, null, 0, avisos);

        var dur = m[^1].T - m[0].T;
        int? pct0 = m.FirstOrDefault(x => x.Pct != null)?.Pct, pctN = m.LastOrDefault(x => x.Pct != null)?.Pct;
        double? wh0 = m.FirstOrDefault(x => x.Wh != null)?.Wh, whN = m.LastOrDefault(x => x.Wh != null)?.Wh;
        int dPct = pct0 is int a && pctN is int b ? a - b : 0;
        double? dWh = wh0 is double w0 && whN is double wn && w0 - wn > 0 ? w0 - wn : null;
        double horas = dur.TotalHours;

        // Consumo medio: por la energía gastada; si no hay, por las lecturas de potencia.
        double? media = dWh != null && dur.TotalMinutes >= 3 ? dWh / horas : null;
        if (media == null)
        {
            var potencias = m.Where(x => x.W < -0.3).Select(x => -x.W!.Value).ToList();
            if (potencias.Count > 0) media = potencias.Average();
        }

        TimeSpan? autonomia = null, nueva = null;
        if (media > 0.3 && llenaWh > 0) autonomia = TimeSpan.FromHours(llenaWh.Value / media.Value);
        if (media > 0.3 && disenoWh > 0) nueva = TimeSpan.FromHours(disenoWh.Value / media.Value);
        // El equipo se apaga cuando el porcentaje llega a 0: si baja más deprisa que la energía, manda el porcentaje.
        if (dPct >= 3 && horas > 0)
        {
            var porPorcentaje = TimeSpan.FromHours(100 / (dPct / horas));
            if (autonomia == null || porPorcentaje < autonomia) autonomia = porPorcentaje;
        }

        // Caídas bruscas: el porcentaje baja 5 puntos o más entre dos lecturas seguidas (en menos de un minuto).
        int caidas = 0;
        for (int i = 1; i < m.Count; i++)
            if (m[i - 1].Pct is int p1 && m[i].Pct is int p2 && p1 - p2 >= 5 && m[i].T - m[i - 1].T <= TimeSpan.FromSeconds(60))
                caidas++;
        if (caidas > 0)
            avisos.Add((Veredicto.Fallo, Tf("El porcentaje ha caído de golpe {0} {1}: alguna celda está en mal estado y el equipo puede apagarse sin avisar.", caidas, (caidas == 1 ? Tx("vez") : Tx("veces")))));

        // Coherencia: lo que baja el porcentaje frente a la energía que se ha gastado de verdad.
        if (dWh > 0 && llenaWh > 0 && dPct >= 4)
        {
            var esperado = dWh.Value / llenaWh.Value * 100;
            if (dPct / esperado > 1.4)
                avisos.Add((Veredicto.Aviso, Tf("El porcentaje ha bajado {0} puntos, cuando por la energía gastada deberían ser unos {1}: el indicador está descalibrado o la batería tiene menos capacidad de la que informa.", dPct, Formato.N(esperado, "0"))));
        }

        if (ligero && autonomia is TimeSpan t && t < TimeSpan.FromHours(1.5) && dur >= TimeSpan.FromMinutes(5))
            avisos.Add((Veredicto.Aviso, Tf("Con un uso ligero solo daría para unas {0} horas: autonomía muy corta.", Formato.N(t.TotalHours, "0.0"))));
        if (dur < TimeSpan.FromMinutes(10))
            avisos.Add((Veredicto.Pendiente, Tx("La prueba ha sido corta: la estimación es aproximada.")));

        return new ResultadoDescarga(dur, pct0, pctN, dWh, media, autonomia, nueva, caidas, avisos);
    }
}

/// <summary>
/// Prueba de descarga guardada en disco a cada lectura: si el equipo se apaga o Bancada se cierra a mitad,
/// al volver a abrirlo se ve hasta dónde llegó (y a qué porcentaje se apagó, que también es un dato).
/// </summary>
public sealed class RegistroDescarga
{
    public DateTime Inicio { get; set; }
    public ModoDescarga Modo { get; set; }
    /// <summary>Brillo fijado durante la prueba (null si se dejó como estaba o no se puede controlar).</summary>
    public int? Brillo { get; set; }
    /// <summary>Brillo que había antes, para dejarlo igual al terminar.</summary>
    public int? BrilloAntes { get; set; }
    public double? LlenaWh { get; set; }
    public double? DisenoWh { get; set; }
    /// <summary>Porcentaje al que se activó el ahorro de batería del sistema (null si no se activó).</summary>
    public int? AhorroAl { get; set; }
    public bool Terminada { get; set; }
    public List<MuestraDescarga> Muestras { get; set; } = new();

    static string Ruta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bancada", "descarga.json");

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            var temporal = Ruta + ".tmp";
            File.WriteAllText(temporal, System.Text.Json.JsonSerializer.Serialize(this));
            File.Move(temporal, Ruta, true);
        }
        catch { }
    }

    public static RegistroDescarga? Leer()
    {
        try { return File.Exists(Ruta) ? System.Text.Json.JsonSerializer.Deserialize<RegistroDescarga>(File.ReadAllText(Ruta)) : null; }
        catch { return null; }
    }
}

/// <summary>Impide que el equipo se suspenda o apague la pantalla mientras dura una prueba larga.</summary>
internal sealed class MantenerDespierto : IDisposable
{
    readonly List<Process> inhibidores = new();
    readonly bool windows;

    public MantenerDespierto(string motivo)
    {
        if (OperatingSystem.IsWindows())
        {
            // Se aplica al hilo que llama (el de la interfaz, que vive mientras la aplicación).
            windows = SetThreadExecutionState(0x80000000 | 0x1 | 0x2) != 0;
            return;
        }
        // GNOME respeta su propio inhibidor para no apagar la pantalla; systemd-inhibit evita la suspensión en todos.
        // «tail --pid» termina solo si Bancada se cierra de golpe: el bloqueo nunca se queda colgado.
        var espera = $"tail --pid={Environment.ProcessId} -f /dev/null";
        Lanzar("gnome-session-inhibit", $"--inhibit idle:suspend --reason \"{motivo}\" {espera}");
        Lanzar("systemd-inhibit", $"--what=idle:sleep --who=Bancada --why=\"{motivo}\" --mode=block {espera}");
    }

    void Lanzar(string programa, string args)
    {
        var ruta = Plataforma.Buscar(programa);
        if (ruta == null) return;
        try
        {
            var p = Process.Start(new ProcessStartInfo(ruta, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p != null) inhibidores.Add(p);
        }
        catch { }
    }

    public void Dispose()
    {
        if (windows) SetThreadExecutionState(0x80000000);
        foreach (var p in inhibidores)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            p.Dispose();
        }
        inhibidores.Clear();
    }

    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint estado);
}
