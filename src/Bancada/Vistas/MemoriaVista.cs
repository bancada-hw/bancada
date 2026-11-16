using System.Diagnostics;
using Avalonia.Controls;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Memoria RAM: módulos instalados y prueba de la memoria libre con varios patrones.</summary>
internal sealed class MemoriaVista : UserControl, IAutomatica
{
    Task? enCurso;
    bool hecha;

    /// <summary>Revisión completa: prueba la memoria libre (o espera a la prueba que ya esté en marcha).</summary>
    public async Task EjecutarAsync(int minutos)
    {
        if (enCurso is { IsCompleted: false }) await enCurso;
        else if (!hecha) await Empezar();
    }

    readonly StackPanel panelModulos = new() { Spacing = 10 };
    readonly StackPanel panelPrueba = new() { Spacing = 12 };
    readonly TextBlock textoTamano = Ui.Texto("", "valor");
    readonly ProgressBar barra = new() { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6, IsVisible = false };
    readonly TextBlock textoProgreso = Ui.Texto("", "etiqueta");
    readonly StackPanel panelResultado = new() { Spacing = 10 };
    readonly Button botonEmpezar;
    readonly Button botonCancelar;
    CancellationTokenSource? cancelar;

    public MemoriaVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Memoria RAM"),
            Tx("Llena la memoria libre con distintos patrones y comprueba que se lee exactamente lo que se escribió. Un solo error significa que un módulo está mal.")));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Módulos instalados"), panelModulos));

        botonEmpezar = Ui.Boton(Tx("Empezar la prueba"), "primario", async (_, _) => await Empezar());
        botonCancelar = Ui.Boton(Tx("Cancelar"), "secundario", (_, _) => cancelar?.Cancel());
        botonCancelar.IsVisible = false;
        panelPrueba.Children.Add(textoTamano);
        panelPrueba.Children.Add(Ui.Texto(Tx("Dura entre uno y varios minutos según la cantidad de memoria. Mientras tanto, el equipo puede ir más lento: no abras otros programas."), "etiqueta"));
        panelPrueba.Children.Add(Ui.Botones(botonEmpezar, botonCancelar));
        panelPrueba.Children.Add(barra);
        panelPrueba.Children.Add(textoProgreso);
        panelPrueba.Children.Add(panelResultado);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba de memoria"), panelPrueba));

        var completa = new List<Control>
        {
            Ui.Texto(Tx("Con el sistema en marcha solo se puede probar la memoria que está libre. Para probarla entera hay que hacerlo al arrancar el equipo, antes de que cargue el sistema."), "subtitulo")
        };
        if (OperatingSystem.IsWindows())
        {
            completa.Add(Ui.Boton(Tx("Abrir el Diagnóstico de memoria de Windows"), "secundario", (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("mdsched.exe") { UseShellExecute = true }); } catch { }
            }));
            completa.Add(Ui.Texto(Tx("Windows reinicia el equipo, prueba toda la memoria y muestra el resultado al volver a entrar."), "etiqueta"));
        }
        else
        {
            completa.Add(Ui.Texto(Tx("Muchas distribuciones incluyen MemTest86+ en el menú de arranque (GRUB). Si no aparece, instálalo (paquete memtest86+) y elige esa opción al encender."), "etiqueta"));
        }
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba completa al arrancar"), completa.ToArray()));
        cuerpo.Children.Add(new BarraVeredicto("memoria", Tx("¿La memoria funciona bien?")));
        Content = pagina;

        AttachedToVisualTree += (_, _) => Refrescar();
        Sesion.EquipoDetectado += Refrescar;
    }

    void Refrescar()
    {
        panelModulos.Children.Clear();
        var e = Sesion.Equipo;
        var (total, libre) = PruebaMemoria.Estado();
        panelModulos.Children.Add(Ui.Columnas(
            Ui.Cifra(Tx("Instalada"), e != null ? Formato.N(Math.Round(e.RamTotalBytes / 1073741824.0), "0") : "—", Tx("GB")),
            Ui.Cifra(Tx("Libre ahora"), Formato.N(libre / 1073741824.0, "0.0"), Tx("GB")),
            Ui.Cifra(Tx("Ranuras"), e?.RanurasRam is int r ? $"{e.ModulosRam.Count}/{r}" : e?.ModulosRam.Count > 0 ? e.ModulosRam.Count.ToString() : "—", "")));
        if (e != null)
            foreach (var m in e.ModulosRam)
            {
                var detalle = string.Join(" · ", new[] { Formato.Ram(m.Bytes), m.MHz > 0 ? $"{m.MHz} MHz" : "", m.Fabricante, m.Referencia }.Where(x => x.Length > 0));
                panelModulos.Children.Add(Ui.Hundido(Ui.Dato(m.Ranura.Length > 0 ? m.Ranura : Tx("Módulo"), detalle, mono: true)));
            }
        if (e is { ModulosRam.Count: 0 })
            panelModulos.Children.Add(Ui.Texto(OperatingSystem.IsWindows()
                ? Tx("El detalle de cada módulo solo se ve abriendo Bancada como administrador.")
                : Tx("El detalle de cada módulo solo se ve abriendo Bancada con sudo."), "etiqueta"));

        if (cancelar == null)
        {
            var t = PruebaMemoria.TamanoRecomendado();
            textoTamano.Text = t > 0
                ? Tf("Se probarán {0} GB de memoria libre, de {1} GB que ve el sistema.", Formato.N(t / 1073741824.0, "0.#"), Formato.N(total / 1073741824.0, "0"))
                : Tx("Queda muy poca memoria libre para hacer la prueba. Cierra programas y vuelve a entrar.");
            botonEmpezar.IsEnabled = t > 0;
        }
    }

    Task Empezar() => enCurso is { IsCompleted: false } ? enCurso : (enCurso = Probar());

    async Task Probar()
    {
        var bytes = PruebaMemoria.TamanoRecomendado();
        if (bytes <= 0) return;
        cancelar = new CancellationTokenSource();
        PruebasEnCurso.Entrar();
        botonEmpezar.IsVisible = false;
        botonCancelar.IsVisible = true;
        barra.IsVisible = true;
        barra.Value = 0;
        panelResultado.Children.Clear();
        var progreso = new Progress<(double pct, string texto)>(p =>
        {
            barra.Value = p.pct;
            textoProgreso.Text = $"{p.texto} · {Formato.N(p.pct, "0")} %";
            Revision.Informar("memoria", p.texto, p.pct);
        });
        var token = cancelar.Token;
        var r = await Task.Run(() => PruebaMemoria.Ejecutar(bytes, progreso, token));
        cancelar = null;
        PruebasEnCurso.Salir();
        botonEmpezar.IsVisible = true;
        botonCancelar.IsVisible = false;
        barra.IsVisible = false;
        textoProgreso.Text = "";

        var gb = Formato.N(r.BytesProbados / 1073741824.0, "0.#");
        hecha |= !r.Cancelada;
        if (r.Cancelada)
        {
            panelResultado.Children.Add(Ui.Chip(Tx("Prueba cancelada"), Veredicto.Pendiente));
        }
        else if (r.Errores == 0)
        {
            panelResultado.Children.Add(Ui.Chip(Tf("Sin errores en {0} GB", gb), Veredicto.Ok));
            panelResultado.Children.Add(Ui.Texto(Tf("8 patrones escritos y comprobados en {0}.", Ui.Duracion(r.Duracion)), "etiqueta"));
            Sesion.Anotar("memoria", Tx("Prueba"), Tf("sin errores en {0} GB (8 patrones, {1})", gb, Ui.Duracion(r.Duracion)));
            if (Sesion.Obtener("memoria") == Veredicto.Pendiente) Sesion.Fijar("memoria", Veredicto.Ok);
        }
        else
        {
            panelResultado.Children.Add(Ui.Chip(Tf("{0} errores en {1} GB", Formato.N(r.Errores, "#,0"), gb), Veredicto.Fallo));
            panelResultado.Children.Add(Ui.Aviso(Tx("La memoria devuelve datos distintos de los que se escribieron: hay un módulo defectuoso o mal asentado. Prueba los módulos de uno en uno para saber cuál es.")));
            foreach (var d in r.Detalles) panelResultado.Children.Add(Ui.Texto(d, "etiqueta mono"));
            Sesion.Anotar("memoria", Tx("Prueba"), Tf("{0} errores en {1} GB", r.Errores, gb));
            Sesion.Fijar("memoria", Veredicto.Fallo);
        }
        Refrescar();
    }
}
