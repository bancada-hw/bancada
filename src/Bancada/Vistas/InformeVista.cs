using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Skia.Helpers;
using Bancada.Modelo;
using Bancada.Sistema;
using SkiaSharp;

namespace Bancada.Vistas;

/// <summary>Informe final: resumen de todas las pruebas, observaciones y exportación a PDF.</summary>
internal sealed class InformeVista : UserControl
{
    readonly StackPanel panelResumen = new() { Spacing = 10 };
    readonly StackPanel panelPruebas = new() { Spacing = 10 };
    readonly TextBox observaciones = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        PlaceholderText = Tx("Opcional: estado físico, golpes, arañazos, lo que ha dicho el cliente…")
    };
    readonly StackPanel panelGuardado = new() { Spacing = 10 };

    public InformeVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Informe"),
            Tx("Resumen de todo lo comprobado. Guárdalo en PDF para entregarlo con el equipo o archivarlo.")));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Resultado"), panelResumen));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Pruebas"), panelPruebas));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Observaciones"), observaciones));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Guardar"),
            Ui.Texto(Tx("Se guarda en tu carpeta Documentos. Incluye el número de serie del equipo."), "subtitulo"),
            Ui.Boton(Tx("Guardar informe en PDF"), "primario", async (_, _) => await Guardar()),
            panelGuardado));
        Content = pagina;

        AttachedToVisualTree += (_, _) => Pintar();
    }

    void Pintar()
    {
        var conteo = Pruebas.Lista.GroupBy(p => Sesion.Obtener(p.Id)).ToDictionary(g => g.Key, g => g.Count());
        int C(Veredicto v) => conteo.TryGetValue(v, out var n) ? n : 0;
        panelResumen.Children.Clear();
        panelResumen.Children.Add(Ui.Columnas(
            Ui.Cifra(Tx("Funcionan"), C(Veredicto.Ok).ToString(), ""),
            Ui.Cifra(Tx("Con defectos"), C(Veredicto.Aviso).ToString(), ""),
            Ui.Cifra(Tx("No funcionan"), C(Veredicto.Fallo).ToString(), ""),
            Ui.Cifra(Tx("Sin probar"), C(Veredicto.Pendiente).ToString(), "")));
        if (C(Veredicto.Pendiente) > 0)
            panelResumen.Children.Add(Ui.Texto(Tx("Las pruebas sin probar aparecerán así en el informe."), "etiqueta"));

        panelPruebas.Children.Clear();
        foreach (var (id, nombre) in Pruebas.Lista)
        {
            var v = Sesion.Obtener(id);
            var p = new StackPanel { Spacing = 4 };
            p.Children.Add(Ui.Fila(Ui.Texto(nombre, "valor negrita"), Ui.Chip(Ui.TextoVeredicto(v), v)));
            foreach (var l in Sesion.Detalles(id)) p.Children.Add(Ui.Texto(l, "etiqueta"));
            panelPruebas.Children.Add(Ui.Hundido(p));
        }
    }

    async Task Guardar()
    {
        panelGuardado.Children.Clear();
        try
        {
            var e = Sesion.Equipo;
            var equipo = e == null ? "equipo" : $"{e.Marca} {e.Modelo}".Trim();
            var limpio = string.Concat(equipo.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '-' : c));
            var archivo = Tf("Informe Bancada - {0} - {1:yyyy-MM-dd HHmm}.pdf", limpio, DateTime.Now);
            var ruta = Path.Combine(Plataforma.CarpetaDocumentos(), archivo);
            await Pdf.Crear(ruta, observaciones.Text ?? "");
            var ok = Ui.Chip(Tx("Informe guardado"), Veredicto.Ok);
            panelGuardado.Children.Add(ok);
            panelGuardado.Children.Add(Ui.Texto(ruta, "etiqueta mono"));
            panelGuardado.Children.Add(Ui.Botones(
                Ui.Boton(Tx("Abrir el informe"), "secundario", (_, _) => Plataforma.Abrir(ruta)),
                Ui.Boton(Tx("Abrir la carpeta"), "secundario", (_, _) => Plataforma.Abrir(Path.GetDirectoryName(ruta)!))));
        }
        catch (Exception ex)
        {
            panelGuardado.Children.Add(Ui.Chip(Tx("No se ha podido guardar"), Veredicto.Fallo));
            panelGuardado.Children.Add(Ui.Texto(ex.Message, "etiqueta"));
        }
    }
}

