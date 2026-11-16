using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Bancada.Sistema;

namespace Bancada.Modelo;

/// <summary>Forma física del teclado: ISO (Intro alto y tecla junto a Mayús izquierda), ANSI (EE. UU.) o ABNT2 (Brasil).</summary>
public enum FormaTeclado { Iso, Ansi, Abnt2 }

/// <summary>Una distribución de teclado: su forma y lo que va rotulado en cada tecla de caracteres (por código físico).</summary>
public sealed record Distribucion(string Id, string Nombre, FormaTeclado Forma, string Idioma, IReadOnlyDictionary<string, string> Leyendas)
{
    /// <summary>Rótulo de una tecla; las letras que no cambian se rotulan con su propia letra.</summary>
    public string Leyenda(string codigo, string porDefecto) => Leyendas.TryGetValue(codigo, out var v) ? v : porDefecto;
}

/// <summary>
/// Distribuciones de teclado disponibles. Por defecto se usa la del idioma de la interfaz, afinando la variante
/// con el teclado del sistema (inglés de EE. UU. o del Reino Unido, español de España o de Latinoamérica…).
/// Si se elige una a mano, se recuerda y no cambia con el idioma.
/// </summary>
public static class Teclados
{
    /// <summary>Distribución forzada desde la línea de órdenes (capturas y pruebas); no se guarda.</summary>
    public static string? Forzada { get; set; }

    // «Código=rótulo» separados por espacios; «¶» es un salto de línea dentro del rótulo.
    public static IReadOnlyList<Distribucion> Lista { get; } = new[]
    {
        Crear("es-ES", "Español (España)", FormaTeclado.Iso, "es",
            "Backquote=º Minus=' Equal=¡ BracketLeft=` BracketRight=+ Semicolon=Ñ Quote=´ Backslash=Ç IntlBackslash=< Comma=, Period=. Slash=-"),
        Crear("es-LA", "Español (Latinoamérica)", FormaTeclado.Iso, "es",
            "Backquote=| Minus=' Equal=¿ BracketLeft=´ BracketRight=+ Semicolon=Ñ Quote={ Backslash=} IntlBackslash=< Comma=, Period=. Slash=-"),
        Crear("en-US", "English (US)", FormaTeclado.Ansi, "en",
            "Backquote=` Minus=- Equal== BracketLeft=[ BracketRight=] Backslash=\\ Semicolon=; Quote=' Comma=, Period=. Slash=/"),
        Crear("en-GB", "English (UK)", FormaTeclado.Iso, "en",
            "Backquote=` Minus=- Equal== BracketLeft=[ BracketRight=] Semicolon=; Quote=' Backslash=# IntlBackslash=\\ Comma=, Period=. Slash=/"),
        Crear("pt-PT", "Português (Portugal)", FormaTeclado.Iso, "pt",
            "Backquote=\\ Minus=' Equal=« BracketLeft=+ BracketRight=´ Semicolon=Ç Quote=º Backslash=~ IntlBackslash=< Comma=, Period=. Slash=-"),
        Crear("pt-BR", "Português (Brasil, ABNT2)", FormaTeclado.Abnt2, "pt",
            "Backquote=' Minus=- Equal== BracketLeft=´ BracketRight=[ Semicolon=Ç Quote=~ Backslash=] IntlBackslash=\\ Comma=, Period=. Slash=; IntlRo=/"),
        Crear("de-DE", "Deutsch (QWERTZ)", FormaTeclado.Iso, "de",
            "Backquote=^ Minus=ß Equal=´ Y=Z Z=Y BracketLeft=Ü BracketRight=+ Semicolon=Ö Quote=Ä Backslash=# IntlBackslash=< Comma=, Period=. Slash=-"),
        Crear("fr-FR", "Français (AZERTY)", FormaTeclado.Iso, "fr",
            "Backquote=² Digit1=1¶& Digit2=2¶é Digit3=3¶\" Digit4=4¶' Digit5=5¶( Digit6=6¶- Digit7=7¶è Digit8=8¶_ Digit9=9¶ç Digit0=0¶à " +
            "Minus=) Equal== Q=A W=Z A=Q Z=W BracketLeft=^ BracketRight=$ Semicolon=M Quote=ù Backslash=* IntlBackslash=< M=, Comma=; Period=: Slash=!"),
        Crear("it-IT", "Italiano", FormaTeclado.Iso, "it",
            "Backquote=\\ Minus=' Equal=ì BracketLeft=è BracketRight=+ Semicolon=ò Quote=à Backslash=ù IntlBackslash=< Comma=, Period=. Slash=-"),
    };

