using Mantel.Widgets.Clock;
using Mantel.Widgets.Countdown;
using Mantel.Widgets.Disks;
using Mantel.Widgets.Network;
using Mantel.Widgets.NowPlaying;
using Mantel.Widgets.SystemStats;
using Mantel.Widgets.Weather;

namespace Mantel.Core;

/// <summary>
/// Every widget type Mantel knows about. Adding a widget = adding one entry here.
/// (In v2 this becomes the plugin entry point.)
/// </summary>
public static class WidgetRegistry
{
    public static IReadOnlyList<WidgetDescriptor> All { get; } = new[]
    {
        new WidgetDescriptor("clock", "Clock", "Time and date. Add more for world clocks.",
            EnabledByDefault: true, Create: s => new ClockWidget(s))
        {
            AllowMultiple = true,
            BuildOptions = ClockWidget.BuildOptions,
            Subtitle = ClockWidget.Subtitle,
        },

        new WidgetDescriptor("now-playing", "Now Playing", "Spotify, browsers and most players, with controls",
            EnabledByDefault: true, Create: s => new NowPlayingWidget(s))
        {
            BuildOptions = NowPlayingWidget.BuildOptions,
        },

        new WidgetDescriptor("weather", "Weather", "Current conditions and 3-day forecast",
            EnabledByDefault: false, Create: s => new WeatherWidget(s))
        {
            BuildOptions = WeatherWidget.BuildOptions,
            Subtitle = WeatherWidget.Subtitle,
        },

        new WidgetDescriptor("system-stats", "PC stats", "CPU and memory usage",
            EnabledByDefault: true, Create: s => new SystemStatsWidget(s)),

        new WidgetDescriptor("countdown", "Countdown", "Days, hours and minutes until a date",
            EnabledByDefault: false, Create: s => new CountdownWidget(s))
        {
            AllowMultiple = true,
            BuildOptions = CountdownWidget.BuildOptions,
            Subtitle = CountdownWidget.Subtitle,
        },

        new WidgetDescriptor("disks", "Disk space", "Free space on each drive",
            EnabledByDefault: false, Create: s => new DisksWidget(s)),

        new WidgetDescriptor("network", "Network", "Live download and upload speed",
            EnabledByDefault: false, Create: s => new NetworkWidget(s)),
    };

    public static WidgetDescriptor? Find(string type) =>
        All.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Makes sure every registered type has at least one instance in settings, so a
    /// widget added in an update shows up in the toggle list. New widgets start off for
    /// existing users, so an update never adds clutter to someone's desktop.
    /// </summary>
    public static bool EnsureInstances(MantelSettings settings, bool isFirstRun)
    {
        var changed = false;
        foreach (var d in All)
        {
            if (settings.Widgets.Any(w => w.Type == d.Type)) continue;
            settings.Widgets.Add(new WidgetInstanceSettings
            {
                Type = d.Type,
                Enabled = isFirstRun && d.EnabledByDefault,
            });
            changed = true;
        }
        return changed;
    }
}
