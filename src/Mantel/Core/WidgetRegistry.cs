using Mantel.Widgets.Clock;
using Mantel.Widgets.SystemStats;

namespace Mantel.Core;

/// <summary>
/// Every widget type Mantel knows about. Adding a widget = adding one line here.
/// (In v2 this becomes the plugin entry point.)
/// </summary>
public static class WidgetRegistry
{
    public static IReadOnlyList<WidgetDescriptor> All { get; } = new[]
    {
        new WidgetDescriptor(
            Type: "clock",
            Name: "Clock",
            Description: "Time and date",
            EnabledByDefault: true,
            Create: s => new ClockWidget(s)),

        new WidgetDescriptor(
            Type: "system-stats",
            Name: "PC stats",
            Description: "CPU and memory usage",
            EnabledByDefault: true,
            Create: s => new SystemStatsWidget(s)),
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
