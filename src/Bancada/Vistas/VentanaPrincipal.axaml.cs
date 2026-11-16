using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Bancada.Modelo;
using Bancada.Sistema;
using Path = Avalonia.Controls.Shapes.Path;

namespace Bancada.Vistas;

public partial class VentanaPrincipal : Window
{
    sealed record Seccion(string Id, string Titulo, string Icono, Func<Control> Crear);

    List<Seccion> secciones = new();
    readonly Dictionary<string, Control> vistas = new();
    readonly Dictionary<string, (Button boton, Ellipse punto)> navegacion = new();
    string actual = "inicio";
    bool cambiandoIdioma;

    public VentanaPrincipal()
    {
        InitializeComponent();

        Construir();
        foreach (var (codigo, nombre) in Idioma.Disponibles) SelectorIdioma.Items.Add(new ComboBoxItem { Content = nombre, Tag = codigo });
        MarcarIdioma();
        SelectorIdioma.SelectionChanged += (_, _) =>
        {
            if (!cambiandoIdioma && SelectorIdioma.SelectedItem is ComboBoxItem { Tag: string codigo }) CambiarIdioma(codigo);
        };

        Sesion.Cambio += (id, v) => { if (navegacion.TryGetValue(id, out var n)) Ui.FijarEstado(n.punto, v); };
        Sesion.EquipoDetectado += MostrarEquipo;
        Revision.Conectar(VistaDe, Ir);
        Opened += AlAbrir;
    }

    /// <summary>Monta los textos de la ventana y la barra lateral en el idioma actual.</summary>
    void Construir()
    {
        secciones = CrearSecciones();
        PanelNav.Children.Clear();
        navegacion.Clear();
        foreach (var s in secciones) PanelNav.Children.Add(BotonNavegacion(s));
        foreach (var (id, n) in navegacion) Ui.FijarEstado(n.punto, Sesion.Obtener(id));

        Title = "Bancada · " + Tx("Diagnóstico de equipos");
        TextoSubtitulo.Text = Tx("Diagnóstico de equipos");
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TextoVersion.Text = Tf("Versión {0}.{1}.{2}", version?.Major, version?.Minor, version?.Build) + " · Bill Bull";
        TextoTipo.Text = Tx("Detectando equipo…");
        MostrarEquipo();
        ZonaGuia.Content = new BarraGuia();
    }

