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

    public bool AutoUpdate { get; set; } = true;

    /// <summary>Also receive prerelease (beta) builds.</summary>
    public bool BetaUpdates { get; set; }

    /// <summary>Last version that ran, used to show a "what's new" toast after an update.</summary>
    public string? LastRunVersion { get; set; }

    public List<WidgetInstanceSettings> Widgets { get; set; } = new();
}

/// <summary>One widget on the desktop. A type can have several instances (two clocks, say).</summary>
public sealed class WidgetInstanceSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Matches <see cref="WidgetDescriptor.Type"/>.</summary>
    public string Type { get; set; } = "";

    public bool Enabled { get; set; }

    /// <summary>Position in physical screen pixels. Null means "place it for me".</summary>
    public int? X { get; set; }
    public int? Y { get; set; }

    /// <summary>Per-widget options, e.g. a clock's time zone. Strings keep the format stable.</summary>
    public Dictionary<string, string> Options { get; set; } = new();
}
