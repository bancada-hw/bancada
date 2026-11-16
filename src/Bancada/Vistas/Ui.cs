using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Bancada.Modelo;

namespace Bancada.Vistas;

/// <summary>Piezas de interfaz comunes. El aspecto vive en Estilo/Estilos.axaml; aquí solo se montan.</summary>
internal static class Ui
{
    public static T Clases<T>(this T control, string clases) where T : StyledElement
    {
        foreach (var c in clases.Split(' ', StringSplitOptions.RemoveEmptyEntries)) control.Classes.Add(c);
        return control;
    }

    public static TextBlock Texto(string texto, string clases = "") =>
        new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap }.Clases(clases);

    public static ScrollViewer Pagina(out StackPanel cuerpo)
    {
        cuerpo = new StackPanel { Spacing = 18, Margin = new Thickness(40, 34, 40, 40), MaxWidth = 1100 };
        return new ScrollViewer
        {
            Content = cuerpo,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    public static Control Cabecera(string titulo, string explicacion)
    {
        var p = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        p.Children.Add(Texto(titulo, "titulo"));
        p.Children.Add(Texto(explicacion, "subtitulo"));
        return p;
    }

    public static Border Tarjeta(string? seccion, params Control[] hijos)
    {
        var panel = new StackPanel { Spacing = 14 };
        if (seccion != null) panel.Children.Add(Texto(seccion.ToUpperInvariant(), "seccion"));
        foreach (var h in hijos) panel.Children.Add(h);
        return new Border { Child = panel }.Clases("tarjeta");
    }

    public static Control Dato(string etiqueta, string valor, bool mono = false)
    {
        var p = new StackPanel { Spacing = 2 };
        p.Children.Add(Texto(etiqueta, "etiqueta"));
        p.Children.Add(Texto(string.IsNullOrWhiteSpace(valor) ? "—" : valor, mono ? "valor mono" : "valor"));
        return p;
    }

    public static string Clase(Veredicto v) => v switch
    {
        Veredicto.Ok => "ok",
        Veredicto.Aviso => "aviso",
        Veredicto.Fallo => "fallo",
        _ => ""
    };

    public static void FijarEstado(StyledElement e, Veredicto v)
    {
        e.Classes.Set("ok", v == Veredicto.Ok);
        e.Classes.Set("aviso", v == Veredicto.Aviso);
        e.Classes.Set("fallo", v == Veredicto.Fallo);
    }

    public static Border Chip(string texto, Veredicto estado)
    {
        var punto = new Ellipse().Clases("punto");
        FijarEstado(punto, estado);
        var chip = new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { punto, Texto(texto) }
            }
        }.Clases("chip");
        FijarEstado(chip, estado);
        return chip;
    }

    public static string TextoVeredicto(Veredicto v) => v switch
    {
        Veredicto.Ok => Tx("Funciona"),
        Veredicto.Aviso => Tx("Con defectos"),
        Veredicto.Fallo => Tx("No funciona"),
        Veredicto.NoAplica => Tx("No aplica"),
        _ => Tx("Sin probar")
    };

    /// <summary>Cifra grande con su unidad y un título encima (velocidades, temperaturas…).</summary>
    public static StackPanel Cifra(string titulo, string valor, string unidad, out TextBlock textoValor)
    {
        var p = new StackPanel { Spacing = 2 };
        p.Children.Add(Texto(titulo.ToUpperInvariant(), "seccion"));
        var linea = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        textoValor = Texto(valor, "cifra");
        textoValor.TextWrapping = TextWrapping.NoWrap;
        linea.Children.Add(textoValor);
        var u = Texto(unidad, "etiqueta");
        u.VerticalAlignment = VerticalAlignment.Bottom;
        u.Margin = new Thickness(0, 0, 0, 7);
        linea.Children.Add(u);
        p.Children.Add(linea);
        return p;
    }

    public static StackPanel Cifra(string titulo, string valor, string unidad) => Cifra(titulo, valor, unidad, out _);

    /// <summary>Reparte controles en columnas iguales.</summary>
    public static Grid Columnas(params Control[] hijos)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", hijos.Select(_ => "*"))) };
        for (int i = 0; i < hijos.Length; i++)
        {
            Grid.SetColumn(hijos[i], i);
            g.Children.Add(hijos[i]);
        }
        return g;
    }

    public static Border Aviso(string texto) => new Border { Child = Texto(texto, "valor") }.Clases("aviso-caja");

    public static Border Hundido(Control hijo) => new Border { Child = hijo }.Clases("hundido");

    /// <summary>Fila de botones con separación.</summary>
    public static WrapPanel Botones(params Control[] botones)
    {
        var w = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in botones)
        {
            b.Margin = new Thickness(0, 0, 10, 0);
            w.Children.Add(b);
        }
        return w;
    }

    /// <summary>Indicador con aspecto de tecla que se enciende al comprobar algo (botones del ratón, puertos…).</summary>
    public static Border Indicador(string texto, double ancho = 120, double alto = 46)
    {
        return new Border
        {
            Width = ancho,
            Height = alto,
            Margin = new Thickness(0, 0, 8, 8),
            Child = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap }
        }.Clases("tecla");
    }

    /// <summary>Duración en texto: "3 h 20 min", "12 min", "45 s".</summary>
    public static string Duracion(TimeSpan t) =>
        t.TotalHours >= 1 ? Tf("{0} h {1} min", (int)t.TotalHours, t.Minutes) :
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min" : $"{Math.Max(0, (int)t.TotalSeconds)} s";

    public static Button Boton(string texto, string clase, EventHandler<RoutedEventArgs> alPulsar)
    {
        var b = new Button { Content = texto }.Clases(clase);
        b.Click += alPulsar;
        return b;
    }

    /// <summary>Fila con texto a la izquierda y un control alineado a la derecha.</summary>
    public static Grid Fila(Control izquierda, Control derecha)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        izquierda.VerticalAlignment = VerticalAlignment.Center;
        derecha.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(derecha, 1);
        g.Children.Add(izquierda);
        g.Children.Add(derecha);
        return g;
    }

    /// <summary>Dos columnas de tarjetas que se reparten el ancho.</summary>
    public static Grid DosColumnas(IEnumerable<Control> izquierda, IEnumerable<Control> derecha)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,*") };
        var a = new StackPanel { Spacing = 18 };
        var b = new StackPanel { Spacing = 18 };
        foreach (var c in izquierda) a.Children.Add(c);
        foreach (var c in derecha) b.Children.Add(c);
        Grid.SetColumn(b, 2);
        g.Children.Add(a);
        g.Children.Add(b);
        return g;
    }

    public static Control Cargando(string texto)
    {
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(Texto(texto, "suave"));
        p.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 });
        return p;
    }

    /// <summary>Barra horizontal de 0 a 100 coloreada por el valor.</summary>
    public static Control Barra(int porcentaje, double ancho = 120)
    {
        var p = Math.Clamp(porcentaje, 0, 100);
        var estado = p >= 60 ? Veredicto.Ok : p >= 35 ? Veredicto.Aviso : Veredicto.Fallo;
        var relleno = new Border { Width = ancho * p / 100, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) }.Clases("barra");
        FijarEstado(relleno, estado);
        return new Border { Width = ancho, Child = relleno, VerticalAlignment = VerticalAlignment.Center }.Clases("barra-fondo");
    }
}

