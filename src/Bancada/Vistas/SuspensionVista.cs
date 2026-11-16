using Avalonia.Controls;
using Avalonia.Threading;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Suspensión: el equipo se duerme, se despierta (solo o con una tecla) y se comprueba que vuelve bien.</summary>
internal sealed class SuspensionVista : UserControl
{
    const int Segundos = 30;

    readonly StackPanel panelEquipo = new() { Spacing = 8 };
    readonly StackPanel panelResultado = new() { Spacing = 8 };
    readonly TextBlock textoEstado = Ui.Texto("", "valor");
    readonly TextBlock textoOcupado = Ui.Texto("", "etiqueta");
    readonly TextBlock textoInstrucciones = Ui.Texto(Tf("Guarda lo que tengas abierto. Bancada suspenderá el equipo y lo despertará solo a los {0} segundos. Si no vuelve solo, despiértalo con una tecla o con el botón de encendido.", Segundos), "subtitulo");
    readonly Button botonSuspender;
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly VigiaSuspension vigia = new();
    CapacidadSuspension? capacidad;
    bool esperando, comprobando, preparando, cargado;
    DateTime inicio;
    bool redAntes;
    int fallosAntes;

    /// <summary>Se completa cuando acaba una prueba (true) o no se pudo suspender (false). Para --suspender.</summary>
    public static readonly TaskCompletionSource<bool> Terminada = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SuspensionVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Suspensión"),
            Tx("Muchos portátiles no vuelven bien de la suspensión: pantalla negra, sin Wi-Fi, sin sonido o colgados. Esta prueba suspende el equipo, lo despierta y comprueba que todo vuelve.")));

        if (Suspension.PruebaSinTerminar() is DateTime cuando)
        {
            cuerpo.Children.Add(Ui.Aviso(Tf("La prueba de suspensión del {0:dd/MM/yyyy} a las {0:HH:mm} no terminó: el equipo no volvió y hubo que reiniciarlo. Eso es un fallo de suspensión.", cuando)));
            Sesion.Anotar("suspension", Tx("Resultado"), Tx("el equipo no volvió de la suspensión y hubo que reiniciarlo"));
            Sesion.Fijar("suspension", Veredicto.Fallo);
            Suspension.Desmarcar();
        }

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Este equipo"), panelEquipo));

        botonSuspender = Ui.Boton(Tf("Suspender {0} segundos", Segundos), "primario", (_, _) => Suspender());
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba"),
            textoInstrucciones,
            botonSuspender,
            textoOcupado,
            textoEstado,
            Ui.Texto(OperatingSystem.IsWindows()
                ? Tx("También puedes suspenderlo tú (menú Inicio, botón de encendido o cerrando la tapa) y despertarlo: Bancada lo detecta igual mientras esté en esta pantalla.")
                : Tx("También puedes suspenderlo tú (menú del sistema, botón de encendido o cerrando la tapa) y despertarlo: Bancada lo detecta igual mientras esté en esta pantalla."), "etiqueta"),
            panelResultado));
        cuerpo.Children.Add(new BarraVeredicto("suspension", Tx("¿El equipo se suspende y vuelve bien?")));
        Content = pagina;

        // Sin texto, el estado no ocupa sitio en la tarjeta.
        textoEstado.IsVisible = false;
        textoEstado.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty) textoEstado.IsVisible = !string.IsNullOrEmpty(textoEstado.Text);
        };
        reloj.Tick += async (_, _) => await Tic();
        AttachedToVisualTree += async (_, _) =>
        {
            reloj.Start();
            if (cargado) return;
            cargado = true;
            capacidad = await Task.Run(Suspension.Capacidad);
            await Sesion.EsperarEquipoAsync();
            PintarEquipo();
        };
        DetachedFromVisualTree += (_, _) => { if (!esperando) reloj.Stop(); };
    }

    void PintarEquipo()
    {
        panelEquipo.Children.Clear();
        if (capacidad == null) return;
        panelEquipo.Children.Add(Ui.Fila(Ui.Texto(capacidad.Tipo, "valor negrita"),
            capacidad.Puede ? Ui.Chip(Tx("Disponible"), Veredicto.Ok) : Ui.Chip(Tx("No disponible"), Sesion.Equipo?.EsVirtual == true ? Veredicto.Pendiente : Veredicto.Aviso)));
        if (capacidad.Nota != null) panelEquipo.Children.Add(Ui.Texto(capacidad.Nota, "etiqueta"));
        if (capacidad.Puede && !capacidad.DespiertaSolo)
        {
            // Se despierta a mano: es como falla en la vida real (al abrir la tapa o pulsar una tecla).
            textoInstrucciones.Text = Tf("Guarda lo que tengas abierto. Bancada pondrá el equipo en suspensión (se apaga la pantalla). Espera al menos {0} segundos y despiértalo con una tecla, el touchpad o el botón de encendido, como lo haría el cliente.", Segundos);
            botonSuspender.Content = Tx("Suspender ahora");
        }
        ActualizarBoton();
        Sesion.Anotar("suspension", Tx("Tipo"), capacidad.Tipo);
        if (!capacidad.Puede && Sesion.Equipo?.EsVirtual == true && Sesion.Obtener("suspension") == Veredicto.Pendiente)
            Sesion.Fijar("suspension", Veredicto.NoAplica);
        if (Opciones.Suspender)
        {
            if (capacidad.Puede) Suspender();
            else Terminada.TrySetResult(false);
        }
    }

    /// <summary>
    /// No se suspende con otra prueba en marcha (estrés, memoria, descarga de batería…): la estropearía.
    /// En la revisión completa, este paso espera a que terminen las automáticas.
    /// </summary>
    void ActualizarBoton()
    {
        var propia = esperando || comprobando || preparando;
        var otra = !propia && PruebasEnCurso.Hay;
        botonSuspender.IsEnabled = capacidad?.Puede == true && !propia && !otra;
        textoOcupado.IsVisible = otra && capacidad?.Puede == true;
        if (textoOcupado.IsVisible)
            textoOcupado.Text = Revision.EnMarcha is string id
                ? Tf("Espera a que termine la prueba en marcha ({0}): suspender ahora la estropearía.", Pruebas.Nombre(id))
                : Tx("Espera a que termine la prueba en marcha: suspender ahora la estropearía.");
    }

    async void Suspender()
    {
        if (esperando || preparando || PruebasEnCurso.Hay) return;
        preparando = true;
        panelResultado.Children.Clear();
        botonSuspender.IsEnabled = false;
        textoEstado.Text = Tx("Preparando…");
        redAntes = Suspension.HayRed();
        fallosAntes = await Task.Run(Suspension.DispositivosConFallos);
        inicio = DateTime.Now;
        esperando = true;
        preparando = false;
        PruebasEnCurso.Entrar();
        Suspension.Marcar();
        textoEstado.Text = Tx("Suspendiendo…");
        revisado = false;
        await Task.Delay(1500); // que se vea el mensaje antes de que se apague la pantalla
        var error = Suspension.Suspender(Segundos, capacidad?.Moderna == true);
        if (error != null)
        {
            esperando = false;
            PruebasEnCurso.Salir();
            Suspension.Desmarcar();
            botonSuspender.IsEnabled = true;
            textoEstado.Text = error;
            Terminada.TrySetResult(false);
        }
        else textoEstado.Text = Tx("Esperando a que el equipo se suspenda…");
    }

    async Task Tic()
    {
        ActualizarBoton();
        var dormido = vigia.Comprobar();
        if (comprobando) return; // mientras se comprueba la vuelta no se toca nada (antes se pisaba el resultado)
        if (dormido is TimeSpan d) await AlVolver(d);
        else if (esperando && Suspension.ErrorAlSuspender is string rechazo)
        {
            // Windows la ha rechazado en el acto.
            esperando = false;
            PruebasEnCurso.Salir();
            Suspension.Desmarcar();
            textoEstado.Text = rechazo;
            Terminada.TrySetResult(false);
        }
        else if (esperando && !revisado && DateTime.Now - inicio > TimeSpan.FromSeconds(Segundos + 8))
        {
            // Pasado el tiempo sin notar nada: puede que el programa no se haya congelado (en la suspensión moderna
            // no siempre se congela). Se mira el registro de Windows antes de dar la prueba por fallida.
            revisado = true;
            InformeSuspension? informe = null;
            informe = await Task.Run(() => Suspension.Analizar(inicio));
            if (informe is { Modo.Length: > 0 }) { await AlVolver(informe.Duracion ?? DateTime.Now - inicio); return; }
            Suspension.EncenderPantalla(); // si solo se apagó la pantalla, que vuelva
            esperando = false;
            PruebasEnCurso.Salir();
            Suspension.Desmarcar();
            textoEstado.Text = Tx("No parece que se haya suspendido. Puede que Windows o el sistema lo hayan impedido (algún programa o dispositivo no lo deja).");
            if (informe?.Frenos.Count > 0)
                textoEstado.Text += " " + Tf("Lo frenó el controlador: {0}.", string.Join(", ", informe.Frenos));
            Terminada.TrySetResult(false);
        }
    }

    bool revisado;

    async Task AlVolver(TimeSpan dormido)
    {
        comprobando = true;
        bool automatica = esperando;
        if (!automatica)
        {
            // Suspensión hecha a mano: se miden igual las comprobaciones de después.
            panelResultado.Children.Clear();
            redAntes = true;
            fallosAntes = 0;
            inicio = DateTime.Now - dormido;
        }
        Suspension.EncenderPantalla();
        textoEstado.Text = Tx("El equipo ha vuelto. Comprobando la red y los dispositivos…");

        var red = redAntes ? await Suspension.EsperarRedAsync(40) : null;
        var fallosDespues = await Task.Run(Suspension.DispositivosConFallos);
        // Lo que dice Windows manda sobre lo que mide el programa: tipo real, duración y qué lo despertó.
        // Windows tarda unos segundos en escribir los eventos de la vuelta: se reintenta hasta 15 s.
        InformeSuspension? informe = null;
        if (OperatingSystem.IsWindows())
            for (int intento = 0; intento < 8 && informe is not { Modo.Length: > 0 }; intento++)
            {
                if (intento > 0) await Task.Delay(2000);
                informe = await Task.Run(() => Suspension.Analizar(inicio.AddSeconds(-5)));
            }
        if (informe?.Duracion is TimeSpan real && real > TimeSpan.FromSeconds(1)) dormido = real;

        var lineas = new List<(Veredicto v, string texto)>();
        lineas.Add((Veredicto.Ok, informe?.Modo switch
        {
            InformeSuspension.Moderna => Tf("Ha entrado en suspensión moderna y ha vuelto tras {0}.", Ui.Duracion(dormido)),
            InformeSuspension.Clasica => Tf("Ha entrado en suspensión (S3) y ha vuelto tras {0}.", Ui.Duracion(dormido)),
            InformeSuspension.Hibernacion => Tf("Windows lo ha hibernado (apagado guardando la memoria en el disco) en lugar de suspenderlo, y ha vuelto tras {0}.", Ui.Duracion(dormido)),
            _ => Tf("Ha estado suspendido {0} y ha vuelto.", Ui.Duracion(dormido))
        }));
        if (automatica && capacidad?.DespiertaSolo == true)
        {
            bool solo = informe?.DespertoTemporizador == true || (informe?.Despertador == null && dormido.TotalSeconds <= Segundos + 20);
            lineas.Add((Veredicto.Ok, solo
                ? Tx("Se ha despertado solo con el temporizador.")
                : Tx("No se despertó solo: hubo que despertarlo a mano (puede que los temporizadores de reactivación estén desactivados en las opciones de energía).")));
        }
        if (informe?.Despertador is string quien && informe.DespertoTemporizador != true)
            lineas.Add((Veredicto.Ok, Tf("Según Windows, lo despertó: {0}.", quien)));
        foreach (var f in informe?.Frenos ?? new List<string>())
            lineas.Add((Veredicto.Ok, Tf("El controlador {0} retrasó la entrada en suspensión (suele ser de una máquina virtual, de la red o de un disco externo).", f)));
        if (redAntes)
            lineas.Add(red is double s
                ? (Veredicto.Ok, Tf("La red se ha recuperado en {0} s.", Formato.N(s, "0")))
                : (Veredicto.Aviso, Tx("La red no ha vuelto tras la suspensión: es un fallo típico del controlador del Wi-Fi o de la tarjeta de red.")));
        if (fallosDespues > fallosAntes)
            lineas.Add((Veredicto.Aviso, Tf("Tras volver hay {0} dispositivo(s) con fallos que antes funcionaban (míralo en la prueba de Placa base).", fallosDespues - fallosAntes)));
        lineas.Add((Veredicto.Pendiente, Tx("Comprueba tú que la pantalla, el sonido, el teclado y el touchpad funcionan como antes.")));

        panelResultado.Children.Clear();
        foreach (var (v, texto) in lineas)
            panelResultado.Children.Add(v == Veredicto.Aviso ? Ui.Aviso(texto) : Ui.Texto("• " + texto, v == Veredicto.Pendiente ? "etiqueta" : "valor"));
        textoEstado.Text = Tx("Prueba terminada.");
        Sesion.Anotar("suspension", Tx("Resultado"), lineas.Where(l => l.v != Veredicto.Pendiente).Select(l => l.texto).ToArray());
        var peor = lineas.Any(l => l.v == Veredicto.Aviso) ? Veredicto.Aviso : Veredicto.Ok;
        if (Sesion.Obtener("suspension") == Veredicto.Pendiente) Sesion.Fijar("suspension", peor);

        Suspension.Desmarcar();
        if (esperando) PruebasEnCurso.Salir();
        esperando = false;
        comprobando = false;
        ActualizarBoton();
        Terminada.TrySetResult(true);
    }
}
