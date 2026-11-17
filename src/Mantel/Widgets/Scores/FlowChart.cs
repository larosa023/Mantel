using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mantel.Widgets.Scores;

/// <summary>
/// The game's flow: the score margin through the game. Above the middle line the home team leads
/// (in its color), below it the away team. Faint lines mark the periods; for a live game the line
/// stops where the game is now.
/// </summary>
internal sealed class FlowChart : Control
{
    private readonly IReadOnlyList<(double T, int M)> _points;
    private readonly double _length;
    private readonly double _periodLength;
    private readonly int _periodCount;
    private readonly Color _home;
    private readonly Color _away;

    public FlowChart(IReadOnlyList<(double T, int M)> points, double length, double periodLength, int periodCount, Color home, Color away)
    {
        _points = points;
        _length = Math.Max(1, length);
        _periodLength = periodLength;
        _periodCount = periodCount;
        _home = Visible(home);
        _away = Visible(away);
        // Two teams with near-identical colors would read as one; push the away side to gray.
        if (Distance(_home, _away) < 60) _away = Color.Parse("#9A9A9A");
    }

    /// <summary>Team colors are picked for jerseys, not dark cards: lift the very dark ones.</summary>
    private static Color Visible(Color c)
    {
        var lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return lum < 0.18 ? Color.Parse("#B8B8B8") : c;
    }

    private static double Distance(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var mid = h / 2;

        var lineColor = this.TryFindResource("Mantel.TextMuted", ActualThemeVariant, out var r) && r is ISolidColorBrush b
            ? b.Color : Colors.Gray;
        var faint = new Pen(new SolidColorBrush(lineColor, 0.25), 1);

        // Period markers, then the even line.
        var t = 0.0;
        for (var p = 1; t + 1 < _length; p++)
        {
            t += p <= _periodCount ? _periodLength : 300;
            if (t >= _length) break;
            var x = Math.Round(t / _length * w) + 0.5;
            ctx.DrawLine(faint, new Point(x, 2), new Point(x, h - 2));
        }
        ctx.DrawLine(new Pen(new SolidColorBrush(lineColor, 0.45), 1), new Point(0, Math.Round(mid) + 0.5), new Point(w, Math.Round(mid) + 0.5));

        if (_points.Count < 2) return;
        var max = Math.Max(10, _points.Max(p => Math.Abs(p.M)));
        Point At((double T, int M) p) => new(Math.Clamp(p.T / _length, 0, 1) * w, mid - p.M / (double)max * (mid - 3));

        // The area between the line and the middle, as one shape, painted twice through clips:
        // the top half in the home color, the bottom half in the away color.
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            var first = At(_points[0]);
            g.BeginFigure(new Point(first.X, mid), true);
            foreach (var p in _points) g.LineTo(At(p));
            g.LineTo(new Point(At(_points[^1]).X, mid));
            g.EndFigure(true);
        }
        using (ctx.PushClip(new Rect(0, 0, w, mid)))
            ctx.DrawGeometry(new SolidColorBrush(_home, 0.55), null, area);
        using (ctx.PushClip(new Rect(0, mid, w, h - mid)))
            ctx.DrawGeometry(new SolidColorBrush(_away, 0.55), null, area);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(_points[0]), false);
            foreach (var p in _points.Skip(1)) g.LineTo(At(p));
            g.EndFigure(false);
        }
        using (ctx.PushClip(new Rect(0, 0, w, mid)))
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(_home), 1.4), line);
        using (ctx.PushClip(new Rect(0, mid, w, h - mid)))
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(_away), 1.4), line);
    }
}
