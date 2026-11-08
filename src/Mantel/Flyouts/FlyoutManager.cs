using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Flyouts;

/// <summary>
/// Runs every enabled flyout: its tray icon, its screen-edge trigger, and its window.
/// Edge detection is one cursor read every 100 ms, and only while some flyout uses an edge.
/// </summary>
public sealed class FlyoutManager : IDisposable
{
    private static readonly TimeSpan LeaveDelay = TimeSpan.FromMilliseconds(450);

    private readonly MantelSettings _settings;
    private readonly Func<WidgetInstanceSettings, ContextMenu> _menuFor;
    private readonly Func<bool> _isFullscreen;

    /// <summary>The flyout header's gear and empty-state button open Settings through this.</summary>
    private Action? _openSettings;
    public Action? OpenSettings { get => _openSettings; set => _openSettings = value; }
    private readonly List<Host> _hosts = new();
    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private Host? _edgeCandidate;
    private DateTime _edgeSince;
    private DateTime _outsideSince = DateTime.MaxValue;

    private sealed record Host(FlyoutSettings Settings, FlyoutWindow Window, TrayIcon? Tray);

    public FlyoutManager(MantelSettings settings, Func<WidgetInstanceSettings, ContextMenu> menuFor, Func<bool> isFullscreen)
    {
        _settings = settings;
        _menuFor = menuFor;
        _isFullscreen = isFullscreen;
        _edgeTimer.Tick += (_, _) => PollEdges();
    }

    /// <summary>Tears down and recreates every flyout from settings. Cheap; widgets are created lazily.</summary>
    private readonly Hotkeys _hotkeys = new();

    /// <summary>App-wide shortcuts (like hide all widgets), registered alongside the flyouts' own.</summary>
    public Func<IEnumerable<(string Key, Action Action)>>? GlobalShortcuts { get; set; }

    private static TimeSpan DwellFor(FlyoutSettings f) => TimeSpan.FromMilliseconds(f.EdgeDelay switch
    {
        "instant" => 40,
        "long" => 500,
        _ => 180,
    });

    public void Rebuild()
    {
        DisposeHosts();

        foreach (var f in _settings.Flyouts.Where(f => f.Enabled))
        {
            try
            {
                var instances = _settings.Widgets.Where(w => w.FlyoutId == f.Id && w.Enabled).ToList();
                var window = new FlyoutWindow(f, instances, _menuFor, () => _openSettings?.Invoke());

                TrayIcon? tray = null;
                if (f.TrayIcon)
                {
                    tray = new TrayIcon { Icon = TrayHost.LoadIcon(f.Icon), ToolTipText = $"Mantel — {f.Name}" };
                    var w = window;
                    var settings = f;
                    tray.Clicked += (_, _) => ToggleFromTaskbar(w, settings);
                    TrayHost.Icons.Add(tray);
                }

                _hosts.Add(new Host(f, window, tray));
            }
            catch (Exception ex) { Log.Error($"Flyout '{f.Name}' failed to set up.", ex); }
        }

        if (_hosts.Any(h => h.Settings.Edge is "left" or "right" or "top")) _edgeTimer.Start();
        else _edgeTimer.Stop();

        // Keyboard shortcuts toggle their flyout, like clicking its taskbar icon.
        _hotkeys.SetAll(_hosts
            .Where(h => !string.IsNullOrEmpty(h.Settings.Hotkey))
            .Select(h => (h.Settings.Hotkey, (Action)(() => ToggleFromTaskbar(h.Window, h.Settings, fromShortcut: true))))
            .Concat(GlobalShortcuts?.Invoke() ?? Array.Empty<(string, Action)>()));
    }

    /// <summary>Opens a flyout from code (e.g. Settings' "Try it" button).</summary>
    public void Open(string flyoutId)
    {
        var host = _hosts.FirstOrDefault(h => h.Settings.Id == flyoutId);
        if (host is null) return;
        var origin = OriginFor(host.Settings);
        CloseOthers(host.Window);
        // Pinned: opened on purpose (from Settings or the tray menu), so it stays until clicked away
        // instead of sliding shut because the mouse isn't on it.
        host.Window.OpenFlyout(origin, Cursor(), pinned: true);
    }

    public void CloseAll()
    {
        foreach (var h in _hosts) h.Window.CloseFlyout();
    }