/// <summary>Informe en PDF (A4, texto seleccionable) dibujado con los mismos controles de la interfaz.</summary>
internal static class Pdf
{
    const double Ancho = 794, Alto = 1123, Margen = 56, Cabecera = 64, Pie = 54;
    static readonly FontFamily Fuente = new("fonts:Inter#Inter");
    static readonly IBrush Tinta = new SolidColorBrush(Color.Parse("#1A1D1B"));
    static readonly IBrush Suave = new SolidColorBrush(Color.Parse("#5F6761"));
    static readonly IBrush Linea = new SolidColorBrush(Color.Parse("#E2DED5"));
    static readonly IBrush Fondo = new SolidColorBrush(Color.Parse("#F4F2ED"));
    static readonly IBrush Mascara = new SolidColorBrush(Color.Parse("#0F3D2E"));
    static readonly IBrush Cobre = new SolidColorBrush(Color.Parse("#B5642A"));

    static TextBlock T(string texto, double tam = 10.5, IBrush? color = null, FontWeight peso = FontWeight.Normal) => new()
    {
        Text = texto,
        FontFamily = Fuente,
        FontSize = tam,
        FontWeight = peso,
        Foreground = color ?? Tinta,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = tam * 1.45
    };

    static (IBrush fondo, IBrush texto) ColoresVeredicto(Veredicto v) => v switch
    {
        Veredicto.Ok => (new SolidColorBrush(Color.Parse("#DDF0E3")), new SolidColorBrush(Color.Parse("#1F7A42"))),
        Veredicto.Aviso => (new SolidColorBrush(Color.Parse("#FBEFC9")), new SolidColorBrush(Color.Parse("#8A6200"))),
        Veredicto.Fallo => (new SolidColorBrush(Color.Parse("#F8DCD9")), new SolidColorBrush(Color.Parse("#A32F27"))),
        _ => (new SolidColorBrush(Color.Parse("#ECE9E2")), Suave)
    };

    static Border Pastilla(Veredicto v)
    {
        var (fondo, texto) = ColoresVeredicto(v);
        return new Border
        {
            Background = fondo,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = T(Ui.TextoVeredicto(v), 9.5, texto, FontWeight.SemiBold)
        };
    }

