namespace Mantel.Core;

/// <summary>Everything Mantel remembers between runs. Saved as JSON in %AppData%\Mantel.</summary>
public sealed class MantelSettings
{
    /// <summary>When locked, widgets can't be dragged.</summary>
    public bool Locked { get; set; }

    /// <summary>Snap dragged widgets to an 8px grid and to screen edges.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Hide widgets (and pause their work) while a full-screen app or game runs.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    /// <summary>Theme id (see ThemeManager.All).</summary>
    public string Theme { get; set; } = "glass";

    /// <summary>"" = the theme's accent, "#RRGGBB" = custom, "amplite" = follow Amplite's accent.</summary>
    public string Accent { get; set; } = "";

    /// <summary>Size of desktop widgets, as a percentage (80–140).</summary>
    public int WidgetScale { get; set; } = 100;

    /// <summary>How solid widget cards are, as a percentage of the theme's own (20–100).</summary>
    public int CardOpacity { get; set; } = 100;

    public bool AutoUpdate { get; set; } = true;

    /// <summary>Also receive prerelease (beta) builds.</summary>
    public bool BetaUpdates { get; set; }

    /// <summary>Last version that ran, used to show a "what's new" toast after an update.</summary>
    public string? LastRunVersion { get; set; }

    public List<WidgetInstanceSettings> Widgets { get; set; } = new();

    public List<FlyoutSettings> Flyouts { get; set; } = new();

    /// <summary>Widget types this install has already been offered, so removed ones don't come back.</summary>
    public List<string> KnownTypes { get; set; } = new();

    /// <summary>Whether the starter flyouts (Music, Scores, Glance) have been added.</summary>
    public bool StarterFlyoutsAdded { get; set; }
}

/// <summary>One widget, on the desktop or inside a flyout. A type can have several instances.</summary>
public sealed class WidgetInstanceSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Matches <see cref="WidgetDescriptor.Type"/>.</summary>
    public string Type { get; set; } = "";

    public bool Enabled { get; set; }

    /// <summary>Null = on the desktop. Otherwise the id of the flyout it lives in.</summary>
    public string? FlyoutId { get; set; }

    /// <summary>Position in physical screen pixels. Null means "place it for me".</summary>
    public int? X { get; set; }
    public int? Y { get; set; }

    /// <summary>Per-widget options, e.g. a clock's time zone. Strings keep the format stable.</summary>
    public Dictionary<string, string> Options { get; set; } = new();
}

/// <summary>A panel of widgets that slides out from a screen edge and/or opens from its own taskbar icon.</summary>
public sealed class FlyoutSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Flyout";

    public bool Enabled { get; set; }

    /// <summary>Show an icon in the taskbar's notification area that opens this flyout.</summary>
    public bool TrayIcon { get; set; } = true;

    /// <summary>"none", "left", "right" or "top": push the mouse to this edge to open it.</summary>
    public string Edge { get; set; } = "none";

    /// <summary>Icon for the tray: "music", "scores", "glance" or "mantel".</summary>
    public string Icon { get; set; } = "mantel";

    // --- Motion ---------------------------------------------------------------------------

    /// <summary>"slide", "fade", "zoom", "spring" or "none".</summary>
    public string Animation { get; set; } = "slide";

    /// <summary>"fast", "normal" or "relaxed".</summary>
    public string Speed { get; set; } = "normal";

    // --- Placement ------------------------------------------------------------------------

    /// <summary>Where along its edge it sits: "auto", "start", "center" or "end".
    /// Auto: side edges center, the top edge follows the mouse.</summary>
    public string Align { get; set; } = "auto";

    /// <summary>Side-edge flyouts run the full height of the screen (otherwise they fit their content).</summary>
    public bool FullHeight { get; set; } = true;

    /// <summary>Space between the flyout and the screen edge, in pixels.</summary>
    public int Gap { get; set; } = 6;

    /// <summary>Which screen a flyout opened by its taskbar icon or shortcut uses: "mouse" or "primary".
    /// (Pushing against an edge always opens on that screen.)</summary>
    public string Monitor { get; set; } = "mouse";

    // --- Triggers -------------------------------------------------------------------------

    /// <summary>How long the mouse rests against the edge before it opens: "instant", "short" or "long".</summary>
    public string EdgeDelay { get; set; } = "short";

    /// <summary>Keyboard shortcut key used with Ctrl+Alt ("M" → Ctrl+Alt+M). Empty = none.</summary>
    public string Hotkey { get; set; } = "";
}
