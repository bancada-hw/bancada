using Avalonia.Controls;
using Avalonia.Threading;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Puertos USB: se conecta algo en cada puerto y se comprueba que responde y a qué velocidad.</summary>
internal sealed class UsbVista : UserControl
{
    readonly StackPanel panelPuertos = new() { Spacing = 8 };
    readonly TextBlock textoPuertos = Ui.Texto(Tx("Ningún puerto comprobado todavía."), "valor negrita");
    readonly StackPanel panelAhora = new() { Spacing = 8 };
    readonly StackPanel panelInternos = new() { Spacing = 6 };
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly Dictionary<string, DispositivoUsb> puertosProbados = new();
    HashSet<string> clavesAnteriores = new();
    bool leyendo, primeraLectura = true;

    public UsbVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Puertos USB"),
            Tx("Conecta un pendrive, un ratón o cualquier aparato USB en cada puerto, de uno en uno. Cada puerto que responde queda marcado, con la velocidad a la que funciona.")));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Puertos comprobados"),
            textoPuertos,
            panelPuertos,
            Ui.Texto(Tx("Para probar la velocidad USB 3, usa un pendrive o disco USB 3 (suelen tener el interior del conector azul). Un ratón o un teclado siempre funcionan a velocidad USB 1 o 2."), "etiqueta")));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Conectado ahora"), panelAhora));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Dispositivos USB internos"), panelInternos,
            Ui.Texto(Tx("Cámara, lector de huellas, Bluetooth y otros componentes que van por USB dentro del equipo."), "etiqueta")));
        cuerpo.Children.Add(new BarraVeredicto("usb", Tx("¿Los puertos USB funcionan bien?")));
        Content = pagina;

        reloj.Tick += async (_, _) => await Leer();
        AttachedToVisualTree += async (_, _) =>
        {
            reloj.Start();
            await Leer();
        };
        DetachedFromVisualTree += (_, _) => reloj.Stop();
    }

    async Task Leer()
    {
        if (leyendo) return;
        leyendo = true;
        var lista = await Task.Run(Usb.Listar);
        leyendo = false;

        var externos = lista.Where(d => !d.Interno).ToList();
        foreach (var d in externos)
        {
            bool nuevo = !primeraLectura && !clavesAnteriores.Contains(d.Clave);
            // Un puerto queda comprobado con lo que ya estaba conectado y con cada cosa nueva que se enchufa.
            if (!puertosProbados.TryGetValue(d.IdPuerto, out var previo) || nuevo || (previo.Mbps ?? 0) < (d.Mbps ?? 0))
                puertosProbados[d.IdPuerto] = d;
        }
        clavesAnteriores = lista.Select(d => d.Clave).ToHashSet();
        primeraLectura = false;

        PintarPuertos();
        PintarAhora(externos);
        PintarInternos(lista.Where(d => d.Interno).ToList());
    }

    void PintarPuertos()
    {
        panelPuertos.Children.Clear();
        textoPuertos.Text = puertosProbados.Count switch
        {
            0 => Tx("Ningún puerto comprobado todavía. Conecta algo en un puerto USB."),
            1 => Tx("1 puerto funciona"),
            var n => Tf("{0} puertos funcionan", n)
        };
        foreach (var d in puertosProbados.Values.OrderBy(d => d.Puerto))
            panelPuertos.Children.Add(Fila(d));

        var lentos = puertosProbados.Values.Count(d => d.VaLento);
        Sesion.Anotar("usb", Tx("Puertos que funcionan"), puertosProbados.Count.ToString());
        Sesion.Anotar("usb", Tx("Detalle"), puertosProbados.Values.OrderBy(d => d.Puerto)
            .Select(d => Tf("{0} con {1} a {2}", d.Puerto, d.Nombre, d.Velocidad)).ToArray());
        Sesion.Anotar("usb", Tx("Aviso"), lentos > 0 ? Tf("{0} aparato(s) USB 3 funcionando a velocidad USB 2", lentos) : "");
    }

    static Control Fila(DispositivoUsb d)
    {
        var info = new StackPanel { Spacing = 2 };
        bool directo = !d.PorConcentrador;
        info.Children.Add(Ui.Texto($"{d.Puerto}{(d.PuertoUsb3 && directo ? Tx(" · puerto USB 3") : "")}", "valor negrita"));
        info.Children.Add(Ui.Texto(d.Nombre, "etiqueta"));
        Control estado = d.VaLento
            ? Ui.Chip(Tf("{0}: debería ir a USB 3", d.Velocidad), Veredicto.Aviso)
            : Ui.Chip(d.Velocidad.Length > 0 ? d.Velocidad : Tx("Funciona"), Veredicto.Ok);
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(Ui.Fila(info, estado));
        if (d.VaLento)
            p.Children.Add(Ui.Texto(Tx("El aparato y el puerto son USB 3, pero se han conectado a velocidad USB 2. Prueba otro cable; si sigue igual, el conector del puerto puede estar dañado."), "etiqueta"));
        return Ui.Hundido(p);
    }

    void PintarAhora(List<DispositivoUsb> externos)
    {
        panelAhora.Children.Clear();
        if (externos.Count == 0)
        {
            panelAhora.Children.Add(Ui.Texto(Tx("No hay nada conectado por USB."), "suave"));
            return;
        }
        foreach (var d in externos)
            panelAhora.Children.Add(Ui.Fila(Ui.Texto($"{d.Nombre}", "valor"), Ui.Texto($"{d.Puerto} · {d.Velocidad}", "etiqueta")));
    }

    void PintarInternos(List<DispositivoUsb> internos)
    {
        panelInternos.Children.Clear();
        if (internos.Count == 0)
        {
            panelInternos.Children.Add(Ui.Texto(Tx("Ninguno."), "suave"));
            return;
        }
        foreach (var d in internos)
            panelInternos.Children.Add(Ui.Fila(Ui.Texto(d.Nombre, "valor"), Ui.Texto(d.Velocidad, "etiqueta")));
    }
}