    static Grid FilaDato(string etiqueta, string valor)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), Margin = new Thickness(0, 0, 0, 5) };
        var a = T(etiqueta, 10, Suave);
        var b = T(string.IsNullOrWhiteSpace(valor) ? "—" : valor, 10.5);
        Grid.SetColumn(b, 1);
        g.Children.Add(a);
        g.Children.Add(b);
        return g;
    }

    static StackPanel Seccion(string titulo)
    {
        var p = new StackPanel { Spacing = 6, Margin = new Thickness(0, 14, 0, 4) };
        p.Children.Add(T(titulo.ToUpperInvariant(), 9, Cobre, FontWeight.Bold));
        p.Children.Add(new Border { Height = 1, Background = Linea });
        return p;
    }

    static List<Control> Bloques(string observaciones)
    {
        var b = new List<Control>();
        var e = Sesion.Equipo;
        var equipo = e == null ? Tx("Equipo sin identificar") : $"{e.Marca} {e.Modelo}".Trim();

        var titulo = new StackPanel { Spacing = 4 };
        titulo.Children.Add(T(Tx("Informe de diagnóstico"), 24, Tinta, FontWeight.Bold));
        titulo.Children.Add(T(equipo, 15, Tinta, FontWeight.SemiBold));
        titulo.Children.Add(T(Tf("Realizado el {0:dd/MM/yyyy} a las {1:HH:mm}", DateTime.Now, DateTime.Now), 10, Suave));
        b.Add(titulo);

        // Resumen en cifras
        var conteo = Pruebas.Lista.GroupBy(p => Sesion.Obtener(p.Id)).ToDictionary(g => g.Key, g => g.Count());
        int C(Veredicto v) => conteo.TryGetValue(v, out var n) ? n : 0;
        var resumen = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(0, 16, 0, 0) };
        var cifras = new[] { (Veredicto.Ok, Tx("funcionan")), (Veredicto.Aviso, Tx("con defectos")), (Veredicto.Fallo, Tx("no funcionan")), (Veredicto.Pendiente, Tx("sin probar")) };
        for (int i = 0; i < cifras.Length; i++)
        {
            var (v, texto) = cifras[i];
            var (fondo, color) = ColoresVeredicto(v);
            var celda = new Border
            {
                Background = fondo,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                Child = new StackPanel { Children = { T(C(v).ToString(), 20, color, FontWeight.Bold), T(texto, 9.5, color) } }
            };
            Grid.SetColumn(celda, i);
            resumen.Children.Add(celda);
        }
        b.Add(resumen);

        // Equipo
        if (e != null)
        {
            var datos = new StackPanel();
            datos.Children.Add(Seccion(Tx("Equipo")));
            datos.Children.Add(FilaDato(Tx("Fabricante y modelo"), equipo));
            datos.Children.Add(FilaDato(Tx("Número de serie"), e.NumeroSerie));
            datos.Children.Add(FilaDato(Tx("Tipo"), TextoEquipo.Tipo(e.Tipo) + (e.EsVirtual ? Tx(" (máquina virtual)") : "")));
            datos.Children.Add(FilaDato(Tx("Sistema"), e.SistemaOperativo));
            datos.Children.Add(FilaDato(Tx("Procesador"), Tf("{0} · {1} núcleos, {2} hilos", e.Procesador, e.NucleosFisicos, e.HilosLogicos)));
            var ram = Formato.Ram(e.RamTotalBytes) + (e.ModulosRam.Count > 0
                ? " · " + string.Join(", ", e.ModulosRam.Select(m => $"{Formato.Ram(m.Bytes)}{(m.MHz > 0 ? $" {m.MHz} MHz" : "")}"))
                : "");
            datos.Children.Add(FilaDato(Tx("Memoria"), ram));
            datos.Children.Add(FilaDato(Tx("Gráficos"), string.Join(", ", e.Graficas)));
            datos.Children.Add(FilaDato(Tx("Almacenamiento"), string.Join(", ", e.Discos.Select(d => $"{d.Modelo} ({Formato.Bytes(d.Bytes)} {d.Tipo})".Replace("  ", " ")))));
            if (e.Bateria is { } bat)
                datos.Children.Add(FilaDato(Tx("Batería"), string.Join(" · ", new[]
                {
                    bat.SaludPct is int s ? Tf("salud {0} %", s) : "",
                    bat.Ciclos is int c ? Tf("{0} ciclos", c) : ""
                }.Where(x => x.Length > 0))));
            datos.Children.Add(FilaDato(Tx("Placa base"), e.Placa));
            datos.Children.Add(FilaDato("BIOS", e.Bios));
            b.Add(datos);
        }

        b.Add(Seccion(Tx("Pruebas")));
        foreach (var (id, nombre) in Pruebas.Lista)
        {
            var v = Sesion.Obtener(id);
            var p = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 0, 6) };
            var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            cab.Children.Add(T(nombre, 12, Tinta, FontWeight.SemiBold));
            var pastilla = Pastilla(v);
            Grid.SetColumn(pastilla, 1);
            cab.Children.Add(pastilla);
            p.Children.Add(cab);
            foreach (var l in Sesion.Detalles(id)) p.Children.Add(T(l, 9.5, Suave));
            p.Children.Add(new Border { Height = 1, Background = Linea, Margin = new Thickness(0, 6, 0, 0) });
            b.Add(p);
        }

        if (!string.IsNullOrWhiteSpace(observaciones))
        {
            var o = new StackPanel();
            o.Children.Add(Seccion(Tx("Observaciones")));
            o.Children.Add(T(observaciones.Trim(), 10.5));
            b.Add(o);
        }
        return b;
    }

    public static async Task Crear(string ruta, string observaciones)
    {
        var anchoContenido = Ancho - 2 * Margen;
        var altoContenido = Alto - Cabecera - Pie;
        var bloques = Bloques(observaciones);
        foreach (var bl in bloques) bl.Measure(new Size(anchoContenido, double.PositiveInfinity));

        // Repartir los bloques en páginas.
        var paginas = new List<List<Control>> { new() };
        double ocupado = 0;
        foreach (var bl in bloques)
        {
            var h = bl.DesiredSize.Height;
            if (ocupado + h > altoContenido && paginas[^1].Count > 0)
            {
                paginas.Add(new());
                ocupado = 0;
            }
            paginas[^1].Add(bl);
            ocupado += h;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var meta = new SKDocumentPdfMetadata
        {
            Title = Tx("Informe de diagnóstico"),
            Creator = "Bancada",
            Producer = $"Bancada {version?.Major}.{version?.Minor}.{version?.Build}",
            Creation = DateTime.Now,
            // El lienzo del PDF trabaja en unidades de RasterDpi: a 96, una unidad es un píxel lógico de Avalonia
            // y la hoja de 794 × 1123 ocupa justo un A4 (595 × 842 puntos).
            RasterDpi = 96
        };
        var temporal = ruta + ".tmp";
        using (var flujo = new SKFileWStream(temporal))
        using (var doc = SKDocument.CreatePdf(flujo, meta))
        {
            for (int i = 0; i < paginas.Count; i++)
            {
                var hoja = Hoja(paginas[i], i + 1, paginas.Count, version);
                hoja.Measure(new Size(Ancho, Alto));
                hoja.Arrange(new Rect(0, 0, Ancho, Alto));
                var lienzo = doc.BeginPage((float)(Ancho * 0.75), (float)(Alto * 0.75));
                await DrawingContextHelper.RenderAsync(lienzo, hoja, new Rect(0, 0, Ancho, Alto), new Vector(96, 96));
                doc.EndPage();
            }
            doc.Close();
        }
        File.Move(temporal, ruta, overwrite: true);
    }

    static Control Hoja(List<Control> bloques, int numero, int total, Version? version)
    {
        var raiz = new Grid { Width = Ancho, Height = Alto, Background = Brushes.White, RowDefinitions = new RowDefinitions($"{Cabecera},*,{Pie}") };

        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(Margen, 22, Margen, 0), VerticalAlignment = VerticalAlignment.Top };
        var marca = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        marca.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = Cobre, VerticalAlignment = VerticalAlignment.Center });
        marca.Children.Add(T("Bancada", 12, Mascara, FontWeight.Bold));
        cab.Children.Add(marca);
        var fecha = T(Tf("Fecha: {0:dd/MM/yyyy}", DateTime.Now), 9.5, Suave);
        fecha.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(fecha, 2);
        cab.Children.Add(fecha);
        raiz.Children.Add(cab);

        var contenido = new StackPanel { Margin = new Thickness(Margen, 0) };
        foreach (var b in bloques) contenido.Children.Add(b);
        Grid.SetRow(contenido, 1);
        raiz.Children.Add(contenido);

        var pie = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(Margen, 0, Margen, 22), VerticalAlignment = VerticalAlignment.Bottom };
        pie.Children.Add(T(Tf("Generado con Bancada {0}.{1} · diagnóstico de hardware libre y gratuito", version?.Major, version?.Minor) + " · Bill Bull · github.com/bancada-hw/bancada", 8.5, Suave));
        var num = T(Tf("Página {0} de {1}", numero, total), 8.5, Suave);
        Grid.SetColumn(num, 1);
        pie.Children.Add(num);
        Grid.SetRow(pie, 2);
        raiz.Children.Add(pie);
        return raiz;
    }
}
