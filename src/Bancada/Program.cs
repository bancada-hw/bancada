using Avalonia;

namespace Bancada;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Opciones.Leer(args);
        Modelo.Idioma.Iniciar(Opciones.Idioma);
        Modelo.Teclados.Forzada = Opciones.Teclado;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/// <summary>
/// Opciones de línea de órdenes. Sirven para sacar capturas automáticas de cada pantalla
/// (documentación y pruebas): Bancada --captura salida.png --vista red --tema oscuro
/// </summary>
internal static class Opciones
{
    public static string? Captura { get; private set; }
    public static string? Vista { get; private set; }
    public static string? Tema { get; private set; }
    public static int Espera { get; private set; } = 6;
    public static bool Completa { get; private set; }
    /// <summary>--informe salida.pdf: genera el informe en PDF tras detectar el equipo y se cierra.</summary>
    public static string? Informe { get; private set; }
    /// <summary>--idioma en: abre en ese idioma sin cambiar el guardado (capturas y pruebas).</summary>
    public static string? Idioma { get; private set; }
    /// <summary>--cambiar-idioma en: cambia de idioma con la ventana abierta antes de capturar (prueba del cambio en caliente).</summary>
    public static string? CambiarA { get; private set; }
    /// <summary>--teclado en-GB: distribución de teclado para capturas y pruebas (no se guarda).</summary>
    public static string? Teclado { get; private set; }
    /// <summary>--revision: empieza la revisión completa al abrir (pruebas y capturas).</summary>
    public static bool Revision { get; private set; }
    /// <summary>--minutos 1: duración de las pruebas de estrés de la revisión completa (por defecto 3).</summary>
    public static int? MinutosEstres { get; private set; }
    /// <summary>--cada 20: con --captura, una captura cada 20 segundos recorriendo la revisión completa.</summary>
    public static int? Cada { get; private set; }
    /// <summary>--suspender: abre la prueba de suspensión y la lanza sola (con --captura, captura al terminar). Suspende el equipo de verdad.</summary>
    public static bool Suspender { get; private set; }

    public static void Leer(string[] args)
    {
        Completa = args.Contains("--completa");
        Revision = args.Contains("--revision");
        Suspender = args.Contains("--suspender");
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--captura": Captura = args[++i]; break;
                case "--informe": Informe = args[++i]; break;
                case "--idioma": Idioma = args[++i]; break;
                case "--cambiar-idioma": CambiarA = args[++i]; break;
                case "--teclado": Teclado = args[++i]; break;
                case "--vista": Vista = args[++i]; break;
                case "--tema": Tema = args[++i]; break;
                case "--espera": if (int.TryParse(args[++i], out var s)) Espera = s; break;
                case "--minutos": if (int.TryParse(args[++i], out var m) && m > 0) MinutosEstres = m; break;
                case "--cada": if (int.TryParse(args[++i], out var c) && c > 0) Cada = c; break;
            }
        }
    }
}
