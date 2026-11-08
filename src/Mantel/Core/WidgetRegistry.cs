using Mantel.Widgets.Calendar;
using Mantel.Widgets.Clock;
using Mantel.Widgets.Countdown;
using Mantel.Widgets.Disks;
using Mantel.Widgets.Headlines;
using Mantel.Widgets.Launcher;
using Mantel.Widgets.Network;
using Mantel.Widgets.Notes;
using Mantel.Widgets.NowPlaying;
using Mantel.Widgets.Scores;
using Mantel.Widgets.SystemStats;
using Mantel.Widgets.Timer;
using Mantel.Widgets.Weather;

namespace Mantel.Core;

/// <summary>
/// Every widget type Mantel knows about. Adding a widget = adding one entry here.
/// Any type can be added more than once (two clocks, a scores widget per league).
/// </summary>
public static class WidgetRegistry
{
    public static IReadOnlyList<WidgetDescriptor> All { get; } = new[]
    {
        new WidgetDescriptor("clock", "Clock", "Time and date. Add more for world clocks.",
            EnabledByDefault: true, Create: s => new ClockWidget(s))
        {
            BuildOptions = ClockWidget.BuildOptions,
            Subtitle = ClockWidget.Subtitle,
        },

        new WidgetDescriptor("now-playing", "Now Playing", "Amplite, Spotify, browsers and most players",
            EnabledByDefault: true, Create: s => new NowPlayingWidget(s))
        {
            BuildOptions = NowPlayingWidget.BuildOptions,
        },

        new WidgetDescriptor("scores", "Scores", "Live games from Fullcourt",
            EnabledByDefault: false, Create: s => new ScoresWidget(s))
        {
            BuildOptions = ScoresWidget.BuildOptions,
            Subtitle = ScoresWidget.Subtitle,
        },

        new WidgetDescriptor("weather", "Weather", "Current conditions and 3-day forecast",
            EnabledByDefault: false, Create: s => new WeatherWidget(s))
        {
            BuildOptions = WeatherWidget.BuildOptions,
            Subtitle = WeatherWidget.Subtitle,
        },

        new WidgetDescriptor("system-stats", "PC stats", "CPU and memory, top apps and uptime",
            EnabledByDefault: true, Create: s => new SystemStatsWidget(s))
        {
            BuildOptions = SystemStatsWidget.BuildOptions,
        },

        new WidgetDescriptor("timer", "Timer", "Countdown, stopwatch or Pomodoro",
            EnabledByDefault: false, Create: s => new TimerWidget(s))
        {
            BuildOptions = TimerWidget.BuildOptions,
            Subtitle = TimerWidget.Subtitle,
        },

        new WidgetDescriptor("headlines", "Headlines", "The latest NBA, tech or world news, or any feed",
            EnabledByDefault: false, Create: s => new HeadlinesWidget(s))
        {
            BuildOptions = HeadlinesWidget.BuildOptions,
            Subtitle = HeadlinesWidget.Subtitle,
        },

        new WidgetDescriptor("countdown", "Countdown", "Days, hours and minutes until a date",
            EnabledByDefault: false, Create: s => new CountdownWidget(s))
        {
            BuildOptions = CountdownWidget.BuildOptions,
            Subtitle = CountdownWidget.Subtitle,
        },

        new WidgetDescriptor("calendar", "Calendar", "This month, with today highlighted",
            EnabledByDefault: false, Create: s => new CalendarWidget(s))
        {
            BuildOptions = CalendarWidget.BuildOptions,
        },

        new WidgetDescriptor("notes", "Notes", "A sticky note you can type on",
            EnabledByDefault: false, Create: s => new NotesWidget(s))
        {
            BuildOptions = NotesWidget.BuildOptions,
            Subtitle = NotesWidget.Subtitle,
        },

        new WidgetDescriptor("launcher", "App launcher", "Your favorite apps, folders and files, one click away",
            EnabledByDefault: false, Create: s => new LauncherWidget(s))
        {
            BuildOptions = LauncherWidget.BuildOptions,
            Subtitle = LauncherWidget.Subtitle,
        },

        new WidgetDescriptor("disks", "Disk space", "Free space on each drive",
            EnabledByDefault: false, Create: s => new DisksWidget(s)),

        new WidgetDescriptor("network", "Network", "Live download and upload speed",
            EnabledByDefault: false, Create: s => new NetworkWidget(s)),
    };

    public static WidgetDescriptor? Find(string type) =>
        All.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Offers each new widget type once: it shows up in Settings (off, for existing users, so an
    /// update never adds clutter), and if the user later removes it, it stays removed.
    /// Also adds the starter flyouts the first time. Returns true if settings changed.
    /// </summary>
    public static bool EnsureInstances(MantelSettings settings, bool isFirstRun)
    {
        var changed = false;

        // Installs from before KnownTypes existed: everything they already have counts as known.
        if (settings.KnownTypes.Count == 0 && settings.Widgets.Count > 0)
        {
            settings.KnownTypes.AddRange(settings.Widgets.Select(w => w.Type).Distinct());
            changed = true;
        }

        foreach (var d in All)
        {
            if (settings.KnownTypes.Contains(d.Type)) continue;
            settings.KnownTypes.Add(d.Type);
            if (!settings.Widgets.Any(w => w.Type == d.Type && w.FlyoutId is null))
            {
                settings.Widgets.Add(new WidgetInstanceSettings
                {
                    Type = d.Type,
                    Enabled = isFirstRun && d.EnabledByDefault,
                });
            }
            changed = true;
        }

        if (!settings.StarterFlyoutsAdded)
        {
            AddStarterFlyouts(settings);
            settings.StarterFlyoutsAdded = true;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Three ready-made flyouts, off until the user turns them on: Music and Scores open from
    /// their own taskbar icons, Glance slides in from the right edge of the screen.
    /// </summary>
    private static void AddStarterFlyouts(MantelSettings settings)
    {
        var music = new FlyoutSettings { Name = "Music", Icon = "music", TrayIcon = true, Edge = "none" };
        var scores = new FlyoutSettings { Name = "Scores", Icon = "scores", TrayIcon = true, Edge = "none" };
        var glance = new FlyoutSettings { Name = "Glance", Icon = "glance", TrayIcon = false, Edge = "right" };
        settings.Flyouts.AddRange(new[] { music, scores, glance });

        void Add(FlyoutSettings f, string type, Dictionary<string, string>? options = null) =>
            settings.Widgets.Add(new WidgetInstanceSettings
            {
                Type = type, Enabled = true, FlyoutId = f.Id, Options = options ?? new(),
            });

        Add(music, "now-playing", new() { ["hideWhenIdle"] = "false" });
        Add(scores, "scores", new() { ["league"] = "nba", ["max"] = "8" });
        Add(glance, "clock");
        Add(glance, "weather");
        Add(glance, "system-stats");
    }
}
