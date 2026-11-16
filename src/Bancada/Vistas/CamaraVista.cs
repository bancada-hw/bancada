using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Imagen en directo de cada cámara. Se apaga sola al salir de la prueba.</summary>
internal sealed class CamaraVista : UserControl
{
    readonly StackPanel panelCamaras = new() { Spacing = 10 };
    readonly Image imagen = new() { Stretch = Stretch.Uniform };
    readonly TextBlock textoApagada = Ui.Texto(Tx("Cámara apagada"), "suave");
    readonly TextBlock textoInfo = Ui.Texto("", "etiqueta mono");
    readonly TextBlock textoAviso = Ui.Texto("", "etiqueta");
    readonly Button botonApagar;
    List<Camara.Info> camaras = new();
    Thread? hilo;
    volatile bool encendida;
    volatile bool pintando;
    WriteableBitmap? lienzo;
    bool cargado;

    public CamaraVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Cámara"),
            Tx("Enciende cada cámara y comprueba que la imagen se ve nítida, con colores normales y sin cortes. La imagen no se guarda ni sale del equipo.")));

        botonApagar = Ui.Boton(Tx("Apagar la cámara"), "secundario", (_, _) => Apagar());
        botonApagar.IsVisible = false;

        textoApagada.HorizontalAlignment = HorizontalAlignment.Center;
        textoApagada.VerticalAlignment = VerticalAlignment.Center;
        var visor = new Border
        {
            Height = 400,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#0B0F0D")),
            Child = new Grid { Children = { textoApagada, imagen } }
        };

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Cámaras detectadas"), panelCamaras));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Imagen"), visor, Ui.Fila(textoInfo, botonApagar), textoAviso));
        cuerpo.Children.Add(new BarraVeredicto("camara", Tx("¿La cámara funciona bien?")));
        Content = pagina;

        AttachedToVisualTree += async (_, _) =>
        {
            if (cargado) return;
            cargado = true;
            panelCamaras.Children.Add(Ui.Cargando(Tx("Buscando cámaras…")));
            camaras = await Task.Run(Camara.Listar);
            await Sesion.EsperarEquipoAsync();
            PintarLista();
        };
        // Por privacidad, la cámara nunca se queda encendida fuera de esta pantalla.
        DetachedFromVisualTree += (_, _) => Apagar();
    }

    void PintarLista()
    {
        panelCamaras.Children.Clear();
        if (camaras.Count == 0)
        {
            var tipo = Sesion.Equipo?.Tipo;
            panelCamaras.Children.Add(
                Sesion.Equipo?.EsVirtual == true ? Ui.Chip(Tx("Sin cámaras visibles desde una máquina virtual"), Veredicto.Pendiente) :
                tipo is TipoEquipo.Sobremesa or TipoEquipo.MiniPc ? Ui.Chip(Tx("No hay ninguna cámara conectada (normal en un sobremesa sin webcam)"), Veredicto.Pendiente) :
                Ui.Chip(Tx("No se ha encontrado ninguna cámara"), Veredicto.Fallo));
            if (tipo is not (TipoEquipo.Sobremesa or TipoEquipo.MiniPc))
                panelCamaras.Children.Add(Ui.Texto(Tx("En portátiles, revisa si hay una tecla o un interruptor que desactive la cámara, o si está deshabilitada en la BIOS."), "etiqueta"));
            Sesion.Anotar("camara", Tx("Cámaras"), Tx("ninguna detectada"));
            return;
        }
        foreach (var c in camaras)
        {
            var cam = c;
            panelCamaras.Children.Add(Ui.Hundido(Ui.Fila(Ui.Texto(c.Nombre, "valor negrita"),
                Ui.Boton(Tx("Encender"), "primario", (_, _) => Encender(cam)))));
        }
        Sesion.Anotar("camara", Tx("Cámaras"), string.Join(", ", camaras.Select(c => c.Nombre)));
    }

    void Encender(Camara.Info info)
    {
        Apagar();
        encendida = true;
        textoApagada.Text = Tx("Encendiendo…");
        textoAviso.Text = "";
        textoInfo.Text = "";
        botonApagar.IsVisible = true;
        hilo = new Thread(() => Bucle(info)) { IsBackground = true, Name = Tx("Cámara") };
        hilo.Start();
    }

    void Apagar()
    {
        encendida = false;
        hilo?.Join(2000);
        hilo = null;
        imagen.Source = null;
        lienzo?.Dispose();
        lienzo = null;
        textoApagada.Text = Tx("Cámara apagada");
        botonApagar.IsVisible = false;
    }

    void Bucle(Camara.Info info)
    {
        Camara? cam = null;
        try
        {
            cam = Camara.Abrir(info);
            var fotograma = Array.Empty<byte>();
            var reloj = Stopwatch.StartNew();
            int fotogramas = 0, oscuros = 0;
            double fps = 0;
            while (encendida)
            {
                if (!cam.Leer(ref fotograma)) continue;
                fotogramas++;
                if (reloj.ElapsedMilliseconds >= 1000)
                {
                    fps = fotogramas * 1000.0 / reloj.ElapsedMilliseconds;
                    fotogramas = 0;
                    reloj.Restart();
                    oscuros = Brillo(fotograma) < 10 ? oscuros + 1 : 0;
                }
                if (pintando) continue; // si la interfaz va con retraso, se salta el fotograma
                pintando = true;
                var copia = fotograma;
                int w = cam.Ancho, h = cam.Alto;
                var formato = cam.Formato;
                var fpsTexto = fps;
                var negra = oscuros >= 2;
                fotograma = new byte[copia.Length];
                Dispatcher.UIThread.Post(() => Pintar(info, copia, w, h, formato, fpsTexto, negra));
            }
        }
        catch (Exception ex)
        {
            var mensaje = ex is InvalidOperationException ? ex.Message : Tf("Error con la cámara: {0}", ex.Message);
            Dispatcher.UIThread.Post(() =>
            {
                textoApagada.Text = Tx("No se ha podido encender");
                textoAviso.Text = mensaje;
                botonApagar.IsVisible = false;
                Sesion.Anotar("camara", info.Nombre, Tx("no se pudo encender: ") + mensaje);
            });
        }
        finally
        {
            try { cam?.Dispose(); } catch { }
        }
    }

    static double Brillo(byte[] bgra)
    {
        long suma = 0;
        int n = 0;
        for (int i = 0; i + 2 < bgra.Length; i += 4 * 97)
        {
            suma += (bgra[i] + 2 * bgra[i + 1] + bgra[i + 2]) / 4;
            n++;
        }
        return n == 0 ? 0 : (double)suma / n;
    }

    void Pintar(Camara.Info info, byte[] bgra, int w, int h, string formato, double fps, bool negra)
    {
        try
        {
            if (!encendida) return;
            if (lienzo == null || lienzo.PixelSize.Width != w || lienzo.PixelSize.Height != h)
            {
                lienzo?.Dispose();
                lienzo = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                imagen.Source = lienzo;
            }
            using (var fb = lienzo.Lock())
            {
                int fila = w * 4;
                for (int y = 0; y < h; y++)
                    Marshal.Copy(bgra, y * fila, fb.Address + y * fb.RowBytes, fila);
            }
            imagen.InvalidateVisual();
            textoApagada.Text = "";
            textoInfo.Text = Tf("{0} · {1} × {2} · {3} fotogramas/s", info.Nombre, w, h, Formato.N(fps, "0"));
            textoAviso.Text = negra
                ? Tx("La imagen sale negra: comprueba la tapa de privacidad de la cámara o la tecla que la desactiva.")
                : "";
            if (fps > 0) Sesion.Anotar("camara", info.Nombre, Tf("imagen {0} × {1} a {2} fotogramas/s", w, h, Formato.N(fps, "0")) + (negra ? Tx(" (negra)") : ""));
        }
        finally
        {
            pintando = false;
        }
    }
}
