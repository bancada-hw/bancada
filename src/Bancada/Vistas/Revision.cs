using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Bancada.Modelo;

namespace Bancada.Vistas;

/// <summary>Prueba que sabe hacerse sola, sin que su pantalla esté abierta (revisión completa).</summary>
internal interface IAutomatica
{
    /// <summary>Hace la prueba entera. <paramref name="minutos"/> es la duración de las pruebas de estrés.</summary>
    Task EjecutarAsync(int minutos);
}

/// <summary>
/// Revisión completa guiada: lleva al técnico paso a paso por las pruebas que necesitan a una persona
/// mientras Bancada hace las automáticas en segundo plano, una detrás de otra.
/// </summary>
internal static class Revision
{
    /// <summary>Pasos en los que hay que hacer algo. El sonido va pronto: las pruebas de estrés esperan a que termine (el ventilador haría ruido).</summary>
    public static readonly string[] Manuales = { "teclado", "audio", "puntero", "pantalla", "camara", "usb", "bateria", "suspension" };

    /// <summary>Pruebas que se hacen solas, en este orden: primero las silenciosas.</summary>
    public static readonly string[] Automaticas = { "placa", "discos", "red", "memoria", "procesador", "grafica" };

    static readonly HashSet<string> Ruidosas = new() { "procesador", "grafica" };

    public static int MinutosEstres => Opciones.MinutosEstres ?? 3;

    static Func<string, Control>? vistaDe;
    static Action<string>? ir;
    static Task? fondo;
    static readonly HashSet<string> hechas = new();

    /// <summary>La guía está a la vista (barra de pasos arriba).</summary>
    public static bool Activa { get; private set; }
    public static int Paso { get; private set; }
    /// <summary>Pantalla que está abierta ahora mismo.</summary>
    public static string Pantalla { get; private set; } = "inicio";
    /// <summary>Prueba automática en marcha (null si ninguna).</summary>
    public static string? EnMarcha { get; private set; }
    /// <summary>Las pruebas de estrés esperan a que se termine la de sonido.</summary>
    public static bool EsperandoSonido { get; private set; }
    public static bool Empezada => fondo != null;
    public static bool FondoEnMarcha => fondo is { IsCompleted: false };
    public static bool Hecha(string id) => hechas.Contains(id);
    public static int AutomaticasHechas => hechas.Count;
    public static string PasoActual => Manuales[Paso];

    public static event Action? Cambio;

    /// <summary>Lo que está haciendo ahora la automática en marcha ("Patrón 3 de 8", "quedan 2 min"…).</summary>
    public static string Detalle { get; private set; } = "";
    /// <summary>Cuánto lleva la automática en marcha, de 0 a 100 (null si no se sabe).</summary>
    public static double? Porcentaje { get; private set; }
    static readonly Stopwatch cronometro = new();
    /// <summary>Tiempo que lleva la automática en marcha.</summary>
    public static TimeSpan Transcurrido => cronometro.Elapsed;
    /// <summary>Cambia el detalle o el porcentaje (muchas veces por segundo: solo lo escuchan los paneles de progreso).</summary>
    public static event Action? Progreso;

    /// <summary>
    /// Las pantallas cuentan aquí cómo va su prueba. Solo cuenta si es la que la revisión tiene en marcha,
    /// así que pueden llamarlo siempre, también cuando el técnico la lanza a mano. Desde el hilo de la interfaz.
    /// </summary>
    public static void Informar(string id, string detalle, double? porcentaje = null)
    {
        if (EnMarcha != id) return;
        Detalle = detalle;
        Porcentaje = porcentaje is double p ? Math.Clamp(p, 0, 100) : null;
        Progreso?.Invoke();
    }

    /// <summary>La ventana le da cómo crear una pantalla sin mostrarla y cómo ir a una.</summary>
    public static void Conectar(Func<string, Control> vista, Action<string> navegar)
    {
        vistaDe = vista;
        ir = navegar;
    }

    public static void Empezar()
    {
        Paso = 0;
        Activa = true;
        fondo ??= Fondo();
        Avisar();
        ir?.Invoke(PasoActual);
    }

    /// <summary>Vuelve al paso en el que se quedó (o a uno concreto).</summary>
    public static void Continuar(int? paso = null)
    {
        if (paso is int p) Paso = Math.Clamp(p, 0, Manuales.Length - 1);
        Activa = true;
        fondo ??= Fondo();
        Avisar();
        ir?.Invoke(PasoActual);
    }

    public static void Siguiente()
    {
        if (Paso >= Manuales.Length - 1)
        {
            Terminar();
            return;
        }
        Paso++;
        Avisar();
        ir?.Invoke(PasoActual);
    }

