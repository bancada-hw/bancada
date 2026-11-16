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

/// <summary>
/// Gráfica: se elige cuál probar (por su nombre) y se pone al máximo con cálculo comprobable y una animación,
/// vigilando temperatura, frecuencia y rendimiento. Aparte, prueba de la memoria de vídeo.
/// </summary>
internal sealed class GraficaVista : UserControl, IAutomatica
{
    readonly Propuesta propuesta = new("grafica");
    readonly HashSet<Gpu> estresHecho = new(), memoriaHecha = new();
    TaskCompletionSource fin = new();
    Task? memoriaEnCurso;
    Task? carga;

    /// <summary>
    /// Revisión completa: cada gráfica que se pueda poner a trabajar, una detrás de otra, pasa la prueba de estrés
    /// de los minutos indicados y la de memoria de vídeo. Se salta lo que ya se haya hecho a mano.
    /// </summary>
    public async Task EjecutarAsync(int minutos)
    {
        await CargarAsync();
        if (gpus.Count == 0 && Sesion.Equipo?.EsVirtual == true && Sesion.Obtener("grafica") == Veredicto.Pendiente)
            Sesion.Fijar("grafica", Veredicto.NoAplica);
        var lista = gpus.ToList();
        totalRevision = lista.Count;
        indiceRevision = 0;
        foreach (var g in lista)
        {
            indiceRevision++;
            await EsperarLibre();
            if (!estresHecho.Contains(g))
            {
                Seleccionar(g);
                Empezar(minutos);
                await fin.Task;
                if (errorGrave != null) continue;
            }
            await EsperarLibre();
            if (!memoriaHecha.Contains(g))
            {
                Seleccionar(g);
                await ProbarMemoria();
            }
        }
    }

    async Task EsperarLibre()
    {
        while (activo || memoriaEnCurso != null)
        {
            if (activo) await fin.Task;
            if (memoriaEnCurso is { } m) await m;
        }
    }

