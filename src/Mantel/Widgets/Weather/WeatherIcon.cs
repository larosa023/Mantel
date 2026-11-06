using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Mantel.Widgets.Weather;

/// <summary>
/// Weather icons drawn from simple shapes on a 32×32 canvas, scaled to any size.
/// No icon font or image files, so they look sharp at every DPI and match the theme.
/// </summary>
public static class WeatherIcon
{
    private static readonly IBrush SunBrush = Brush.Parse("#F2B84B");
    private static readonly IBrush MoonBrush = Brush.Parse("#D9D3C7");
    private static readonly IBrush CloudLight = Brush.Parse("#D5D8DD");
    private static readonly IBrush CloudDark = Brush.Parse("#9AA0A8");
    private static readonly IBrush RainBrush = Brush.Parse("#6FA8E8");
    private static readonly IBrush SnowBrush = Brush.Parse("#EEF2F7");
    private static readonly IBrush BoltBrush = Brush.Parse("#F2C94C");

    public static Control Create(WeatherKind kind, bool isDay, double size)
    {
        var c = new Canvas { Width = 32, Height = 32 };

        switch (kind)
        {
            case WeatherKind.Clear:
                if (isDay) Sun(c, 16, 16, 6.5, rays: 11.5);
                else Moon(c, 16, 16, 9);
                break;

            case WeatherKind.PartlyCloudy:
                if (isDay) Sun(c, 20, 11, 5, rays: 8.5);
                else Moon(c, 21, 10, 6.5);
                Cloud(c, CloudLight, dy: 4);
                break;

            case WeatherKind.Cloudy:
                Cloud(c, CloudLight, dy: 2);
                break;

            case WeatherKind.Fog:
                Cloud(c, CloudDark, dy: -2);
                for (var i = 0; i < 3; i++) Stroke(c, CloudLight, 7 + i * 2, 25 + i * 3, 25 - i * 2, 25 + i * 3, 2);
                break;

            case WeatherKind.Drizzle:
                Cloud(c, CloudDark, dy: -3);
                foreach (var x in new[] { 11.0, 17, 23 }) Stroke(c, RainBrush, x, 25, x - 1, 28, 2);
                break;

            case WeatherKind.Rain:
                Cloud(c, CloudDark, dy: -3);
                foreach (var x in new[] { 11.0, 17, 23 }) Stroke(c, RainBrush, x, 24, x - 2.5, 30, 2);
                break;

            case WeatherKind.Snow:
                Cloud(c, CloudLight, dy: -3);
                foreach (var (x, y) in new[] { (10.0, 26.0), (16.0, 29.0), (22.0, 26.0) }) Dot(c, SnowBrush, x, y, 3.2);
                break;

            case WeatherKind.Thunder:
                Cloud(c, CloudDark, dy: -3);
                var bolt = new Polygon
                {
                    Fill = BoltBrush,
                    Points = new[]
                    {
                        new Point(18, 21), new Point(12.5, 27.5), new Point(16, 27.5),
                        new Point(14, 32), new Point(20.5, 25), new Point(17, 25), new Point(19.5, 21),
                    },
                };
                c.Children.Add(bolt);
                break;
        }

        return new Viewbox { Width = size, Height = size, Child = c };
    }

    private static void Sun(Canvas c, double cx, double cy, double r, double rays)
    {
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            var inner = r + 2;
            Stroke(c, SunBrush,
                cx + Math.Cos(a) * inner, cy + Math.Sin(a) * inner,
                cx + Math.Cos(a) * rays, cy + Math.Sin(a) * rays, 2);
        }
        Dot(c, SunBrush, cx, cy, r * 2);
    }

    private static void Moon(Canvas c, double cx, double cy, double r)
    {
        // A crescent: a full disc with an offset disc cut out of it.
        var disc = new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2));
        var bite = new EllipseGeometry(new Rect(cx - r + r * 0.7, cy - r - r * 0.35, r * 2, r * 2));
        c.Children.Add(new Path
        {
            Fill = MoonBrush,
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, disc, bite),
        });
    }

    /// <summary>Three puffs on a rounded base. <paramref name="dy"/> shifts it up or down.</summary>
    private static void Cloud(Canvas c, IBrush brush, double dy)
    {
        Ellipse(c, brush, 5, 12 + dy, 12, 12);
        Ellipse(c, brush, 11, 6 + dy, 14, 14);
        Ellipse(c, brush, 19, 11 + dy, 10, 10);
        var baseRect = new Rectangle { Fill = brush, Width = 20, Height = 9, RadiusX = 4.5, RadiusY = 4.5 };
        Canvas.SetLeft(baseRect, 7);
        Canvas.SetTop(baseRect, 15 + dy);
        c.Children.Add(baseRect);
    }

    private static void Ellipse(Canvas c, IBrush brush, double x, double y, double w, double h)
    {
        var e = new Avalonia.Controls.Shapes.Ellipse { Fill = brush, Width = w, Height = h };
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        c.Children.Add(e);
    }

    private static void Dot(Canvas c, IBrush brush, double cx, double cy, double d) =>
        Ellipse(c, brush, cx - d / 2, cy - d / 2, d, d);

    private static void Stroke(Canvas c, IBrush brush, double x1, double y1, double x2, double y2, double width) =>
        c.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = brush,
            StrokeThickness = width,
            StrokeLineCap = PenLineCap.Round,
        });
}
