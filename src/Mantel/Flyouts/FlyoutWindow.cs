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
/// The window that brings a <see cref="FlyoutPanel"/> on screen, with the motion and placement
/// chosen in the flyout's settings (slide, fade, zoom or spring; aligned to the start, center or
/// end of its edge; full height or fitted). Widgets inside are created the first time it opens
/// and only run while it's open.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private const double Shadow = 14; // the panel's transparent margin for its shadow

    private readonly FlyoutSettings _flyout;
    private readonly FlyoutPanel _panel;
    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private DispatcherTimer? _animation;
    private PixelPoint _anchor;
    private bool _placed;

    public FlyoutOrigin Origin { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>Opened on purpose (taskbar icon, shortcut, Settings): stays open until it loses focus.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>When it last closed; lets a tray click that closed it (by stealing focus) not reopen it.</summary>
    public DateTime ClosedAt { get; private set; }

    public FlyoutWindow(FlyoutSettings flyout, IReadOnlyList<WidgetInstanceSettings> instances,
                        Func<WidgetInstanceSettings, ContextMenu> menuFor, Action openSettings)
    {
        _flyout = flyout;

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
            RenderTransform = new TransformGroup { Children = { _scale, _slide } },
        };
        Content = _panel;

        // Opened on purpose it takes focus, so clicking anywhere else closes it, like Windows' own flyouts.
        Deactivated += (_, _) => { if (IsOpen && (Origin == FlyoutOrigin.Taskbar || IsPinned)) CloseFlyout(); };

        // Content can grow after it opens (scores and weather load a moment later). Re-anchor so it
        // stays aligned (a taskbar flyout grows upward instead of running off the screen).
        _panel.SizeChanged += (_, _) => { if (IsOpen && _placed) PlaceAt(_anchor); };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) CloseFlyout(); };
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero && OperatingSystem.IsWindows())
                NativeMethods.MakeToolWindow(hwnd);
        };
    }

    /// <summary>
    /// Opens it. <paramref name="cursor"/> (physical pixels) picks the screen for edge triggers and
    /// where a top flyout following the mouse lands.
    /// </summary>
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

        // Size it for the screen it's opening on: full-height side panels fill it, others cap at it.
        var screen = TargetScreen(cursor);
        if (screen is not null)
        {
            var workHeight = screen.WorkingArea.Height / screen.Scaling;
            var room = workHeight - Shadow * 2 + (Shadow - Gap) * 2;
            if (origin is FlyoutOrigin.Left or FlyoutOrigin.Right && _flyout.FullHeight)
                _panel.SetHeight(room);
            else
            {
                _panel.SetHeight(null);
                _panel.SetMaxHeight(room);
            }
        }

        ShowActivated = origin == FlyoutOrigin.Taskbar || pinned;
        Opacity = 0;
        IsOpen = true;

        foreach (var w in _panel.Widgets) SafeRun(w.Start, "start");
        Show();
        if (ShowActivated) Activate();

        // Size is known only after the first layout pass: place it then, and animate it in.
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

    // --- Placement -----------------------------------------------------------------------------

    private double Gap => Math.Clamp(_flyout.Gap, 0, 64);

    /// <summary>Edge triggers use the screen being touched; opened on purpose, the flyout's monitor setting decides.</summary>
    private Avalonia.Platform.Screen? TargetScreen(PixelPoint cursor)
    {
        if (IsPinned && _flyout.Monitor == "primary" && Screens.Primary is { } primary) return primary;
        return Screens.ScreenFromPoint(cursor) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
    }

    private void PlaceAt(PixelPoint cursor)
    {
        var screen = TargetScreen(cursor);
        if (screen is null) return;
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        // The panel sits inside a transparent shadow margin; tuck part of it off-screen so the
        // panel itself lands exactly `Gap` pixels from the edge.
        var tuck = (int)((Shadow - Gap) * screen.Scaling);
        var align = _flyout.Align;

        // Where along an edge: start, center or end of the screen, or (auto/mouse) under the mouse.
        int Along(int start, int length, int itemSize, int mouse, bool followMouseOnAuto)
        {
            var lo = start - tuck;
            var hi = start + length - itemSize + tuck;
            return align switch
            {
                "start" => lo,
                "end" => hi,
                "center" => start + (length - itemSize) / 2,
                _ => followMouseOnAuto ? Clamp(mouse - itemSize / 2, lo, hi) : start + (length - itemSize) / 2,
            };
        }

        int x, y;
        switch (Origin)
        {
            case FlyoutOrigin.Left:
                x = area.X - tuck;
                y = Along(area.Y, area.Height, size.Height, cursor.Y, followMouseOnAuto: false);
                break;
            case FlyoutOrigin.Right:
                x = area.Right - size.Width + tuck;
                y = Along(area.Y, area.Height, size.Height, cursor.Y, followMouseOnAuto: false);
                break;
            case FlyoutOrigin.Top:
                // Pushed against the edge it follows the mouse; opened on purpose it's centered.
                x = Along(area.X, area.Width, size.Width, cursor.X, followMouseOnAuto: !IsPinned);
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

    // --- Motion --------------------------------------------------------------------------------

    private double SpeedFactor => _flyout.Speed switch { "fast" => 0.6, "relaxed" => 1.6, _ => 1.0 };

    /// <summary>
    /// Animates in or out by hand (a window's position isn't animatable, so the content moves).
    /// slide: glides in from its edge. fade: just fades. zoom: grows from its edge.
    /// spring: slides in and settles with a little overshoot. none: appears instantly.
    /// </summary>
    private void Animate(bool opening, Action? done = null)
    {
        _animation?.Stop();
        var style = _flyout.Animation;

        // Grow and slide from the side the flyout is attached to.
        var (dir, origin) = Origin switch
        {
            FlyoutOrigin.Left => (new Vector(-1, 0), new RelativePoint(0, 0.5, RelativeUnit.Relative)),
            FlyoutOrigin.Right => (new Vector(1, 0), new RelativePoint(1, 0.5, RelativeUnit.Relative)),
            FlyoutOrigin.Top => (new Vector(0, -1), new RelativePoint(0.5, 0, RelativeUnit.Relative)),
            _ => (new Vector(0, 1), new RelativePoint(1, 1, RelativeUnit.Relative)),
        };
        _panel.RenderTransformOrigin = origin;

        if (style == "none")
        {
            Apply(opening ? 1 : 0, 0, 1);
            done?.Invoke();
            return;
        }

        var baseMs = style switch { "spring" => 420, "zoom" => 220, "fade" => 180, _ => 240 };
        var length = TimeSpan.FromMilliseconds((opening ? baseMs : baseMs * 0.65) * SpeedFactor);
        var distance = style == "spring" ? 64 : 48;
        var started = DateTime.UtcNow;
        var from = Opacity;
        var to = opening ? 1.0 : 0.0;

        void Step()
        {
            var t = Math.Clamp((DateTime.UtcNow - started) / length, 0, 1);
            var fade = from + (to - from) * (opening ? EaseOut(Math.Min(1, t * 1.6)) : t);

            // How far from "in place" it is: 1 = fully away, 0 = settled. Spring overshoots below 0.
            double away = opening
                ? 1 - (style == "spring" ? EaseOutBack(t) : EaseOut(t))
                : t * t;

            switch (style)
            {
                case "fade":
                    Apply(fade, 0, 1);
                    break;
                case "zoom":
                    Apply(fade, 0, 1 - 0.12 * away);
                    break;
                default: // slide, spring
                    Apply(fade, away * distance, 1);
                    break;
            }

            if (t >= 1)
            {
                _animation?.Stop();
                Apply(to, 0, 1);
                done?.Invoke();
            }
        }

        void Apply(double opacity, double offset, double scale)
        {
            Opacity = Math.Clamp(opacity, 0, 1);
            _slide.X = dir.X * offset;
            _slide.Y = dir.Y * offset;
            _scale.ScaleX = scale;
            _scale.ScaleY = scale;
        }

        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, (_, _) => Step());
        Step();
        _animation.Start();
    }

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 4);

    /// <summary>Overshoots a little past 1, then settles back.</summary>
    private static double EaseOutBack(double t)
    {
        const double c1 = 1.55, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static void SafeRun(Action a, string what)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"Flyout widget failed to {what}.", ex); }
    }
}