/// <summary>Iconos de línea en una cuadrícula de 24×24.</summary>
internal static class Iconos
{
    public const string Chip = "M6,6 H18 V18 H6 Z M9.5,9.5 H14.5 V14.5 H9.5 Z M9,2 V6 M15,2 V6 M9,18 V22 M15,18 V22 M2,9 H6 M2,15 H6 M18,9 H22 M18,15 H22";
    public const string Teclado = "M2,6 H22 V18 H2 Z M5.5,9.5 H6.5 M9,9.5 H10 M12.5,9.5 H13.5 M16,9.5 H17 M19,9.5 H19.5 M5.5,12.5 H6.5 M9,12.5 H10 M12.5,12.5 H13.5 M16,12.5 H18.5 M7,15.2 H17";
    public const string Pantalla = "M3,4 H21 V16 H3 Z M8,20 H16 M12,16 V20";
    public const string Red = "M2.5,9 C7.8,3.8 16.2,3.8 21.5,9 M5.5,12.3 C9.2,8.8 14.8,8.8 18.5,12.3 M8.6,15.6 C10.6,13.8 13.4,13.8 15.4,15.6 M12,19 L12,19.2";
    public const string Puntero = "M12,3 C8.1,3 6,5.6 6,9 V15 C6,18.4 8.1,21 12,21 C15.9,21 18,18.4 18,15 V9 C18,5.6 15.9,3 12,3 Z M12,6.5 V10 M6,10.5 H18";
    public const string Audio = "M4,9 H8 L13,5 V19 L8,15 H4 Z M16.5,8.5 C18.2,10.2 18.2,13.8 16.5,15.5 M19,6 C22.3,9.3 22.3,14.7 19,18";
    public const string Camara = "M3,7.5 H7 L9,5 H15 L17,7.5 H21 V19 H3 Z M12,9.8 A3.4,3.4 0 1 0 12,16.6 A3.4,3.4 0 1 0 12,9.8 Z";
    public const string Usb = "M8.5,3 H15.5 V9 H8.5 Z M10.6,5.6 H10.9 M13.1,5.6 H13.4 M6.5,9 H17.5 V14.5 C17.5,17.5 15.2,19.5 12,19.5 C8.8,19.5 6.5,17.5 6.5,14.5 Z M12,19.5 V22";
    public const string Bateria = "M2.5,7 H19 V17 H2.5 Z M19,10 H21.5 V14 H19 M6,10.5 V13.5 M9.5,10.5 V13.5 M13,10.5 V13.5";
    public const string Discos = "M4,6 C4,3.4 20,3.4 20,6 V18 C20,20.6 4,20.6 4,18 Z M4,6 C4,8.6 20,8.6 20,6 M4,12 C4,14.6 20,14.6 20,12";
    public const string Memoria = "M2,7 H22 V15 H2 Z M5,10 H7 V12 H5 Z M9,10 H11 V12 H9 Z M13,10 H15 V12 H13 Z M17,10 H19 V12 H17 Z M5,15 V18 M9,15 V18 M15,15 V18 M19,15 V18";
    public const string Temperatura = "M10,4.5 C10,2.5 14,2.5 14,4.5 V13.6 C16.2,15 16.2,19.5 12,20.5 C7.8,19.5 7.8,15 10,13.6 Z M12,9 V16.5";
    public const string Grafica = "M2,6.5 H19 V16.5 H2 Z M19,9.5 H22 M19,13.5 H22 M5,16.5 V19.5 M9,16.5 V19.5 M13,16.5 V19.5 M10.5,8.5 A3,3 0 1 0 10.5,14.5 A3,3 0 1 0 10.5,8.5 Z";
    public const string Suspension = "M19.5,14.5 A8,8 0 1 1 9.5,4.5 A6.5,6.5 0 0 0 19.5,14.5 Z M16,4 H19 L16,7.5 H19";
    public const string Placa = "M3,3 H21 V21 H3 Z M7,7 H11 V11 H7 Z M14,8 H17.5 M14,10.5 H17.5 M7,15 H17 V17.5 M11,9 H14";
    public const string Revision = "M8.5,4 H6 V21.5 H18 V4 H15.5 M8.5,2.5 H15.5 V5.5 H8.5 Z M9,13.5 L11.2,15.7 L15.5,11";
    public const string Informe ="M6,2.5 H14 L19,7.5 V21.5 H6 Z M14,2.5 V7.5 H19 M9,12 H16 M9,15.5 H16 M9,19 H13";
}
