using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bancada.Modelo;
using Bancada.Red;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>Batería: desgaste, consumo en directo, prueba de descarga con autonomía real y prueba del cargador.</summary>
internal sealed class BateriaVista : UserControl
{
    readonly StackPanel panelBaterias = new() { Spacing = 10 };
    readonly StackPanel panelAhora = new() { Spacing = 14 };
    readonly StackPanel panelCargador = new() { Spacing = 10 };
    readonly TextBlock textoCarga, textoPotencia, textoVoltaje, textoTiempo;
    readonly TextBlock tituloPotencia = Ui.Texto(Tx("Potencia").ToUpperInvariant(), "seccion");
    readonly TextBlock textoEstado = Ui.Texto("", "valor negrita");
    readonly TextBlock textoAutonomia = Ui.Texto("", "etiqueta");
    readonly Grafica grafica = new() { Unidad = " W", Capacidad = 300, Height = 150, Minimo = 0 };
    readonly Border pasoDesenchufar, pasoEnchufar;
    readonly TextBlock textoDesenchufar = Ui.Texto(Tx("1. Desenchufa el cargador"), "valor");
    readonly TextBlock textoEnchufar = Ui.Texto(Tx("2. Vuelve a enchufarlo"), "valor");
    readonly TextBlock textoCargadorNota = Ui.Texto("", "etiqueta");
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly StackPanel cuerpo;

    // ---------- Prueba de descarga (sigue en marcha aunque se cambie de pantalla) ----------

