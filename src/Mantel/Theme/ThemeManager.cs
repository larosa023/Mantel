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

        // --- Popular palettes ------------------------------------------------------------------

        new ThemeDef("catppuccin", "Catppuccin", "Soft pastel mocha: lavender, pink and peach.", false,
            "#F21E1E2E", "#313244", 16, Normal, 1, "0 6 24 0 #59000000",
            "#CBA6F7", "#CDD6F4", "#A6ADC8", "#7F849C", "#313244", "#313244"),

        new ThemeDef("dracula", "Dracula", "The classic dark purple with neon pink.", false,
            "#F2282A36", "#44475A", 14, Normal, 1, "0 6 22 0 #66000000",
            "#FF79C6", "#F8F8F2", "#C1C4D6", "#8B93B8", "#44475A", "#44475A"),

        new ThemeDef("tokyo", "Tokyo Night", "Deep navy with electric blue.", false,
            "#F21A1B26", "#292E42", 14, Normal, 1, "0 6 24 0 #66000000",
            "#7AA2F7", "#C0CAF5", "#9AA5CE", "#737AA2", "#292E42", "#292E42"),

        new ThemeDef("gruvbox", "Gruvbox", "Retro warm browns and mustard.", false,
            "#F2282828", "#3C3836", 10, Normal, 1, "0 4 16 0 #59000000",
            "#FABD2F", "#EBDBB2", "#BDAE93", "#928374", "#3C3836", "#3C3836"),

        new ThemeDef("rosepine", "Rosé Pine", "Muted dusk with rose and gold.", false,
            "#F2191724", "#26233A", 18, Normal, 1, "0 6 24 0 #59000000",
            "#EBBCBA", "#E0DEF4", "#908CAA", "#6E6A86", "#26233A", "#26233A"),

        // --- Bigger changes ----------------------------------------------------------------------

        new ThemeDef("frost", "Frost", "Bright frosted glass for dark wallpapers.", true,
            "#B3F4F7FB", "#66FFFFFF", 22, new Thickness(20, 16), 1, "0 10 30 0 #40000000",
            "#3B82F6", "#0F172A", "#334155", "#475569", "#1F0F172A", "#1A0F172A"),

        new ThemeDef("synthwave", "Synthwave", "80s sunset: magenta to orange glow, bold numbers.", false,
            "#F2241B35", "#C0F92AAD", 18, new Thickness(20, 16), 1.5, "0 0 26 0 #80F92AAD",
            "#F92AAD", "#FDF6FF", "#D4B8E8", "#A88BC7", "#3A2C55", "#3A2C55")
        { Glow = true, BigWeight = FontWeight.Bold },

        new ThemeDef("mono", "Mono", "Pure grayscale, crisp and minimal. Square-ish corners.", false,
            "#F2111111", "#33FFFFFF", 4, new Thickness(18, 14), 1, "0 0 0 0 #00000000",
            "#FFFFFF", "#F5F5F5", "#A3A3A3", "#8A8A8A", "#2A2A2A", "#262626")
        { BigWeight = FontWeight.ExtraLight, BigSize = 52 },

        new ThemeDef("pill", "Pill", "Extra-round cards with roomy padding.", false,
            "#E6181A20", "#1FFFFFFF", 30, new Thickness(26, 20), 1, "0 10 30 0 #59000000",
            "#5AD19A", "#F0F4F2", "#A7B3AD", "#86928C", "#24FFFFFF", "#1AFFFFFF"),

        new ThemeDef("solar", "Solarized Light", "Low-glare cream with teal and orange.", true,
            "#F2FDF6E3", "#1A586E75", 12, Normal, 1, "0 4 16 0 #26000000",
            "#CB4B16", "#073642", "#586E75", "#657B83", "#1A073642", "#14073642"),
    };

    /// <summary>Desktop widget size multiplier (1.0 = normal), set from Appearance.</summary>
    public static double WidgetScale { get; private set; } = 1.0;

    /// <summary>Whether card and flyout borders are drawn (Appearance → Card borders).</summary>
    public static bool Borders { get; private set; } = true;

    public static ThemeDef Current { get; private set; } = All[0];

    /// <summary>The accent in use right now (the theme's own, a custom color, or Amplite's).</summary>
    public static Color Accent { get; private set; } = Color.Parse(All[0].Accent);

    public static event Action? AccentChanged;

    public static ThemeDef Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    /// <summary>Applies a theme's colors and shapes to every widget (styles read these as dynamic resources).</summary>
    public static void Apply(Application app, string? themeId, string? accentOverride, int cardOpacity = 100, int widgetScale = 100, bool borders = true)
    {
        var t = Find(themeId);
        Current = t;
        WidgetScale = Math.Clamp(widgetScale, 70, 150) / 100.0;
        var r = app.Resources;

        // Card opacity scales the theme's own background alpha (and its border with it).
        var opacity = Math.Clamp(cardOpacity, 0, 100) / 100.0;
        var bg = Color.Parse(t.CardBackground);
        r["Mantel.CardBackground"] = new SolidColorBrush(Color.FromArgb((byte)(bg.A * opacity), bg.R, bg.G, bg.B));
        r["Mantel.CardBorder"] = Brush(t.CardBorder);
        r["Mantel.CardBorderThickness"] = new Thickness(borders ? t.CardBorderThickness : 0);
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

        // Flyouts float over windows, not the wallpaper, so they're always nearly opaque,
        // even on see-through themes; Bare gets a dark glass panel since it has no card at all.
        var card = Color.Parse(t.CardBackground);
        var flyoutBg = t.Bare ? Color.Parse("#EB101114") : Color.FromArgb(Math.Max(card.A, (byte)0xF2), card.R, card.G, card.B);
        r["Mantel.FlyoutBackground"] = new SolidColorBrush(flyoutBg);
        r["Mantel.FlyoutBorder"] = Brush(!borders ? "#00000000" : t.Bare ? "#22FFFFFF" : t.CardBorderThickness > 0 ? t.CardBorder : "#1AFFFFFF");
        Borders = borders;
        r["Mantel.SectionBackground"] = Brush(t.IsLight ? "#0A000000" : t.Id == "terminal" ? "#0D39FF14" : "#0DFFFFFF");
        r["Mantel.FlyoutRadius"] = new CornerRadius(t.Bare ? 22 : t.CardRadius <= 0 ? 0 : t.CardRadius + 6);
        r["Mantel.SectionRadius"] = new CornerRadius(t.Bare ? 14 : Math.Max(0, t.CardRadius - 2));

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
        // Toggle switches: the knob sits on the accent when on, so it takes the accent's contrast color
        // (a white knob vanished on Mono's white accent).
        var knob = new SolidColorBrush(Contrast(accent));
        r["ToggleSwitchKnobFillOn"] = knob;
        r["ToggleSwitchKnobFillOnPointerOver"] = knob;
        r["ToggleSwitchKnobFillOnPressed"] = knob;
        // Off: a clearer outline and knob than Fluent's faint default.
        r["ToggleSwitchStrokeOff"] = new SolidColorBrush(Color.Parse("#B3FFFFFF"));
        r["ToggleSwitchKnobFillOff"] = new SolidColorBrush(Color.Parse("#E6FFFFFF"));

        // Fluent's own controls (toggle switches, sliders, focus rings) follow the accent too.
        r["SystemAccentColor"] = accent;
        r["SystemAccentColorLight1"] = Shift(accent, 0.15);
        r["SystemAccentColorLight2"] = Shift(accent, 0.3);
        r["SystemAccentColorLight3"] = Shift(accent, 0.45);
        r["SystemAccentColorDark1"] = Shift(accent, -0.15);
        r["SystemAccentColorDark2"] = Shift(accent, -0.3);
        r["SystemAccentColorDark3"] = Shift(accent, -0.45);
        if (Current.Glow)
        {
            r["Mantel.CardBorder"] = new SolidColorBrush(Color.FromArgb(Borders ? (byte)0xC0 : (byte)0, accent.R, accent.G, accent.B));
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

    /// <summary>Lighter (amount &gt; 0) or darker (amount &lt; 0) version of a color.</summary>
    private static Color Shift(Color c, double amount)
    {
        byte Mix(byte v) => (byte)Math.Clamp(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
        return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

        /// <summary>Dark text on light accents, light text on dark ones.</summary>
    private static Color Contrast(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? Color.Parse("#141519") : Colors.White;

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
