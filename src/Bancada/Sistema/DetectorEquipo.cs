using Bancada.Modelo;

namespace Bancada.Sistema;

public static class DetectorEquipo
{
    public static async Task<InfoEquipo> DetectarAsync()
    {
        var i = await Detectar();
        if (Opciones.Anonimo && i.NumeroSerie.Length > 0) i.NumeroSerie = "BNC0123456";
        return i;
    }

    static Task<InfoEquipo> Detectar() => Task.Run(() =>
    {
        try
        {
            if (OperatingSystem.IsWindows()) return DetectorWindows.Detectar();
            if (OperatingSystem.IsLinux()) return DetectorLinux.Detectar();
        }
        catch (Exception ex)
        {
            var fallo = new InfoEquipo();
            fallo.Notas.Add(Tx("No se pudo leer el equipo: ") + ex.Message);
            return fallo;
        }
        var otro = new InfoEquipo();
        otro.Notas.Add(Tx("Sistema operativo no compatible todavía."));
        return otro;
    });
}