    public static void Anterior()
    {
        if (Paso == 0) return;
        Paso--;
        Avisar();
        ir?.Invoke(PasoActual);
    }

    /// <summary>Oculta la guía; lo que esté en segundo plano sigue.</summary>
    public static void Salir()
    {
        Activa = false;
        Avisar();
        ir?.Invoke("revision");
    }

    public static void Terminar()
    {
        Activa = false;
        Avisar();
        ir?.Invoke("informe");
    }

    /// <summary>La ventana avisa de cada cambio de pantalla: si desde el menú se va a otro paso, la guía le sigue.</summary>
    public static void AlNavegar(string id)
    {
        Pantalla = id;
        if (Activa)
        {
            var i = Array.IndexOf(Manuales, id);
            if (i >= 0) Paso = i;
        }
        Avisar(); // también fuera de la guía: la barra de progreso se oculta o aparece según la pantalla
    }

    static async Task Fondo()
    {
        foreach (var id in Automaticas)
        {
            if (Ruidosas.Contains(id)) await EsperarSonido();
            EnMarcha = id;
            Detalle = "";
            Porcentaje = null;
            cronometro.Restart();
            Avisar();
            try
            {
                if (vistaDe?.Invoke(id) is IAutomatica a) await a.EjecutarAsync(MinutosEstres);
            }
            catch
            {
                // Una prueba que falla no para las demás: su pantalla ya enseña el error.
            }
            hechas.Add(id);
        }
        EnMarcha = null;
        Detalle = "";
        Porcentaje = null;
        cronometro.Reset();
        Avisar();
    }

    /// <summary>Hasta que se haya hecho la prueba de sonido (o se deje la guía), no se ponen los ventiladores a tope.</summary>
    static async Task EsperarSonido()
    {
        var pasoSonido = Array.IndexOf(Manuales, "audio");
        bool Listo() => !Activa || Paso > pasoSonido || Sesion.Obtener("audio") != Veredicto.Pendiente;
        if (Listo()) return;
        EnMarcha = null;
        EsperandoSonido = true;
        Avisar();
        while (!Listo()) await Task.Delay(1000);
        EsperandoSonido = false;
    }

    static void Avisar() => Cambio?.Invoke();

    /// <summary>Qué está haciendo Bancada por su cuenta, en una línea.</summary>
    public static string TextoFondo()
    {
        if (!Empezada) return "";
        if (EsperandoSonido) return Tx("Las pruebas de estrés empezarán cuando termines la de sonido, para que el ventilador no moleste.");
        if (EnMarcha is string id)
            return Tf("En segundo plano: {0} ({1} de {2})", Pruebas.Nombre(id), AutomaticasHechas + 1, Automaticas.Length);
        return FondoEnMarcha ? Tx("En segundo plano: preparando…") : Tx("Las pruebas automáticas han terminado.");
    }
}

/// <summary>Página de la revisión completa: qué se va a hacer, qué tener a mano y cómo va.</summary>
internal sealed class RevisionVista : UserControl
{
    readonly StackPanel cuerpo;
    readonly Action<string> ir;

    public RevisionVista(Action<string> ir)
    {
        this.ir = ir;
        Content = Ui.Pagina(out cuerpo);
        Construir();
        AttachedToVisualTree += (_, _) =>
        {
            Revision.Cambio += Construir;
            Sesion.Cambio += AlCambiarVeredicto;
            Construir();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Revision.Cambio -= Construir;
            Sesion.Cambio -= AlCambiarVeredicto;
        };
    }

    void AlCambiarVeredicto(string id, Veredicto v) => Construir();

