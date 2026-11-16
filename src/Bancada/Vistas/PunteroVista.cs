using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Bancada.Modelo;

namespace Bancada.Vistas;

/// <summary>Touchpad y ratón: movimiento por toda la zona, botones, doble clic, arrastre y desplazamiento.</summary>
internal sealed class PunteroVista : UserControl
{
    static (string id, string texto)[] Acciones => new (string id, string texto)[]
    {
        ("izq", Tx("Clic izquierdo")),
        ("der", Tx("Clic derecho")),
        ("doble", Tx("Doble clic")),
        ("arrastrar", Tx("Arrastrar")),
        ("arriba", Tx("Desplazar arriba")),
        ("abajo", Tx("Desplazar abajo")),
        ("lateral", Tx("Desplazar a los lados")),
        ("central", Tx("Clic central")),
    };

    static (string id, string texto)[] Opcionales => new (string id, string texto)[]
    {
        ("tactil", Tx("Pantalla táctil")),
        ("lapiz", Tx("Lápiz")),
        ("x1", Tx("Botón atrás")),
        ("x2", Tx("Botón adelante")),
    };

    readonly ZonaPuntero zona = new() { Height = 300 };
    readonly Dictionary<string, Border> indicadores = new();
    readonly HashSet<string> comprobados = new();
    readonly TextBlock textoCobertura = Ui.Texto(Tx("Cobertura: 0 %"), "valor negrita");
    readonly TextBlock textoPendientes = Ui.Texto("", "etiqueta");

    public PunteroVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Touchpad y ratón"),
            Tx("Mueve el puntero por la zona de abajo hasta llenarla y prueba cada botón y gesto. Con el touchpad, recorre también sus bordes y esquinas: si el puntero se para o da saltos, esa parte está dañada.")));

        zona.Accion += Marcar;
        zona.Boton += (id, pulsado) => { if (indicadores.TryGetValue(id, out var b)) b.Classes.Set("activa", pulsado); };
        zona.Cobertura += pct =>
        {
            textoCobertura.Text = Tf("Cobertura: {0} %", pct);
            Sesion.Anotar("puntero", Tx("Zona recorrida"), $"{pct} %");
        };

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Zona de prueba"),
            new Border { Child = zona, CornerRadius = new CornerRadius(10), ClipToBounds = true },
            Ui.Fila(textoCobertura, Ui.Boton(Tx("Empezar de nuevo"), "secundario", (_, _) => Reiniciar()))));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Botones y gestos"), Rejilla(Acciones), textoPendientes));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Si el equipo los tiene"),
            Rejilla(Opcionales),
            Ui.Texto(Tx("Toca la zona con el dedo o con el lápiz, o pulsa los botones laterales del ratón. Si el equipo no los tiene, ignóralos."), "etiqueta")));
        cuerpo.Children.Add(new BarraVeredicto("puntero", Tx("¿El touchpad o el ratón funcionan bien?")));
        Content = pagina;
        ActualizarPendientes();
    }

    WrapPanel Rejilla(IEnumerable<(string id, string texto)> lista)
    {
        var w = new WrapPanel();
        foreach (var (id, texto) in lista)
        {
            var b = Ui.Indicador(texto, 150, 50);
            indicadores[id] = b;
            w.Children.Add(b);
        }
        return w;
    }

    void Marcar(string id)
    {
        if (!comprobados.Add(id)) return;
        if (indicadores.TryGetValue(id, out var b)) b.Classes.Add("pulsada");
        ActualizarPendientes();
    }

    void ActualizarPendientes()
    {
        var faltan = Acciones.Where(a => !comprobados.Contains(a.id)).Select(a => a.texto.ToLowerInvariant()).ToList();
        textoPendientes.Text = faltan.Count == 0 ? Tx("Todo comprobado.") : Tx("Falta: ") + string.Join(", ", faltan) + ".";
        var hechas = Acciones.Concat(Opcionales).Where(a => comprobados.Contains(a.id)).Select(a => a.texto);
        Sesion.Anotar("puntero", Tx("Comprobado"), string.Join(", ", hechas));
        Sesion.Anotar("puntero", Tx("Sin comprobar"), faltan.Count == 0 ? "" : string.Join(", ", faltan));
    }

    void Reiniciar()
    {
        comprobados.Clear();
        foreach (var b in indicadores.Values) b.Classes.Remove("pulsada");
        zona.Reiniciar();
        textoCobertura.Text = Tx("Cobertura: 0 %");
        ActualizarPendientes();
    }
}

