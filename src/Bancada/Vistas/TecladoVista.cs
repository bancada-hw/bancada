using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Bancada.Modelo;

namespace Bancada.Vistas;

/// <summary>
/// Prueba de teclado: se dibuja el teclado de la distribución elegida (forma ISO, ANSI o ABNT2 y sus rótulos)
/// y se ilumina cada tecla que llega. Se usa la tecla física, no el carácter: cambiar de distribución solo
/// cambia el dibujo, no lo que ya se ha comprobado.
/// </summary>
internal sealed class TecladoVista : UserControl
{
    sealed record DefTecla(string Codigo, string Texto, double X, double Y, double W = 1, double H = 1, bool Numerica = false);

    const double U = 46; // tamaño de una tecla normal, en píxeles de dibujo

    readonly Dictionary<PhysicalKey, Border> teclas = new();
    readonly HashSet<PhysicalKey> numericas = new();
    readonly HashSet<PhysicalKey> pulsadas = new();
    readonly HashSet<string> otrasVistas = new();
    readonly TextBlock contador = Ui.Texto("", "valor negrita");
    readonly TextBlock contadorNumerico = Ui.Texto("", "etiqueta");
    readonly WrapPanel otras = new();
    readonly TextBlock sinOtras = Ui.Texto(Tx("Ninguna todavía. Prueba las combinaciones con Fn (volumen, brillo…)."), "suave");
    readonly ContentControl zonaTeclado = new();
    readonly ComboBox selector = new() { MinWidth = 260 };
    TopLevel? ventana;
    bool rellenando;

