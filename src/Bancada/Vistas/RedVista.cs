using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Bancada.Modelo;
using Bancada.Red;

namespace Bancada.Vistas;

/// <summary>Pruebas de red: tarjetas, Wi-Fi, Bluetooth, conexión a Internet y velocidad.</summary>
internal sealed class RedVista : UserControl, IAutomatica
{
    Task? carga;
    bool velocidadMedida;
    readonly Propuesta propuesta = new("red");

    Task CargarAsync() => carga ??= CargarTodo();

    /// <summary>Revisión completa: tarjetas, Wi-Fi, Bluetooth, conexión y velocidad de Internet.</summary>
    public async Task EjecutarAsync(int minutos)
    {
        Revision.Informar("red", Tx("Tarjetas de red, Wi-Fi, Bluetooth y conexión a Internet…"));
        await CargarAsync();
        if (!velocidadMedida) await MedirVelocidad();
    }

    void Parte(string parte, Veredicto v, params string[] lineas)
    {
        propuesta.Parte(parte, v, proponer: false);
        Sesion.Anotar("red", parte, lineas);
    }

    void Proponer() => propuesta.Proponer();

    readonly StackPanel panelAdaptadores = new() { Spacing = 10 };
    readonly StackPanel panelWifi = new() { Spacing = 10 };
    readonly StackPanel panelBluetooth = new() { Spacing = 10 };
    readonly StackPanel panelConexion = new() { Spacing = 10 };
    readonly StackPanel panelVelocidad = new() { Spacing = 12 };
    readonly Button botonBuscarWifi;
    readonly Button botonConexion;
    readonly Button botonVelocidad;
    List<Adaptador> adaptadores = new();

    bool EsSobremesa => Sesion.Equipo?.Tipo is TipoEquipo.Sobremesa or TipoEquipo.MiniPc;
    bool EsVirtual => Sesion.Equipo?.EsVirtual == true;

    /// <summary>Qué decir cuando no aparece una radio (Wi-Fi o Bluetooth), según el tipo de equipo.</summary>
    Border ChipSinRadio(string radio, string mensajeNormal, Veredicto gravedad) =>
        EsVirtual ? Ui.Chip(Tf("{0} no visible desde una máquina virtual", radio), Veredicto.Pendiente) :
        EsSobremesa ? Ui.Chip(Tf("Este equipo no tiene {0} (normal en muchos sobremesa)", radio), Veredicto.Pendiente) :
        Ui.Chip(mensajeNormal, gravedad);

    public RedVista()
    {
        var pagina = Ui.Pagina(out var cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Red"),
            Tx("Tarjetas de red, Wi-Fi, Bluetooth, conexión a Internet y velocidad. Para probar el puerto Ethernet, conecta un cable a un router o a un switch.")));

        botonBuscarWifi = Ui.Boton(Tx("Buscar redes Wi-Fi"), "secundario", async (_, _) => await CargarWifi(escanear: true));
        botonConexion = Ui.Boton(Tx("Comprobar otra vez"), "secundario", async (_, _) => await ComprobarConexion());
        botonVelocidad = Ui.Boton(Tx("Medir velocidad"), "primario", async (_, _) => await MedirVelocidad());

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Tarjetas de red"), panelAdaptadores));
        cuerpo.Children.Add(Ui.Tarjeta("Wi-Fi", panelWifi, botonBuscarWifi));
        cuerpo.Children.Add(Ui.Tarjeta("Bluetooth", panelBluetooth));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Conexión a Internet"), panelConexion, botonConexion));
        panelVelocidad.Children.Add(Ui.Texto(Tx("Descarga y sube datos de prueba a los servidores de Cloudflare durante unos 25 segundos."), "subtitulo"));
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Velocidad de Internet"), panelVelocidad, botonVelocidad));
        cuerpo.Children.Add(new BarraVeredicto("red", Tx("¿La red funciona bien?")));
        Content = pagina;

