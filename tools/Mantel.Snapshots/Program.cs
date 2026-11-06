using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel;
using Mantel.Core;
using Mantel.Theme;
using Mantel.UI;
using Mantel.Updates;
using Mantel.Widgets.NowPlaying;

// Renders Mantel's real UI headlessly to PNGs: every theme as a desktop of widgets, the
// Settings window, and the flyouts. CI pushes the images to the "snapshots" branch.

var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "snapshots");
Directory.CreateDirectory(outDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var app = Application.Current!;
var failures = 0;

void Run(string name, Action render)
{
    var sw = Stopwatch.StartNew();
    try
    {
        render();
        Console.WriteLine($"ok    {name} ({sw.ElapsedMilliseconds} ms)");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}: {ex}");
    }
}

// --- Desktop of widgets, once per theme ---------------------------------------------------------

foreach (var theme in ThemeManager.All)
{
    Run($"theme-{theme.Id}", () =>
    {
        ThemeManager.Apply(app, theme.Id, null);
        var widgets = Scenes.DesktopWidgets();
        var window = Scenes.Desktop(widgets, $"{theme.Name}: {theme.Description}");
        Scenes.Capture(window, widgets, Path.Combine(outDir, $"theme-{theme.Id}.png"), settleMs: 7000);
    });
}

ThemeManager.Apply(app, "glass", null);

// --- Settings window, every page -------------------------------------------------------------

Run("settings", () =>
{
    var settings = Scenes.SampleSettings();
    var store = SettingsStore.InMemory(settings);
    var manager = new WidgetManager(store);
    var updates = new UpdateService(settings);

    foreach (var page in SettingsWindow.PageNames)
    {
        var window = new SettingsWindow(store, manager, updates, () => { }) { Width = 980, Height = 720 };
        window.ShowPage(page);
        Scenes.Capture(window, Array.Empty<IWidget>(), Path.Combine(outDir, $"settings-{page}.png"), settleMs: 800);
    }
});

// --- Flyouts ------------------------------------------------------------------------------------

foreach (var preset in Scenes.Flyouts())
{
    Run($"flyout-{preset.Key}", () =>
    {
        ThemeManager.Apply(app, "glass", null);
        var (window, widgets) = preset.Value();
        Scenes.Capture(window, widgets, Path.Combine(outDir, $"flyout-{preset.Key}.png"), settleMs: 7000);
    });
}

// Keep the run's log next to the images; it explains any empty widget.
var log = Path.Combine(SettingsStore.Folder, "mantel.log");
if (File.Exists(log)) File.Copy(log, Path.Combine(outDir, "mantel.log"), overwrite: true);

Console.WriteLine(failures == 0 ? "All snapshots rendered." : $"{failures} snapshot(s) failed.");
return failures == 0 ? 0 : 1;

internal static class Scenes
{
    public const double DesktopWidth = 1440, DesktopHeight = 900;

