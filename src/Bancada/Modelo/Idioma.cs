using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bancada.Modelo;

/// <summary>
/// Idioma de la interfaz. Los textos se escriben en castellano en el código y se traducen con
/// <see cref="Tx"/> (texto fijo) y <see cref="Tf"/> (texto con datos, al estilo de string.Format).
/// Cada traducción es un archivo Idiomas/xx.json que asocia el texto en castellano con el traducido:
/// para añadir un idioma basta con copiar en.json, traducir los valores y compilar.
/// </summary>
public static class Idioma
{
    public const string Original = "es";
    static Dictionary<string, string>? tabla;

    /// <summary>Código del idioma en uso ("es", "en"…).</summary>
    public static string Actual { get; private set; } = Original;

    /// <summary>Idiomas disponibles: el castellano y los que tienen archivo de traducción.</summary>
    public static IReadOnlyList<(string Codigo, string Nombre)> Disponibles { get; } = CargarDisponibles();

    /// <summary>Formato de números del idioma en uso (separador decimal y de miles).</summary>
    public static NumberFormatInfo Numeros { get; private set; } = CrearNumeros(Original);

    public static event Action? Cambio;

    public static string Tx(string texto) =>
        tabla != null && tabla.TryGetValue(texto, out var t) && t.Length > 0 ? t : texto;

    public static string Tf(string formato, params object?[] datos)
    {
        var f = Tx(formato);
        try { return string.Format(Numeros, f, datos); }
        catch (FormatException) { return string.Format(Numeros, formato, datos); } // traducción mal escrita: se usa el original
    }

    /// <summary>Elige el idioma guardado o, si no hay, el del sistema (castellano o, si no hay traducción, inglés).</summary>
    public static void Iniciar(string? forzado = null)
    {
        var codigo = forzado ?? Ajustes.Leer("idioma") ?? DelSistema();
        if (Disponibles.All(d => d.Codigo != codigo)) codigo = Disponibles.Any(d => d.Codigo == "en") ? "en" : Original;
        Fijar(codigo, guardar: false);
    }

    public static void Fijar(string codigo, bool guardar = true)
    {
        tabla = codigo == Original ? null : Cargar(codigo);
        Actual = tabla != null || codigo == Original ? codigo : Original;
        Numeros = CrearNumeros(Actual);
        if (guardar) Ajustes.Guardar("idioma", Actual);
        Cambio?.Invoke();
    }

    static NumberFormatInfo CrearNumeros(string codigo) => codigo switch
    {
        "en" => new NumberFormatInfo { NumberDecimalSeparator = ".", NumberGroupSeparator = "," },
        // En francés los miles se separan con un espacio que no se parte en dos líneas: 13 424,5
        "fr" => new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = " " },
        _ => new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." }
    };

    static string Recurso(string codigo) => $"Idiomas.{codigo}.json";

    static IReadOnlyList<(string, string)> CargarDisponibles()
    {
        var lista = new List<(string, string)> { (Original, "Español") };
        foreach (var r in Assembly.GetExecutingAssembly().GetManifestResourceNames().Order())
        {
            if (!r.StartsWith("Idiomas.") || !r.EndsWith(".json")) continue;
            var codigo = r["Idiomas.".Length..^".json".Length];
            var t = Cargar(codigo);
            lista.Add((codigo, t != null && t.TryGetValue("__nombre", out var n) ? n : codigo));
        }
        return lista;
    }

    static Dictionary<string, string>? Cargar(string codigo)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Recurso(codigo));
            if (s == null) return null;
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString() ?? "";
            return d;
        }
        catch { return null; }
    }

    /// <summary>Idioma del sistema: en Windows, el de la interfaz; en Linux, LC_ALL, LC_MESSAGES o LANG.</summary>
    static string DelSistema()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return (GetUserDefaultUILanguage() & 0x3FF) switch
                {
                    0x0A => "es", 0x09 => "en", 0x16 => "pt", 0x0C => "fr", 0x07 => "de", 0x10 => "it", 0x03 => "ca", 0x2D => "eu", 0x56 => "gl",
                    _ => "en"
                };
            foreach (var v in new[] { "LC_ALL", "LC_MESSAGES", "LANG", "LANGUAGE" })
            {
                var valor = Environment.GetEnvironmentVariable(v);
                if (string.IsNullOrEmpty(valor) || valor is "C" or "POSIX" || valor.StartsWith("C.")) continue;
                return valor.Split('_', '.', ':', '@')[0].ToLowerInvariant();
            }
        }
        catch { }
        return "en";
    }

    [DllImport("kernel32.dll")]
    static extern ushort GetUserDefaultUILanguage();
}

/// <summary>Ajustes que se recuerdan entre usos (por ahora, el idioma). Se guardan en la carpeta de configuración del usuario.</summary>
public static class Ajustes
{
    static readonly string Ruta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bancada", "ajustes.json");

    public static string? Leer(string clave)
    {
        try
        {
            if (!File.Exists(Ruta)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(Ruta));
            return doc.RootElement.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch { return null; }
    }

    public static void Guardar(string clave, string valor)
    {
        try
        {
            var datos = new Dictionary<string, string>();
            if (File.Exists(Ruta))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Ruta));
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) datos[p.Name] = p.Value.GetString() ?? "";
            }
            datos[clave] = valor;
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(datos, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

/// <summary>Pruebas largas en marcha (estrés, descarga, memoria…): mientras haya alguna no se cambia de idioma.</summary>
public static class PruebasEnCurso
{
    static int n;
    public static bool Hay => Volatile.Read(ref n) > 0;
    public static void Entrar() => Interlocked.Increment(ref n);
    public static void Salir() => Interlocked.Decrement(ref n);
}
