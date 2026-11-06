using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mantel.UI;

/// <summary>
/// A tiny history graph: the last N samples as a line with a soft fill underneath, in the
/// theme's accent. Draws itself; no layout work beyond its fixed size.
/// </summary>
public sealed class Sparkline : Control
{
    private readonly double[] _samples;
    private int _count;
    private int _next;

    public Sparkline(int capacity = 60, double height = 34)
    {
        _samples = new double[capacity];
        Height = height;
        ClipToBounds = true;
    }

    /// <summary>Fixed top of the scale (1.0 for percentages). Null = scale to the biggest sample.</summary>
    public double? Max { get; set; } = 1.0;

    public void Push(double value)
    {
        if (_count == 0)
        {
            // Start as a flat line at the first reading, so a fresh graph isn't an empty gap.
            Array.Fill(_samples, double.IsFinite(value) ? Math.Max(0, value) : 0);
            _count = _samples.Length;
            _next = 0;
            InvalidateVisual();
            return;
        }
        _samples[_next] = double.IsFinite(value) ? Math.Max(0, value) : 0;
        _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (_count < 2 || w <= 0 || h <= 0) return;

        var accent = (this.FindResource("Mantel.Accent") as ISolidColorBrush)?.Color ?? Color.Parse("#E8A15A");
        var max = Max ?? Enumerable.Range(0, _count).Select(Sample).DefaultIfEmpty(0).Max();
        if (max <= 0) max = 1;

        var step = w / (_samples.Length - 1);
        var start = w - step * (_count - 1); // new samples come in from the right
        var points = new List<Point>(_count);
        for (var i = 0; i < _count; i++)
        {
            var v = Math.Min(Sample(i) / max, 1);
            points.Add(new Point(start + i * step, h - 1.5 - v * (h - 3)));
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false);
            foreach (var p in points.Skip(1)) g.LineTo(p);
            g.EndFigure(false);
        }

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, h), true);
            foreach (var p in points) g.LineTo(p);
            g.LineTo(new Point(points[^1].X, h));
            g.EndFigure(true);
        }

        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x55, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1),
            },
        };
        context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }

    /// <summary>Sample i in time order (0 = oldest kept).</summary>
    private double Sample(int i)
    {
        var oldest = _count < _samples.Length ? 0 : _next;
        return _samples[(oldest + i) % _samples.Length];
    }
}
