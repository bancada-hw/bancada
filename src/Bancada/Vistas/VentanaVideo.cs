using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Bancada.Sistema;

namespace Bancada.Vistas;

/// <summary>
/// Ventana con el vídeo en bucle de la prueba de batería. Va aparte para que el vídeo siga aunque el técnico
/// cambie de pantalla en Bancada. Si se cierra, la prueba se para (sin vídeo dejaría de medir lo que dice).
/// </summary>
internal sealed class VentanaVideo : Window
{
    readonly ReproductorVideo reproductor = ReproductorVideo.Crear();
    readonly DispatcherTimer vigilante = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool cerrandoDesdeFuera;

    /// <summary>El técnico ha cerrado la ventana.</summary>
    public event Action? CerradaAMano;

    public string Motor => reproductor.Motor;
    public string? Problema => reproductor.Problema;
    public int Vueltas => reproductor.Vueltas;

    public VentanaVideo(string archivo)
    {
        Title = Tx("Bancada · vídeo de la prueba de batería");
        Width = 960;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Avalonia.Media.Brushes.Black;
        var anfitrion = new AnfitrionVideo(reproductor, archivo);
        Content = anfitrion;
        SizeChanged += (_, _) => reproductor.Redimensionar();
        vigilante.Tick += (_, _) => reproductor.Vigilar();
        Opened += (_, _) => vigilante.Start();
        Closed += (_, _) =>
        {
            vigilante.Stop();
            reproductor.Dispose();
            if (!cerrandoDesdeFuera) CerradaAMano?.Invoke();
        };
    }

    /// <summary>Cierra la ventana al terminar la prueba (sin avisar de que la ha cerrado el técnico).</summary>
    public void Terminar()
    {
        cerrandoDesdeFuera = true;
        Close();
    }

    /// <summary>Hueco nativo (HWND o ventana X11) donde pinta el reproductor del sistema.</summary>
    sealed class AnfitrionVideo(ReproductorVideo reproductor, string archivo) : NativeControlHost
    {
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle padre)
        {
            var propia = base.CreateNativeControlCore(padre);
            // Se empieza cuando la ventana nativa ya existe y tiene tamaño.
            Dispatcher.UIThread.Post(() => reproductor.Empezar(propia.Handle, archivo), DispatcherPriority.Background);
            return propia;
        }
    }
}
