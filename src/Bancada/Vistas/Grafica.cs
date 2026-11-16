using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Bancada.Vistas;

/// <summary>Gráfica de líneas sencilla que avanza en el tiempo (lo más reciente a la derecha).</summary>
internal sealed class Grafica : Control
{
    public sealed record Serie(string Nombre, Color Color)
    {
        public List<double> Valores { get; } = new();
    }

    public List<Serie> Series { get; } = new();
    public double? Minimo { get; set; }
    public double? Maximo { get; set; }
    public int Capacidad { get; set; } = 300;
    public string Unidad { get; set; } = "";

    public Grafica()
    {
        Height = 180;
    }

    public void Anadir(int serie, double valor)
    {
        var v = Series[serie].Valores;
        v.Add(valor);
        if (v.Count > Capacidad) v.RemoveRange(0, v.Count - Capacidad);
        InvalidateVisual();
    }

    /// <summary>Sustituye una serie entera. Si hay muchos puntos se reparten a partes iguales, así cabe una prueba de horas.</summary>
    public void Fijar(int serie, IReadOnlyList<double> valores, int maximo = 600)
    {
        var v = Series[serie].Valores;
        v.Clear();
        if (valores.Count <= maximo) v.AddRange(valores);
        else for (int i = 0; i < maximo; i++) v.Add(valores[(int)((long)i * (valores.Count - 1) / (maximo - 1))]);
        Capacidad = Math.Max(2, Series.Max(s => s.Valores.Count));
        InvalidateVisual();
    }

    public void Vaciar()
    {
        foreach (var s in Series) s.Valores.Clear();
        InvalidateVisual();
    }

    IBrush Pincel(string clave, IBrush porDefecto) =>
        this.TryFindResource(clave, ActualThemeVariant, out var r) && r is IBrush b ? b : porDefecto;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        const double margenIzq = 46, margenAbajo = 6, margenArriba = 8;
        ctx.FillRectangle(Pincel("FondoHundido", Brushes.LightGray), new Rect(0, 0, w, h), 8);

        var todos = Series.SelectMany(s => s.Valores).ToList();
        double min = Minimo ?? (todos.Count > 0 ? todos.Min() : 0);
        double max = Maximo ?? (todos.Count > 0 ? todos.Max() : 10);
        if (Minimo == null) min = Math.Max(0, Math.Floor(min - (max - min) * 0.1));
        if (Maximo == null) max = Math.Ceiling(Math.Max(max * 1.15, min + 10));
        if (max - min < 1e-9) max = min + 1;

        var areaH = h - margenAbajo - margenArriba;
        var areaW = w - margenIzq - 10;
        double Y(double v) => margenArriba + areaH * (1 - (v - min) / (max - min));

        var tinta = Pincel("TintaSuave", Brushes.Gray);
        var linea = new Pen(Pincel("Borde", Brushes.Gray), 1);
        var tf = new Typeface(FontFamily.Default);
        for (int i = 0; i <= 4; i++)
        {
            var v = min + (max - min) * i / 4;
            var y = Y(v);
            ctx.DrawLine(linea, new Point(margenIzq, y), new Point(w - 10, y));
            var t = new FormattedText($"{Modelo.Formato.N(v, max - min < 10 ? "0.#" : "0")}{Unidad}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, tinta);
            ctx.DrawText(t, new Point(margenIzq - 8 - t.Width, y - t.Height / 2));
        }

        foreach (var s in Series)
        {
            if (s.Valores.Count < 2) continue;
            double paso = areaW / (Capacidad - 1);
            double x0 = margenIzq + areaW - paso * (s.Valores.Count - 1);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(x0, Y(s.Valores[0])), false);
                for (int i = 1; i < s.Valores.Count; i++) g.LineTo(new Point(x0 + paso * i, Y(s.Valores[i])));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(s.Color), 2.2, lineJoin: PenLineJoin.Round), geo);
        }
    }
}