    static readonly TimeSpan[] Duraciones = { TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4) };
    static readonly int[] Porcentajes = { 90, 80, 70, 60, 50, 40, 30, 20, 10, 5 };
    static readonly int?[] Brillos = { null, 25, 50, 75, 100 };
    /// <summary>En la prueba por tiempo, por debajo de esto se para sola para que el equipo no se apague.</summary>
    const int SueloSeguridad = 10;

    /// <summary>Autonomía medida con cada tipo de uso en esta sesión (sobrevive a que se rehaga la pantalla).</summary>
    static readonly Dictionary<ModoDescarga, TimeSpan> autonomiaPorModo = new();
    static bool enMarcha;

    readonly RadioButton[] radiosModo = new RadioButton[4];
    readonly RadioButton porTiempo, porPorcentaje;
    readonly ComboBox comboTiempo, comboPct, comboBrillo;
    readonly TextBlock notaPct = Ui.Texto("", "etiqueta");
    readonly TextBlock notaBrillo = Ui.Texto("", "etiqueta");
    readonly StackPanel panelOpciones = new() { Spacing = 16 };
    readonly Button botonEmpezar, botonParar, botonOtra;
    readonly TextBlock textoDescarga = Ui.Texto("", "valor");
    readonly TextBlock textoEleccion = Ui.Texto("", "valor negrita");
    readonly TextBlock textoDetalle = Ui.Texto("", "etiqueta");
    readonly Grafica graficaPct = new() { Unidad = " %", Capacidad = 60, Height = 140, Minimo = 0, Maximo = 100 };
    readonly Grafica graficaW = new() { Unidad = " W", Capacidad = 60, Height = 120, Minimo = 0 };
    readonly StackPanel panelGraficas = new() { Spacing = 8, IsVisible = false };
    readonly StackPanel panelResultadoDescarga = new() { Spacing = 10 };
    readonly StackPanel panelModos = new() { Spacing = 8 };
    readonly StackPanel panelInterrumpida = new() { Spacing = 10 };
    readonly DispatcherTimer relojDescarga = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly System.Diagnostics.Stopwatch cronoDescarga = new();
    RegistroDescarga? registro;
    TimeSpan? duracionDescarga;
    int? objetivoPct;
    bool descargando;
    int lecturas;
    MantenerDespierto? despierto;
    CargaNavegacion? navegacion;
    CargaMaxima? maxima;
    VentanaVideo? video;
    EstadoEnergia? ultimo;

    bool? enchufadoAntes;
    bool desenchufadoVisto, reenchufadoVisto;
    DateTime? momentoReenchufe;
    bool sinBateriaMostrado;

    public BateriaVista()
    {
        var pagina = Ui.Pagina(out cuerpo);
        cuerpo.Children.Add(Ui.Cabecera(Tx("Batería y cargador"),
            Tx("Cuánto se ha desgastado la batería, cuánto consume el equipo, cuánto dura de verdad y si el cargador funciona.")));

        cuerpo.Children.Add(Ui.Tarjeta(Tx("Batería"), panelBaterias));

        var c1 = Ui.Cifra(Tx("Carga"), "—", "%", out textoCarga);
        var c2 = Ui.Cifra(Tx("Potencia"), "—", "W", out textoPotencia);
        c2.Children[0] = tituloPotencia;
        var c3 = Ui.Cifra(Tx("Voltaje"), "—", "V", out textoVoltaje);
        var c4 = Ui.Cifra(Tx("Tiempo restante"), "—", "", out textoTiempo);
        panelAhora.Children.Add(textoEstado);
        panelAhora.Children.Add(Ui.Columnas(c1, c2, c3, c4));
        grafica.Series.Add(new Grafica.Serie(Tx("Potencia"), Color.Parse("#D99055")));
        panelAhora.Children.Add(grafica);
        panelAhora.Children.Add(textoAutonomia);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Ahora mismo"), panelAhora));

        // 1. Qué hace el equipo mientras se descarga.
        radiosModo[(int)ModoDescarga.Reposo] = Opcion(Tx("Reposo con la pantalla encendida"),
            Tx("El equipo no hace nada. Mide lo mínimo que gasta: si sale alto, algo consume de más estando quieto (un programa, un controlador o una pieza averiada)."));
        radiosModo[(int)ModoDescarga.Navegacion] = Opcion(Tx("Navegación por Internet"),
            Tx("Bancada abre una página web real cada 15 segundos, con sus imágenes, como alguien leyendo. Es lo más parecido a un uso de oficina. Necesita conexión a Internet."));
        radiosModo[(int)ModoDescarga.Video] = Opcion(Tx("Vídeo en bucle"),
            Tx("Reproduce un vídeo 1080p sin sonido en una ventana aparte, con el decodificador de la gráfica, como al ver una película. Es la autonomía que suelen publicar las revistas. No cierres esa ventana: la prueba se pararía."));
        radiosModo[(int)ModoDescarga.Maxima] = Opcion(Tx("Carga máxima"),
            Tx("Procesador y gráfica al 100 %. Da la autonomía más corta y comprueba que la batería aguanta los picos de consumo sin que el equipo se apague de golpe."));
        var modoGuardado = Enum.TryParse<ModoDescarga>(Ajustes.Leer("bateria.modo"), out var mg) ? mg : ModoDescarga.Navegacion;
        radiosModo[(int)modoGuardado].IsChecked = true;
        var listaModos = new StackPanel { Spacing = 8 };
        foreach (var r in radiosModo) listaModos.Children.Add(r);
        panelOpciones.Children.Add(Bloque(Tx("1. Qué hará el equipo durante la prueba"), listaModos));

        // 2. Cuándo parar: por tiempo o por porcentaje, una de las dos.
        comboTiempo = Combo(Duraciones.Select(Ui.Duracion), 1);
        comboPct = Combo(Porcentajes.Select(p => $"{p} %"), Array.IndexOf(Porcentajes, 20));
        porTiempo = new RadioButton { GroupName = "paradaDescarga", Content = Tx("Durante un tiempo"), MinWidth = 260, VerticalAlignment = VerticalAlignment.Center };
        porPorcentaje = new RadioButton { GroupName = "paradaDescarga", Content = Tx("Hasta que la batería baje al"), MinWidth = 260, VerticalAlignment = VerticalAlignment.Center };
        var parada = Ajustes.Leer("bateria.parada") ?? "t:1";
        if (parada.Length > 2 && int.TryParse(parada[2..], out var ip))
        {
            if (parada[0] == 'p' && ip >= 0 && ip < Porcentajes.Length) { porPorcentaje.IsChecked = true; comboPct.SelectedIndex = ip; }
            else if (ip >= 0 && ip < Duraciones.Length) { porTiempo.IsChecked = true; comboTiempo.SelectedIndex = ip; }
        }
        if (porPorcentaje.IsChecked != true) porTiempo.IsChecked = true;
        var listaParada = new StackPanel { Spacing = 8 };
        listaParada.Children.Add(FilaParada(porTiempo, comboTiempo,
            Tf("Rápida. La autonomía total se calcula a partir de lo que baja la batería en ese rato. Si llega al {0} % antes de tiempo, se para sola para que el equipo no se apague.", SueloSeguridad)));
        listaParada.Children.Add(FilaParada(porPorcentaje, comboPct,
            Tx("Más lenta, pero mide de verdad cuánto dura. Empieza con la batería lo más cargada posible para que el resultado valga más.")));
        listaParada.Children.Add(notaPct);
        porTiempo.IsCheckedChanged += (_, _) => ActualizarParada();
        comboPct.SelectionChanged += (_, _) => ActualizarParada();
        panelOpciones.Children.Add(Bloque(Tx("2. Cuándo parar"), listaParada));

        // 3. Brillo de la pantalla.
        comboBrillo = Combo(Brillos.Select(b => b is int v ? $"{v} %" : Tx("Dejarlo como está")),
            int.TryParse(Ajustes.Leer("bateria.brillo"), out var ib) && ib >= 0 && ib < Brillos.Length ? ib : 2);
        comboBrillo.HorizontalAlignment = HorizontalAlignment.Left;
        notaBrillo.Text = Tx("El brillo es lo que más cambia la autonomía. Fijarlo durante la prueba permite comparar un equipo con otro. Al terminar se deja como estaba.");
        panelOpciones.Children.Add(Bloque(Tx("3. Brillo de la pantalla durante la prueba"), new StackPanel { Spacing = 6, Children = { comboBrillo, notaBrillo } }));

        botonEmpezar = Ui.Boton(Tx("Empezar la prueba"), "primario", (_, _) => EmpezarDescarga());
        botonEmpezar.IsEnabled = false;
        botonParar = Ui.Boton(Tx("Parar"), "secundario", (_, _) => TerminarDescarga(Tx("Parada a mano.")));
        botonParar.IsVisible = false;
        // Al terminar, el resultado queda a la vista; las opciones vuelven con este botón.
        botonOtra = Ui.Boton(Tx("Hacer otra prueba"), "primario", (_, _) =>
        {
            panelOpciones.IsVisible = botonEmpezar.IsVisible = true;
            textoEleccion.IsVisible = botonOtra!.IsVisible = false;
        });
        botonOtra.IsVisible = false;

        graficaPct.Series.Add(new Grafica.Serie(Tx("Carga"), Color.Parse("#2E9D57")));
        graficaW.Series.Add(new Grafica.Serie(Tx("Consumo"), Color.Parse("#D99055")));
        panelGraficas.Children.Add(Ui.Texto(Tx("Carga de la batería").ToUpperInvariant(), "seccion"));
        panelGraficas.Children.Add(graficaPct);
        panelGraficas.Children.Add(Ui.Texto(Tx("Consumo del equipo").ToUpperInvariant(), "seccion"));
        panelGraficas.Children.Add(graficaW);

        var panelDescarga = new StackPanel { Spacing = 14 };
        panelDescarga.Children.Add(panelInterrumpida);
        panelDescarga.Children.Add(Ui.Texto(Tx("Con el cargador desenchufado, mide cuánto dura la batería de verdad: autonomía real, consumo medio y caídas bruscas del porcentaje (celdas en mal estado). Mientras dura, la pantalla no se apaga y el equipo no se suspende. Cierra los demás programas para que no falseen el consumo."), "subtitulo"));
        panelDescarga.Children.Add(panelOpciones);
        textoEleccion.IsVisible = false;
        panelDescarga.Children.Add(textoEleccion);
        panelDescarga.Children.Add(Ui.Botones(botonEmpezar, botonParar, botonOtra));
        panelDescarga.Children.Add(textoDescarga);
        panelDescarga.Children.Add(textoDetalle);
        panelDescarga.Children.Add(panelGraficas);
        panelDescarga.Children.Add(panelResultadoDescarga);
        panelDescarga.Children.Add(panelModos);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba de descarga"), panelDescarga));
        relojDescarga.Tick += async (_, _) => await MuestrearDescarga();
        ActualizarParada();
        PintarModos();
        _ = ComprobarBrillo();
        RecuperarInterrumpida();

        pasoDesenchufar = Ui.Hundido(textoDesenchufar);
        pasoEnchufar = Ui.Hundido(textoEnchufar);
        panelCargador.Children.Add(Ui.Texto(Tx("Sigue los pasos. Así se comprueban el cargador, su cable y el conector del equipo."), "subtitulo"));
        panelCargador.Children.Add(pasoDesenchufar);
        panelCargador.Children.Add(pasoEnchufar);
        panelCargador.Children.Add(textoCargadorNota);
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Prueba del cargador"), panelCargador));

        cuerpo.Children.Add(new BarraVeredicto("bateria", Tx("¿La batería y el cargador funcionan bien?")));
        Content = pagina;

        reloj.Tick += async (_, _) => await Refrescar();
        AttachedToVisualTree += async (_, _) =>
        {
            reloj.Start();
            if (Environment.GetEnvironmentVariable("BANCADA_PROBAR_VIDEO") == "1") ProbarVideo();
            await Refrescar();
        };
        DetachedFromVisualTree += (_, _) => reloj.Stop();
    }

    /// <summary>
    /// BANCADA_PROBAR_VIDEO=1: abre la ventana del vídeo sin prueba de descarga (no hace falta desenchufar) y
    /// a los 45 segundos (más de una vuelta del vídeo, que dura 20) deja en la carpeta temporal «Bancada/video.txt» con qué se reproduce y si va bien.
    /// </summary>
    static async void ProbarVideo()
    {
        var ventana = new VentanaVideo(await Task.Run(ReproductorVideo.Archivo));
        ventana.Show();
        await Task.Delay(45000);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "Bancada", "video.txt"),
            $"motor={ventana.Motor}\nproblema={ventana.Problema ?? "ninguno"}\nvueltas={ventana.Vueltas}\n");
    }

    async Task Refrescar()
    {
        var e = await Task.Run(Energia.Leer);
        if (!e.HayBateria)
        {
            await Sesion.EsperarEquipoAsync();
            MostrarSinBateria();
            return;
        }
        PintarBaterias(e);
        PintarAhora(e);
        ComprobarCargador(e);
        ultimo = e;
        if (!descargando)
        {
            botonEmpezar.IsEnabled = !e.Enchufado;
            if (registro == null) // sin pisar el resultado de una prueba ya hecha
                textoDescarga.Text = e.Enchufado ? Tx("Desenchufa el cargador para poder empezar.") : Tx("Listo. Elige las opciones y pulsa «Empezar la prueba».");
        }
    }

    // ---------- Opciones de la prueba de descarga ----------

    static RadioButton Opcion(string titulo, string explicacion) => new()
    {
        GroupName = "modoDescarga",
        Content = new StackPanel
        {
            Spacing = 2,
            Children = { Ui.Texto(titulo, "valor negrita"), Ui.Texto(explicacion, "etiqueta") }
        }
    };

    static Control Bloque(string titulo, Control contenido) =>
        new StackPanel { Spacing = 8, Children = { Ui.Texto(titulo.ToUpperInvariant(), "seccion"), contenido } };

    static ComboBox Combo(IEnumerable<string> textos, int elegido)
    {
        var c = new ComboBox { MinWidth = 160 };
        foreach (var t in textos) c.Items.Add(new ComboBoxItem { Content = t });
        c.SelectedIndex = elegido;
        return c;
    }

    static Control FilaParada(RadioButton radio, ComboBox combo, string explicacion)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { radio, combo } };
        var nota = Ui.Texto(explicacion, "etiqueta");
        nota.Margin = new Thickness(28, 0, 0, 0);
        return new StackPanel { Spacing = 2, Children = { fila, nota } };
    }

    ModoDescarga ModoElegido() => (ModoDescarga)Math.Max(0, Array.FindIndex(radiosModo, r => r.IsChecked == true));

    void ActualizarParada()
    {
        comboTiempo.IsEnabled = porTiempo.IsChecked == true;
        comboPct.IsEnabled = porPorcentaje.IsChecked == true;
        var pct = Porcentajes[Math.Max(0, comboPct.SelectedIndex)];
        notaPct.Text = porPorcentaje.IsChecked == true && pct <= 5
            ? Tx("Ojo: al 5 % algunos equipos hibernan o se apagan por su cuenta antes de que la prueba termine. Si pasa, al volver a abrir Bancada verás el resultado hasta donde llegó.")
            : "";
        notaPct.IsVisible = notaPct.Text.Length > 0;
    }

    async Task ComprobarBrillo()
    {
        if (await Task.Run(Brillo.Leer) != null) return;
        comboBrillo.SelectedIndex = 0;
        comboBrillo.IsEnabled = false;
        notaBrillo.Text = Tx("Bancada no puede cambiar el brillo de esta pantalla (pasa con monitores externos y máquinas virtuales): se usará el que tenga. Ponlo a mano a la mitad para que el resultado se pueda comparar.");
    }

    void GuardarOpciones()
    {
        Ajustes.Guardar("bateria.modo", ModoElegido().ToString());
        Ajustes.Guardar("bateria.parada", porPorcentaje.IsChecked == true ? $"p:{comboPct.SelectedIndex}" : $"t:{comboTiempo.SelectedIndex}");
        if (comboBrillo.IsEnabled) Ajustes.Guardar("bateria.brillo", comboBrillo.SelectedIndex.ToString());
    }

    static string NombreModo(ModoDescarga m) => m switch
    {
        ModoDescarga.Reposo => Tx("Reposo"),
        ModoDescarga.Navegacion => Tx("Navegación"),
        ModoDescarga.Video => Tx("Vídeo"),
        _ => Tx("Carga máxima")
    };

    static string UsoModo(ModoDescarga m) => m switch
    {
        ModoDescarga.Reposo => Tx("en reposo con la pantalla encendida"),
        ModoDescarga.Navegacion => Tx("navegando por Internet"),
        ModoDescarga.Video => Tx("viendo vídeo"),
        _ => Tx("a carga máxima")
    };

    // ---------- Prueba de descarga ----------

    async void EmpezarDescarga()
    {
        if (descargando || enMarcha) return;
        botonEmpezar.IsEnabled = false;
        try
        {
            var e = await Task.Run(Energia.Leer);
            if (e.Enchufado || !e.HayBateria)
            {
                textoDescarga.Text = Tx("Desenchufa el cargador para poder empezar.");
                return;
            }
            var modo = ModoElegido();
            duracionDescarga = porTiempo.IsChecked == true ? Duraciones[Math.Max(0, comboTiempo.SelectedIndex)] : null;
            objetivoPct = duracionDescarga == null ? Porcentajes[Math.Max(0, comboPct.SelectedIndex)] : null;
            if (objetivoPct is int o && e.CargaPct <= o)
            {
                textoDescarga.Text = Tf("La batería ya está al {0} %: elige un porcentaje más bajo o para por tiempo.", e.CargaPct);
                return;
            }
            if (duracionDescarga != null && e.CargaPct <= SueloSeguridad + 3)
            {
                textoDescarga.Text = Tx("La batería está casi vacía: cárgala un poco antes de hacer la prueba.");
                return;
            }

            string? archivoVideo = null;
            if (modo == ModoDescarga.Navegacion)
            {
                textoDescarga.Text = Tx("Comprobando la conexión a Internet…");
                if (!await HayInternet())
                {
                    textoDescarga.Text = Tx("El modo navegación necesita conexión a Internet. Conéctate a una red Wi‑Fi o elige otro modo.");
                    return;
                }
            }
            else if (modo == ModoDescarga.Video)
            {
                textoDescarga.Text = Tx("Preparando el vídeo…");
                try { archivoVideo = await Task.Run(ReproductorVideo.Archivo); }
                catch (Exception ex)
                {
                    textoDescarga.Text = Tx("No se ha podido preparar el vídeo.") + " " + ex.Message;
                    return;
                }
            }
            GuardarOpciones();

            // Brillo fijo durante la prueba; se recuerda el que había para dejarlo igual.
            int? brilloAntes = await Task.Run(Brillo.Leer), brillo = brilloAntes;
            bool restaurar = false;
            if (Brillos[Math.Max(0, comboBrillo.SelectedIndex)] is int b && brilloAntes != null && await Task.Run(() => Brillo.Fijar(b)))
            {
                brillo = b;
                restaurar = true;
            }

            registro = new RegistroDescarga
            {
                Inicio = DateTime.Now, Modo = modo, Brillo = brillo, BrilloAntes = restaurar ? brilloAntes : null,
                LlenaWh = e.LlenaWh, DisenoWh = e.DisenoWh
            };
            descargando = enMarcha = true;
            PruebasEnCurso.Entrar();
            // Durante la prueba las opciones se recogen en una línea: así se ve lo que importa sin desplazarse.
            panelOpciones.IsVisible = false;
            textoEleccion.Text = string.Join(" · ", NombreModo(modo),
                duracionDescarga is TimeSpan dt ? Tf("durante {0}", Ui.Duracion(dt)) : Tf("hasta el {0} %", objetivoPct),
                brillo is int bv ? Tf("brillo {0} %", bv) : Tx("brillo sin cambiar"));
            textoEleccion.IsVisible = true;
            botonEmpezar.IsVisible = false;
            botonParar.IsVisible = true;
            botonOtra.IsVisible = false;
            panelInterrumpida.Children.Clear();
            panelResultadoDescarga.Children.Clear();
            graficaPct.Vaciar();
            graficaW.Vaciar();
            panelGraficas.IsVisible = true;
            despierto = new MantenerDespierto(Tx("Prueba de descarga de la batería"));
            switch (modo)
            {
                case ModoDescarga.Navegacion:
                    navegacion = new CargaNavegacion();
                    navegacion.Iniciar();
                    break;
                case ModoDescarga.Maxima:
                    maxima = new CargaMaxima();
                    maxima.Iniciar();
                    break;
                case ModoDescarga.Video:
                    video = new VentanaVideo(archivoVideo!);
                    video.CerradaAMano += () =>
                    {
                        video = null;
                        TerminarDescarga(Tx("Has cerrado la ventana del vídeo: prueba detenida."));
                    };
                    video.Show();
                    break;
            }
            if (await Task.Run(Energia.AhorroActivo) == true) registro.AhorroAl = e.CargaPct;

            cronoDescarga.Restart();
            lecturas = 0;
            Muestra(e);
            textoDescarga.Text = Tx("En marcha. La primera lectura llega en 10 segundos.");
            textoDetalle.Text = "";
            relojDescarga.Start();
        }
        finally
        {
            if (!descargando) botonEmpezar.IsEnabled = ultimo is { Enchufado: false };
        }
    }

    static async Task<bool> HayInternet()
    {
        try
        {
            using var http = Conectividad.NuevoHttp(8);
            using var r = await http.GetAsync("https://www.google.com/generate_204");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    void Muestra(EstadoEnergia e)
    {
        if (registro == null) return;
        ultimo = e;
        registro.Muestras.Add(new MuestraDescarga(cronoDescarga.Elapsed, e.CargaPct, e.RestanteWh, e.PotenciaW));
        registro.Guardar();
        PintarGraficas(registro);
    }

    void PintarGraficas(RegistroDescarga r)
    {
        graficaPct.Fijar(0, r.Muestras.Where(x => x.Pct != null).Select(x => (double)x.Pct!.Value).ToList());
        var vatios = r.Muestras.Where(x => x.W != null).Select(x => Math.Max(0, -x.W!.Value)).ToList();
        graficaW.Fijar(0, vatios);
        graficaW.IsVisible = vatios.Count > 0;
        panelGraficas.Children[2].IsVisible = vatios.Count > 0;
    }

    async Task MuestrearDescarga()
    {
        if (!descargando || registro == null) return;
        var e = await Task.Run(Energia.Leer);
        if (!descargando) return;
        if (e.Enchufado)
        {
            TerminarDescarga(Tx("Has enchufado el cargador: prueba detenida."));
            return;
        }
        Muestra(e);
        lecturas++;
        if (lecturas % 6 == 0 && registro.AhorroAl == null && await Task.Run(Energia.AhorroActivo) == true)
            registro.AhorroAl = e.CargaPct;
        if (!descargando) return;

        var t = cronoDescarga.Elapsed;
        var progreso = duracionDescarga is TimeSpan d
            ? Tf("{0} de {1}", Ui.Duracion(t), Ui.Duracion(d))
            : Tf("{0}, hasta el {1} %", Ui.Duracion(t), objetivoPct);
        textoDescarga.Text = Tf("En marcha · {0} · batería {1} % → {2} %", progreso, registro.Muestras[0].Pct, e.CargaPct) +
                             (e.PotenciaW is double w && w < 0 ? $" · {Formato.N(-w, "0.0")} W" : "");
        textoDetalle.Text = string.Join(" ", new[] { Estimacion(e, t), Actividad() }.Where(x => x.Length > 0));

        if (video?.Problema is string pv) TerminarDescarga(Tx("El vídeo no se puede reproducir, así que la prueba no mediría lo que dice:") + " " + pv);
        else if (duracionDescarga is TimeSpan dd && t >= dd) TerminarDescarga(null);
        else if (objetivoPct is int o && e.CargaPct <= o) TerminarDescarga(null);
        else if (duracionDescarga != null && e.CargaPct <= SueloSeguridad)
            TerminarDescarga(Tf("La batería ha llegado al {0} %: prueba detenida para que el equipo no se apague.", SueloSeguridad));
    }

    /// <summary>Cuánto falta, con el consumo de los últimos 5 minutos.</summary>
    string Estimacion(EstadoEnergia e, TimeSpan t)
    {
        if (registro == null) return "";
        var recientes = registro.Muestras.Where(x => x.T >= t - TimeSpan.FromMinutes(5) && x.W < -0.3).Select(x => -x.W!.Value).ToList();
        double? vatios = recientes.Count > 0 ? recientes.Average() : null;
        // Puntos de porcentaje por hora: por el consumo si se sabe; si no, por lo que ha bajado hasta ahora.
        double? ritmo = vatios is double v && e.LlenaWh > 0 ? v / e.LlenaWh.Value * 100 :
                        t > TimeSpan.FromMinutes(10) && registro.Muestras[0].Pct - e.CargaPct is int bajado && bajado >= 2 ? bajado / t.TotalHours : null;

        if (duracionDescarga is TimeSpan d)
        {
            var falta = d - t;
            var texto = Tf("Faltan {0}.", Ui.Duracion(falta));
            if (ritmo is double r && e.CargaPct is int p)
                texto += " " + Tf("Terminará con la batería en torno al {0} %.", Math.Max(0, (int)Math.Round(p - r * falta.TotalHours)));
            return texto;
        }
        if (objetivoPct is int o && e.CargaPct is int pc)
            return ritmo is double r2 && r2 > 0.1
                ? Tf("Llegará al {0} % en unas {1}.", o, Ui.Duracion(TimeSpan.FromHours((pc - o) / r2)))
                : Tx("Calculando cuánto falta…");
        return "";
    }

    /// <summary>Lo que está haciendo el equipo ahora, para que el técnico vea que la carga funciona.</summary>
    string Actividad()
    {
        if (navegacion != null)
            return Tf("Páginas abiertas: {0}.", navegacion.Cargadas) +
                   (navegacion.Fallidas > 0 ? " " + Tf("{0} no han respondido (normal si alguna web va lenta).", navegacion.Fallidas) : "");
        if (maxima != null)
        {
            var g = maxima.Graficas;
            return g.Count > 0 ? Tf("Trabajando: procesador y {0}.", string.Join(", ", g))
                               : Tx("Trabajando: procesador (la gráfica no se puede cargar porque no tiene OpenCL).");
        }
        if (video != null)
            return Tf("Vídeo reproduciéndose con {0}.", video.Motor) +
                   (video.Vueltas > 0 ? " " + Tf("Ha dado {0} vueltas.", video.Vueltas) : "");
        return "";
    }

    void TerminarDescarga(string? motivo)
    {
        if (!descargando || registro == null) return;
        descargando = enMarcha = false;
        PruebasEnCurso.Salir();
        relojDescarga.Stop();
        navegacion?.Dispose();
        navegacion = null;
        maxima?.Dispose();
        maxima = null;
        video?.Terminar();
        video = null;
        despierto?.Dispose();
        despierto = null;
        if (registro.BrilloAntes is int b) _ = Task.Run(() => Brillo.Fijar(b));
        registro.Terminada = true;
        registro.Guardar();

        botonParar.IsVisible = false;
        botonOtra.IsVisible = true;
        botonEmpezar.IsEnabled = ultimo is { Enchufado: false };

        var r = Descarga.Analizar(registro.Muestras, registro.LlenaWh, registro.DisenoWh, registro.Modo != ModoDescarga.Maxima);
        textoDescarga.Text = motivo ?? Tf("Terminada ({0}).", Ui.Duracion(r.Duracion));
        textoDetalle.Text = "";
        PintarResultadoDescarga(r, registro, panelResultadoDescarga);
    }

    /// <summary>Si la última prueba no terminó (se apagó el equipo o se cerró Bancada), enseña hasta dónde llegó.</summary>
    void RecuperarInterrumpida()
    {
        if (enMarcha) return;
        var previo = RegistroDescarga.Leer();
        if (previo is not { Terminada: false }) return;
        previo.Terminada = true;
        previo.Guardar();
        if (previo.BrilloAntes is int b) _ = Task.Run(() => Brillo.Fijar(b));
        if (previo.Muestras.Count < 2) return;

        var ultima = previo.Muestras[^1];
        panelInterrumpida.Children.Add(Ui.Aviso(Tf("La prueba de descarga del {0} no terminó: el equipo se apagó o Bancada se cerró con la batería al {1}\u00A0%, {2} después de empezar. Esto es lo que dio hasta ese momento.",
            previo.Inicio.ToString("dd/MM HH:mm"), ultima.Pct?.ToString() ?? "—", Ui.Duracion(ultima.T))));
        var r = Descarga.Analizar(previo.Muestras, previo.LlenaWh, previo.DisenoWh, previo.Modo != ModoDescarga.Maxima);
        var resultado = new StackPanel { Spacing = 10 };
        PintarResultadoDescarga(r, previo, resultado);
        panelInterrumpida.Children.Add(resultado);
    }

    void PintarResultadoDescarga(ResultadoDescarga r, RegistroDescarga reg, StackPanel destino)
    {
        destino.Children.Clear();
        if (r.Duracion < TimeSpan.FromMinutes(2))
        {
            destino.Children.Add(Ui.Texto(Tx("Ha durado muy poco para sacar conclusiones: deja al menos 10 minutos."), "etiqueta"));
            return;
        }
        int? gastado = r.PctInicio is int a && r.PctFin is int f ? a - f : null;
        destino.Children.Add(Ui.Columnas(
            Ui.Cifra(Tx("Autonomía real"), r.Autonomia is TimeSpan t ? Formato.N(t.TotalHours, "0.0") : "—", "h"),
            Ui.Cifra(Tx("Consumo medio"), r.PotenciaMedia is double w ? Formato.N(w, "0.0") : "—", "W"),
            Ui.Cifra(Tx("Batería gastada"), gastado?.ToString() ?? "—", "%")));

        var uso = UsoModo(reg.Modo);
        if (r.Autonomia is TimeSpan au)
            destino.Children.Add(Ui.Texto(
                Tf("Con la batería llena y {0}, daría para unas {1}.", uso, Ui.Duracion(au)) +
                (r.AutonomiaNueva is TimeSpan nu && nu - au > TimeSpan.FromMinutes(5) ? Tf(" Nueva habría dado {0}.", Ui.Duracion(nu)) : ""), "valor"));

        var condiciones = Tf("Condiciones: {0}, brillo {1}, {2}.", NombreModo(reg.Modo).ToLowerInvariant(),
            reg.Brillo is int br ? $"{br} %" : Tx("desconocido"), reg.Inicio.ToString("dd/MM HH:mm"));
        destino.Children.Add(Ui.Texto(condiciones, "etiqueta"));
        if (reg.AhorroAl is int ah)
            destino.Children.Add(Ui.Texto(Tf("El ahorro de batería del sistema se activó al {0} %: desde ahí el equipo gasta menos, así que la autonomía sale algo más larga que con un uso normal.", ah), "etiqueta"));

        var graves = r.Avisos.Where(x => x.v is Veredicto.Aviso or Veredicto.Fallo).ToList();
        if (graves.Count == 0)
            destino.Children.Add(Ui.Chip(Tx("Se descarga de forma normal, sin caídas bruscas"), Veredicto.Ok));
        foreach (var (_, texto) in graves) destino.Children.Add(Ui.Aviso(texto));
        foreach (var (_, texto) in r.Avisos.Where(x => x.v == Veredicto.Pendiente))
            destino.Children.Add(Ui.Texto(texto, "etiqueta"));

        Sesion.Anotar("bateria", Tf("Descarga ({0})", NombreModo(reg.Modo)),
            $"{Ui.Duracion(r.Duracion)} {uso}: {r.PctInicio} % → {r.PctFin} %" +
            (reg.Brillo is int bb ? Tf(", brillo {0} %", bb) : "") +
            (r.PotenciaMedia is double pm ? Tf(", consumo medio {0} W", Formato.N(pm, "0.0")) : "") +
            (r.Autonomia is TimeSpan ar ? Tf(", autonomía real {0}", Ui.Duracion(ar)) : ""));
        Sesion.Anotar("bateria", Tx("Aviso de la descarga"), graves.Select(x => x.texto).ToArray());
        if (graves.Any(x => x.v == Veredicto.Fallo) && Sesion.Obtener("bateria") == Veredicto.Pendiente)
            Sesion.Fijar("bateria", Veredicto.Fallo);

        if (r.Autonomia is TimeSpan am && r.Duracion >= TimeSpan.FromMinutes(5))
        {
            autonomiaPorModo[reg.Modo] = am;
            PintarModos();
        }
    }

    /// <summary>Resumen de la autonomía medida con cada tipo de uso en esta sesión.</summary>
    void PintarModos()
    {
        panelModos.Children.Clear();
        if (autonomiaPorModo.Count == 0) return;
        panelModos.Children.Add(Ui.Texto(Tx("Autonomía con la batería llena, por tipo de uso").ToUpperInvariant(), "seccion"));
        var datos = Enum.GetValues<ModoDescarga>().Where(autonomiaPorModo.ContainsKey)
            .Select(m => Ui.Dato(NombreModo(m), Ui.Duracion(autonomiaPorModo[m]))).ToArray();
        panelModos.Children.Add(Ui.Columnas(datos));
        if (autonomiaPorModo.Count < 4)
            panelModos.Children.Add(Ui.Texto(Tx("Haz la prueba con otros tipos de uso para completar el resumen. Todos salen en el informe."), "etiqueta"));
        Sesion.Anotar("bateria", Tx("Autonomía por uso"),
            string.Join(" · ", Enum.GetValues<ModoDescarga>().Where(autonomiaPorModo.ContainsKey)
                .Select(m => $"{NombreModo(m)} {Ui.Duracion(autonomiaPorModo[m])}")));
    }

    void MostrarSinBateria()
    {
        if (sinBateriaMostrado) return;
        sinBateriaMostrado = true;
        reloj.Stop();
        cuerpo.Children.RemoveRange(1, cuerpo.Children.Count - 1);
        var escritorio = Sesion.Equipo?.Tipo is TipoEquipo.Sobremesa or TipoEquipo.MiniPc or TipoEquipo.TodoEnUno;
        cuerpo.Children.Add(Ui.Tarjeta(Tx("Batería"),
            Ui.Chip(escritorio ? Tx("Este equipo no tiene batería") : Tx("No se ha encontrado ninguna batería"),
                escritorio || Sesion.Equipo?.EsVirtual == true ? Veredicto.Pendiente : Veredicto.Fallo),
            Ui.Texto(escritorio
                ? Tx("Es lo normal en un sobremesa. Esta prueba no aplica.")
                : Tx("Si es un portátil, la batería puede estar desconectada por dentro, agotada del todo o averiada."), "etiqueta")));
        cuerpo.Children.Add(new BarraVeredicto("bateria", Tx("¿La batería y el cargador funcionan bien?")));
        if (escritorio && Sesion.Obtener("bateria") == Veredicto.Pendiente) Sesion.Fijar("bateria", Veredicto.NoAplica);
    }

    void PintarBaterias(EstadoEnergia e)
    {
        panelBaterias.Children.Clear();
        foreach (var b in e.Baterias)
        {
            var titulo = string.Join(" · ", new[] { b.Fabricante, b.Nombre, b.Quimica }.Where(x => x.Length > 0).Distinct());
            var salud = b.SaludPct is int s
                ? Ui.Chip(Tf("Salud {0} %", s), s >= 80 ? Veredicto.Ok : s >= 60 ? Veredicto.Aviso : Veredicto.Fallo)
                : Ui.Chip(Tx("Salud no disponible"), Veredicto.Pendiente);
            var datos = new List<Control>();
            if (b.DisenoWh is double d) datos.Add(Ui.Dato(Tx("Capacidad de fábrica"), $"{Formato.N(d, "0.0")} Wh"));
            if (b.LlenaWh is double l) datos.Add(Ui.Dato(Tx("Capacidad actual"), $"{Formato.N(l, "0.0")} Wh"));
            datos.Add(Ui.Dato(Tx("Ciclos de carga"), b.Ciclos?.ToString() ?? Tx("No disponible")));

            var p = new StackPanel { Spacing = 12 };
            p.Children.Add(Ui.Fila(Ui.Texto(titulo, "valor negrita"), salud));
            p.Children.Add(Ui.Columnas(datos.ToArray()));
            if (b.SaludPct is int sp)
                p.Children.Add(Ui.Texto(
                    sp >= 80 ? Tx("Desgaste normal.") :
                    sp >= 60 ? Tx("Batería gastada: dura bastante menos que nueva. Se puede seguir usando.") :
                    Tx("Batería muy gastada: conviene cambiarla."), "etiqueta"));
            panelBaterias.Children.Add(Ui.Hundido(p));

            Sesion.Anotar("bateria", Tx("Batería"), titulo);
            if (b.SaludPct is int sa)
                Sesion.Anotar("bateria", Tx("Salud"), Tf("{0} % ({1} de {2} Wh)", sa, Formato.N(b.LlenaWh ?? 0, "0.0"), Formato.N(b.DisenoWh ?? 0, "0.0")));
            if (b.Ciclos is int c) Sesion.Anotar("bateria", Tx("Ciclos"), c.ToString());
        }
    }

    void PintarAhora(EstadoEnergia e)
    {
        textoEstado.Text = e.Cargando ? Tx("Enchufado y cargando") :
                           e.Enchufado ? (e.CargaPct >= 95 ? Tx("Enchufado, carga completa") : Tx("Enchufado, sin cargar")) :
                           Tx("Funcionando con batería");
        textoCarga.Text = e.CargaPct?.ToString() ?? "—";
        tituloPotencia.Text = (e.PotenciaW > 0.3 ? Tx("Cargando a") : e.PotenciaW < -0.3 ? Tx("Consumo") : Tx("Potencia")).ToUpperInvariant();
        textoPotencia.Text = e.PotenciaW is double w ? Formato.N(Math.Abs(w), "0.0") : "—";
        textoVoltaje.Text = e.VoltajeV is double v ? Formato.N(v, "0.00") : "—";
        textoTiempo.Text = e.Restante is TimeSpan t ? Ui.Duracion(t) : "—";
        if (e.PotenciaW is double pw) grafica.Anadir(0, Math.Abs(pw));

        if (!e.Enchufado && e.PotenciaW is < -0.5 && e.LlenaWh is > 0)
        {
            var completa = TimeSpan.FromHours(e.LlenaWh.Value / -e.PotenciaW.Value);
            textoAutonomia.Text = Tf("Con este consumo, la batería llena daría para unas {0}. Depende mucho del brillo y de lo que estés haciendo.", Ui.Duracion(completa));
            Sesion.Anotar("bateria", Tx("Autonomía estimada"), Tf("{0} con batería llena a {1} W", Ui.Duracion(completa), Formato.N(-e.PotenciaW.Value, "0.0")));
        }
        else if (e.Enchufado)
        {
            textoAutonomia.Text = Tx("Desenchufa el cargador para ver cuánto consume el equipo y cuánto aguantaría la batería.") +
                (!e.Cargando && e.CargaPct < 95
                    ? Tx(" Enchufado y sin cargar suele significar que hay un límite de carga activado para cuidar la batería (por ejemplo, parar al 80 %).")
                    : "");
        }
    }

    void ComprobarCargador(EstadoEnergia e)
    {
        if (enchufadoAntes == true && !e.Enchufado) desenchufadoVisto = true;
        if (enchufadoAntes == null && !e.Enchufado) desenchufadoVisto = true; // ya estaba desenchufado
        if (desenchufadoVisto && enchufadoAntes == false && e.Enchufado)
        {
            reenchufadoVisto = true;
            momentoReenchufe = DateTime.Now;
        }
        enchufadoAntes = e.Enchufado;

        if (desenchufadoVisto)
        {
            pasoDesenchufar.Classes.Add("ok-caja");
            textoDesenchufar.Text = Tx("✓ 1. Cargador desenchufado: el equipo funciona con la batería");
        }
        if (reenchufadoVisto)
        {
            textoEnchufar.Text = Tx("✓ 2. Cargador detectado al enchufarlo");
            if (e.Cargando || e.CargaPct >= 95)
            {
                textoCargadorNota.Text = e.Cargando ? (e.PotenciaW > 0.3 ? Tf("La batería está cargando a {0} W. El cargador funciona.", Formato.N(e.PotenciaW!.Value, "0")) : Tx("La batería está cargando. El cargador funciona.")) : Tx("La batería ya está llena. El cargador funciona.");
                Sesion.Anotar("bateria", Tx("Cargador"), Tx("detectado al enchufar y carga la batería"));
            }
            else if (momentoReenchufe is DateTime m && DateTime.Now - m > TimeSpan.FromSeconds(12))
            {
                textoCargadorNota.Text = Tx("El cargador se detecta, pero la batería no carga. Puede ser un cargador de poca potencia, un límite de carga configurado (por ejemplo, parar al 80 %) o una batería que ya no admite carga.");
                Sesion.Anotar("bateria", Tx("Cargador"), Tx("detectado, pero la batería no carga"));
            }
            else textoCargadorNota.Text = Tx("Esperando a que empiece a cargar…");
        }
        else if (desenchufadoVisto)
        {
            textoCargadorNota.Text = Tx("Ahora vuelve a enchufar el cargador.");
        }
    }
}