    void Construir()
    {
        cuerpo.Children.Clear();
        cuerpo.Children.Add(Ui.Cabecera(Tx("Revisión completa"),
            Tx("Bancada te lleva paso a paso por las pruebas en las que tienes que hacer algo (pulsar teclas, mirar, escuchar…) mientras hace sola las demás en segundo plano. Al terminar tienes el informe completo. Calcula unos 20 minutos.")));

        var manualesHechas = Revision.Manuales.Count(id => Sesion.Obtener(id) != Veredicto.Pendiente);
        Control acciones;
        if (!Revision.Empezada)
            acciones = Ui.Botones(Ui.Boton(Tx("Empezar la revisión"), "primario", (_, _) => Revision.Empezar()));
        else
        {
            var botones = new List<Control>();
            if (manualesHechas < Revision.Manuales.Length || Revision.FondoEnMarcha)
                botones.Add(Ui.Boton(Tf("Continuar en el paso {0}: {1}", Revision.Paso + 1, Pruebas.Nombre(Revision.PasoActual)), "primario",
                    (_, _) => Revision.Continuar()));
            botones.Add(Ui.Boton(Tx("Ver el informe"), botones.Count == 0 ? "primario" : "secundario", (_, _) => ir("informe")));
            acciones = Ui.Botones(botones.ToArray());
        }
        var estado = new StackPanel { Spacing = 10 };
        estado.Children.Add(acciones);
        cuerpo.Children.Add(estado);
        if (Revision.Empezada) cuerpo.Children.Add(new PanelFondo(conLista: false));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Ten a mano"),
            Ui.Texto("• " + Tx("El cargador: la prueba de batería lo necesita y, enchufado, las pruebas de estrés no se frenan."), "valor"),
            Ui.Texto("• " + Tx("Un pendrive, un ratón o cualquier aparato USB para probar los puertos."), "valor"),
            Ui.Texto("• " + Tx("Unos auriculares, si quieres probar la toma de auriculares."), "valor"),
            Ui.Texto("• " + Tx("Guarda lo que tengas abierto: el último paso suspende el equipo unos segundos."), "valor")));

        // Lo que hace el técnico (por orden) y lo que hace Bancada sola.
        var manuales = new List<Control>
        {
            Ui.Texto(Tf("{0} de {1} hechas", manualesHechas, Revision.Manuales.Length), "etiqueta")
        };
        for (int i = 0; i < Revision.Manuales.Length; i++)
        {
            var id = Revision.Manuales[i];
            var paso = i;
            var v = Sesion.Obtener(id);
            var boton = Ui.Boton($"{i + 1}. {Pruebas.Nombre(id)}", "secundario", (_, _) =>
            {
                if (Revision.Empezada) Revision.Continuar(paso);
                else ir(id);
            });
            boton.Width = 230;
            boton.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (Revision.Empezada && Revision.Paso == i) boton.Classes.Add("actual");
            manuales.Add(Ui.Fila(boton, Ui.Chip(Ui.TextoVeredicto(v), v)));
        }

        var automaticas = new List<Control>
        {
            Ui.Texto(Tf("Se prueba toda la memoria libre; el procesador y cada gráfica pasan {0} min al máximo.", Revision.MinutosEstres), "etiqueta")
        };
        foreach (var id in Revision.Automaticas)
        {
            var v = Sesion.Obtener(id);
            Border chip =
                Revision.EnMarcha == id ? Ui.Chip(Tx("En marcha…"), Veredicto.Pendiente) :
                v != Veredicto.Pendiente ? Ui.Chip(Ui.TextoVeredicto(v), v) :
                Revision.Hecha(id) ? Ui.Chip(Tx("Hecha: falta tu veredicto"), v) :
                Ui.Chip(Revision.Empezada ? Tx("En espera") : Tx("Sin probar"), Veredicto.Pendiente);
            var prueba = id;
            var boton = Ui.Boton(Pruebas.Nombre(id), "secundario", (_, _) => ir(prueba));
            boton.Width = 230;
            boton.HorizontalContentAlignment = HorizontalAlignment.Left;
            automaticas.Add(Ui.Fila(boton, chip));
        }

        var columnas = Ui.DosColumnas(
            new Control[] { Ui.Tarjeta(Tx("Lo que haces tú"), manuales.ToArray()) },
            new Control[] { Ui.Tarjeta(Tx("Lo que hace Bancada sola"), automaticas.ToArray()) });
        cuerpo.Children.Add(columnas);
    }
}

/// <summary>Barra de la guía, encima de cada pantalla mientras dura la revisión completa.</summary>
internal sealed class BarraGuia : Border
{
    readonly TextBlock textoPaso = Ui.Texto("", "seccion");
    readonly TextBlock textoNombre = Ui.Texto("", "valor negrita");
    readonly ContentControl huecoChip = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly PanelFondo fondo = new(conLista: true);
    readonly StackPanel pasos = new() { Spacing = 0 };
    readonly StackPanel segmentos = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly Button botonAnterior, botonSiguiente, botonSalir, botonVolver;

    protected override Type StyleKeyOverride => typeof(Border);

