using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Bancada.Modelo;

namespace Bancada.Vistas;

/// <summary>Resumen del equipo: qué es, qué lleva dentro y cómo van las pruebas.</summary>
internal sealed class InicioVista : UserControl
{
    readonly StackPanel cuerpo;
    readonly Action<string> ir;
    readonly ContentControl huecoRevision = new();

    public InicioVista(Action<string> ir)
    {
        this.ir = ir;
        Content = Ui.Pagina(out cuerpo);
        Construir();
        Sesion.EquipoDetectado += Construir;
        Sesion.Cambio += (_, _) => Construir();
        Revision.Cambio += () => huecoRevision.Content = TarjetaRevision();
    }

    void Construir()
    {
        cuerpo.Children.Clear();
        cuerpo.Children.Add(Ui.Cabecera(Tx("Resumen del equipo"),
            Tx("Lo que Bancada ha detectado. Después, recorre las pruebas de la izquierda y marca en cada una si funciona.")));

        var e = Sesion.Equipo;
        if (e == null)
        {
            cuerpo.Children.Add(Ui.Tarjeta(null, Ui.Cargando(Tx("Leyendo el hardware del equipo…"))));
            return;
        }

        cuerpo.Children.Add(TarjetaIdentidad(e));
        foreach (var nota in e.Notas)
            cuerpo.Children.Add(new Border { Child = Ui.Texto(nota, "valor") }.Clases("aviso-caja"));
        if (OperatingSystem.IsWindows() && !Sistema.Plataforma.EsAdministrador && e.Notas.Count > 0)
            cuerpo.Children.Add(Ui.Boton(Tx("Abrir Bancada como administrador"), "secundario", (_, _) => Sistema.Plataforma.ReiniciarComoAdministrador()));

        huecoRevision.Content = TarjetaRevision();
        cuerpo.Children.Add(huecoRevision);

        cuerpo.Children.Add(Ui.DosColumnas(
            new Control[] { TarjetaProcesador(e), TarjetaMemoria(e), TarjetaGraficos(e) },
            new Control?[] { TarjetaDiscos(e), TarjetaBateria(e), TarjetaPlaca(e) }.OfType<Control>()));

        cuerpo.Children.Add(TarjetaPruebas());
    }

    static Control TarjetaIdentidad(InfoEquipo e)
    {
        var nombre = $"{e.Marca} {e.Modelo}".Trim();
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new[]
                 {
                     Ui.Chip(TextoEquipo.Tipo(e.Tipo), e.Tipo == TipoEquipo.Desconocido ? Veredicto.Aviso : Veredicto.Ok),
                     Ui.Chip(e.SistemaOperativo, Veredicto.Pendiente)
                 })
        {
            c.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(c);
        }
        if (e.EsVirtual)
        {
            var v = Ui.Chip(Tx("Máquina virtual"), Veredicto.Aviso);
            v.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(v);
        }

