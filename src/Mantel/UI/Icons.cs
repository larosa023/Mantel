using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Mantel.UI;

/// <summary>
/// Line icons drawn on a 24×24 grid (round caps and joins, like Windows 11's Fluent icons).
/// Drawn as geometry so they stay sharp at every size and DPI and take any color.
/// </summary>
public static class Icons
{
    private const string Circle9 = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0";

    public const string Clock = Circle9 + " M12 7v5l3.2 2";
    public const string Music = "M9 18V5l11-2v13 M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0 M20 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0";
    public const string Basketball = Circle9 + " M12 3v18 M3 12h18 M5.7 5.6c3.4 3.6 3.4 9.2 0 12.8 M18.3 5.6c-3.4 3.6-3.4 9.2 0 12.8";
    public const string Weather = "M7.5 18h9.5a4 4 0 0 0 .4-8a5.5 5.5 0 0 0-10.7 1.3A3.4 3.4 0 0 0 7.5 18z";
    public const string Cpu = "M7 7h10v10H7z M10 10h4v4h-4z M10 3v4 M14 3v4 M10 17v4 M14 17v4 M3 10h4 M3 14h4 M17 10h4 M17 14h4";
    public const string Timer = "M4 13a8 8 0 1 0 16 0a8 8 0 1 0 -16 0 M12 9v4l2.6 1.8 M9.5 2.5h5";
    public const string Disk = "M3 6.5h18v11H3z M3 13h18 M7 15.5h.01";
    public const string Network = "M8 4v15 M4.5 15.5 8 19l3.5-3.5 M16 20V5 M12.5 8.5 16 5l3.5 3.5";
    public const string Palette = "M12 3a9 9 0 1 0 0 18c1.2 0 2-.9 2-2 0-1.4-1-1.7-1-3 0-1.1.9-2 2-2h2.4A3.6 3.6 0 0 0 21 10.4C21 6.3 17 3 12 3z M7.6 11.5h.01 M10.2 7.6h.01 M14.6 7.6h.01";
    public const string Widgets = "M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z";
    public const string Flyout = "M3 5h18v14H3z M14.5 5v14 M9 10l2 2-2 2";
    public const string Sliders = "M4 7h16 M4 17h16 M9 7m-2.4 0a2.4 2.4 0 1 0 4.8 0a2.4 2.4 0 1 0 -4.8 0 M15 17m-2.4 0a2.4 2.4 0 1 0 4.8 0a2.4 2.4 0 1 0 -4.8 0";
    public const string Update = "M20 12a8 8 0 1 1-2.3-5.6 M20 4v4.5h-4.5";
    public const string Gear = Circle9 + " M12 9.2a2.8 2.8 0 1 0 0 5.6a2.8 2.8 0 1 0 0-5.6z";
    public const string Glance = "M4 5h16v14H4z M4 10h16 M8 14h3 M8 16.5h6";
    public const string Mantel = "M4 8h16 M6 8v11 M18 8v11 M7 5h3v3H7z M11 4h3v4h-3z M15 6h3v2h-3z";
    public const string ChevronRight = "M9 6l6 6-6 6";
    public const string ChevronLeft = "M15 6l-6 6 6 6";
    public const string Plus = "M12 5v14 M5 12h14";
    public const string Trash = "M4 7h16 M10 11v6 M14 11v6 M6 7l1 13h10l1-13 M9 7V4h6v3";
    public const string ArrowUp = "M12 19V5 M6.5 10.5 12 5l5.5 5.5";
    public const string ArrowDown = "M12 5v14 M6.5 13.5 12 19l5.5-5.5";
    public const string Open = "M14 4h6v6 M20 4l-9 9 M18 14v6H4V6h6";
    public const string Edge = "M4 4v16 M20 4v16 M9 12h6 M12 9l3 3-3 3";
    public const string Taskbar = "M3 16h18v4H3z M15 18h3 M8 4h8v8H8z";
    public const string Check = "M5 12.5l4.5 4.5L19 7.5";
    public const string Info = Circle9 + " M12 11v5 M12 8h.01";

    /// <summary>The icon for a widget type.</summary>
    public static string ForWidget(string type) => type switch
    {
        "clock" => Clock,
        "now-playing" => Music,
        "scores" => Basketball,
        "weather" => Weather,
        "system-stats" => Cpu,
        "countdown" => Timer,
        "disks" => Disk,
        "network" => Network,
        _ => Widgets,
    };

    /// <summary>The icon for a flyout's picture ("music", "scores", "glance", "mantel").</summary>
    public static string ForFlyout(string icon) => icon switch
    {
        "music" => Music,
        "scores" => Basketball,
        "glance" => Glance,
        _ => Mantel,
    };

    /// <summary>An icon control. With no brush it uses the text color of wherever it's placed.</summary>
    public static Control Create(string geometry, double size = 18, IBrush? brush = null, double thickness = 1.8, bool accent = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(geometry),
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
        };
        if (brush is not null) path.Stroke = brush;
        else path.Classes.Add(accent ? "icon-accent" : "icon-stroke");

        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } },
        };
    }
}