        AttachedToVisualTree += async (_, _) => await CargarAsync();
    }

    async Task CargarTodo()
    {
        panelAdaptadores.Children.Add(Ui.Cargando(Tx("Buscando tarjetas de red…")));
        adaptadores = await Task.Run(Adaptadores.Listar);
        PintarAdaptadores();
        await Task.WhenAll(CargarWifi(escanear: false), CargarBluetooth(), ComprobarConexion());
        Proponer();
    }

    // ---------- Tarjetas de red ----------

    void PintarAdaptadores()
    {
        panelAdaptadores.Children.Clear();
        if (adaptadores.Count == 0)
        {
            panelAdaptadores.Children.Add(Ui.Chip(Tx("No se ha encontrado ninguna tarjeta de red"), Veredicto.Fallo));
            Parte(Tx("Tarjetas de red"), Veredicto.Fallo, Tx("No se ha encontrado ninguna tarjeta de red"));
            return;
        }
        Parte(Tx("Tarjetas de red"), Veredicto.Ok, adaptadores.Select(a =>
            $"{a.Tipo} {a.Modelo} ({(a.Conectado ? (a.VelocidadBps > 0 ? Tf("conectado a {0}", Formato.Bps(a.VelocidadBps)) : Tx("conectado")) : Tx("sin conexión"))})").ToArray());
        foreach (var a in adaptadores)
        {
            var info = new StackPanel { Spacing = 3 };
            info.Children.Add(Ui.Texto($"{a.Tipo} · {a.Modelo}", "valor negrita"));
            var datos = new List<string>();
            if (a.Conectado && a.VelocidadBps > 0) datos.Add(Formato.Bps(a.VelocidadBps));
            if (a.Ipv4.Length > 0) datos.Add("IP " + a.Ipv4);
            if (a.Mac.Length > 0) datos.Add("MAC " + a.Mac);
            info.Children.Add(Ui.Texto(string.Join("  ·  ", datos), "etiqueta mono"));

            if (a.Tipo == "Ethernet" && a.Conectado && a.VelocidadBps is > 0 and < 1_000_000_000)
                info.Children.Add(Ui.Texto(Tx("Enlace por debajo de 1 Gbps. Si el puerto y el router son gigabit, revisa el cable."), "etiqueta"));

            var estado = a.Conectado
                ? Ui.Chip(Tx("Conectado"), Veredicto.Ok)
                : a.Tipo == "Ethernet"
                    ? Ui.Chip(Tx("Sin cable"), Veredicto.Pendiente)
                    : Ui.Chip(Tx("Desconectado"), Veredicto.Pendiente);
            panelAdaptadores.Children.Add(new Border { Child = Ui.Fila(info, estado) }.Clases("hundido"));
        }
    }

    // ---------- Wi-Fi ----------

    async Task CargarWifi(bool escanear)
    {
        botonBuscarWifi.IsEnabled = false;
        panelWifi.Children.Clear();
        panelWifi.Children.Add(Ui.Cargando(escanear ? Tx("Buscando redes cercanas…") : Tx("Leyendo el Wi-Fi…")));
        var estado = await Wifi.EstadoAsync(escanear);
        panelWifi.Children.Clear();
        botonBuscarWifi.IsEnabled = true;

        if (!estado.HayAdaptador)
        {
            panelWifi.Children.Add(ChipSinRadio("Wi-Fi", Tx("No se ha encontrado tarjeta Wi-Fi"), Veredicto.Fallo));
            if (estado.Error != null) panelWifi.Children.Add(Ui.Texto(estado.Error, "etiqueta"));
            botonBuscarWifi.IsVisible = false;
            Parte("Wi-Fi", EsSobremesa || EsVirtual ? Veredicto.Pendiente : Veredicto.Fallo,
                EsSobremesa || EsVirtual ? Tx("no tiene") : Tx("No se ha encontrado tarjeta Wi-Fi"));
            return;
        }
        if (estado.Actual is { } conectada)
            Parte("Wi-Fi", Veredicto.Ok, Tf("conectado, señal {0} %", conectada.SenalPct) + (conectada.Banda.Length > 0 ? ", " + conectada.Banda : ""));
        else if (!escanear)
            Parte("Wi-Fi", Veredicto.Ok, Tx("tarjeta detectada, sin conexión"));

        if (estado.Actual is { } c)
        {
            var detalle = string.Join(" · ", new[]
            {
                Tf("señal {0} %", c.SenalPct),
                c.Banda,
                c.VelocidadMbps is int v ? $"{v} {Tx("Mbps")}" : ""
            }.Where(x => x.Length > 0));
            var info = new StackPanel { Spacing = 3 };
            info.Children.Add(Ui.Texto(Tf("Conectado a «{0}»", c.Ssid), "valor negrita"));
            info.Children.Add(Ui.Texto(detalle, "etiqueta"));
            panelWifi.Children.Add(new Border { Child = Ui.Fila(info, Ui.Barra(c.SenalPct)) }.Clases("hundido"));
        }
        else
        {
            panelWifi.Children.Add(Ui.Chip(Tx("Tarjeta Wi-Fi detectada, sin conexión"), Veredicto.Pendiente));
        }

        if (estado.Error != null)
            panelWifi.Children.Add(new Border { Child = Ui.Texto(estado.Error, "valor") }.Clases("aviso-caja"));

        if (estado.Redes.Count > 0)
        {
            panelWifi.Children.Add(Ui.Texto(Tf("{0} redes al alcance", estado.Redes.Count), "etiqueta"));
            foreach (var r in estado.Redes.Take(12)) panelWifi.Children.Add(FilaRed(r));
        }
        else if (escanear && estado.Error == null)
        {
            panelWifi.Children.Add(Ui.Chip(Tx("No se ve ninguna red: puede que una antena esté suelta o rota"), Veredicto.Aviso));
            Parte("Wi-Fi", Veredicto.Aviso, Tx("No se ve ninguna red: puede que una antena esté suelta o rota"));
            Proponer();
        }
    }

    static Control FilaRed(RedWifi r)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,130,90,150") };
        var nombre = Ui.Texto((r.Conectada ? "● " : "") + r.Ssid, r.Conectada ? "valor negrita" : "valor");
        nombre.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        nombre.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
        var barra = Ui.Barra(r.SenalPct);
        var pct = Ui.Texto(r.Dbm is int d ? $"{d} dBm" : $"{r.SenalPct} %", "etiqueta mono");
        var canal = Ui.Texto(r.Canal > 0 ? Tf("canal {0} · {1}", r.Canal, r.Banda) : r.Banda, "etiqueta");
        foreach (var (c, col) in new (Control, int)[] { (nombre, 0), (barra, 1), (pct, 2), (canal, 3) })
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, col);
            g.Children.Add(c);
        }
        pct.Margin = new Thickness(14, 0, 0, 0);
        canal.Margin = new Thickness(8, 0, 0, 0);
        return g;
    }

    // ---------- Bluetooth ----------

    async Task CargarBluetooth()
    {
        panelBluetooth.Children.Clear();
        panelBluetooth.Children.Add(Ui.Cargando(Tx("Buscando adaptador Bluetooth…")));
        var bt = await Bluetooth.DetectarAsync();
        panelBluetooth.Children.Clear();
        if (!bt.Hay)
        {
            panelBluetooth.Children.Add(ChipSinRadio("Bluetooth", bt.Estado, Veredicto.Aviso));
            Parte("Bluetooth", EsSobremesa || EsVirtual ? Veredicto.Pendiente : Veredicto.Aviso, EsSobremesa || EsVirtual ? Tx("no tiene") : bt.Estado);
            return;
        }
        Parte("Bluetooth", bt.Resultado, $"{bt.Nombre} ({bt.Estado})");
        panelBluetooth.Children.Add(new Border
        {
            Child = Ui.Fila(Ui.Texto(bt.Nombre, "valor negrita"), Ui.Chip(bt.Estado, bt.Resultado))
        }.Clases("hundido"));
    }

    // ---------- Conexión ----------

    async Task ComprobarConexion()
    {
        botonConexion.IsEnabled = false;
        panelConexion.Children.Clear();
        panelConexion.Children.Add(Ui.Cargando(Tx("Comprobando router, DNS e Internet…")));
        var puerta = adaptadores.FirstOrDefault(a => a.Conectado && a.PuertaEnlace.Length > 0)?.PuertaEnlace;
        var pasos = await Conectividad.ComprobarAsync(puerta);
        panelConexion.Children.Clear();
        foreach (var p in pasos)
            panelConexion.Children.Add(Ui.Fila(Ui.Texto(p.Nombre, "valor"), Ui.Chip(p.Detalle, p.Resultado)));

        var primero = pasos.FirstOrDefault(p => p.Resultado == Veredicto.Fallo);
        Parte(Tx("Conexión a Internet"), primero != null ? Veredicto.Aviso : Veredicto.Ok,
            string.Join(", ", pasos.Select(p => $"{p.Nombre.ToLowerInvariant()} {p.Detalle}")));
        if (carga?.IsCompleted == true) Proponer();
        if (primero != null)
            panelConexion.Children.Add(new Border
            {
                Child = Ui.Texto(Tf("La conexión se corta en: {0}. Lo que va después no puede funcionar.", primero.Nombre.ToLowerInvariant()), "valor")
            }.Clases("aviso-caja"));
        botonConexion.IsEnabled = true;
    }

    // ---------- Velocidad ----------

    async Task MedirVelocidad()
    {
        velocidadMedida = true;
        botonVelocidad.IsEnabled = false;
        PruebasEnCurso.Entrar();
        panelVelocidad.Children.Clear();
        var estado = Ui.Texto(Tx("Preparando…"), "suave");
        panelVelocidad.Children.Add(estado);
        panelVelocidad.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 });
        try
        {
            var progreso = new Progress<string>(t =>
            {
                estado.Text = t;
                Revision.Informar("red", Tf("Velocidad de Internet: {0}", t));
            });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var r = await Conectividad.MedirVelocidadAsync(progreso, cts.Token);
            panelVelocidad.Children.Clear();
            var cifras = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            var columnas = new[]
            {
                (Tx("Bajada"), Formato.N(r.BajadaMbps, r.BajadaMbps < 100 ? "0.0" : "0"), Tx("Mbps")),
                (Tx("Subida"), Formato.N(r.SubidaMbps, r.SubidaMbps < 100 ? "0.0" : "0"), Tx("Mbps")),
                (Tx("Latencia"), Formato.N(r.LatenciaMs, "0"), "ms"),
            };
            for (int i = 0; i < columnas.Length; i++)
            {
                var (titulo, valor, unidad) = columnas[i];
                var p = new StackPanel { Spacing = 2 };
                p.Children.Add(Ui.Texto(titulo.ToUpperInvariant(), "seccion"));
                var linea = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                linea.Children.Add(Ui.Texto(valor, "cifra"));
                var u = Ui.Texto(unidad, "etiqueta");
                u.VerticalAlignment = VerticalAlignment.Bottom;
                u.Margin = new Thickness(0, 0, 0, 7);
                linea.Children.Add(u);
                p.Children.Add(linea);
                Grid.SetColumn(p, i);
                cifras.Children.Add(p);
            }
            panelVelocidad.Children.Add(cifras);
            panelVelocidad.Children.Add(Ui.Texto(Tx("Medido contra los servidores de Cloudflare: 10 segundos de bajada y 10 de subida, sin contar el arranque de la conexión. El resultado depende también del router y de la línea contratada."), "etiqueta"));
            Parte(Tx("Velocidad de Internet"), Veredicto.Ok, Tf("bajada {0} Mbps, subida {1} Mbps, latencia {2} ms",
                Formato.N(r.BajadaMbps, "0.#"), Formato.N(r.SubidaMbps, "0.#"), Formato.N(r.LatenciaMs, "0")));
        }
        catch (Exception ex)
        {
            panelVelocidad.Children.Clear();
            panelVelocidad.Children.Add(Ui.Chip(Tx("No se pudo medir la velocidad"), Veredicto.Fallo));
            panelVelocidad.Children.Add(Ui.Texto(ex is OperationCanceledException ? Tx("Ha tardado demasiado.") : ex.Message, "etiqueta"));
            Parte(Tx("Velocidad de Internet"), Veredicto.Aviso, Tx("No se pudo medir la velocidad"));
        }
        Proponer();
        PruebasEnCurso.Salir();
        botonVelocidad.IsEnabled = true;
    }
}