/// <summary>Superficie que se va coloreando por donde pasa el puntero y avisa de cada botón o gesto.</summary>
internal sealed class ZonaPuntero : Control, ICustomHitTest
{
    const int Columnas = 28, Filas = 10;
    readonly bool[,] visitado = new bool[Columnas, Filas];
    readonly List<Point> rastro = new();
    Point? inicioArrastre;
    int ultimaCobertura = -1;

    public event Action<string>? Accion;
    public event Action<string, bool>? Boton;
    public event Action<int>? Cobertura;

    public ZonaPuntero()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;
    }

    public bool HitTest(Point p) => new Rect(Bounds.Size).Contains(p);

    public void Reiniciar()
    {
        Array.Clear(visitado);
        rastro.Clear();
        ultimaCobertura = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Touch) Accion?.Invoke("tactil");
        if (e.Pointer.Type == PointerType.Pen) Accion?.Invoke("lapiz");
        switch (p.Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed:
                Boton?.Invoke("izq", true);
                Accion?.Invoke("izq");
                if (e.ClickCount >= 2) Accion?.Invoke("doble");
                inicioArrastre = p.Position;
                break;
            case PointerUpdateKind.RightButtonPressed:
                Boton?.Invoke("der", true);
                Accion?.Invoke("der");
                break;
            case PointerUpdateKind.MiddleButtonPressed:
                Boton?.Invoke("central", true);
                Accion?.Invoke("central");
                break;
            case PointerUpdateKind.XButton1Pressed:
                Accion?.Invoke("x1");
                break;
            case PointerUpdateKind.XButton2Pressed:
                Accion?.Invoke("x2");
                break;
        }
        Pasar(p.Position);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        switch (e.GetCurrentPoint(this).Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                Boton?.Invoke("izq", false);
                inicioArrastre = null;
                break;
            case PointerUpdateKind.RightButtonReleased:
                Boton?.Invoke("der", false);
                break;
            case PointerUpdateKind.MiddleButtonReleased:
                Boton?.Invoke("central", false);
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        Pasar(p.Position);
        if (inicioArrastre is { } ini && p.Properties.IsLeftButtonPressed)
        {
            var d = p.Position - ini;
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) > 60) Accion?.Invoke("arrastrar");
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0) Accion?.Invoke("arriba");
        if (e.Delta.Y < 0) Accion?.Invoke("abajo");
        if (e.Delta.X != 0) Accion?.Invoke("lateral");
        e.Handled = true; // que no se desplace la página
    }

    void Pasar(Point p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0 || p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h) return;
        visitado[(int)(p.X / w * Columnas), (int)(p.Y / h * Filas)] = true;
        rastro.Add(p);
        if (rastro.Count > 600) rastro.RemoveRange(0, rastro.Count - 600);

        int n = 0;
        foreach (var v in visitado) if (v) n++;
        var pct = (int)Math.Round(n * 100.0 / (Columnas * Filas));
        if (pct != ultimaCobertura)
        {
            ultimaCobertura = pct;
            Cobertura?.Invoke(pct);
        }
        InvalidateVisual();
    }

    IBrush Pincel(string clave, IBrush porDefecto) =>
        this.TryFindResource(clave, ActualThemeVariant, out var r) && r is IBrush b ? b : porDefecto;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        ctx.FillRectangle(Pincel("FondoHundido", Brushes.LightGray), new Rect(0, 0, w, h));

        var cw = w / Columnas;
        var ch = h / Filas;
        var relleno = new SolidColorBrush(Color.Parse("#2E9D57"), 0.30);
        for (int x = 0; x < Columnas; x++)
            for (int y = 0; y < Filas; y++)
                if (visitado[x, y]) ctx.FillRectangle(relleno, new Rect(x * cw, y * ch, cw, ch));

        var linea = new Pen(Pincel("Borde", Brushes.Gray), 1);
        for (int x = 1; x < Columnas; x++) ctx.DrawLine(linea, new Point(x * cw, 0), new Point(x * cw, h));
        for (int y = 1; y < Filas; y++) ctx.DrawLine(linea, new Point(0, y * ch), new Point(w, y * ch));

        if (rastro.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(rastro[0], false);
                for (int i = 1; i < rastro.Count; i++) g.LineTo(rastro[i]);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(Pincel("Cobre", Brushes.Chocolate), 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
        }
        else
        {
            var texto = new FormattedText(Tx("Mueve el puntero por toda esta zona"), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 16, Pincel("TintaSuave", Brushes.Gray));
            ctx.DrawText(texto, new Point((w - texto.Width) / 2, (h - texto.Height) / 2));
        }
    }
}
