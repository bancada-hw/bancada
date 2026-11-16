using Avalonia;
using Avalonia.Controls;
using Bancada.Modelo;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Discos: salud SMART y velocidad real de lectura y escritura.</summary>
internal sealed class DiscosVista : UserControl, IAutomatica
{
    readonly StackPanel panelSalud = new() { Spacing = 10 };
    readonly StackPanel panelVolumenes = new() { Spacing = 10 };
    readonly StackPanel panelResultado = new() { Spacing = 12 };
    readonly List<Button> botonesMedir = new();
    List<SaludDisco> discos = new();
    List<Volumen> volumenes = new();
    CancellationTokenSource? cancelar;
    Task? carga;
    bool medidaHecha;

    Task CargarAsync() => carga ??= Cargar();

    /// <summary>Revisión completa: salud de todos los discos y velocidad de la unidad del sistema.</summary>
    public async Task EjecutarAsync(int minutos)
    {
        Revision.Informar("discos", Tx("Leyendo la salud de los discos…"));
        await CargarAsync();
        if (!medidaHecha && volumenes.Count > 0) await Medir(volumenes[0]);
    }

    async Task Cargar()
    {
        panelSalud.Children.Add(Ui.Cargando(Tx("Leyendo la salud de los discos…")));
        panelVolumenes.Children.Add(Ui.Cargando(Tx("Buscando unidades…")));
        var (lista, nota) = await Task.Run(Almacenamiento.Salud);
        discos = lista;
        PintarSalud(nota);
        volumenes = await Task.Run(Almacenamiento.Volumenes);
        PintarVolumenes(volumenes);
    }

