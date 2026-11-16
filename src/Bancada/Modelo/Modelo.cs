using System.Globalization;
using System.Text.RegularExpressions;

namespace Bancada.Modelo;

public enum Veredicto { Pendiente, Ok, Aviso, Fallo, NoAplica }

/// <summary>Las pruebas de Bancada, en el orden en que aparecen.</summary>
public static class Pruebas
{
    public static (string Id, string Nombre)[] Lista => new (string Id, string Nombre)[]
    {
        ("teclado", Tx("Teclado")),
        ("puntero", Tx("Touchpad y ratón")),
        ("pantalla", Tx("Pantalla")),
        ("audio", Tx("Sonido")),
        ("camara", Tx("Cámara")),
        ("red", Tx("Red")),
        ("usb", Tx("Puertos USB")),
        ("bateria", Tx("Batería y cargador")),
        ("discos", Tx("Discos")),
        ("memoria", Tx("Memoria RAM")),
        ("procesador", Tx("Procesador y temperaturas")),
        ("grafica", Tx("Gráfica")),
        ("suspension", Tx("Suspensión")),
        ("placa", Tx("Placa base")),
    };

    public static string Nombre(string id) => Lista.FirstOrDefault(p => p.Id == id).Nombre ?? id;
}

public enum TipoEquipo { Desconocido, Portatil, Sobremesa, TodoEnUno, MiniPc, Tableta }

public sealed class InfoEquipo
{
    public string Marca { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string NumeroSerie { get; set; } = "";
    public TipoEquipo Tipo { get; set; }
    public string SistemaOperativo { get; set; } = "";
    public string Bios { get; set; } = "";
    public string Placa { get; set; } = "";
    public string Procesador { get; set; } = "";
    public int NucleosFisicos { get; set; }
    public int HilosLogicos { get; set; }
    public long RamTotalBytes { get; set; }
    public int? RanurasRam { get; set; }
    public List<ModuloRam> ModulosRam { get; } = new();
    public List<string> Graficas { get; } = new();
    public List<Disco> Discos { get; } = new();
    public Bateria? Bateria { get; set; }
    public bool EsVirtual { get; set; }
    public List<string> Notas { get; } = new();
}

public sealed record ModuloRam(string Ranura, long Bytes, int MHz, string Fabricante, string Referencia);

public sealed record Disco(string Modelo, long Bytes, string Tipo, string Conexion, string Salud);

public sealed record Bateria(int? CargaPct, string Estado, int? SaludPct, int? Ciclos);

/// <summary>Estado de la sesión de diagnóstico: equipo detectado y veredicto de cada prueba.</summary>
public static class Sesion
{
    static readonly Dictionary<string, Veredicto> veredictos = new();
    static readonly Dictionary<string, List<string>> detalles = new();

    public static InfoEquipo? Equipo { get; private set; }
    public static event Action<string, Veredicto>? Cambio;
    public static event Action? EquipoDetectado;

    public static Veredicto Obtener(string prueba) =>
        veredictos.TryGetValue(prueba, out var v) ? v : Veredicto.Pendiente;

    public static void Fijar(string prueba, Veredicto v)
    {
        veredictos[prueba] = v;
        Cambio?.Invoke(prueba, v);
    }

    /// <summary>Lo medido en una prueba, para el informe. Cada llamada sustituye lo anterior de esa parte.</summary>
    public static void Anotar(string prueba, string parte, params string[] lineas)
    {
        lock (detalles)
        {
            if (!detalles.TryGetValue(prueba, out var lista)) detalles[prueba] = lista = new();
            lista.RemoveAll(l => l.StartsWith(parte + ": "));
            foreach (var l in lineas.Where(l => !string.IsNullOrWhiteSpace(l))) lista.Add($"{parte}: {l}");
        }
    }

    public static IReadOnlyList<string> Detalles(string prueba)
    {
        lock (detalles) return detalles.TryGetValue(prueba, out var l) ? l.ToList() : new List<string>();
    }

    public static void PublicarEquipo(InfoEquipo equipo)
    {
        Equipo = equipo;
        EquipoDetectado?.Invoke();
        detectado.TrySetResult();
    }