    static Distribucion Crear(string id, string nombre, FormaTeclado forma, string idioma, string leyendas)
    {
        var d = new Dictionary<string, string>();
        foreach (var par in leyendas.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            d[par[..i]] = par[(i + 1)..].Replace('¶', '\n');
        }
        return new Distribucion(id, nombre, forma, idioma, d);
    }

    public static Distribucion? Buscar(string? id) => Lista.FirstOrDefault(d => d.Id == id);

    /// <summary>La que se usa ahora: la forzada, la elegida a mano o la automática.</summary>
    public static Distribucion Actual => Buscar(Forzada) ?? Buscar(Ajustes.Leer("teclado")) ?? Automatica;

    /// <summary>True si el usuario ha elegido una distribución concreta.</summary>
    public static bool ElegidaAMano => Buscar(Ajustes.Leer("teclado")) != null;

    /// <summary>Guarda la elección; null vuelve a la automática.</summary>
    public static void Elegir(string? id)
    {
        if (Forzada != null) { Forzada = id; return; } // en pruebas no se toca lo guardado
        Ajustes.Guardar("teclado", id ?? "auto");
    }

    /// <summary>La del idioma de la interfaz, con la variante del teclado del sistema si es del mismo idioma.</summary>
    public static Distribucion Automatica
    {
        get
        {
            var sistema = Buscar(DelSistema());
            if (sistema != null && sistema.Idioma == Idioma.Actual) return sistema;
            var id = Idioma.Actual switch
            {
                // La interfaz en portugués es la de Brasil: su teclado es el ABNT2.
                "es" => "es-ES", "en" => "en-US", "pt" => "pt-BR", "de" => "de-DE", "fr" => "fr-FR", "it" => "it-IT",
                _ => sistema?.Id ?? "en-US"
            };
            return Buscar(id)!;
        }
    }

    static string? sistemaLeido;
    static bool sistemaConsultado;

    /// <summary>Distribución configurada en el sistema operativo, si se puede saber.</summary>
    public static string? DelSistema()
    {
        if (sistemaConsultado) return sistemaLeido;
        sistemaConsultado = true;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var nombre = new StringBuilder(9);
                if (GetKeyboardLayoutName(nombre)) sistemaLeido = DesdeWindows(nombre.ToString());
            }
            else if (OperatingSystem.IsLinux())
            {
                var xkb = Environment.GetEnvironmentVariable("XKB_DEFAULT_LAYOUT");
                if (string.IsNullOrEmpty(xkb))
                {
                    var estado = Plataforma.Ejecutar("localectl", "status", 3000) ?? "";
                    xkb = Regex.Match(estado, @"X11 Layout:\s*(\S+)").Groups[1].Value;
                }
                sistemaLeido = DesdeLinux(xkb.Split(',')[0].Trim());
            }
        }
        catch { }
        return sistemaLeido;
    }

    /// <summary>Identificador de distribución de Windows (KLID, p. ej. «0000080A») → distribución.</summary>
    static string? DesdeWindows(string klid) => klid.Length < 4 ? null : klid[^4..].ToUpperInvariant() switch
    {
        "040A" or "0C0A" => "es-ES",
        "080A" => "es-LA",
        "0409" => "en-US",
        "0809" => "en-GB",
        "0816" => "pt-PT",
        "0416" => "pt-BR",
        "0407" or "0C07" => "de-DE",
        "040C" or "080C" => "fr-FR",
        "0410" => "it-IT",
        _ => null
    };

    static string? DesdeLinux(string xkb) => xkb switch
    {
        "es" => "es-ES",
        "latam" => "es-LA",
        "us" => "en-US",
        "gb" => "en-GB",
        "pt" => "pt-PT",
        "br" => "pt-BR",
        "de" or "at" => "de-DE",
        "fr" or "be" => "fr-FR",
        "it" => "it-IT",
        _ => null
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetKeyboardLayoutName(StringBuilder nombre);
}