    public TecladoVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Teclado"),
            Tx("Pulsa todas las teclas una vez. Cada tecla que el equipo recibe se pone en verde; las que se queden en blanco no funcionan o no envían señal.")));

        RellenarSelector();
        selector.SelectionChanged += (_, _) =>
        {
            if (rellenando || selector.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            Teclados.Elegir(id == "auto" ? null : id);
            ConstruirTeclado();
        };
        var filaDistribucion = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var etiqueta = Ui.Texto(Tx("Distribución del teclado"), "valor");
        etiqueta.VerticalAlignment = VerticalAlignment.Center;
        filaDistribucion.Children.Add(etiqueta);
        filaDistribucion.Children.Add(selector);

        var reiniciar = Ui.Boton(Tx("Empezar de nuevo"), "secundario", (_, _) => Reiniciar());
        var resumen = new StackPanel { Spacing = 2 };
        resumen.Children.Add(contador);
        resumen.Children.Add(contadorNumerico);

        cuerpo.Children.Add(Ui.Tarjeta(null, filaDistribucion, zonaTeclado, Ui.Fila(resumen, reiniciar)));

        var otrasPanel = new StackPanel { Spacing = 8 };
        otrasPanel.Children.Add(sinOtras);
        otrasPanel.Children.Add(otras);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Otras teclas recibidas"), otrasPanel));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Ten en cuenta"),
            Ui.Texto(Tx("• La tecla Fn no envía señal propia: se comprueba con sus combinaciones.\n") +
                     Tx("• En Windows, Alt Gr enciende también Ctrl izquierdo: es normal.\n") +
                     Tx("• Impr Pant puede abrir la herramienta de recortes de Windows.\n") +
                     Tx("• Si el portátil no tiene teclado numérico, ignora ese bloque."), "subtitulo")));

        cuerpo.Children.Add(new BarraVeredicto("teclado", Tx("¿Funcionan todas las teclas?")));
        Content = pagina;
        ConstruirTeclado();

        // Las teclas se capturan en toda la ventana solo mientras esta pantalla está a la vista.
        // Se guarda la ventana al engancharse: al desengancharse ya no se puede buscar, y si no se quita
        // la captura, el resto de la app (por ejemplo, las observaciones del informe) deja de recibir teclas.
        AttachedToVisualTree += (_, _) =>
        {
            ventana = TopLevel.GetTopLevel(this);
            ventana?.AddHandler(KeyDownEvent, AlPulsar, RoutingStrategies.Tunnel, handledEventsToo: true);
            ventana?.AddHandler(KeyUpEvent, AlSoltar, RoutingStrategies.Tunnel, handledEventsToo: true);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ventana?.RemoveHandler(KeyDownEvent, AlPulsar);
            ventana?.RemoveHandler(KeyUpEvent, AlSoltar);
            ventana = null;
        };
    }

    void RellenarSelector()
    {
        rellenando = true;
        selector.Items.Clear();
        var auto = new ComboBoxItem { Content = Tf("Automática: {0}", Teclados.Automatica.Nombre), Tag = "auto" };
        selector.Items.Add(auto);
        foreach (var d in Teclados.Lista) selector.Items.Add(new ComboBoxItem { Content = d.Nombre, Tag = d.Id });
        var actual = Teclados.Actual;
        selector.SelectedItem = Teclados.ElegidaAMano || Teclados.Forzada != null
            ? selector.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == actual.Id)
            : auto;
        rellenando = false;
    }

    /// <summary>Dibuja el teclado de la distribución actual, conservando las teclas ya comprobadas.</summary>
    void ConstruirTeclado()
    {
        var d = Teclados.Actual;
        teclas.Clear();
        numericas.Clear();
        var lienzo = new Canvas { Width = 23 * U, Height = 6.5 * U };
        foreach (var t in Disposicion(d))
        {
            var codigo = Convertir(t.Codigo);
            var caja = new Border
            {
                Width = t.W * U - 5,
                Height = t.H * U - 5,
                Child = new TextBlock { Text = t.Texto }
            }.Clases("tecla");
            Canvas.SetLeft(caja, t.X * U);
            Canvas.SetTop(caja, t.Y * U);
            lienzo.Children.Add(caja);
            if (codigo is not { } k) continue;
            teclas[k] = caja;
            if (t.Numerica) numericas.Add(k);
            if (pulsadas.Contains(k)) caja.Classes.Add("pulsada");
        }
        // Teclas pulsadas que no existen en esta forma de teclado (p. ej. la de junto a Mayús en ANSI) dejan de contar.
        pulsadas.RemoveWhere(k => !teclas.ContainsKey(k));
        zonaTeclado.Content = new Viewbox { Child = lienzo, Stretch = Stretch.Uniform, MaxHeight = 380 };
        Sesion.Anotar("teclado", Tx("Distribución"), d.Nombre);
        ActualizarContador();
    }

    static PhysicalKey? Convertir(string codigo) =>
        Enum.TryParse<PhysicalKey>(codigo, out var k) ? k :
        Enum.TryParse("Key" + codigo, out k) ? k : null;

    void AlPulsar(object? sender, KeyEventArgs e)
    {
        Marcar(e, activa: true);
        e.Handled = true; // que Tab, flechas o espacio no muevan el foco mientras se prueba
    }

    void AlSoltar(object? sender, KeyEventArgs e)
    {
        Marcar(e, activa: false);
        e.Handled = true;
    }

    void Marcar(KeyEventArgs e, bool activa)
    {
        var k = e.PhysicalKey;
        if (k != PhysicalKey.None && teclas.TryGetValue(k, out var caja))
        {
            caja.Classes.Set("activa", activa);
            if (pulsadas.Add(k))
            {
                caja.Classes.Add("pulsada");
                ActualizarContador();
            }
            return;
        }
        var nombre = k != PhysicalKey.None ? k.ToString() : e.Key.ToString();
        if (nombre == "None" || !otrasVistas.Add(nombre)) return;
        sinOtras.IsVisible = false;
        var chip = Ui.Chip(Traducir(nombre), Veredicto.Ok);
        chip.Margin = new Thickness(0, 0, 8, 8);
        otras.Children.Add(chip);
    }

    void ActualizarContador()
    {
        int total = teclas.Count - numericas.Count;
        int hechas = pulsadas.Count(k => !numericas.Contains(k));
        int hechasNum = pulsadas.Count(numericas.Contains);
        contador.Text = Tf("{0} de {1} teclas comprobadas", hechas, total);
        contadorNumerico.Text = Tf("Teclado numérico: {0} de {1}", hechasNum, numericas.Count);
        Sesion.Anotar("teclado", Tx("Teclas comprobadas"), Tf("{0} de {1}", hechas, total));
    }

    void Reiniciar()
    {
        pulsadas.Clear();
        foreach (var caja in teclas.Values)
        {
            caja.Classes.Remove("pulsada");
            caja.Classes.Remove("activa");
        }
        otrasVistas.Clear();
        otras.Children.Clear();
        sinOtras.IsVisible = true;
        ActualizarContador();
    }

    static string Traducir(string nombre) => nombre switch
    {
        "AudioVolumeUp" or "VolumeUp" => Tx("Subir volumen"),
        "AudioVolumeDown" or "VolumeDown" => Tx("Bajar volumen"),
        "AudioVolumeMute" or "VolumeMute" => Tx("Silenciar"),
        "MediaPlayPause" => Tx("Reproducir / pausa"),
        "MediaStop" => Tx("Detener"),
        "MediaTrackNext" or "MediaNextTrack" => Tx("Pista siguiente"),
        "MediaTrackPrevious" or "MediaPreviousTrack" => Tx("Pista anterior"),
        "BrowserHome" => Tx("Inicio del navegador"),
        "BrowserSearch" => Tx("Buscar"),
        "LaunchMail" => Tx("Correo"),
        "LaunchApp2" => Tx("Calculadora"),
        _ => nombre
    };

    /// <summary>
    /// Teclado completo de la distribución. Posiciones en unidades de tecla.
    /// ISO: Intro de dos filas y tecla junto a Mayús izquierda. ANSI: Intro de una fila y barra invertida
    /// encima. ABNT2: como ISO, con una tecla más a la izquierda de Mayús derecha.
    /// </summary>
    static IEnumerable<DefTecla> Disposicion(Distribucion d)
    {
        bool ansi = d.Forma == FormaTeclado.Ansi;
        bool abnt2 = d.Forma == FormaTeclado.Abnt2;
        string L(string codigo, string porDefecto) => d.Leyenda(codigo, porDefecto);

        // Fila de funciones
        yield return new("Escape", "Esc", 0, 0);
        for (int i = 0; i < 12; i++) yield return new($"F{i + 1}", $"F{i + 1}", 2 + i + (i / 4) * 0.5, 0);

        // Fila de números
        yield return new("Backquote", L("Backquote", "`"), 0, 1.5);
        string[] num = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        for (int i = 0; i < 10; i++) yield return new("Digit" + num[i], L("Digit" + num[i], num[i]), 1 + i, 1.5);
        yield return new("Minus", L("Minus", "-"), 11, 1.5);
        yield return new("Equal", L("Equal", "="), 12, 1.5);
        yield return new("Backspace", "⌫", 13, 1.5, 2);

        // Fila de la Q
        yield return new("Tab", "Tab ⇥", 0, 2.5, 1.5);
        string[] q = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
        for (int i = 0; i < 10; i++) yield return new(q[i], L(q[i], q[i]), 1.5 + i, 2.5);
        yield return new("BracketLeft", L("BracketLeft", "["), 11.5, 2.5);
        yield return new("BracketRight", L("BracketRight", "]"), 12.5, 2.5);
        if (ansi) yield return new("Backslash", L("Backslash", "\\"), 13.5, 2.5, 1.5);
        else yield return new("Enter", Tx("Intro"), 13.75, 2.5, 1.25, 2);

        // Fila de la A
        yield return new("CapsLock", Tx("Bloq Mayús"), 0, 3.5, 1.75);
        string[] a = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
        for (int i = 0; i < 9; i++) yield return new(a[i], L(a[i], a[i]), 1.75 + i, 3.5);
        yield return new("Semicolon", L("Semicolon", ";"), 10.75, 3.5);
        yield return new("Quote", L("Quote", "'"), 11.75, 3.5);
        if (ansi) yield return new("Enter", Tx("Intro"), 12.75, 3.5, 2.25);
        else yield return new("Backslash", L("Backslash", "\\"), 12.75, 3.5);

        // Fila de la Z
        if (ansi) yield return new("ShiftLeft", Tx("⇧ Mayús"), 0, 4.5, 2.25);
        else
        {
            yield return new("ShiftLeft", Tx("⇧ Mayús"), 0, 4.5, 1.25);
            yield return new("IntlBackslash", L("IntlBackslash", "\\"), 1.25, 4.5);
        }
        string[] z = { "Z", "X", "C", "V", "B", "N", "M" };
        for (int i = 0; i < 7; i++) yield return new(z[i], L(z[i], z[i]), 2.25 + i, 4.5);
        yield return new("Comma", L("Comma", ","), 9.25, 4.5);
        yield return new("Period", L("Period", "."), 10.25, 4.5);
        yield return new("Slash", L("Slash", "/"), 11.25, 4.5);
        if (abnt2)
        {
            yield return new("IntlRo", L("IntlRo", "/"), 12.25, 4.5);
            yield return new("ShiftRight", Tx("⇧ Mayús"), 13.25, 4.5, 1.75);
        }
        else yield return new("ShiftRight", Tx("⇧ Mayús"), 12.25, 4.5, 2.75);

        // Fila inferior
        var meta = OperatingSystem.IsWindows() ? "Win" : "Super";
        yield return new("ControlLeft", "Ctrl", 0, 5.5, 1.25);
        yield return new("MetaLeft", meta, 1.25, 5.5, 1.25);
        yield return new("AltLeft", "Alt", 2.5, 5.5, 1.25);
        yield return new("Space", "", 3.75, 5.5, 6.25);
        yield return new("AltRight", d.Id == "en-US" ? "Alt" : "Alt Gr", 10, 5.5, 1.25);
        yield return new("MetaRight", meta, 11.25, 5.5, 1.25);
        yield return new("ContextMenu", Tx("Menú"), 12.5, 5.5, 1.25);
        yield return new("ControlRight", "Ctrl", 13.75, 5.5, 1.25);

        // Bloque de edición y flechas
        yield return new("PrintScreen", Tx("Impr\nPant"), 15.5, 0);
        yield return new("ScrollLock", Tx("Bloq\nDespl"), 16.5, 0);
        yield return new("Pause", Tx("Pausa"), 17.5, 0);
        yield return new("Insert", "Insert", 15.5, 1.5);
        yield return new("Home", Tx("Inicio"), 16.5, 1.5);
        yield return new("PageUp", Tx("Re\nPág"), 17.5, 1.5);
        yield return new("Delete", Tx("Supr"), 15.5, 2.5);
        yield return new("End", Tx("Fin"), 16.5, 2.5);
        yield return new("PageDown", Tx("Av\nPág"), 17.5, 2.5);
        yield return new("ArrowUp", "↑", 16.5, 4.5);
        yield return new("ArrowLeft", "←", 15.5, 5.5);
        yield return new("ArrowDown", "↓", 16.5, 5.5);
        yield return new("ArrowRight", "→", 17.5, 5.5);

        // Teclado numérico
        yield return new("NumLock", Tx("Bloq\nNum"), 19, 1.5, Numerica: true);
        yield return new("NumPadDivide", "/", 20, 1.5, Numerica: true);
        yield return new("NumPadMultiply", "*", 21, 1.5, Numerica: true);
        yield return new("NumPadSubtract", "-", 22, 1.5, Numerica: true);
        yield return new("NumPad7", "7", 19, 2.5, Numerica: true);
        yield return new("NumPad8", "8", 20, 2.5, Numerica: true);
        yield return new("NumPad9", "9", 21, 2.5, Numerica: true);
        yield return new("NumPadAdd", "+", 22, 2.5, 1, 2, Numerica: true);
        yield return new("NumPad4", "4", 19, 3.5, Numerica: true);
        yield return new("NumPad5", "5", 20, 3.5, Numerica: true);
        yield return new("NumPad6", "6", 21, 3.5, Numerica: true);
        yield return new("NumPad1", "1", 19, 4.5, Numerica: true);
        yield return new("NumPad2", "2", 20, 4.5, Numerica: true);
        yield return new("NumPad3", "3", 21, 4.5, Numerica: true);
        yield return new("NumPadEnter", Tx("Intro"), 22, 4.5, 1, 2, Numerica: true);
        yield return new("NumPad0", "0", 19, 5.5, 2, Numerica: true);
        yield return new("NumPadDecimal", d.Leyenda("NumPadDecimal", "."), 21, 5.5, Numerica: true);
    }
}