    void MarcarIdioma()
    {
        cambiandoIdioma = true;
        SelectorIdioma.SelectedItem = SelectorIdioma.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == Idioma.Actual);
        cambiandoIdioma = false;
    }

    /// <summary>Cambia el idioma al momento: se rehacen las pantallas y se conservan los resultados de las pruebas.</summary>
    async void CambiarIdioma(string codigo) => await CambiarIdiomaAsync(codigo, guardar: true);

    async Task CambiarIdiomaAsync(string codigo, bool guardar)
    {
        if (codigo == Idioma.Actual) return;
        // Con pruebas en marcha (o la revisión completa haciendo las suyas) no se rehacen las pantallas.
        if (PruebasEnCurso.Hay || Revision.FondoEnMarcha)
        {
            MarcarIdioma();
            var anterior = TextoVersion.Text;
            TextoVersion.Text = Tx("Espera a que termine la prueba en marcha");
            await Task.Delay(4000);
            TextoVersion.Text = anterior;
            return;
        }
        Idioma.Fijar(codigo, guardar);
        Contenido.Content = null; // las pantallas viejas se desenganchan (paran sus temporizadores y la cámara)
        vistas.Clear();
        Construir();
        MarcarIdioma();
        Ir(actual);
    }

    List<Seccion> CrearSecciones() => new()
    {
            new("inicio", Tx("Resumen"), Iconos.Chip, () => new InicioVista(Ir)),
            new("revision", Tx("Revisión completa"), Iconos.Revision, () => new RevisionVista(Ir)),
            new("teclado", Tx("Teclado"), Iconos.Teclado, () => new TecladoVista()),
            new("puntero", Tx("Touchpad y ratón"), Iconos.Puntero, () => new PunteroVista()),
            new("pantalla", Tx("Pantalla"), Iconos.Pantalla, () => new PantallaVista()),
            new("audio", Tx("Sonido"), Iconos.Audio, () => new AudioVista()),
            new("camara", Tx("Cámara"), Iconos.Camara, () => new CamaraVista()),
            new("red", Tx("Red"), Iconos.Red, () => new RedVista()),
            new("usb", Tx("Puertos USB"), Iconos.Usb, () => new UsbVista()),
            new("bateria", Tx("Batería"), Iconos.Bateria, () => new BateriaVista()),
            new("discos", Tx("Discos"), Iconos.Discos, () => new DiscosVista()),
            new("memoria", Tx("Memoria RAM"), Iconos.Memoria, () => new MemoriaVista()),
            new("procesador", Tx("Procesador"), Iconos.Temperatura, () => new ProcesadorVista()),
            new("grafica", Tx("Gráfica"), Iconos.Grafica, () => new GraficaVista()),
            new("suspension", Tx("Suspensión"), Iconos.Suspension, () => new SuspensionVista()),
            new("placa", Tx("Placa base"), Iconos.Placa, () => new PlacaVista()),
            new("informe", Tx("Informe"), Iconos.Informe, () => new InformeVista()),
    };

    Button BotonNavegacion(Seccion s)
    {
        var marca = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Margin = new Thickness(-8, 0, 9, 0) }.Clases("marca");
        var icono = new Path
        {
            Data = Geometry.Parse(s.Icono),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Margin = new Thickness(0, 0, 12, 0)
        }.Clases("icono-nav");
        var punto = new Ellipse().Clases("punto");

        var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        var texto = new TextBlock { Text = s.Titulo, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icono, 1);
        Grid.SetColumn(texto, 2);
        Grid.SetColumn(punto, 3);
        fila.Children.Add(marca);
        fila.Children.Add(icono);
        fila.Children.Add(texto);
        if (s.Id is not ("inicio" or "revision" or "informe")) fila.Children.Add(punto);

        var boton = new Button { Content = fila }.Clases("nav");
        Avalonia.Automation.AutomationProperties.SetName(boton, s.Titulo); // lectores de pantalla
        boton.Click += (_, _) => Ir(s.Id);
        navegacion[s.Id] = (boton, punto);
        return boton;
    }

    void Ir(string id)
    {
        if (secciones.All(s => s.Id != id)) id = "inicio";
        actual = id;
        foreach (var (clave, n) in navegacion) n.boton.Classes.Set("activo", clave == id);
        Contenido.Content = VistaDe(id);
        Revision.AlNavegar(id);
    }

    /// <summary>La pantalla de una sección, creándola si hace falta, sin mostrarla (la revisión completa hace así sus pruebas en segundo plano).</summary>
    Control VistaDe(string id)
    {
        if (!vistas.TryGetValue(id, out var vista))
        {
            vista = secciones.First(s => s.Id == id).Crear();
            vistas[id] = vista;
        }
        return vista;
    }

    void MostrarEquipo()
    {
        var e = Sesion.Equipo;
        if (e == null) return;
        TextoTipo.Text = TextoEquipo.Tipo(e.Tipo).ToUpperInvariant();
        TextoModelo.Text = $"{e.Marca} {e.Modelo}".Trim();
    }

    async void AlAbrir(object? sender, EventArgs e)
    {
        if (Opciones.Tema == "oscuro") Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        else if (Opciones.Tema == "claro") Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        // Si la última prueba de suspensión no terminó (el equipo no volvió), se abre directamente para avisar.
        Ir(Opciones.Suspender ? "suspension" : Opciones.Vista ?? (Suspension.PruebaSinTerminar() != null ? "suspension" : "inicio"));
        Sesion.PublicarEquipo(await DetectorEquipo.DetectarAsync());
        if (Opciones.Revision)
        {
            Revision.Empezar();
            if (Opciones.Vista != null) Ir(Opciones.Vista);
        }

        if (Opciones.Captura != null || Opciones.Informe != null)
        {
            // Con --suspender se espera a que vuelva y termine la comprobación (como mucho 5 minutos).
            if (Opciones.Suspender) await Task.WhenAny(SuspensionVista.Terminada.Task, Task.Delay(TimeSpan.FromMinutes(5)));
            await Task.Delay(Opciones.Espera * 1000);
            if (Opciones.CambiarA != null)
            {
                // Prueba del cambio de idioma en caliente (no se guarda).
                await CambiarIdiomaAsync(Opciones.CambiarA, guardar: false);
                await Task.Delay(Opciones.Espera * 1000);
            }
            if (Opciones.Captura != null && Opciones.Cada is int cada)
                await CapturarRevision(Opciones.Captura, cada);
            else if (Opciones.Captura != null) Capturar(Opciones.Captura);
            if (Opciones.Informe != null) await Pdf.Crear(Opciones.Informe, Tx("Informe de prueba generado automáticamente."));
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
    }

    /// <summary>
    /// --cada N: una captura cada N segundos recorriendo la revisión completa (avanza un paso en cada una y,
    /// al acabar los pasos, enseña la prueba automática en marcha) hasta que terminan las de segundo plano.
    /// </summary>
    async Task CapturarRevision(string ruta, int cada)
    {
        var base_ = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(ruta)) ?? ".", System.IO.Path.GetFileNameWithoutExtension(ruta));
        for (int n = 1; n <= 300; n++)
        {
            Capturar($"{base_}-{n:000}.png");
            if (!Revision.Activa && !Revision.FondoEnMarcha) break;
            if (Revision.Activa) Revision.Siguiente();
            else if (Revision.EnMarcha is string id && actual != id) Ir(id);
            await Task.Delay(cada * 1000);
        }
        Ir("revision");
        await Task.Delay(1500);
        Capturar($"{base_}-fin.png");
    }

    /// <summary>Guarda una imagen PNG de la ventana (documentación y comprobaciones automáticas).</summary>
    void Capturar(string ruta)
    {
        // Con --completa se captura la página entera de la vista, no solo lo que cabe en la ventana.
        Control objetivo = Raiz;
        if (Opciones.Completa && Contenido.Content is UserControl { Content: ScrollViewer { Content: Panel pagina } })
        {
            pagina.Background = Raiz.Background;
            objetivo = pagina;
        }
        var escala = RenderScaling;
        var tam = new PixelSize((int)(objetivo.Bounds.Width * escala), (int)(objetivo.Bounds.Height * escala));
        using var imagen = new RenderTargetBitmap(tam, new Vector(96 * escala, 96 * escala));
        imagen.Render(objetivo);
#pragma warning disable CS0618 // PNG por defecto: basta con la sobrecarga sencilla
        imagen.Save(ruta);
#pragma warning restore CS0618
    }
}