    private void ToggleFromTaskbar(FlyoutWindow window, FlyoutSettings settings, bool fromShortcut = false)
    {
        if (window.IsOpen) { window.CloseFlyout(); return; }
        if (fromShortcut)
        {
            CloseOthers(window);
            window.OpenFlyout(OriginFor(settings), Cursor(), pinned: true);
            return;
        }
        // Clicking the tray icon first takes focus away from an open flyout, which closes it;
        // don't treat that same click as "open it again".
        if (DateTime.UtcNow - window.ClosedAt < TimeSpan.FromMilliseconds(350)) return;
        CloseOthers(window);
        // The flyout's screen edge decides where it appears, however it's opened. With no edge set,
        // it pops up above the clock like Windows' own flyouts.
        var origin = OriginFor(settings);
        window.OpenFlyout(origin, Cursor(), pinned: origin != FlyoutOrigin.Taskbar);
    }

    private static FlyoutOrigin OriginFor(FlyoutSettings f) => f.Edge switch
    {
        "left" => FlyoutOrigin.Left,
        "right" => FlyoutOrigin.Right,
        "top" => FlyoutOrigin.Top,
        _ => FlyoutOrigin.Taskbar,
    };

    private void CloseOthers(FlyoutWindow keep)
    {
        foreach (var h in _hosts) if (!ReferenceEquals(h.Window, keep)) h.Window.CloseFlyout();
    }

    // --- Screen edges -------------------------------------------------------------------

    private void PollEdges()
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = Cursor();

        // An edge flyout that's open closes once the mouse has wandered off it for a moment.
        var open = _hosts.FirstOrDefault(h => h.Window.IsOpen && h.Window.Origin != FlyoutOrigin.Taskbar && !h.Window.IsPinned);
        if (open is not null)
        {
            if (open.Window.Contains(p, slack: 24) || AtEdge(open, p)) _outsideSince = DateTime.MaxValue;
            else if (_outsideSince == DateTime.MaxValue) _outsideSince = DateTime.UtcNow;
            else if (DateTime.UtcNow - _outsideSince > LeaveDelay)
            {
                open.Window.CloseFlyout();
                _outsideSince = DateTime.MaxValue;
            }
            return;
        }

        // Pushing against an edge (not while dragging or in a full-screen game) opens its flyout.
        if (NativeMethods.IsMouseButtonDown() || _isFullscreen())
        {
            _edgeCandidate = null;
            return;
        }

        var hit = _hosts.FirstOrDefault(h => AtEdge(h, p));
        if (hit is null) { _edgeCandidate = null; return; }
        if (!ReferenceEquals(hit, _edgeCandidate))
        {
            _edgeCandidate = hit;
            _edgeSince = DateTime.UtcNow;
            return;
        }
        if (DateTime.UtcNow - _edgeSince < DwellFor(hit.Settings)) return;

        _edgeCandidate = null;
        _outsideSince = DateTime.MaxValue;
        CloseOthers(hit.Window);
        hit.Window.OpenFlyout(hit.Settings.Edge switch
        {
            "left" => FlyoutOrigin.Left,
            "top" => FlyoutOrigin.Top,
            _ => FlyoutOrigin.Right,
        }, p);
    }

    /// <summary>
    /// Is the cursor touching this flyout's edge? Only outer edges count: on two side-by-side
    /// monitors the shared edge is where the mouse crosses over, so it never triggers.
    /// </summary>
    private static bool AtEdge(Host h, PixelPoint p)
    {
        var screens = h.Window.Screens;
        var screen = screens.ScreenFromPoint(p);
        if (screen is null) return false;
        var b = screen.Bounds;
        const int zone = 2;

        return h.Settings.Edge switch
        {
            "right" => p.X >= b.Right - zone && screens.ScreenFromPoint(new PixelPoint(b.Right + 4, p.Y)) is null,
            "left" => p.X <= b.X + zone - 1 && screens.ScreenFromPoint(new PixelPoint(b.X - 4, p.Y)) is null,
            "top" => p.Y <= b.Y + zone - 1 && screens.ScreenFromPoint(new PixelPoint(p.X, b.Y - 4)) is null,
            _ => false,
        };
    }

    private static PixelPoint Cursor() =>
        OperatingSystem.IsWindows() && NativeMethods.GetCursorPos(out var pt) ? new PixelPoint(pt.X, pt.Y) : default;

    // --- Cleanup ---------------------------------------------------------------------------

    private void DisposeHosts()
    {
        _edgeTimer.Stop();
        foreach (var h in _hosts)
        {
            if (h.Tray is not null) TrayHost.Icons.Remove(h.Tray); // removing it disposes it
            h.Window.DisposeWidgets();
            h.Window.Close();
        }
        _hosts.Clear();
        _edgeCandidate = null;
    }

    public void Dispose()
    {
        DisposeHosts();
        _hotkeys.Dispose();
    }
}