    static readonly TaskCompletionSource detectado = new();

    /// <summary>Espera a que termine la detección del equipo (las pruebas que dependen del tipo de equipo la necesitan).</summary>
    public static Task EsperarEquipoAsync() => detectado.Task;
}

/// <summary>
/// Ritmo de trabajo de una prueba de estrés, medido con el tiempo real entre lecturas (el temporizador de la
/// interfaz no marca exactamente cada segundo, y menos con el equipo cargado). Referencia: el mejor momento de
/// la prueba (la mayor mediana de 5 lecturas seguidas), porque hay gráficas que tardan medio minuto en subir de
/// reloj; final: la mediana de las últimas 10 lecturas. Así lo que se mide es cuánto se frena.
/// </summary>
public sealed class Ritmo
{
    readonly List<(double seg, double porSegundo)> muestras = new();
    double segPrevio;
    long previo;

    public void Reiniciar()
    {
        muestras.Clear();
        segPrevio = 0;
        previo = 0;
    }

    /// <summary>Añade una lectura del contador; devuelve el ritmo en % de la referencia (null mientras no la hay).</summary>
    public double? Anotar(double seg, long contador, double desde = 2)
    {
        var dt = seg - segPrevio;
        if (dt < 0.25) return null;
        var porSegundo = (contador - previo) / dt;
        segPrevio = seg;
        previo = contador;
        if (seg < desde || porSegundo <= 0) return null;
        muestras.Add((seg, porSegundo));
        return Referencia is double r && r > 0 ? porSegundo * 100 / r : null;
    }

    public double? Referencia
    {
        get
        {
            if (muestras.Count == 0) return null;
            const int ancho = 5;
            if (muestras.Count < ancho) return Mediana(muestras.Select(m => m.porSegundo).ToList());
            double mejor = 0;
            for (int i = 0; i + ancho <= muestras.Count; i++)
                mejor = Math.Max(mejor, Mediana(muestras.Skip(i).Take(ancho).Select(m => m.porSegundo).ToList()));
            return mejor;
        }
    }

    /// <summary>Rendimiento final en % de la referencia.</summary>
    public double Final => Referencia is double r && r > 0 && muestras.Count > 0
        ? Mediana(muestras.TakeLast(10).Select(m => m.porSegundo).ToList()) * 100 / r
        : 100;

    static double Mediana(List<double> v)
    {
        v.Sort();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}

/// <summary>
/// Junta lo que sale en cada parte de una prueba (cada gráfica, Wi-Fi, conexión…) y propone el peor resultado
/// como veredicto. Si el técnico ha puesto otro a mano, no se toca.
/// </summary>
public sealed class Propuesta(string prueba)
{
    readonly Dictionary<string, Veredicto> partes = new();
    Veredicto? propuesto;

    /// <summary>Resultado de una parte; Pendiente cuenta como correcto (por ejemplo, «este sobremesa no tiene Wi-Fi»).</summary>
    public void Parte(string parte, Veredicto v, bool proponer = true)
    {
        partes[parte] = v;
        if (proponer) Proponer();
    }

    public void Proponer()
    {
        if (partes.Count == 0) return;
        var peor = partes.Values.Select(v => v is Veredicto.Pendiente or Veredicto.NoAplica ? Veredicto.Ok : v).Max();
        var actual = Sesion.Obtener(prueba);
        if (actual != Veredicto.Pendiente && actual != propuesto) return;
        propuesto = peor;
        Sesion.Fijar(prueba, peor);
    }
}

/// <summary>Formato de números según el idioma elegido (coma o punto decimal), sin depender de la cultura del sistema.</summary>
public static class Formato
{
    public static string N(double v, string formato = "0.#") => v.ToString(formato, Idioma.Numeros);

    // Las unidades también se traducen: en francés son Go, To, Mo y Mbit/s.
    public static string Bytes(long b) =>
        b >= 1_000_000_000_000 ? N(b / 1e12, "0.#") + " " + Tx("TB") :
        b >= 1_000_000_000 ? N(b / 1e9, "0") + " " + Tx("GB") :
        b >= 1_000_000 ? N(b / 1e6, "0") + " " + Tx("MB") : b + " " + Tx("B");