    /// <summary>A dark, photo-like wallpaper so see-through themes look the way they will on a real desktop.</summary>
    public static IBrush Wallpaper() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#101318"), 0),
            new GradientStop(Color.Parse("#1B2536"), 0.45),
            new GradientStop(Color.Parse("#3A2433"), 0.8),
            new GradientStop(Color.Parse("#140E12"), 1),
        },
    };

    public static List<IWidget> DesktopWidgets()
    {
        IWidget Make(string type, Dictionary<string, string>? options = null) =>
            WidgetRegistry.Find(type)!.Create(new WidgetInstanceSettings { Type = type, Enabled = true, Options = options ?? new() });

        return new List<IWidget>
        {
            Make("clock"),
            Make("weather", Holmdel()),
            Make("now-playing", new() { ["hideWhenIdle"] = "false" }),
            Make("system-stats"),
            Make("network"),
            Make("scores", new() { ["league"] = "nba", ["max"] = "6" }),
            Make("countdown", new() { ["title"] = "Bid due", ["target"] = DateTime.Now.AddDays(4).AddHours(3).ToString("yyyy-MM-ddTHH:mm") }),
            Make("disks"),
        };
    }

    public static Dictionary<string, string> Holmdel() => new()
    {
        ["place"] = "Holmdel", ["lat"] = "40.345", ["lon"] = "-74.184", ["units"] = "f",
    };

    /// <summary>Lays widget cards out in three columns over the wallpaper.</summary>
    public static Window Desktop(IReadOnlyList<IWidget> widgets, string caption)
    {
        var columns = new[] { new[] { 0, 1 }, new[] { 2, 3, 4 }, new[] { 5, 6, 7 } };
        var grid = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(40, 56, 40, 40) };
        foreach (var col in columns)
        {
            var stack = new StackPanel { Spacing = -8 };
            foreach (var i in col) stack.Children.Add(WidgetCard.Create(widgets[i].View));
            grid.Children.Add(stack);
        }

        var label = new TextBlock { Text = caption, Margin = new Thickness(52, 20, 0, 0), FontSize = 13, Foreground = Brushes.White, Opacity = 0.6 };
        var window = new Window
        {
            Width = DesktopWidth, Height = DesktopHeight,
            Background = Wallpaper(),
            Content = new Panel { Children = { label, grid } },
        };
        WidgetCard.ApplyFont(window);
        return window;
    }

    public static Dictionary<string, Func<(Window, IReadOnlyList<IWidget>)>> Flyouts() => new()
    {
        ["glance"] = () => Flyout(MakeFlyout("Glance", "glance", "right",
            ("clock", null), ("weather", Holmdel()), ("system-stats", null), ("countdown", new() { ["title"] = "Bid due", ["target"] = DateTime.Now.AddDays(4).ToString("yyyy-MM-ddTHH:mm") }))),
        ["music"] = () => Flyout(MakeFlyout("Music", "music", "none", ("now-playing", new() { ["hideWhenIdle"] = "false" }))),
        ["scores"] = () => Flyout(MakeFlyout("Scores", "scores", "none", ("scores", new() { ["league"] = "nba", ["max"] = "8" }))),
    };

    private static (FlyoutSettings, List<WidgetInstanceSettings>) MakeFlyout(string name, string icon, string edge,
        params (string Type, Dictionary<string, string>? Options)[] items)
    {
        var f = new FlyoutSettings { Name = name, Icon = icon, Edge = edge, Enabled = true };
        var list = items.Select(i => new WidgetInstanceSettings { Type = i.Type, Enabled = true, FlyoutId = f.Id, Options = i.Options ?? new() }).ToList();
        return (f, list);
    }

    /// <summary>Renders a flyout's panel exactly as the app builds it, docked to the right of a desktop.</summary>
    private static (Window, IReadOnlyList<IWidget>) Flyout((FlyoutSettings Flyout, List<WidgetInstanceSettings> Items) spec)
    {
        var panel = new Mantel.Flyouts.FlyoutPanel(spec.Flyout, spec.Items, _ => new ContextMenu(), () => { });
        panel.BuildWidgets();
        // Edge flyouts run the full height of the screen, as they do in the app.
        if (spec.Flyout.Edge is "left" or "right") panel.SetHeight(DesktopHeight - 20);
        PreviewMusic(panel.Widgets);

        var window = new Window
        {
            Width = 900, Height = DesktopHeight,
            Background = Wallpaper(),
            Content = new Panel
            {
                Children =
                {
                    new Border
                    {
                        Child = panel,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = spec.Flyout.Edge == "none" ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, spec.Flyout.Edge == "none" ? 0 : 0),
                    },
                },
            },
        };
        WidgetCard.ApplyFont(window);
        return (window, panel.Widgets);
    }

    private static void PreviewMusic(IEnumerable<IWidget> widgets)
    {
        foreach (var np in widgets.OfType<NowPlayingWidget>())
            np.ShowPreview("AMPLITE", "Change (In the House of Flies)", "Deftones · White Pony", SampleArt(), 0.42, "EQ · Bass boost");
    }

    public static void Capture(Window window, IReadOnlyList<IWidget> widgets, string path, int settleMs)
    {
        window.Show();
        foreach (var w in widgets) w.Start();
        PreviewMusic(widgets);
        Pump(settleMs);
        PreviewMusic(widgets); // the media session may have reset it to idle while starting
        Pump(300);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        frame.Save(path);
        foreach (var w in widgets) { w.Stop(); w.Dispose(); }
        window.Close();
    }

    /// <summary>Runs the UI thread for a while so network calls finish and timers tick.</summary>
    public static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
    }

    /// <summary>A stand-in album cover: a warm gradient with a circle, nothing copyrighted.</summary>
    private static Bitmap? _art;
    public static Bitmap SampleArt()
    {
        if (_art is not null) return _art;
        var size = new PixelSize(160, 160);
        var rtb = new RenderTargetBitmap(size);
        var cover = new Border
        {
            Width = 160, Height = 160,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#2F4858"), 0), new GradientStop(Color.Parse("#86BBD8"), 1) },
            },
            Child = new Border { Width = 70, Height = 70, CornerRadius = new CornerRadius(35), Background = Brush.Parse("#F6AE2D") },
        };
        cover.Measure(new Size(160, 160));
        cover.Arrange(new Rect(0, 0, 160, 160));
        rtb.Render(cover);
        return _art = rtb;
    }

    public static MantelSettings SampleSettings()
    {
        var s = new MantelSettings { Theme = "glass" };
        WidgetRegistry.EnsureInstances(s, isFirstRun: true);
        foreach (var f in s.Flyouts) f.Enabled = f.Name != "Scores";
        var weather = s.Widgets.First(w => w.Type == "weather" && w.FlyoutId is null);
        weather.Enabled = true;
        foreach (var (k, v) in Holmdel()) weather.Options[k] = v;
        return s;
    }
}
