using Avalonia;
using Avalonia.Media;

namespace Mantel.Theme;

/// <summary>
/// One look for every widget. Simple themes only swap colors; bigger ones change the
/// whole feel: corner radius, padding, fonts, or no card at all.
/// </summary>
public sealed record ThemeDef(
    string Id,
    string Name,
    string Description,
    bool IsLight,
    string CardBackground,
    string CardBorder,
    double CardRadius,
    Thickness CardPadding,
    double CardBorderThickness,
    string CardShadow,
    string Accent,
    string Text,
    string TextMuted,
    string TextLabel,
    string Track,
    string Divider)
{
    /// <summary>Font for all widget text. Null = the app default (Inter).</summary>
    public string? FontFamily { get; init; }

    /// <summary>Weight and size of the headline numbers (time, temperature, countdown).</summary>
    public FontWeight BigWeight { get; init; } = FontWeight.Light;
    public double BigSize { get; init; } = 44;

    /// <summary>No card: text floats on the wallpaper with a soft shadow so it stays readable.</summary>
    public bool Bare { get; init; }

    /// <summary>Outer glow in the accent color (Neon).</summary>
    public bool Glow { get; init; }
}

public static class ThemeManager
{
    private static readonly Thickness Normal = new(18, 14);

    public static IReadOnlyList<ThemeDef> All { get; } = new[]
    {
        new ThemeDef("glass", "Glass", "Dark, soft and warm. The default.", false,
            "#E0141519", "#22FFFFFF", 16, Normal, 1, "0 6 24 0 #59000000",
            "#E8A15A", "#F0ECE4", "#A8A49D", "#8C8A86", "#1FFFFFFF", "#1AFFFFFF"),

        new ThemeDef("smoke", "Smoke", "See-through cards that let the wallpaper show.", false,
            "#8C101114", "#1AFFFFFF", 18, Normal, 1, "0 4 18 0 #33000000",
            "#9EC5FF", "#F4F6F8", "#B9BEC6", "#9AA0A8", "#26FFFFFF", "#1FFFFFFF"),

        new ThemeDef("light", "Light", "Clean white cards for light wallpapers.", true,
            "#F2FFFFFF", "#14000000", 16, Normal, 1, "0 6 24 0 #26000000",
            "#D9822B", "#1C1D21", "#5F6268", "#7A7D84", "#14000000", "#12000000"),

        new ThemeDef("paper", "Paper", "Warm cream with serif numbers.", true,
            "#F7F1E6", "#1F5A4630", 6, new Thickness(20, 16), 1, "0 2 10 0 #26000000",
            "#B5532B", "#2B241C", "#6E6253", "#8A7D6C", "#1A2B241C", "#1A2B241C")
        { FontFamily = "Georgia, Cambria, serif", BigWeight = FontWeight.Normal, BigSize = 46 },

        new ThemeDef("amoled", "AMOLED", "Pure black, no borders. Easy on OLED screens.", false,
            "#FF000000", "#00000000", 14, Normal, 0, "0 0 0 0 #00000000",
            "#FFFFFF", "#FFFFFF", "#9A9A9A", "#7A7A7A", "#22FFFFFF", "#1AFFFFFF"),

        new ThemeDef("nord", "Nord", "Cool arctic blues.", false,
            "#F02E3440", "#3B4252", 12, Normal, 1, "0 6 20 0 #4D000000",
            "#88C0D0", "#ECEFF4", "#C4CBD8", "#8F9BB3", "#3B4252", "#3B4252"),

        new ThemeDef("neon", "Neon", "Deep navy with a glowing accent edge.", false,
            "#F00B0E1F", "#C0FF3CAC", 20, new Thickness(20, 16), 1.5, "0 0 22 0 #80FF3CAC",
            "#FF3CAC", "#F5F3FF", "#B7B2D9", "#8D88B8", "#262A4A", "#262A4A")
        { Glow = true, BigWeight = FontWeight.SemiBold },

        new ThemeDef("bare", "Bare", "No cards. Big text right on your wallpaper.", false,
            "#00000000", "#00000000", 0, new Thickness(6, 4), 0, "0 0 0 0 #00000000",
            "#FFFFFF", "#FFFFFF", "#E6FFFFFF", "#CCFFFFFF", "#40FFFFFF", "#33FFFFFF")
        { Bare = true, BigWeight = FontWeight.Thin, BigSize = 64 },

        new ThemeDef("terminal", "Terminal", "Green-on-black monospace, square corners.", false,
            "#F2050805", "#8039FF14", 0, new Thickness(16, 12), 1, "0 0 0 0 #00000000",
            "#39FF14", "#C8FFC0", "#6FBF63", "#4F9944", "#1A39FF14", "#2639FF14")
        { FontFamily = "Cascadia Mono, Consolas, monospace", BigWeight = FontWeight.Normal, BigSize = 40 },
    };