    public static string Ram(long b) => N(Math.Round(b / 1073741824.0), "0") + " " + Tx("GB");

    public static string Bps(long bps) =>
        bps <= 0 ? "" : bps >= 1_000_000_000 ? N(bps / 1e9, "0.#") + " " + Tx("Gbps") : N(bps / 1e6, "0") + " " + Tx("Mbps");
}

public static class TextoEquipo
{
    public static string Tipo(TipoEquipo t) => t switch
    {
        TipoEquipo.Portatil => Tx("Portátil"),
        TipoEquipo.Sobremesa => Tx("Sobremesa"),
        TipoEquipo.TodoEnUno => Tx("Todo en uno"),
        TipoEquipo.MiniPc => Tx("Mini PC"),
        TipoEquipo.Tableta => Tx("Tableta"),
        _ => Tx("Tipo sin identificar")
    };

    /// <summary>Tipo de equipo a partir del tipo de chasis SMBIOS.</summary>
    public static TipoEquipo DesdeChasis(IEnumerable<int> tipos, bool tieneBateria)
    {
        foreach (var t in tipos)
        {
            switch (t)
            {
                case 8 or 9 or 10 or 14 or 31 or 32: return TipoEquipo.Portatil;
                case 30: return TipoEquipo.Tableta;
                case 13: return TipoEquipo.TodoEnUno;
                case 35 or 36: return TipoEquipo.MiniPc;
                case 3 or 4 or 5 or 6 or 7 or 15 or 16 or 24: return TipoEquipo.Sobremesa;
            }
        }
        return tieneBateria ? TipoEquipo.Portatil : TipoEquipo.Desconocido;
    }

    static readonly Regex Relleno = new(
        @"^(to be filled.*|system serial.*|default string|none|not applicable|not specified|system product name|system manufacturer|system version|o\.e\.m\..*|0+|x+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Quita los textos de relleno que dejan algunos fabricantes en la BIOS.</summary>
    public static string Limpio(string? s)
    {
        var t = (s ?? "").Trim();
        return Relleno.IsMatch(t) ? "" : t;
    }

    public static string Marca(string? m)
    {
        var s = Limpio(m);
        var l = s.ToLowerInvariant();
        if (l.StartsWith("lenovo")) return "Lenovo";
        if (l.StartsWith("hewlett") || l == "hp" || l.StartsWith("hp ")) return "HP";
        if (l.StartsWith("dell")) return "Dell";
        if (l.StartsWith("asus")) return "ASUS";
        if (l.StartsWith("acer")) return "Acer";
        if (l.StartsWith("micro-star") || l.StartsWith("msi")) return "MSI";
        if (l.StartsWith("gigabyte")) return "Gigabyte";
        if (l.StartsWith("asrock")) return "ASRock";
        if (l.StartsWith("apple")) return "Apple";
        if (l.StartsWith("microsoft")) return "Microsoft";
        if (l.StartsWith("samsung")) return "Samsung";
        if (l.StartsWith("fujitsu")) return "Fujitsu";
        if (l.StartsWith("intel")) return "Intel";
        if (l.StartsWith("huawei")) return "Huawei";
        if (l.StartsWith("xiaomi")) return "Xiaomi";
        if (l.StartsWith("medion")) return "Medion";
        return s;
    }

    public static string Procesador(string nombre)
    {
        var n = Hardware(nombre);
        n = Regex.Replace(n, @"\s+(CPU\s+)?@.*$", "");
        return Regex.Replace(n, @"\s+CPU$", "").Trim();
    }

    /// <summary>Nombre de un componente sin las marcas (R) y (TM) que meten los fabricantes.</summary>
    public static string Hardware(string nombre)
    {
        var n = Regex.Replace(nombre, @"\((R|TM)\)|®|™", "", RegexOptions.IgnoreCase);
        return Regex.Replace(n, @"\s+", " ").Trim();
    }
}
