using Avalonia.Controls;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Placa base: comprobaciones automáticas al estilo de las herramientas de diagnóstico de los fabricantes.</summary>
internal sealed class PlacaVista : UserControl, IAutomatica
{
    readonly StackPanel panel = new() { Spacing = 10 };
    readonly Button botonRepetir;
    bool ocupado;
    Task? primera;

    /// <summary>Revisión completa: hace las comprobaciones (una sola vez, aunque también se abra la pantalla).</summary>
    public Task EjecutarAsync(int minutos) => primera ??= Comprobar();

    public PlacaVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Placa base"),
            Tx("Comprobaciones automáticas de la placa y sus componentes: firmware, chip de seguridad, dispositivos con fallos, enlaces PCI Express, errores de hardware que ha registrado el sistema, el reloj de la placa y el cálculo en cada núcleo.")));
        botonRepetir = Ui.Boton(Tx("Repetir las comprobaciones"), "secundario", async (_, _) => await Comprobar());
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Comprobaciones"), panel, botonRepetir));
        cuerpo.Children.Add(new BarraVeredicto("placa", Tx("¿La placa base funciona bien?")));
        Content = pagina;

        AttachedToVisualTree += async (_, _) => await EjecutarAsync(0);
    }

    async Task Comprobar()
    {
        if (ocupado) return;
        ocupado = true;
        PruebasEnCurso.Entrar();
        botonRepetir.IsEnabled = false;
        panel.Children.Clear();
        var estado = Ui.Texto(Tx("Empezando…"), "suave");
        panel.Children.Add(estado);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 });
        await Sesion.EsperarEquipoAsync();

        var resultados = await Placa.ComprobarAsync(new Progress<string>(t =>
        {
            estado.Text = t;
            Revision.Informar("placa", t);
        }));
        panel.Children.Clear();
        foreach (var c in resultados)
        {
            var p = new StackPanel { Spacing = 6 };
            p.Children.Add(Ui.Fila(Ui.Texto(c.Nombre, "valor negrita"), Ui.Chip(c.Resumen, c.Resultado)));
            foreach (var l in c.Lineas) p.Children.Add(Ui.Texto("• " + l, "etiqueta"));
            panel.Children.Add(Ui.Hundido(p));
            Sesion.Anotar("placa", c.Nombre, c.Lineas.Count == 0 ? c.Resumen : $"{c.Resumen}. {string.Join(" ", c.Lineas)}");
        }

        var peor = resultados.Where(r => r.Resultado is Veredicto.Ok or Veredicto.Aviso or Veredicto.Fallo)
            .Select(r => r.Resultado).DefaultIfEmpty(Veredicto.Pendiente).Max();
        if (peor != Veredicto.Pendiente && Sesion.Obtener("placa") == Veredicto.Pendiente) Sesion.Fijar("placa", peor);
        botonRepetir.IsEnabled = true;
        ocupado = false;
        PruebasEnCurso.Salir();
    }
}
