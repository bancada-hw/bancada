using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Procesador: temperaturas en directo y prueba de estrés que detecta calentamiento, pérdida de rendimiento y errores de cálculo.</summary>
internal sealed class ProcesadorVista : UserControl, IAutomatica
{
    TaskCompletionSource fin = new();
    bool hecha;

    /// <summary>Revisión completa: prueba de estrés de los minutos indicados (o espera a la que ya esté en marcha).</summary>
    public Task EjecutarAsync(int minutos)
    {
        if (hecha) return Task.CompletedTask;
        if (!ocupado) Empezar(minutos);
        return fin.Task;
    }

    readonly Sensores sensores = new();
    readonly Estres estres = new();
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock textoTemp, textoFrec, textoUso;
    readonly TextBlock textoFuente = Ui.Texto("", "etiqueta");
    readonly StackPanel panelOtros = new() { Spacing = 6 };
    readonly Grafica graficaTemp = new() { Unidad = " °C", Capacidad = 600, Height = 160, Minimo = 30, Maximo = 105 };
    readonly Grafica graficaRend = new() { Unidad = " %", Capacidad = 600, Height = 120, Minimo = 0, Maximo = 110 };
    readonly TextBlock textoEstres = Ui.Texto("", "valor");
    readonly StackPanel panelResultado = new() { Spacing = 8 };
    readonly WrapPanel botonesDuracion;
    readonly Button botonParar;
    readonly Stopwatch cronometro = new();
    TimeSpan duracion;
    bool ocupado, lecturaEnCurso;
    readonly Ritmo ritmo = new();
    double? tempInicial, tempMax;