    public BarraGuia()
    {
        Classes.Add("guia");
        botonAnterior = Ui.Boton(Tx("Anterior"), "secundario", (_, _) => Revision.Anterior());
        botonSiguiente = Ui.Boton("", "primario", (_, _) =>
        {
            // Fuera de los pasos (mirando otra pantalla), el botón principal devuelve al paso.
            if (Revision.Pantalla != Revision.PasoActual) Revision.Continuar();
            else Revision.Siguiente();
        });
        botonSalir = Ui.Boton(Tx("Salir"), "secundario", (_, _) => Revision.Salir());
        // Fuera de la guía, mientras sigan las automáticas, la barra queda con su progreso y un botón para volver.
        botonVolver = Ui.Boton(Tx("Volver a la revisión"), "secundario", (_, _) => Revision.Continuar());

        var nombre = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        nombre.Children.Add(textoNombre);
        nombre.Children.Add(huecoChip);
        textoNombre.VerticalAlignment = VerticalAlignment.Center;
        textoNombre.FontSize = 17;

        var izquierda = new StackPanel { Spacing = 4 };
        var cabeza = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        cabeza.Children.Add(textoPaso);
        segmentos.VerticalAlignment = VerticalAlignment.Center;
        cabeza.Children.Add(segmentos);
        izquierda.Children.Add(cabeza);
        izquierda.Children.Add(nombre);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        botones.Children.Add(botonAnterior);
        botones.Children.Add(botonSiguiente);
        botones.Children.Add(botonSalir);

        pasos.Children.Add(Ui.Fila(izquierda, botones));
        var todo = new StackPanel { Spacing = 10 };
        todo.Children.Add(pasos);
        todo.Children.Add(Ui.Fila(fondo, botonVolver));
        botonVolver.Margin = new Thickness(12, 0, 0, 0);
        Child = todo;
        AttachedToVisualTree += (_, _) =>
        {
            Revision.Cambio += Refrescar;
            Sesion.Cambio += AlCambiarVeredicto;
            Refrescar();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Revision.Cambio -= Refrescar;
            Sesion.Cambio -= AlCambiarVeredicto;
        };
        Refrescar();
    }

    void AlCambiarVeredicto(string id, Veredicto v) => Refrescar();

    void Refrescar()
    {
        // Fuera de la guía sigue a la vista mientras haya automáticas en marcha (salvo en la página de la revisión, que ya lo enseña).
        IsVisible = Revision.Activa || (Revision.FondoEnMarcha && Revision.Pantalla != "revision");
        pasos.IsVisible = Revision.Activa;
        botonVolver.IsVisible = !Revision.Activa;
        if (!Revision.Activa) return;
        var total = Revision.Manuales.Length;
        var id = Revision.PasoActual;
        var enElPaso = Revision.Pantalla == id;
        textoPaso.Text = Tf("REVISIÓN COMPLETA · PASO {0} DE {1}", Revision.Paso + 1, total);
        textoNombre.Text = Pruebas.Nombre(id);
        var v = Sesion.Obtener(id);
        huecoChip.Content = Ui.Chip(v == Veredicto.Pendiente ? Tx("Marca el resultado abajo") : Ui.TextoVeredicto(v), v);

        segmentos.Children.Clear();
        for (int i = 0; i < total; i++)
        {
            var s = new Border { Height = 5, CornerRadius = new CornerRadius(3) }.Clases("segmento");
            Ui.FijarEstado(s, Sesion.Obtener(Revision.Manuales[i]));
            s.Classes.Set("actual", i == Revision.Paso);
            segmentos.Children.Add(s);
        }

        botonAnterior.IsVisible = enElPaso && Revision.Paso > 0;
        botonSiguiente.Content = !enElPaso
            ? Tf("Volver al paso {0}", Revision.Paso + 1)
            : Revision.Paso < total - 1
                ? Tf("Siguiente: {0}", Pruebas.Nombre(Revision.Manuales[Revision.Paso + 1]))
                : Tx("Terminar y ver el informe");
    }
}

/// <summary>
/// Qué prueba está haciendo Bancada por su cuenta y cómo va: nombre, lo que hace ahora, barra de progreso,
/// tiempo y (con <c>conLista</c>) todas las automáticas con su estado. Va en la barra de la guía y en la página de la revisión.
/// </summary>
internal sealed class PanelFondo : Border
{
    readonly TextBlock textoTitulo = Ui.Texto("", "seccion");
    readonly TextBlock textoNombre = Ui.Texto("", "valor negrita");
    readonly TextBlock textoDetalle = Ui.Texto("", "etiqueta");
    readonly TextBlock textoTiempo = Ui.Texto("", "etiqueta mono");
    readonly ProgressBar barra = new() { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6 };
    readonly WrapPanel lista = new() { Orientation = Orientation.Horizontal };
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly bool conLista;

    protected override Type StyleKeyOverride => typeof(Border);

