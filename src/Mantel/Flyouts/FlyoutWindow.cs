using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Flyouts;

/// <summary>Where a flyout comes from: a screen edge, or the taskbar (its tray icon).</summary>
public enum FlyoutOrigin { Left, Right, Top, Taskbar }

/// <summary>
/// The window that slides a <see cref="FlyoutPanel"/> in. Edge flyouts run the full height of
/// the screen like a side panel; taskbar flyouts pop up above the clock like Windows' own.
/// Widgets inside are created the first time it opens and only run while it's open.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private static readonly TimeSpan OpenLength = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan CloseLength = TimeSpan.FromMilliseconds(150);
    private const double Shadow = 14; // the panel's transparent margin for its shadow

    private readonly FlyoutPanel _panel;
    private readonly TranslateTransform _slide = new();
    private DispatcherTimer? _animation;

    public FlyoutOrigin Origin { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>Opened from Settings' "Open it now": stays open until it loses focus, even if it's an edge flyout.</summary>
    public bool IsPinned { get; private set; }

    private PixelPoint _anchor;
    private bool _placed;

    /// <summary>When it last closed; lets a tray click that closed it (by stealing focus) not reopen it.</summary>
    public DateTime ClosedAt { get; private set; }

    public FlyoutWindow(FlyoutSettings flyout, IReadOnlyList<WidgetInstanceSettings> instances,
                        Func<WidgetInstanceSettings, ContextMenu> menuFor, Action openSettings)
    {
        Title = $"Mantel — {flyout.Name}";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WidgetCard.ApplyFont(this);

        _panel = new FlyoutPanel(flyout, instances, menuFor, () => { CloseFlyout(); openSettings(); })
        {
            RenderTransform = _slide,
        };
        Content = _panel;

        // Opened from the taskbar (or pinned) it takes focus, so clicking anywhere else closes it, like Windows' own flyouts.
        Deactivated += (_, _) => { if (IsOpen && (Origin == FlyoutOrigin.Taskbar || IsPinned)) CloseFlyout(); };

        // Content can grow after it opens (scores and weather load a moment later). Re-anchor so a
        // taskbar flyout grows upward instead of running off the bottom of the screen.
        _panel.SizeChanged += (_, _) => { if (IsOpen && _placed) PlaceAt(_anchor); };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) CloseFlyout(); };
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero && OperatingSystem.IsWindows())
                NativeMethods.MakeToolWindow(hwnd);
        };
    }

    /// <summary>Opens on the screen under <paramref name="cursor"/> (physical pixels).</summary>
    public void OpenFlyout(FlyoutOrigin origin, PixelPoint cursor, bool pinned = false)
    {
        if (IsOpen) { if (origin == FlyoutOrigin.Taskbar || pinned) Activate(); return; }
        Origin = origin;
        IsPinned = pinned;
        _anchor = cursor;
        _placed = false;
        _panel.BuildWidgets();
        _panel.RefreshHeader();
        _panel.Horizontal = origin == FlyoutOrigin.Top;

        // Size it for the screen it's opening on: side panels fill the height, others cap at it.
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var workHeight = screen.WorkingArea.Height / screen.Scaling;
            if (origin is FlyoutOrigin.Left or FlyoutOrigin.Right)
                _panel.SetHeight(workHeight - Shadow * 2 + 8);
            else
            {
                _panel.SetHeight(null);
                _panel.SetMaxHeight(workHeight - Shadow * 2);
            }
        }

        ShowActivated = origin == FlyoutOrigin.Taskbar || pinned;
        Opacity = 0;
        IsOpen = true;

        foreach (var w in _panel.Widgets) SafeRun(w.Start, "start");
        Show();
        if (ShowActivated) Activate();

        // Size is known only after the first layout pass: place it then, and slide it in.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsOpen) return;
            PlaceAt(cursor);
            _placed = true;
            Animate(opening: true);
        }, DispatcherPriority.Render);
    }

    public void CloseFlyout()
    {
        if (!IsOpen) return;
        IsOpen = false;
        IsPinned = false;
        ClosedAt = DateTime.UtcNow;
        Animate(opening: false, done: () =>
        {
            if (IsOpen) return; // reopened mid-animation
            Hide();
            foreach (var w in _panel.Widgets) SafeRun(w.Stop, "stop");
        });
    }

    /// <summary>Is a screen point (physical pixels) over the visible flyout, with some slack?</summary>
    public bool Contains(PixelPoint p, int slack)
    {
        if (!IsOpen) return false;
        var scale = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;
        var size = PixelSize.FromSize(Bounds.Size, scale);
        var rect = new PixelRect(Position, size);
        return new PixelRect(rect.X - slack, rect.Y - slack, rect.Width + slack * 2, rect.Height + slack * 2).Contains(p);
    }

    public void DisposeWidgets()
    {
        foreach (var w in _panel.Widgets)
        {
            SafeRun(w.Stop, "stop");
            SafeRun(w.Dispose, "dispose");
        }
    }

    private void PlaceAt(PixelPoint cursor)
    {
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        // The panel sits inside a transparent shadow margin; tuck most of it off-screen at an edge
        // so the panel itself lands a few pixels from the edge.
        var tuck = (int)((Shadow - 6) * screen.Scaling);

        int x, y;
        switch (Origin)
        {
            case FlyoutOrigin.Left:
                x = area.X - tuck;
                y = area.Y + (area.Height - size.Height) / 2;
                break;
            case FlyoutOrigin.Right:
                x = area.Right - size.Width + tuck;
                y = area.Y + (area.Height - size.Height) / 2;
                break;
            case FlyoutOrigin.Top:
                x = Clamp(cursor.X - size.Width / 2, area.X, area.Right - size.Width);
                y = area.Y - tuck;
                break;
            default:
                // Next to the taskbar, wherever it is (bottom on most PCs), at the clock's end.
                if (area.Y > bounds.Y) { x = area.Right - size.Width + tuck; y = area.Y - tuck; }
                else if (area.X > bounds.X) { x = area.X - tuck; y = area.Bottom - size.Height + tuck; }
                else { x = area.Right - size.Width + tuck; y = area.Bottom - size.Height + tuck; }
                break;
        }
        // Never let any part of the panel itself go off the working area.
        x = Clamp(x, area.X - tuck, area.Right - size.Width + tuck);
        y = Clamp(y, area.Y - tuck, area.Bottom - size.Height + tuck);
        Position = new PixelPoint(x, y);
    }

    private static int Clamp(int v, int min, int max) => max < min ? min : Math.Clamp(v, min, max);

    /// <summary>Slide + fade, by hand: Window position isn't animatable, so the content moves instead.</summary>
    private void Animate(bool opening, Action? done = null)
    {
        _animation?.Stop();
        var distance = Origin switch
        {
            FlyoutOrigin.Left => new Vector(-48, 0),
            FlyoutOrigin.Right => new Vector(48, 0),
            FlyoutOrigin.Top => new Vector(0, -40),
            _ => new Vector(0, 28),
        };
        var length = opening ? OpenLength : CloseLength;
        var started = DateTime.UtcNow;
        var from = Opacity;
        var to = opening ? 1.0 : 0.0;

        void Step()
        {
            var t = Math.Clamp((DateTime.UtcNow - started) / length, 0, 1);
            // Opening decelerates into place; closing accelerates away.
            var eased = opening ? 1 - Math.Pow(1 - t, 4) : t * t;
            var progress = from + (to - from) * eased;
            Opacity = progress;
            _slide.X = distance.X * (1 - progress);
            _slide.Y = distance.Y * (1 - progress);
            if (t >= 1)
            {
                _animation?.Stop();
                done?.Invoke();
            }
        }

        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, (_, _) => Step());
        Step();
        _animation.Start();
    }

    private static void SafeRun(Action a, string what)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"Flyout widget failed to {what}.", ex); }
    }
}