    public ProcesadorVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Procesador y temperaturas"),
            Tx("Temperatura, frecuencia y uso en directo. La prueba de estrés pone el procesador al 100 % para ver si se calienta demasiado, si pierde velocidad o si se equivoca al calcular.")));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Ahora mismo"),
            Ui.Columnas(
                Ui.Cifra(Tx("Temperatura"), "—", "°C", out textoTemp),
                Ui.Cifra(Tx("Frecuencia"), "—", "GHz", out textoFrec),
                Ui.Cifra(Tx("Uso"), "—", "%", out textoUso)),
            textoFuente));

        botonesDuracion = Ui.Botones(
            Ui.Boton(Tx("1 minuto"), "secundario", (_, _) => Empezar(1)),
            Ui.Boton(Tx("5 minutos"), "primario", (_, _) => Empezar(5)),
            Ui.Boton(Tx("15 minutos"), "secundario", (_, _) => Empezar(15)),
            Ui.Boton(Tx("30 minutos"), "secundario", (_, _) => Empezar(30)));
        botonParar = Ui.Boton(Tx("Parar"), "secundario", (_, _) => Terminar(cancelada: true));
        botonParar.IsVisible = false;
        graficaTemp.Series.Add(new Grafica.Serie(Tx("Temperatura"), Color.Parse("#D2453B")));
        graficaRend.Series.Add(new Grafica.Serie(Tx("Rendimiento"), Color.Parse("#2E9D57")));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba de estrés"),
            Ui.Texto(Tf("Usa los {0} hilos del procesador. Con 5 minutos basta para ver si la refrigeración aguanta. Enchufa el cargador si es un portátil.", Environment.ProcessorCount), "subtitulo"),
            Ui.Fila(botonesDuracion, botonParar),
            textoEstres,
            Ui.Texto(Tx("Temperatura").ToUpperInvariant(), "seccion"),
            graficaTemp,
            Ui.Texto(Tx("Rendimiento (100 % = su mejor momento en la prueba)").ToUpperInvariant(), "seccion"),
            graficaRend,
            panelResultado));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Todos los sensores"), panelOtros));
        cuerpo.Children.Add(new BarraVeredicto("procesador", Tx("¿El procesador y la refrigeración funcionan bien?")));
        Content = pagina;

        reloj.Tick += async (_, _) => await Tic();
        AttachedToVisualTree += (_, _) => reloj.Start();
        // Si hay una prueba en marcha, se sigue midiendo aunque se cambie de pantalla.
        DetachedFromVisualTree += (_, _) => { if (!estres.Activo) reloj.Stop(); };
    }

    async Task Tic()
    {
        if (estres.Activo) AvisarEstres();
        if (lecturaEnCurso) return;
        lecturaEnCurso = true;
        var l = await Task.Run(sensores.Leer);
        lecturaEnCurso = false;

        textoTemp.Text = l.TempCpu is double t ? Formato.N(t, "0") : "—";
        textoFrec.Text = l.Mhz is double m ? Formato.N(m / 1000, "0.00") : "—";
        textoUso.Text = l.UsoPct is double u ? Formato.N(u, "0") : "—";
        textoFuente.Text = l.TempCpu != null
            ? Tf("Temperatura de: {0}.", l.FuenteTemp) + (OperatingSystem.IsWindows() ? Tx(" Windows solo deja leer las zonas térmicas del equipo; en algunos modelos no es exactamente la del procesador.") : "")
            : OperatingSystem.IsWindows()
                ? Tx("Este equipo no informa de su temperatura a Windows sin programas con controlador propio. La prueba de estrés sigue detectando pérdidas de rendimiento y errores.")
                : Tx("No se ha encontrado ningún sensor de temperatura (en máquinas virtuales es lo normal).");

        PintarOtros(l.Otros);
        if (estres.Activo) AvanzarEstres(l);
    }

    void PintarOtros(List<Sensor> otros)
    {
        panelOtros.Children.Clear();
        if (otros.Count == 0)
        {
            panelOtros.Children.Add(Ui.Texto(Tx("No hay más sensores disponibles."), "suave"));
            return;
        }
        foreach (var s in otros.Take(40))
            panelOtros.Children.Add(Ui.Fila(Ui.Texto(s.Nombre, "valor"), Ui.Texto($"{Formato.N(s.Valor, s.Unidad == "rpm" ? "0" : "0.#")} {s.Unidad}", "valor mono")));
    }

    void Empezar(int minutos)
    {
        if (ocupado) return;
        ocupado = true;
        fin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PruebasEnCurso.Entrar();
        duracion = TimeSpan.FromMinutes(minutos);
        graficaTemp.Vaciar();
        graficaRend.Vaciar();
        panelResultado.Children.Clear();
        ritmo.Reiniciar();
        tempInicial = tempMax = null;
        botonesDuracion.IsEnabled = false;
        botonParar.IsVisible = true;
        estres.Iniciar();
        cronometro.Restart();
        reloj.Start();
        textoEstres.Text = Tx("Arrancando…");
    }

    void AvanzarEstres(LecturaSensores l)
    {
        if (l.TempCpu is double t)
        {
            tempInicial ??= t;
            tempMax = Math.Max(tempMax ?? t, t);
            graficaTemp.Anadir(0, t);
        }
        // Referencia: el primer medio minuto (ver Ritmo).
        if (ritmo.Anotar(cronometro.Elapsed.TotalSeconds, estres.Iteraciones) is double pct) graficaRend.Anadir(0, pct);

        var queda = AvisarEstres();
        if (queda <= TimeSpan.Zero) Terminar(cancelada: false);
    }

    /// <summary>Texto y porcentaje de la prueba de estrés (también mientras se espera a los sensores, que van lentos bajo carga).</summary>
    TimeSpan AvisarEstres()
    {
        var queda = duracion - cronometro.Elapsed;
        textoEstres.Text = Tf("En marcha · quedan {0} · errores de cálculo: {1}", Ui.Duracion(queda < TimeSpan.Zero ? TimeSpan.Zero : queda), estres.Errores) +
                           (tempMax is double tm ? Tf(" · máxima {0} °C", Formato.N(tm, "0")) : "");
        Revision.Informar("procesador", textoEstres.Text, 100 * cronometro.Elapsed.TotalSeconds / Math.Max(1, duracion.TotalSeconds));
        return queda;
    }

    void Terminar(bool cancelada)
    {
        if (!ocupado) return;
        estres.Detener();
        ocupado = false;
        PruebasEnCurso.Salir();
        botonesDuracion.IsEnabled = true;
        botonParar.IsVisible = false;
        var transcurrido = cronometro.Elapsed;
        cronometro.Stop();
        textoEstres.Text = cancelada ? Tf("Parada a los {0}.", Ui.Duracion(transcurrido)) : Tf("Terminada ({0}).", Ui.Duracion(transcurrido));
        hecha |= !cancelada;
        try { Concluir(transcurrido); }
        finally { fin.TrySetResult(); }
    }

    void Concluir(TimeSpan transcurrido)
    {
        if (transcurrido.TotalSeconds < 45)
        {
            panelResultado.Children.Add(Ui.Texto(Tx("La prueba ha durado muy poco para sacar conclusiones."), "etiqueta"));
            return;
        }

        var rendFinalPct = ritmo.Final;
        var errores = estres.Errores;
        var problemas = new List<(Veredicto v, string texto)>();
        if (errores > 0) problemas.Add((Veredicto.Fallo, Tf("El procesador ha dado {0} resultados erróneos: es inestable (avería, voltaje o temperatura).", errores)));
        if (tempMax >= 98) problemas.Add((Veredicto.Aviso, Tf("Llega a {0} °C: está en el límite. Limpia el ventilador y las rejillas; si no mejora, cambia la pasta térmica.", Formato.N(tempMax.Value, "0"))));
        // Entre 92 y 97 °C es lo normal en muchos portátiles actuales a plena carga: se informa, pero no es un defecto.
        else if (tempMax >= 92) problemas.Add((Veredicto.Ok, Tf("Llega a {0} °C: caliente, pero dentro de lo que aguantan muchos portátiles.", Formato.N(tempMax.Value, "0"))));
        if (rendFinalPct < 65) problemas.Add((Veredicto.Aviso, Tf("Al final rinde al {0} % de su mejor momento: se frena para no quemarse (estrangulamiento térmico).", Formato.N(rendFinalPct, "0"))));

        var resumen = new List<string>
        {
            Tf("{0} al 100 %", Ui.Duracion(transcurrido)),
            Tf("rendimiento final {0} %", Formato.N(rendFinalPct, "0")),
            Tf("errores de cálculo {0}", errores)
        };
        if (tempInicial is double ti && tempMax is double tmx) resumen.Insert(1, Tf("temperatura {0} → {1} °C", Formato.N(ti, "0"), Formato.N(tmx, "0")));

        panelResultado.Children.Add(Ui.Columnas(
            Ui.Cifra(Tx("Máxima"), tempMax is double mx ? Formato.N(mx, "0") : "—", "°C"),
            Ui.Cifra(Tx("Rendimiento final"), Formato.N(rendFinalPct, "0"), "%"),
            Ui.Cifra(Tx("Errores"), errores.ToString(), "")));
        var graves = problemas.Where(p => p.v != Veredicto.Ok).ToList();
        var notas = problemas.Where(p => p.v == Veredicto.Ok).Select(p => p.texto).ToArray();
        if (graves.Count == 0)
            panelResultado.Children.Add(Ui.Chip(Tx("Aguanta la carga sin calentarse en exceso ni perder rendimiento"), Veredicto.Ok));
        foreach (var (_, texto) in graves) panelResultado.Children.Add(Ui.Aviso(texto));
        foreach (var texto in notas) panelResultado.Children.Add(Ui.Texto(texto, "etiqueta"));
        Sesion.Anotar("procesador", Tx("Prueba de estrés"), string.Join(", ", resumen));
        Sesion.Anotar("procesador", Tx("Aviso"), graves.Select(p => p.texto).ToArray());
        Sesion.Anotar("procesador", Tx("Nota"), notas);
        propuesta.Parte("estres", graves.Count == 0 ? Veredicto.Ok : graves.Max(p => p.v));
    }

    readonly Propuesta propuesta = new("procesador");
}