        return Ui.Tarjeta(Tx("Equipo"),
            Ui.Texto(nombre.Length > 0 ? nombre : Tx("Fabricante y modelo no disponibles"), "grande"),
            chips,
            Ui.Dato(Tx("Número de serie"), e.NumeroSerie.Length > 0 ? e.NumeroSerie : Tx("No disponible"), mono: true));
    }

    static Control TarjetaProcesador(InfoEquipo e) => Ui.Tarjeta(Tx("Procesador"),
        Ui.Texto(e.Procesador.Length > 0 ? e.Procesador : Tx("No disponible"), "valor negrita"),
        Ui.Texto(Tf("{0} núcleos · {1} hilos", e.NucleosFisicos, e.HilosLogicos), "etiqueta"));

    static Control TarjetaMemoria(InfoEquipo e)
    {
        var hijos = new List<Control> { Ui.Texto(Formato.Ram(e.RamTotalBytes), "valor negrita") };
        if (e.RanurasRam is int ranuras && e.ModulosRam.Count > 0)
            hijos.Add(Ui.Texto(Tf("{0} de {1} ranuras ocupadas", e.ModulosRam.Count, ranuras), "etiqueta"));
        foreach (var m in e.ModulosRam)
        {
            var detalle = string.Join(" · ", new[]
            {
                Formato.Ram(m.Bytes),
                m.MHz > 0 ? $"{m.MHz} MHz" : "",
                m.Fabricante,
                m.Referencia
            }.Where(x => x.Length > 0));
            hijos.Add(new Border { Child = Ui.Dato(m.Ranura.Length > 0 ? m.Ranura : Tx("Módulo"), detalle, mono: true) }.Clases("hundido"));
        }
        return Ui.Tarjeta(Tx("Memoria RAM"), hijos.ToArray());
    }

    static Control TarjetaGraficos(InfoEquipo e)
    {
        var hijos = e.Graficas.Count > 0
            ? e.Graficas.Select(g => (Control)Ui.Texto(g, "valor")).ToArray()
            : new Control[] { Ui.Texto(Tx("No se ha detectado tarjeta gráfica"), "suave") };
        return Ui.Tarjeta(Tx("Gráficos"), hijos);
    }

    static Control TarjetaDiscos(InfoEquipo e)
    {
        if (e.Discos.Count == 0) return Ui.Tarjeta(Tx("Almacenamiento"), Ui.Texto(Tx("No se han detectado discos"), "suave"));
        var hijos = new List<Control>();
        foreach (var d in e.Discos)
        {
            var detalle = string.Join(" · ", new[] { Formato.Bytes(d.Bytes), d.Tipo, d.Conexion }.Where(x => x.Length > 0));
            var info = new StackPanel { Spacing = 2 };
            info.Children.Add(Ui.Texto(d.Modelo, "valor negrita"));
            info.Children.Add(Ui.Texto(detalle, "etiqueta"));
            Control fila = d.Salud.Length == 0
                ? info
                : Ui.Fila(info, Ui.Chip(Tx(d.Salud), d.Salud switch { "Correcta" => Veredicto.Ok, "Aviso" => Veredicto.Aviso, _ => Veredicto.Fallo }));
            hijos.Add(new Border { Child = fila }.Clases("hundido"));
        }
        return Ui.Tarjeta(Tx("Almacenamiento"), hijos.ToArray());
    }

    static Control? TarjetaBateria(InfoEquipo e)
    {
        if (e.Bateria is not { } b) return null;
        var hijos = new List<Control>();
        var carga = b.CargaPct is int c ? Tf("{0} %", c) : "—";
        hijos.Add(Ui.Fila(Ui.Dato(Tx("Carga"), $"{carga}{(b.Estado.Length > 0 ? " · " + b.Estado : "")}"),
            b.SaludPct is int s
                ? Ui.Chip(Tf("Salud {0} %", s), s >= 80 ? Veredicto.Ok : s >= 60 ? Veredicto.Aviso : Veredicto.Fallo)
                : Ui.Chip(Tx("Salud sin leer"), Veredicto.Pendiente)));
        if (b.Ciclos is int ciclos) hijos.Add(Ui.Dato(Tx("Ciclos de carga"), ciclos.ToString()));
        return Ui.Tarjeta(Tx("Batería"), hijos.ToArray());
    }

    static Control TarjetaPlaca(InfoEquipo e) => Ui.Tarjeta(Tx("Placa y BIOS"),
        Ui.Dato(Tx("Placa base"), e.Placa),
        Ui.Dato("BIOS", e.Bios, mono: true));

    Control TarjetaRevision()
    {
        var boton = Revision.Empezada
            ? Ui.Boton(Tx("Ver cómo va la revisión"), "primario", (_, _) => ir("revision"))
            : Ui.Boton(Tx("Revisar este equipo de principio a fin"), "primario", (_, _) => ir("revision"));
        var texto = new StackPanel { Spacing = 4 };
        texto.Children.Add(Ui.Texto(Tx("Revisión completa"), "valor negrita"));
        texto.Children.Add(Ui.Texto(Revision.Empezada
            ? Revision.TextoFondo()
            : Tx("Te guía por las pruebas en las que tienes que hacer algo y hace las demás sola mientras tanto."), "etiqueta"));
        texto.Margin = new Thickness(0, 0, 16, 0);
        return Ui.Tarjeta(null, Ui.Fila(texto, boton));
    }

    Control TarjetaPruebas()
    {
        var izquierda = new List<Control>();
        var derecha = new List<Control>();
        var lista = Pruebas.Lista;
        int mitad = (lista.Length + 1) / 2;
        for (int i = 0; i < lista.Length; i++)
        {
            var (id, nombre) = lista[i];
            var v = Sesion.Obtener(id);
            var enlace = Ui.Boton(nombre, "secundario", (_, _) => ir(id));
            enlace.Width = 236;
            enlace.HorizontalContentAlignment = HorizontalAlignment.Left;
            (i < mitad ? izquierda : derecha).Add(Ui.Fila(enlace, Ui.Chip(Ui.TextoVeredicto(v), v)));
        }
        var hechas = lista.Count(p => Sesion.Obtener(p.Id) != Veredicto.Pendiente);
        var columnas = Ui.DosColumnas(izquierda, derecha);
        foreach (var c in columnas.Children.OfType<StackPanel>()) c.Spacing = 8;
        return Ui.Tarjeta(Tx("Estado de las pruebas"),
            Ui.Texto(Tf("{0} de {1} pruebas revisadas", hechas, lista.Length), "etiqueta"),
            columnas,
            Ui.Boton(Tx("Ver el informe"), "primario", (_, _) => ir("informe")));
    }
}
