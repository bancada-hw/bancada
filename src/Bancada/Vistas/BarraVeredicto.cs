using Avalonia.Controls;
using Avalonia.Layout;
using Bancada.Modelo;

namespace Bancada.Vistas;

/// <summary>Pie de cada prueba: el técnico decide si funciona, si tiene defectos o si no funciona.</summary>
internal sealed class BarraVeredicto : Border
{
    readonly string prueba;
    readonly List<(Button boton, Veredicto valor)> botones = new();

    // Que le apliquen los estilos de Border (tarjeta).
    protected override Type StyleKeyOverride => typeof(Border);

    public BarraVeredicto(string prueba, string pregunta)
    {
        this.prueba = prueba;
        Classes.Add("tarjeta");

        var opciones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (valor, texto) in new[]
                 {
                     (Veredicto.Ok, Tx("Funciona bien")),
                     (Veredicto.Aviso, Tx("Con defectos")),
                     (Veredicto.Fallo, Tx("No funciona"))
                 })
        {
            var b = new Button { Content = texto }.Clases("veredicto " + Ui.Clase(valor));
            // Pulsar otra vez el elegido lo deja sin decidir.
            b.Click += (_, _) => Sesion.Fijar(prueba, Sesion.Obtener(prueba) == valor ? Veredicto.Pendiente : valor);
            botones.Add((b, valor));
            opciones.Children.Add(b);
        }

        Child = Ui.Fila(Ui.Texto(pregunta, "valor negrita"), opciones);
        Sesion.Cambio += (id, _) => { if (id == prueba) Refrescar(); };
        Refrescar();
    }

    void Refrescar()
    {
        var actual = Sesion.Obtener(prueba);
        foreach (var (boton, valor) in botones) boton.Classes.Set("elegido", valor == actual);
    }
}
