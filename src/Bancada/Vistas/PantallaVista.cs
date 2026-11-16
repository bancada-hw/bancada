using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Bancada.Vistas;

internal enum Patron { Negro, Blanco, Rojo, Verde, Azul, Gris, Degradados, Cuadricula }

internal sealed class PantallaVista : UserControl
{
    readonly StackPanel monitores = new() { Spacing = 10 };

    public PantallaVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Pantalla"),
            Tx("La pantalla se llena con colores lisos y patrones. Busca píxeles muertos o atascados, manchas, fugas de luz en los bordes, bandas en los degradados y zonas con distinto brillo.")));

        var muestras = new WrapPanel();
        foreach (var (patron, nombre) in VentanaPantalla.Lista)
        {
            var m = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 14, 8), Width = 76 };
            m.Children.Add(new Border
            {
                Height = 44,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = new LienzoPatron { Patron = patron, Muestra = true }
            }.Clases("hundido"));
            m.Children.Add(Ui.Texto(nombre, "etiqueta"));
            muestras.Children.Add(m);
        }

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba a pantalla completa"),
            muestras,
            Ui.Texto(Tx("Clic, espacio o → pasa al siguiente · clic derecho o ← vuelve · Esc sale."), "subtitulo"),
            Ui.Boton(Tx("Empezar prueba de pantalla"), "primario", (_, _) => Empezar())));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Pantallas conectadas"), monitores));
        cuerpo.Children.Add(new BarraVeredicto("pantalla", Tx("¿La pantalla está bien?")));
        Content = pagina;

        AttachedToVisualTree += (_, _) => RellenarMonitores();
    }

    void RellenarMonitores()
    {
        monitores.Children.Clear();
        if (TopLevel.GetTopLevel(this) is not Window ventana) return;
        int n = 1;
        foreach (var s in ventana.Screens.All)
        {
            var titulo = Tf("Pantalla {0}", n++) + (s.IsPrimary ? " (principal)" : "");
            monitores.Children.Add(Ui.Dato(titulo,
                Tf("{0} × {1} píxeles · escala {2} %", s.Bounds.Width, s.Bounds.Height, Modelo.Formato.N(s.Scaling * 100, "0"))));
        }
    }

    void Empezar()
    {
        var prueba = new VentanaPantalla();
        if (TopLevel.GetTopLevel(this) is Window dueno) prueba.Show(dueno);
        else prueba.Show();
    }
}

/// <summary>Ventana a pantalla completa que recorre los patrones de prueba.</summary>
internal sealed class VentanaPantalla : Window
{
    public static (Patron patron, string nombre)[] Lista => new (Patron patron, string nombre)[]
    {
        (Patron.Negro, Tx("Negro")),
        (Patron.Blanco, Tx("Blanco")),
        (Patron.Rojo, Tx("Rojo")),
        (Patron.Verde, Tx("Verde")),
        (Patron.Azul, Tx("Azul")),
        (Patron.Gris, Tx("Gris")),
        (Patron.Degradados, Tx("Degradados")),
        (Patron.Cuadricula, Tx("Cuadrícula")),
    };

    readonly LienzoPatron lienzo = new();
    readonly Border aviso;
    readonly TextBlock textoAviso;
    IDisposable? ocultar;
    int indice;

    public VentanaPantalla()
    {
        Title = Tx("Prueba de pantalla");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.None);

        textoAviso = new TextBlock { Foreground = Brushes.White, FontSize = 17, TextAlignment = TextAlignment.Center, LineHeight = 26 };
        aviso = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 18, 22, 20)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 56),
            Child = textoAviso
        };
        Content = new Grid { Children = { lienzo, aviso } };

        KeyDown += AlPulsar;
        PointerPressed += AlClic;
        Mostrar(0, primeraVez: true);
    }

    void Mostrar(int i, bool primeraVez = false)
    {
        indice = (i + Lista.Length) % Lista.Length;
        lienzo.Patron = Lista[indice].patron;
        lienzo.InvalidateVisual();
        textoAviso.Text = Tf("{0} de {1} · {2}", indice + 1, Lista.Length, Lista[indice].nombre) +
                          (primeraVez ? Tx("\nClic, espacio o → siguiente  ·  ← anterior  ·  Esc salir") : "");
        aviso.IsVisible = true;
        ocultar?.Dispose();
        ocultar = DispatcherTimer.RunOnce(() => aviso.IsVisible = false, TimeSpan.FromSeconds(primeraVez ? 4 : 1.4));
    }

    void AlPulsar(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Right or Key.Space or Key.Enter or Key.PageDown or Key.Down: Mostrar(indice + 1); break;
            case Key.Left or Key.Back or Key.PageUp or Key.Up: Mostrar(indice - 1); break;
        }
        e.Handled = true;
    }

    void AlClic(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) Mostrar(indice - 1);
        else Mostrar(indice + 1);
    }
}

/// <summary>Dibuja un patrón de prueba a todo el tamaño del control.</summary>
internal sealed class LienzoPatron : Control
{
    public Patron Patron { get; set; }
    public bool Muestra { get; set; }

    static IBrush Solido(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        switch (Patron)
        {
            case Patron.Negro: ctx.FillRectangle(Brushes.Black, r); break;
            case Patron.Blanco: ctx.FillRectangle(Brushes.White, r); break;
            case Patron.Rojo: ctx.FillRectangle(Solido(255, 0, 0), r); break;
            case Patron.Verde: ctx.FillRectangle(Solido(0, 255, 0), r); break;
            case Patron.Azul: ctx.FillRectangle(Solido(0, 0, 255), r); break;
            case Patron.Gris: ctx.FillRectangle(Solido(128, 128, 128), r); break;
            case Patron.Degradados: Degradados(ctx, r); break;
            case Patron.Cuadricula: Cuadricula(ctx, r); break;
        }
    }

    static void Degradados(DrawingContext ctx, Rect r)
    {
        Color[] colores = { Colors.White, Color.FromRgb(255, 0, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 0, 255) };
        double alto = r.Height / colores.Length;
        for (int k = 0; k < colores.Length; k++)
        {
            var banda = new Rect(0, k * alto, r.Width, alto);
            var brocha = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(colores[k], 1) }
            };
            ctx.FillRectangle(brocha, banda);
        }
    }

    void Cuadricula(DrawingContext ctx, Rect r)
    {
        ctx.FillRectangle(Brushes.Black, r);
        double paso = Muestra ? 8 : 64;
        var fina = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1);
        for (double x = 0.5; x < r.Width; x += paso) ctx.DrawLine(fina, new Point(x, 0), new Point(x, r.Height));
        for (double y = 0.5; y < r.Height; y += paso) ctx.DrawLine(fina, new Point(0, y), new Point(r.Width, y));
        var gruesa = new Pen(Brushes.White, Muestra ? 1 : 2);
        double radio = Math.Min(r.Width, r.Height) * 0.42;
        ctx.DrawEllipse(null, gruesa, r.Center, radio, radio);
        ctx.DrawLine(gruesa, new Point(r.Center.X, 0), new Point(r.Center.X, r.Height));
        ctx.DrawLine(gruesa, new Point(0, r.Center.Y), new Point(r.Width, r.Center.Y));
        // Marco en el borde exacto: si no se ve entero, la imagen se sale de la pantalla.
        ctx.DrawRectangle(null, new Pen(Brushes.White, Muestra ? 1 : 2), r.Deflate(Muestra ? 0.5 : 1));
    }
}
