using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Flyouts;

/// <summary>Where a flyout comes from: a screen edge, or the taskbar (its tray icon).</summary>
public enum FlyoutOrigin { Left, Right, Top, Taskbar }

/// <summary>
/// A panel of widget cards that slides in from a screen edge or pops up above the taskbar.
/// Widgets inside are created the first time it opens and only run while it's open.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private static readonly TimeSpan AnimationLength = TimeSpan.FromMilliseconds(170);

    private readonly FlyoutSettings _flyout;
    private readonly IReadOnlyList<WidgetInstanceSettings> _instances;
    private readonly Func<WidgetInstanceSettings, ContextMenu> _menuFor;
    private readonly List<IWidget> _widgets = new();
    private readonly StackPanel _stack = new() { Spacing = -16 }; // cards' shadow margins overlap a bit
    private readonly TranslateTransform _slide = new();
    private readonly Border _root;
    private DispatcherTimer? _animation;
    private bool _built;

    public FlyoutOrigin Origin { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>When it last closed; lets a tray click that closed it (by stealing focus) not reopen it.</summary>
    public DateTime ClosedAt { get; private set; }

    public FlyoutWindow(FlyoutSettings flyout, IReadOnlyList<WidgetInstanceSettings> instances,
                        Func<WidgetInstanceSettings, ContextMenu> menuFor)
    {
        _flyout = flyout;
        _instances = instances;
        _menuFor = menuFor;

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

        _root = new Border { Child = _stack, RenderTransform = _slide, Background = Brushes.Transparent };
        Content = _root;

        // Opened from the taskbar it takes focus, so clicking anywhere else closes it (like Windows' own flyouts).
        Deactivated += (_, _) => { if (IsOpen && Origin == FlyoutOrigin.Taskbar) CloseFlyout(); };
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero && OperatingSystem.IsWindows())
                NativeMethods.MakeToolWindow(hwnd);
        };
    }

    public bool HasWidgets => _instances.Count > 0;

    /// <summary>Opens on the screen under <paramref name="cursor"/> (physical pixels).</summary>
    public void OpenFlyout(FlyoutOrigin origin, PixelPoint cursor)
    {
        if (IsOpen) { if (origin == FlyoutOrigin.Taskbar) Activate(); return; }
        Origin = origin;
        BuildOnce();

        _stack.Orientation = origin == FlyoutOrigin.Top ? Orientation.Horizontal : Orientation.Vertical;
        _stack.Spacing = -16;
        ShowActivated = origin == FlyoutOrigin.Taskbar;
        Opacity = 0;
        IsOpen = true;

        foreach (var w in _widgets) SafeRun(w.Start, "start");
        Show();
        if (origin == FlyoutOrigin.Taskbar) Activate();

        // Size is known only after the first layout pass: place it then, and slide it in.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsOpen) return;
            PlaceAt(cursor);
            Animate(opening: true);
        }, DispatcherPriority.Render);
    }

    public void CloseFlyout()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ClosedAt = DateTime.UtcNow;
        Animate(opening: false, done: () =>
        {
            if (IsOpen) return; // reopened mid-animation
            Hide();
            foreach (var w in _widgets) SafeRun(w.Stop, "stop");
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
        foreach (var w in _widgets)
        {
            SafeRun(w.Stop, "stop");
            SafeRun(w.Dispose, "dispose");
        }
        _widgets.Clear();
    }

    private void BuildOnce()
    {
        if (_built) return;
        _built = true;

        foreach (var s in _instances)
        {
            var d = WidgetRegistry.Find(s.Type);
            if (d is null) continue;
            try
            {
                var widget = d.Create(s);
                _widgets.Add(widget);
                _stack.Children.Add(WidgetCard.Create(widget.View, _menuFor(s)));
            }
            catch (Exception ex) { Log.Error($"Flyout widget '{s.Type}' failed to start.", ex); }
        }

        if (_widgets.Count == 0)
        {
            var hint = new TextBlock { Text = $"{_flyout.Name} is empty.\nAdd widgets to it in Mantel settings.", TextWrapping = TextWrapping.Wrap };
            hint.Classes.Add("muted");
            _stack.Children.Add(WidgetCard.Create(new Border { Child = hint, Width = 240 }));
        }
    }

    private void PlaceAt(PixelPoint cursor)
    {
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        // The cards sit inside a transparent shadow margin; pull the window out by it so cards meet the edge.
        var inset = (int)(4 * screen.Scaling);

        int x, y;
        switch (Origin)
        {
            case FlyoutOrigin.Left:
                x = area.X - inset;
                y = Clamp(cursor.Y - size.Height / 2, area.Y, area.Bottom - size.Height);
                break;
            case FlyoutOrigin.Right:
                x = area.Right - size.Width + inset;
                y = Clamp(cursor.Y - size.Height / 2, area.Y, area.Bottom - size.Height);
                break;
            case FlyoutOrigin.Top:
                x = Clamp(cursor.X - size.Width / 2, area.X, area.Right - size.Width);
                y = area.Y - inset;
                break;
            default:
                // Next to the taskbar, wherever it is (bottom on most PCs).
                if (area.Bottom < bounds.Bottom) { x = area.Right - size.Width; y = area.Bottom - size.Height; }
                else if (area.Y > bounds.Y) { x = area.Right - size.Width; y = area.Y; }
                else if (area.X > bounds.X) { x = area.X; y = area.Bottom - size.Height; }
                else { x = area.Right - size.Width; y = area.Bottom - size.Height; }
                break;
        }
        Position = new PixelPoint(x, y);
    }

    private static int Clamp(int v, int min, int max) => max < min ? min : Math.Clamp(v, min, max);

    /// <summary>Slide + fade, by hand: Window position isn't animatable, so the content moves instead.</summary>
    private void Animate(bool opening, Action? done = null)
    {
        _animation?.Stop();
        var distance = Origin switch
        {
            FlyoutOrigin.Left => new Vector(-40, 0),
            FlyoutOrigin.Right => new Vector(40, 0),
            FlyoutOrigin.Top => new Vector(0, -40),
            _ => new Vector(0, 24),
        };
        var started = DateTime.UtcNow;
        var from = opening ? 0.0 : 1.0;
        var to = opening ? 1.0 : 0.0;

        void Step()
        {
            var t = Math.Clamp((DateTime.UtcNow - started) / AnimationLength, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3); // ease out
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

        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Step());
        Step();
        _animation.Start();
    }

    private static void SafeRun(Action a, string what)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"Flyout widget failed to {what}.", ex); }
    }
}