    public PanelFondo(bool conLista)
    {
        this.conLista = conLista;
        Classes.Add("fondo-revision");
        textoTiempo.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;

        var cabeza = new StackPanel { Spacing = 2 };
        cabeza.Children.Add(textoTitulo);
        cabeza.Children.Add(textoNombre);

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Ui.Fila(cabeza, textoTiempo));
        panel.Children.Add(barra);
        panel.Children.Add(textoDetalle);
        if (conLista) panel.Children.Add(lista);
        Child = panel;

        reloj.Tick += (_, _) => PintarProgreso();
        AttachedToVisualTree += (_, _) =>
        {
            Revision.Cambio += Refrescar;
            Revision.Progreso += PintarProgreso;
            Sesion.Cambio += AlCambiarVeredicto;
            reloj.Start();
            Refrescar();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Revision.Cambio -= Refrescar;
            Revision.Progreso -= PintarProgreso;
            Sesion.Cambio -= AlCambiarVeredicto;
            reloj.Stop();
        };
        Refrescar();
    }

    void AlCambiarVeredicto(string id, Veredicto v)
    {
        if (Array.IndexOf(Revision.Automaticas, id) >= 0) Refrescar();
    }

    void Refrescar()
    {
        IsVisible = Revision.Empezada;
        if (!Revision.Empezada) return;
        var total = Revision.Automaticas.Length;

        if (Revision.EnMarcha is string id)
        {
            textoTitulo.Text = Tf("BANCADA ESTÁ PROBANDO POR SU CUENTA · {0} DE {1}", Revision.AutomaticasHechas + 1, total);
            textoNombre.Text = Pruebas.Nombre(id);
        }
        else if (Revision.EsperandoSonido)
        {
            var siguiente = Revision.Automaticas.FirstOrDefault(a => !Revision.Hecha(a)) ?? "procesador";
            textoTitulo.Text = Tf("BANCADA ESTÁ PROBANDO POR SU CUENTA · {0} DE {1}", Revision.AutomaticasHechas + 1, total);
            textoNombre.Text = Tf("En espera: {0}", Pruebas.Nombre(siguiente));
        }
        else if (Revision.FondoEnMarcha)
        {
            textoTitulo.Text = Tx("BANCADA ESTÁ PROBANDO POR SU CUENTA");
            textoNombre.Text = Tx("Preparando…");
        }
        else
        {
            textoTitulo.Text = Tx("PRUEBAS AUTOMÁTICAS");
            textoNombre.Text = Tf("Terminadas las {0}", total);
        }

        if (conLista) PintarLista();
        PintarProgreso();
    }

    void PintarProgreso()
    {
        if (!Revision.Empezada) return;
        var enMarcha = Revision.EnMarcha != null;
        barra.IsVisible = enMarcha || Revision.EsperandoSonido || Revision.FondoEnMarcha;
        barra.IsIndeterminate = enMarcha && Revision.Porcentaje == null;
        barra.Value = enMarcha ? Revision.Porcentaje ?? 0 : Revision.FondoEnMarcha ? 0 : 100;

        if (Revision.EsperandoSonido)
            textoDetalle.Text = Tx("Las pruebas de estrés empezarán cuando termines la de sonido, para que el ventilador no moleste.");
        else if (enMarcha)
            textoDetalle.Text = Revision.Detalle.Length > 0 ? Revision.Detalle : Tx("Empezando…");
        else
            textoDetalle.Text = Revision.FondoEnMarcha ? "" : Tx("Revisa sus resultados y marca el veredicto de cada una.");
        textoDetalle.IsVisible = textoDetalle.Text.Length > 0;

        textoTiempo.Text = !enMarcha ? "" :
            Revision.Porcentaje is double p ? $"{Formato.N(p, "0")} % · {Ui.Duracion(Revision.Transcurrido)}" : Ui.Duracion(Revision.Transcurrido);
    }

    void PintarLista()
    {
        lista.Children.Clear();
        foreach (var id in Revision.Automaticas)
        {
            var v = Sesion.Obtener(id);
            Border chip;
            if (Revision.EnMarcha == id)
            {
                chip = Ui.Chip(Tf("{0} · en marcha", Pruebas.Nombre(id)), Veredicto.Pendiente);
                chip.Classes.Add("actual");
            }
            else if (Revision.Hecha(id) || v != Veredicto.Pendiente)
                chip = Ui.Chip(v == Veredicto.Pendiente ? Tf("{0} · hecha", Pruebas.Nombre(id)) : Pruebas.Nombre(id), v);
            else
            {
                chip = Ui.Chip(Pruebas.Nombre(id), Veredicto.Pendiente);
                chip.Opacity = 0.55;
            }
            chip.Margin = new Thickness(0, 0, 6, 6);
            lista.Children.Add(chip);
        }
    }
}