    void Seleccionar(Gpu g)
    {
        var item = selector.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, g));
        if (item == null) return;
        selector.SelectedItem = item;
        if (!Equals(elegida, g)) Elegir();
    }

    Task CargarAsync() => carga ??= Cargar();

    async Task Cargar()
    {
        panelGraficas.Children.Add(Ui.Cargando(Tx("Buscando gráficas…")));
        string? problema = null;
        gpus = await Task.Run(() => Gpus.Listar(out problema));
        await Sesion.EsperarEquipoAsync();
        PintarGraficas(problema);
    }

    readonly StackPanel panelGraficas = new() { Spacing = 10 };
    readonly StackPanel filaSelector;
    readonly ComboBox selector = new() { MinWidth = 340 };
    readonly TextBlock detalle = Ui.Texto("", "etiqueta");
    readonly Image imagen = new() { Stretch = Stretch.Uniform };
    readonly TextBlock textoReposo = Ui.Texto(Tx("La animación aparece al empezar la prueba"), "suave");
    readonly TextBlock textoTemp, textoFrec, textoUso, textoVatios;
    readonly TextBlock textoEstado = Ui.Texto("", "valor");
    readonly Grafica graficaTemp = new() { Unidad = " °C", Capacidad = 600, Height = 140, Minimo = 30, Maximo = 105 };
    readonly Grafica graficaRend = new() { Unidad = " %", Capacidad = 600, Height = 110, Minimo = 0, Maximo = 110 };
    readonly StackPanel panelResultado = new() { Spacing = 8 };
    readonly WrapPanel botonesDuracion;
    readonly Button botonParar;
    readonly Button botonMemoria;
    readonly ProgressBar barraMemoria = new() { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6, IsVisible = false };
    readonly TextBlock textoMemoria = Ui.Texto("", "etiqueta");
    readonly StackPanel panelMemoria = new() { Spacing = 8 };
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };

    List<Gpu> gpus = new();
    /// <summary>En la revisión completa: qué gráfica se está probando (desde 1) y cuántas hay.</summary>
    int indiceRevision, totalRevision;
    string PrefijoRevision => totalRevision > 1 ? Tf("Gráfica {0} de {1}", indiceRevision, totalRevision) + " · " : "";
    Gpu? elegida;
    SensorGpu? sensor;
    Thread? hilo;
    volatile bool activo;
    volatile bool pintando;
    volatile int fallosCalculo;
    volatile string? errorGrave;
    TrabajoGpu? trabajo;
    WriteableBitmap? lienzo;
    readonly Stopwatch crono = new();
    TimeSpan duracion;
    readonly Ritmo ritmo = new();
    double? tempInicial, tempMax;
    bool limitada;

    public GraficaVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Gráfica"),
            Tx("Elige la gráfica y ponla al máximo: se vigila la temperatura y el rendimiento, y se comprueba que calcula bien. Una gráfica que se recalienta, pierde velocidad o da errores provoca cuelgues, pantallazos y fallos de imagen.")));

        selector.SelectionChanged += (_, _) => Elegir();
        filaSelector = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var et = Ui.Texto(Tx("Gráfica que se va a probar"), "valor");
        et.VerticalAlignment = VerticalAlignment.Center;
        filaSelector.Children.Add(et);
        filaSelector.Children.Add(selector);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Gráficas"), filaSelector, detalle, panelGraficas));

        botonesDuracion = Ui.Botones(
            Ui.Boton(Tx("1 minuto"), "secundario", (_, _) => Empezar(1)),
            Ui.Boton(Tx("5 minutos"), "primario", (_, _) => Empezar(5)),
            Ui.Boton(Tx("15 minutos"), "secundario", (_, _) => Empezar(15)));
        botonParar = Ui.Boton(Tx("Parar"), "secundario", (_, _) => Terminar(Tx("Parada a mano.")));
        botonParar.IsVisible = false;
        graficaTemp.Series.Add(new Grafica.Serie(Tx("Temperatura"), Color.Parse("#D2453B")));
        graficaRend.Series.Add(new Grafica.Serie(Tx("Rendimiento"), Color.Parse("#2E9D57")));

        textoReposo.HorizontalAlignment = HorizontalAlignment.Center;
        textoReposo.VerticalAlignment = VerticalAlignment.Center;
        var visor = new Border
        {
            Height = 300,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#0B0F0D")),
            Child = new Grid { Children = { textoReposo, imagen } }
        };

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba de estrés"),
            Ui.Texto(Tx("Con 5 minutos basta para ver si la refrigeración aguanta. Enchufa el cargador si es un portátil."), "subtitulo"),
            Ui.Fila(botonesDuracion, botonParar),
            textoEstado,
            visor,
            Ui.Columnas(
                Ui.Cifra(Tx("Temperatura"), "—", "°C", out textoTemp),
                Ui.Cifra(Tx("Frecuencia"), "—", "MHz", out textoFrec),
                Ui.Cifra(Tx("Uso"), "—", "%", out textoUso),
                Ui.Cifra(Tx("Consumo"), "—", "W", out textoVatios)),
            textoFuenteTemp,
            Ui.Texto(Tx("Temperatura").ToUpperInvariant(), "seccion"),
            graficaTemp,
            Ui.Texto(Tx("Rendimiento (100 % = su mejor momento en la prueba)").ToUpperInvariant(), "seccion"),
            graficaRend,
            panelResultado));

        botonMemoria = Ui.Boton(Tx("Comprobar la memoria de vídeo"), "primario", async (_, _) => await ProbarMemoria());
        panelMemoria.Children.Add(Ui.Texto(Tx("Llena la memoria de la gráfica con patrones y comprueba cada dato. Los fallos de memoria de vídeo causan rayas, puntos y colores raros en pantalla."), "subtitulo"));
        panelMemoria.Children.Add(botonMemoria);
        panelMemoria.Children.Add(barraMemoria);
        panelMemoria.Children.Add(textoMemoria);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Memoria de vídeo"), panelMemoria));

        cuerpo.Children.Add(new BarraVeredicto("grafica", Tx("¿La gráfica funciona bien?")));
        Content = pagina;

        reloj.Tick += (_, _) => Tic();
        AttachedToVisualTree += async (_, _) =>
        {
            reloj.Start();
            await CargarAsync();
        };
        // Si hay una prueba en marcha, sigue aunque se cambie de pantalla.
        DetachedFromVisualTree += (_, _) => { if (!activo) reloj.Stop(); };
    }

    void PintarGraficas(string? problema)
    {
        panelGraficas.Children.Clear();
        selector.Items.Clear();
        foreach (var g in gpus) selector.Items.Add(new ComboBoxItem { Content = g.Nombre, Tag = g });

        // Gráficas que el sistema ve pero que no se pueden poner a trabajar (sin OpenCL).
        var sinPrueba = (Sesion.Equipo?.Graficas ?? new List<string>())
            .Where(n => !gpus.Any(g => Coincide(g.Nombre, n))).ToList();
        if (gpus.Count == 0)
        {
            filaSelector.IsVisible = detalle.IsVisible = false;
            botonesDuracion.IsEnabled = botonMemoria.IsEnabled = false;
            panelGraficas.Children.Add(Sesion.Equipo?.EsVirtual == true
                ? Ui.Chip(Tx("Sin gráficas visibles desde una máquina virtual"), Veredicto.Pendiente)
                : Ui.Chip(Tx("No se puede poner a trabajar ninguna gráfica"), Veredicto.Aviso));
            if (problema != null) panelGraficas.Children.Add(Ui.Texto(problema, "etiqueta"));
            panelGraficas.Children.Add(Ui.Texto(OperatingSystem.IsWindows()
                ? Tx("Instala o actualiza el controlador de la gráfica desde la web del fabricante (NVIDIA, AMD o Intel): incluye lo necesario para esta prueba.")
                : Tx("En Linux hace falta OpenCL: con NVIDIA viene con su controlador; con Intel instala intel-opencl-icd y con AMD mesa-opencl-icd o ROCm."), "etiqueta"));
        }
        else selector.SelectedIndex = 0;
        foreach (var n in sinPrueba)
            panelGraficas.Children.Add(Ui.Texto(Tf("{0}: el sistema la ve, pero su controlador no permite ponerla a trabajar.", n), "etiqueta"));
        Sesion.Anotar("grafica", Tx("Gráficas"), string.Join(", ", gpus.Select(g => g.Nombre).Concat(sinPrueba).Distinct()));
    }

    static bool Coincide(string a, string b)
    {
        string K(string s) => new(TextoEquipo.Hardware(s).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var x = K(a);
        var y = K(b);
        return x.Length > 3 && y.Length > 3 && (x.Contains(y) || y.Contains(x));
    }

    void Elegir()
    {
        if (activo || selector.SelectedItem is not ComboBoxItem { Tag: Gpu g }) return;
        elegida = g;
        var partes = new List<string>
        {
            g.Integrada ? Tx("integrada en el procesador, comparte la memoria del sistema") : Tf("{0} de memoria propia", Formato.Bytes(g.Memoria)),
            Tf("{0} unidades de cálculo", g.Unidades),
            g.Mhz > 0 ? $"{g.Mhz} MHz" : "",
            g.Driver.Length > 0 ? Tf("controlador {0}", g.Driver) : ""
        };
        detalle.Text = string.Join(" · ", partes.Where(p => p.Length > 0));
        sensor = null;
        Task.Run(() => SensorGpu.Para(g)).ContinueWith(t => Dispatcher.UIThread.Post(() => { if (elegida == g) sensor = t.Result; }));
    }

    readonly Sensores sensoresCpu = new();
    readonly TextBlock textoFuenteTemp = Ui.Texto("", "etiqueta");

    void Tic()
    {
        var s = sensor;
        var integrada = elegida?.Integrada == true;
        if (s != null || integrada)
        {
            Task.Run(() =>
            {
                var l = s?.Leer() ?? new LecturaGpu(null, null, null, null, false);
                // Las integradas no suelen dar temperatura propia: van dentro del procesador, que sí la da.
                bool deCpu = false;
                if (l.TempC == null && integrada && sensoresCpu.Leer().TempCpu is double cpu)
                {
                    l = l with { TempC = cpu };
                    deCpu = true;
                }
                return (l, deCpu);
            }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                MostrarSensor(t.Result.l);
                textoFuenteTemp.Text = t.Result.deCpu ? Tx("Temperatura del procesador: esta gráfica va dentro de él y no da la suya propia.") : "";
            }));
        }
        else if (!activo)
        {
            textoTemp.Text = textoFrec.Text = textoUso.Text = textoVatios.Text = "—";
        }
        if (activo) Avanzar();
    }

    LecturaGpu? ultima;

    void MostrarSensor(LecturaGpu l)
    {
        ultima = l;
        textoTemp.Text = l.TempC is double t ? Formato.N(t, "0") : "—";
        textoFrec.Text = l.Mhz is double m ? Formato.N(m, "0") : "—";
        textoUso.Text = l.UsoPct is double u ? Formato.N(u, "0") : "—";
        textoVatios.Text = l.Vatios is double w ? Formato.N(w, "0.0") : "—";
    }

    // ---------- Estrés ----------

    void Empezar(int minutos)
    {
        if (activo || elegida == null) return;
        var g = elegida;
        duracion = TimeSpan.FromMinutes(minutos);
        graficaTemp.Vaciar();
        graficaRend.Vaciar();
        panelResultado.Children.Clear();
        ritmo.Reiniciar();
        tempInicial = tempMax = null;
        limitada = false;
        fallosCalculo = 0;
        errorGrave = null;
        activo = true;
        fin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PruebasEnCurso.Entrar();
        selector.IsEnabled = false;
        botonesDuracion.IsEnabled = false;
        botonMemoria.IsEnabled = false;
        botonParar.IsVisible = true;
        textoEstado.Text = Tx("Preparando la gráfica…");
        textoReposo.Text = "";
        crono.Restart();
        reloj.Start();
        hilo = new Thread(() => Bucle(g)) { IsBackground = true, Name = "Estrés de gráfica" };
        hilo.Start();
    }

    void Bucle(Gpu g)
    {
        try
        {
            trabajo = new TrabajoGpu(g);
            var fotograma = new byte[TrabajoGpu.AnchoImagen * TrabajoGpu.AltoImagen * 4];
            var ultimaImagen = Stopwatch.StartNew();
            var ultimaVerificacion = Stopwatch.StartNew();
            float t = 0;
            while (activo)
            {
                var ms = trabajo.Lanzar();
                if (ms > 1500) throw new InvalidOperationException(Tx("La gráfica tarda demasiado en responder."));
                if (ultimaVerificacion.ElapsedMilliseconds > 1000)
                {
                    fallosCalculo += trabajo.Verificar();
                    ultimaVerificacion.Restart();
                }
                if (ultimaImagen.ElapsedMilliseconds > 120 && !pintando)
                {
                    t += 0.03f;
                    trabajo.Fractal(t, fotograma);
                    pintando = true;
                    var copia = (byte[])fotograma.Clone();
                    Dispatcher.UIThread.Post(() => Pintar(copia));
                    ultimaImagen.Restart();
                }
            }
        }
        catch (Exception ex)
        {
            errorGrave = ex.Message;
            Dispatcher.UIThread.Post(() => Terminar(null));
        }
        finally
        {
            try { trabajo?.Dispose(); } catch { }
            trabajo = null;
        }
    }

    void Pintar(byte[] bgra)
    {
        try
        {
            if (!activo) return;
            int w = TrabajoGpu.AnchoImagen, h = TrabajoGpu.AltoImagen;
            if (lienzo == null)
            {
                lienzo = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                imagen.Source = lienzo;
            }
            using (var fb = lienzo.Lock())
                for (int y = 0; y < h; y++)
                    Marshal.Copy(bgra, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
            imagen.InvalidateVisual();
        }
        finally { pintando = false; }
    }

    void Avanzar()
    {
        // Referencia: el primer medio minuto, sin los primeros segundos de calibrado (ver Ritmo).
        if (trabajo is { } tr && ritmo.Anotar(crono.Elapsed.TotalSeconds, tr.Operaciones, desde: 3) is double pct)
            graficaRend.Anadir(0, pct);
        if (ultima?.TempC is double t)
        {
            tempInicial ??= t;
            tempMax = Math.Max(tempMax ?? t, t);
            graficaTemp.Anadir(0, t);
        }
        if (ultima?.LimitadaPorTemperatura == true) limitada = true;

        var queda = duracion - crono.Elapsed;
        textoEstado.Text = Tf("En marcha · quedan {0} · errores de cálculo: {1}", Ui.Duracion(queda), fallosCalculo) +
                           (tempMax is double tm ? Tf(" · máxima {0} °C", Formato.N(tm, "0")) : "");
        Revision.Informar("grafica", PrefijoRevision + Tf("{0}: estrés · {1}", elegida?.Nombre ?? "", textoEstado.Text),
            100 * crono.Elapsed.TotalSeconds / Math.Max(1, duracion.TotalSeconds));
        if (queda <= TimeSpan.Zero) Terminar(null);
    }

    void Terminar(string? motivo)
    {
        if (!activo) return;
        activo = false;
        PruebasEnCurso.Salir();
        hilo?.Join(3000);
        hilo = null;
        selector.IsEnabled = true;
        botonesDuracion.IsEnabled = true;
        botonMemoria.IsEnabled = true;
        botonParar.IsVisible = false;
        imagen.Source = null;
        lienzo = null;
        textoReposo.Text = Tx("La animación aparece al empezar la prueba");
        var transcurrido = crono.Elapsed;
        crono.Stop();
        try { Concluir(motivo, transcurrido); }
        finally { fin.TrySetResult(); }
    }

    void Concluir(string? motivo, TimeSpan transcurrido)
    {
        var g = elegida;
        var nombre = g?.Nombre ?? "";
        var problemas = new List<(Veredicto v, string texto)>();
        if (errorGrave != null)
            problemas.Add((Veredicto.Fallo, Tf("La prueba se ha interrumpido: {0}", errorGrave)));
        textoEstado.Text = motivo ?? (errorGrave != null ? Tx("Prueba interrumpida.") : Tf("Terminada ({0}).", Ui.Duracion(transcurrido)));
        panelResultado.Children.Clear();
        if (transcurrido.TotalSeconds < 45 && errorGrave == null)
        {
            panelResultado.Children.Add(Ui.Texto(Tx("La prueba ha durado muy poco para sacar conclusiones."), "etiqueta"));
            return;
        }
        if (g != null && (motivo == null || errorGrave != null)) estresHecho.Add(g);

        var rendFinal = ritmo.Final;
        if (fallosCalculo > 0)
            problemas.Add((Veredicto.Fallo, Tf("La gráfica ha dado {0} resultados erróneos: está inestable o averiada.", fallosCalculo)));
        if (tempMax >= 95)
            problemas.Add((Veredicto.Aviso, Tf("Llega a {0} °C: demasiado caliente. Limpia ventiladores y disipador; si no mejora, cambia la pasta y las almohadillas térmicas.", Formato.N(tempMax.Value, "0"))));
        if (rendFinal < 70 || limitada)
            problemas.Add((Veredicto.Aviso, Tf("Al final rinde al {0} % de su mejor momento: se frena por temperatura.", Formato.N(rendFinal, "0"))));

        panelResultado.Children.Add(Ui.Columnas(
            Ui.Cifra(Tx("Máxima"), tempMax is double mx ? Formato.N(mx, "0") : "—", "°C"),
            Ui.Cifra(Tx("Rendimiento final"), Formato.N(rendFinal, "0"), "%"),
            Ui.Cifra(Tx("Errores"), fallosCalculo.ToString(), "")));
        if (problemas.Count == 0)
            panelResultado.Children.Add(Ui.Chip(Tx("Aguanta la carga sin calentarse en exceso ni perder rendimiento"), Veredicto.Ok));
        foreach (var (_, texto) in problemas) panelResultado.Children.Add(Ui.Aviso(texto));

        var resumen = new List<string> { Tf("{0} al 100 %", Ui.Duracion(transcurrido)) };
        if (tempInicial is double ti && tempMax is double tmx) resumen.Add(Tf("temperatura {0} → {1} °C", Formato.N(ti, "0"), Formato.N(tmx, "0")));
        resumen.Add(Tf("rendimiento final {0} %", Formato.N(rendFinal, "0")));
        resumen.Add(Tf("errores de cálculo {0}", fallosCalculo));
        // Una parte por gráfica, para que la segunda no borre lo de la primera en el informe.
        Sesion.Anotar("grafica", $"{Tx("Prueba de estrés")} · {nombre}", string.Join(", ", resumen));
        Sesion.Anotar("grafica", $"{Tx("Aviso")} · {nombre}", problemas.Select(p => p.texto).ToArray());
        propuesta.Parte("estres|" + nombre, problemas.Count == 0 ? Veredicto.Ok : problemas.Max(p => p.v));
    }

    // ---------- Memoria de vídeo ----------

    Task ProbarMemoria()
    {
        if (activo || elegida == null) return Task.CompletedTask;
        if (memoriaEnCurso != null) return memoriaEnCurso;
        var g = elegida;
        memoriaEnCurso = ProbarMemoria(g);
        return memoriaEnCurso;
    }

    async Task ProbarMemoria(Gpu g)
    {
        await Task.Yield();
        botonMemoria.IsEnabled = false;
        botonesDuracion.IsEnabled = false;
        selector.IsEnabled = false;
        barraMemoria.IsVisible = true;
        barraMemoria.Value = 0;
        PruebasEnCurso.Entrar();
        var resultado = panelMemoria.Children.OfType<Control>().Where(c => c.Tag as string == "resultado").ToList();
        foreach (var c in resultado) panelMemoria.Children.Remove(c);
        try
        {
            var progreso = new Progress<(double pct, string texto)>(p =>
            {
                barraMemoria.Value = p.pct;
                textoMemoria.Text = p.texto;
                Revision.Informar("grafica", PrefijoRevision + Tf("{0}: memoria de vídeo · {1}", g.Nombre, p.texto), p.pct);
            });
            var (probados, errores, pasadas) = await Task.Run(() =>
            {
                using var t = new TrabajoGpu(g);
                var r = t.ProbarMemoria(progreso, CancellationToken.None);
                return (r.probados, r.errores, t.Pasadas);
            });
            var gb = Formato.N(probados / 1073741824.0, "0.#");
            textoMemoria.Text = Tf("{0} pasadas con patrones distintos.", pasadas);
            Control chip = errores == 0
                ? Ui.Chip(Tf("Sin errores en {0} GB de memoria de vídeo", gb), Veredicto.Ok)
                : Ui.Chip(Tf("{0} errores en {1} GB de memoria de vídeo", Formato.N(errores, "#,0"), gb), Veredicto.Fallo);
            chip.Tag = "resultado";
            panelMemoria.Children.Add(chip);
            if (errores > 0)
            {
                var aviso = Ui.Aviso(Tx("La memoria de vídeo devuelve datos distintos de los escritos: la gráfica tiene un fallo de memoria. Suele verse como rayas o puntos de colores."));
                aviso.Tag = "resultado";
                panelMemoria.Children.Add(aviso);
            }
            Sesion.Anotar("grafica", $"{Tx("Memoria de vídeo")} · {g.Nombre}", errores == 0 ? Tf("sin errores en {0} GB", gb) : Tf("{0} errores en {1} GB", errores, gb));
            propuesta.Parte("memoria|" + g.Nombre, errores == 0 ? Veredicto.Ok : Veredicto.Fallo);
            memoriaHecha.Add(g);
        }
        catch (Exception ex)
        {
            var chip = Ui.Chip(Tx("No se ha podido probar la memoria de vídeo"), Veredicto.Aviso);
            chip.Tag = "resultado";
            panelMemoria.Children.Add(chip);
            textoMemoria.Text = ex.Message;
            memoriaHecha.Add(g);
        }
        memoriaEnCurso = null;
        PruebasEnCurso.Salir();
        barraMemoria.IsVisible = false;
        botonMemoria.IsEnabled = true;
        botonesDuracion.IsEnabled = true;
        selector.IsEnabled = true;
    }
}