    public static ThemeDef Current { get; private set; } = All[0];

    /// <summary>The accent in use right now (the theme's own, a custom color, or Amplite's).</summary>
    public static Color Accent { get; private set; } = Color.Parse(All[0].Accent);

    public static event Action? AccentChanged;

    public static ThemeDef Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    /// <summary>Applies a theme's colors and shapes to every widget (styles read these as dynamic resources).</summary>
    public static void Apply(Application app, string? themeId, string? accentOverride)
    {
        var t = Find(themeId);
        Current = t;
        var r = app.Resources;

        r["Mantel.CardBackground"] = Brush(t.CardBackground);
        r["Mantel.CardBorder"] = Brush(t.CardBorder);
        r["Mantel.CardBorderThickness"] = new Thickness(t.CardBorderThickness);
        r["Mantel.CardRadius"] = new CornerRadius(t.CardRadius);
        r["Mantel.CardPadding"] = t.CardPadding;
        r["Mantel.CardShadow"] = BoxShadows.Parse(t.CardShadow);
        r["Mantel.Text"] = Brush(t.Text);
        r["Mantel.TextMuted"] = Brush(t.TextMuted);
        r["Mantel.TextLabel"] = Brush(t.TextLabel);
        r["Mantel.Track"] = Brush(t.Track);
        r["Mantel.Divider"] = Brush(t.Divider);
        r["Mantel.BigWeight"] = t.BigWeight;
        r["Mantel.BigSize"] = t.BigSize;

        SetAccent(app, ParseAccent(accentOverride) ?? Color.Parse(t.Accent));
    }

    /// <summary>Changes just the accent, live, without rebuilding anything.</summary>
    public static void SetAccent(Application app, Color accent)
    {
        Accent = accent;
        var r = app.Resources;
        r["Mantel.Accent"] = new SolidColorBrush(accent);
        r["Mantel.AccentSoft"] = new SolidColorBrush(Color.FromArgb(0xB3, accent.R, accent.G, accent.B));
        r["Mantel.AccentText"] = new SolidColorBrush(Contrast(accent));
        if (Current.Glow)
        {
            r["Mantel.CardBorder"] = new SolidColorBrush(Color.FromArgb(0xC0, accent.R, accent.G, accent.B));
            r["Mantel.CardShadow"] = new BoxShadows(new BoxShadow
            {
                Blur = 22,
                Color = Color.FromArgb(0x80, accent.R, accent.G, accent.B),
            });
        }
        AccentChanged?.Invoke();
    }

    /// <summary>"#RRGGBB" -> color; anything else (empty, "amplite") -> null.</summary>
    public static Color? ParseAccent(string? value) =>
        value is { Length: 7 } && value[0] == '#' && Color.TryParse(value, out var c) ? c : null;

    /// <summary>Dark text on light accents, light text on dark ones.</summary>
    private static Color Contrast(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? Color.Parse("#141519") : Colors.White;

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