    public DiscosVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Discos"),
            Tx("La salud que informa cada disco (SMART) y su velocidad real. La prueba de velocidad escribe un archivo temporal de hasta 1 GB y lo borra al terminar: no toca tus archivos.")));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Salud"), panelSalud));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Velocidad"), panelVolumenes, panelResultado));
        cuerpo.Children.Add(new BarraVeredicto("discos", Tx("¿Los discos funcionan bien?")));
        Content = pagina;

        AttachedToVisualTree += async (_, _) => await CargarAsync();
    }

    void PintarSalud(string? nota)
    {
        panelSalud.Children.Clear();
        if (discos.Count == 0)
        {
            panelSalud.Children.Add(Ui.Chip(Tx("No se ha encontrado ningún disco"), Veredicto.Fallo));
            return;
        }
        foreach (var d in discos)
        {
            var p = new StackPanel { Spacing = 12 };
            var titulo = new StackPanel { Spacing = 2 };
            titulo.Children.Add(Ui.Texto(d.Modelo, "valor negrita"));
            titulo.Children.Add(Ui.Texto(string.Join(" · ", new[] { Formato.Bytes(d.Bytes), d.Tipo, d.Conexion, d.Firmware.Length > 0 ? "firmware " + d.Firmware : "" }.Where(x => x.Length > 0)), "etiqueta"));
            p.Children.Add(Ui.Fila(titulo, Ui.Chip(d.EstadoTexto, d.Estado)));

            var datos = new List<Control>();
            if (d.TemperaturaC is int t) datos.Add(Ui.Dato(Tx("Temperatura"), $"{t} °C"));
            if (d.HorasEncendido is int h) datos.Add(Ui.Dato(Tx("Horas de uso"), $"{Formato.N(h, "#,0")} h"));
            if (d.VidaUsadaPct is int v) datos.Add(Ui.Dato(Tx("Vida consumida"), $"{v} %"));
            if (d.EscritoBytes is long w && w > 0) datos.Add(Ui.Dato(Tx("Datos escritos"), Formato.Bytes(w)));
            if (d.Reasignados is long r) datos.Add(Ui.Dato(Tx("Sectores reasignados"), r.ToString()));
            if (d.Pendientes is long pe) datos.Add(Ui.Dato(Tx("Sectores pendientes"), pe.ToString()));
            if (d.ErroresMedio is long em) datos.Add(Ui.Dato(Tx("Errores de datos"), em.ToString()));
            if (d.ApagadosBruscos is long ab) datos.Add(Ui.Dato(Tx("Apagados bruscos"), Formato.N(ab, "#,0")));
            if (datos.Count > 0)
            {
                var rejilla = new WrapPanel();
                foreach (var c in datos)
                {
                    c.Width = 170;
                    c.Margin = new Thickness(0, 0, 12, 8);
                    rejilla.Children.Add(c);
                }
                p.Children.Add(rejilla);
            }
            foreach (var m in d.Motivos.Concat(d.Notas)) p.Children.Add(Ui.Texto("• " + m, "etiqueta"));
            panelSalud.Children.Add(Ui.Hundido(p));

            var resumen = new List<string> { d.EstadoTexto };
            if (d.TemperaturaC is int tt) resumen.Add($"{tt} °C");
            if (d.HorasEncendido is int hh) resumen.Add(Tf("{0} h de uso", hh));
            if (d.VidaUsadaPct is int vv) resumen.Add(Tf("{0} % de vida consumida", vv));
            resumen.AddRange(d.Motivos);
            resumen.AddRange(d.Notas);
            Sesion.Anotar("discos", $"{d.Modelo} ({Formato.Bytes(d.Bytes)} {d.Tipo} {d.Conexion})".Replace("  ", " "), string.Join("; ", resumen));
        }
        if (nota != null) panelSalud.Children.Add(Ui.Aviso(nota));

        // Propuesta de veredicto a partir de la salud (se puede cambiar con los botones).
        if (Sesion.Obtener("discos") == Veredicto.Pendiente && discos.Any(d => d.Estado != Veredicto.Pendiente))
            Sesion.Fijar("discos", discos.Max(d => d.Estado == Veredicto.Pendiente ? Veredicto.Ok : d.Estado));
    }

    void PintarVolumenes(List<Volumen> volumenes)
    {
        panelVolumenes.Children.Clear();
        botonesMedir.Clear();
        if (volumenes.Count == 0)
        {
            panelVolumenes.Children.Add(Ui.Texto(Tx("No hay unidades en las que se pueda escribir."), "suave"));
            return;
        }
        foreach (var v in volumenes)
        {
            var info = new StackPanel { Spacing = 2 };
            info.Children.Add(Ui.Texto(v.Disco.Length > 0 ? $"{v.Nombre} · {v.Disco}" : v.Nombre, "valor negrita"));
            info.Children.Add(Ui.Texto(Tf("{0} libres de {1}", Formato.Bytes(v.Libre), Formato.Bytes(v.Total)), "etiqueta"));
            var vol = v;
            var boton = Ui.Boton(Tx("Medir velocidad"), "primario", async (_, _) => await Medir(vol));
            botonesMedir.Add(boton);
            panelVolumenes.Children.Add(Ui.Hundido(Ui.Fila(info, boton)));
        }
    }

    async Task Medir(Volumen v)
    {
        medidaHecha = true;
        foreach (var b in botonesMedir) b.IsEnabled = false;
        panelResultado.Children.Clear();
        var estado = Ui.Texto(Tx("Preparando…"), "suave");
        cancelar = new CancellationTokenSource();
        PruebasEnCurso.Entrar();
        var botonCancelar = Ui.Boton(Tx("Cancelar"), "secundario", (_, _) => cancelar.Cancel());
        panelResultado.Children.Add(Ui.Fila(estado, botonCancelar));
        panelResultado.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 });
        try
        {
            var progreso = new Progress<string>(t =>
            {
                estado.Text = $"{v.Nombre}: {t}";
                Revision.Informar("discos", Tf("Velocidad de {0}: {1}", v.Nombre, t));
            });
            var token = cancelar.Token;
            var r = await Task.Run(() => Almacenamiento.Medir(v, progreso, token), token);
            panelResultado.Children.Clear();
            panelResultado.Children.Add(Ui.Texto($"{v.Nombre}{(v.Disco.Length > 0 ? " · " + v.Disco : "")}", "valor negrita"));
            panelResultado.Children.Add(Ui.Columnas(
                Ui.Cifra(Tx("Lectura"), Formato.N(r.LecturaMBs, "0"), Tx("MB/s")),
                Ui.Cifra(Tx("Escritura"), Formato.N(r.EscrituraMBs, "0"), Tx("MB/s")),
                Ui.Cifra(Tx("Aleatoria 4 KB"), Formato.N(r.Iops, "#,0"), "op/s")));

            var disco = discos.FirstOrDefault(d => v.Disco.Length > 0 && d.Modelo.Contains(v.Disco, StringComparison.OrdinalIgnoreCase))
                        ?? discos.FirstOrDefault(d => v.Disco.Length > 0 && v.Disco.Contains(d.Modelo, StringComparison.OrdinalIgnoreCase));
            if (disco != null)
            {
                var (minimo, texto) = Almacenamiento.Esperado(disco.Tipo, disco.Conexion);
                if (texto.Length > 0) panelResultado.Children.Add(Ui.Texto(texto, "etiqueta"));
                if (minimo > 0 && r.LecturaMBs < minimo)
                    panelResultado.Children.Add(Ui.Aviso(Tx("La lectura está muy por debajo de lo normal para este tipo de disco. Puede estar casi lleno, recalentado, mal conectado o empezando a fallar.")));
            }
            Sesion.Anotar("discos", Tf("Velocidad {0}", v.Nombre),
                Tf("lectura {0} MB/s, escritura {1} MB/s, aleatoria 4 KB {2} op/s", Formato.N(r.LecturaMBs, "0"), Formato.N(r.EscrituraMBs, "0"), Formato.N(r.Iops, "#,0")));
        }
        catch (OperationCanceledException)
        {
            panelResultado.Children.Clear();
            panelResultado.Children.Add(Ui.Texto(Tx("Prueba cancelada."), "suave"));
        }
        catch (Exception ex)
        {
            panelResultado.Children.Clear();
            panelResultado.Children.Add(Ui.Chip(Tx("No se ha podido medir"), Veredicto.Fallo));
            panelResultado.Children.Add(Ui.Texto(ex.Message, "etiqueta"));
        }
        PruebasEnCurso.Salir();
        foreach (var b in botonesMedir) b.IsEnabled = true;
    }
}
