using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Altavoces (izquierdo, derecho, barrido) y micrófono (nivel en directo y grabar-escuchar).</summary>
internal sealed class AudioVista : UserControl
{
    const double AnchoMedidor = 420;

    readonly StackPanel panelDispositivos = new() { Spacing = 10 };
    readonly TextBlock estadoAltavoces = Ui.Texto("", "etiqueta");
    readonly Border relleno = new() { Height = 10, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(5) };
    readonly TextBlock textoNivel = Ui.Texto("—", "valor mono");
    readonly TextBlock estadoMicro = Ui.Texto(Tx("Pulsa «Escuchar el micrófono» y habla o da una palmada."), "etiqueta");
    readonly Button botonEscuchar;
    readonly Button botonGrabar;
    readonly HashSet<string> reproducidos = new();

    Grabadora? grabadora;
    List<short>? grabacion;
    double maximoDb = -120;
    long muestrasSilencio, muestrasTotales;
    bool cargado;

    public AudioVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Sonido"),
            Tx("Comprueba cada altavoz por separado y el micrófono. Después, enchufa unos auriculares y repite para probar la toma de auriculares.")));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Dispositivos de sonido"), panelDispositivos));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Altavoces"),
            Ui.Texto(Tx("Cada botón suena solo por un lado. En el barrido, de graves a agudos, escucha si algo vibra, zumba, cruje o se corta."), "subtitulo"),
            Ui.Botones(
                Ui.Boton("◀  " + Tx("Izquierdo"), "secundario", (_, _) => Sonar(Tx("el izquierdo"), false, Sonido.Campana(Canal.Izquierdo))),
                Ui.Boton(Tx("Derecho") + "  ▶", "secundario", (_, _) => Sonar(Tx("el derecho"), false, Sonido.Campana(Canal.Derecho))),
                Ui.Boton(Tx("Los dos"), "secundario", (_, _) => Sonar(Tx("los dos"), false, Sonido.Campana(Canal.Ambos))),
                Ui.Boton(Tx("Barrido de graves a agudos"), "primario", (_, _) => Sonar(Tx("el barrido"), true, Sonido.Barrido()))),
            estadoAltavoces));

        relleno.Classes.Add("barra");
        relleno.Classes.Add("ok");
        var medidor = new Border { Width = AnchoMedidor, Height = 10, Child = relleno, VerticalAlignment = VerticalAlignment.Center }.Clases("barra-fondo");
        botonEscuchar = Ui.Boton(Tx("Escuchar el micrófono"), "secundario", (_, _) => AlternarEscucha());
        botonGrabar = Ui.Boton(Tx("Grabar 5 segundos y reproducir"), "primario", async (_, _) => await GrabarYReproducir());
        var filaNivel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        filaNivel.Children.Add(medidor);
        filaNivel.Children.Add(textoNivel);

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Micrófono"),
            Ui.Texto(Tx("Habla a un palmo del equipo: la barra debe moverse con tu voz. Al grabar, escucharás lo que ha captado."), "subtitulo"),
            filaNivel,
            Ui.Botones(botonEscuchar, botonGrabar),
            estadoMicro));

        cuerpo.Children.Add(new BarraVeredicto("audio", Tx("¿El sonido funciona bien?")));
        Content = pagina;

        AttachedToVisualTree += async (_, _) =>
        {
            if (cargado) return;
            cargado = true;
            panelDispositivos.Children.Add(Ui.Cargando(Tx("Buscando tarjetas de sonido…")));
            var d = await Task.Run(Sonido.Listar);
            await Sesion.EsperarEquipoAsync();
            PintarDispositivos(d);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            PararGrabadora();
            Sonido.Detener();
        };
    }

    void PintarDispositivos(Sonido.Dispositivos d)
    {
        panelDispositivos.Children.Clear();
        if (d.Tarjetas.Count == 0 && d.Salidas.Count == 0 && d.Entradas.Count == 0)
        {
            panelDispositivos.Children.Add(Sesion.Equipo?.EsVirtual == true
                ? Ui.Chip(Tx("Sin tarjeta de sonido visible desde una máquina virtual"), Veredicto.Pendiente)
                : Ui.Chip(Tx("No se ha encontrado ninguna tarjeta de sonido"), Veredicto.Fallo));
            return;
        }
        void Grupo(string titulo, List<string> lista)
        {
            if (lista.Count == 0) return;
            panelDispositivos.Children.Add(Ui.Dato(titulo, string.Join("\n", lista.Distinct())));
        }
        Grupo(Tx("Tarjetas de sonido"), d.Tarjetas);
        Grupo(Tx("Salidas (altavoces, auriculares, HDMI)"), d.Salidas);
        Grupo(Tx("Entradas (micrófonos)"), d.Entradas);
        Sesion.Anotar("audio", Tx("Tarjetas"), string.Join(", ", d.Tarjetas.Distinct()));
        if (d.Entradas.Count == 0)
            panelDispositivos.Children.Add(Ui.Chip(Tx("No hay ningún micrófono activo"), Veredicto.Aviso));
    }

    void Sonar(string que, bool barrido, byte[] wav)
    {
        var error = Sonido.Reproducir(wav);
        if (error != null)
        {
            estadoAltavoces.Text = error;
            return;
        }
        reproducidos.Add(que);
        estadoAltavoces.Text = barrido ? Tx("Sonando el barrido (6 segundos)…") : $"Sonando: {que}.";
        Sesion.Anotar("audio", Tx("Altavoces probados"), string.Join(", ", reproducidos));
    }

    // ---------- Micrófono ----------

    void AlternarEscucha()
    {
        if (grabadora != null)
        {
            PararGrabadora();
            return;
        }
        if (EmpezarGrabadora()) estadoMicro.Text = Tx("Escuchando… habla o da una palmada.");
    }

    bool EmpezarGrabadora()
    {
        maximoDb = -120;
        muestrasSilencio = muestrasTotales = 0;
        grabadora = Grabadora.Crear();
        grabadora.Muestras += AlRecibir;
        var error = grabadora.Iniciar();
        if (error != null)
        {
            grabadora.Dispose();
            grabadora = null;
            estadoMicro.Text = error;
            return false;
        }
        botonEscuchar.Content = Tx("Dejar de escuchar");
        return true;
    }

    void PararGrabadora()
    {
        var g = grabadora;
        grabadora = null;
        g?.Dispose();
        botonEscuchar.Content = Tx("Escuchar el micrófono");
        relleno.Width = 0;
    }

    void AlRecibir(short[] bloque)
    {
        int pico = 0;
        foreach (var s in bloque)
        {
            var a = Math.Abs((int)s);
            if (a > pico) pico = a;
        }
        lock (this)
        {
            grabacion?.AddRange(bloque);
            muestrasTotales += bloque.Length;
            if (pico == 0) muestrasSilencio += bloque.Length;
        }
        var db = pico == 0 ? -120 : 20 * Math.Log10(pico / 32768.0);
        Dispatcher.UIThread.Post(() =>
        {
            if (grabadora == null) return;
            if (db > maximoDb)
            {
                maximoDb = db;
                if (db > -60) Sesion.Anotar("audio", Tx("Micrófono, nivel máximo"), $"{Formato.N(db, "0")} dBFS");
            }
            relleno.Width = AnchoMedidor * Math.Clamp((db + 60) / 60, 0, 1);
            Ui.FijarEstado(relleno, db > -3 ? Veredicto.Aviso : Veredicto.Ok);
            textoNivel.Text = db <= -119 ? Tx("silencio") : $"{Formato.N(db, "0")} dB";
            if (muestrasTotales > Sonido.Frecuencia * 2 && muestrasSilencio == muestrasTotales)
                estadoMicro.Text = OperatingSystem.IsWindows()
                    ? Tx("Llega silencio absoluto: el micrófono puede estar silenciado (tecla del teclado) o bloqueado en Configuración › Privacidad y seguridad › Micrófono.")
                    : Tx("Llega silencio absoluto: el micrófono puede estar silenciado en los ajustes de sonido o con una tecla del teclado.");
        });
    }

    async Task GrabarYReproducir()
    {
        botonGrabar.IsEnabled = false;
        botonEscuchar.IsEnabled = false;
        bool yaEscuchando = grabadora != null;
        lock (this) grabacion = new List<short>(Sonido.Frecuencia * 6);
        if (!yaEscuchando && !EmpezarGrabadora())
        {
            botonGrabar.IsEnabled = botonEscuchar.IsEnabled = true;
            return;
        }
        for (int s = 5; s > 0; s--)
        {
            estadoMicro.Text = Tf("Grabando… habla ahora ({0})", s);
            await Task.Delay(1000);
        }
        short[] muestras;
        lock (this)
        {
            muestras = grabacion!.ToArray();
            grabacion = null;
        }
        if (!yaEscuchando) PararGrabadora();

        if (muestras.Length == 0)
        {
            estadoMicro.Text = Tx("No ha llegado sonido del micrófono.");
        }
        else
        {
            var error = Sonido.Reproducir(Sonido.Wav(muestras, 1));
            estadoMicro.Text = error ?? Tx("Reproduciendo la grabación. ¿Se te oye claro, sin ruido ni cortes?");
            Sesion.Anotar("audio", Tx("Micrófono"), Tx("grabación de 5 s reproducida"));
        }
        botonGrabar.IsEnabled = botonEscuchar.IsEnabled = true;
    }
}
