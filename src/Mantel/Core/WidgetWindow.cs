using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Mantel.Core;

/// <summary>
/// The borderless, transparent window that hosts one widget on the desktop.
/// It stays at the bottom of the window stack, out of the taskbar and Alt+Tab.
/// </summary>
public sealed class WidgetWindow : Window
{
    private const int GridSize = 8;         // snap grid, in logical pixels
    private const int EdgeMagnet = 20;   // how close to a screen edge before it sticks

    private readonly WidgetInstanceSettings _settings;
    private readonly Func<bool> _snapEnabled;
    private readonly Border _card;
    private readonly DispatcherTimer _settle;
    private bool _locked;
    private bool _snapping;
    private bool _dragged;

    /// <summary>Raised (debounced) after the user drags the widget somewhere new.</summary>
    public event Action? Moved;

    public WidgetWindow(IWidget widget, WidgetInstanceSettings settings, bool locked,
                        Func<bool> snapEnabled, ContextMenu menu)
    {
        _settings = settings;
        _snapEnabled = snapEnabled;

        Title = "Mantel widget";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Mantel.UI.WidgetCard.ApplyFont(this);
        _card = Mantel.UI.WidgetCard.Create(widget.View, menu);
        // Appearance → Widget size scales desktop widgets as a whole (text, art and spacing together).
        var scale = Mantel.Theme.ThemeManager.WidgetScale;
        Content = Math.Abs(scale - 1) < 0.001
            ? _card
            : new LayoutTransformControl { LayoutTransform = new Avalonia.Media.ScaleTransform(scale, scale), Child = _card };

        SetLocked(locked);

        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _settle.Tick += (_, _) => { _settle.Stop(); OnSettled(); };

        PointerPressed += OnPointerPressed;
        PositionChanged += (_, _) => { if (IsVisible && !_snapping) _settle.Start(); };
        Opened += (_, _) => ApplyDesktopBehavior();
        // Clicking a widget activates it, which raises it above other windows. Push it back down.
        Activated += (_, _) => Dispatcher.UIThread.Post(SendToBottom, DispatcherPriority.Background);
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        _card.Classes.Set("unlocked", !locked);
        Cursor = locked ? Cursor.Default : new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Restores the saved position, or picks one if it's missing or off-screen.</summary>
    public void PlaceOnScreen(int fallbackIndex)
    {
        // A widget snapped to a screen edge has its window (with the card's transparent shadow margin)
        // a few pixels off-screen, so judge by where the card itself is, with some slack.
        if (_settings.X is int x && _settings.Y is int y &&
            Screens.All.Any(s => NearScreen(s.Bounds, x + 40, y + 40)))
        {
            _snapping = true; // a restore, not a drag: don't snap or re-save it
            Position = new PixelPoint(x, y);
            _snapping = false;
            return;
        }

        // Default: stack down the right side of the primary screen.
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;
        Position = new PixelPoint(
            area.Right - (int)(300 * scale) - (int)(24 * scale),
            area.Y + (int)(24 * scale) + fallbackIndex * (int)(150 * scale));
    }

    /// <summary>After a drag settles: snap to the grid and screen edges, then save.</summary>
    private void OnSettled()
    {
        // Only the user's own drags snap and save; Mantel moving a widget (restoring it after a
        // theme change, say) leaves its saved spot alone.
        if (!_dragged) return;
        _dragged = false;

        if (_snapEnabled() && !_locked)
        {
            var snapped = Snap(Position);
            if (snapped != Position)
            {
                _snapping = true;
                Position = snapped;
                _snapping = false;
            }
        }

        _settings.X = Position.X;
        _settings.Y = Position.Y;
        Moved?.Invoke();
    }

    private static bool NearScreen(PixelRect b, int px, int py) =>
        px >= b.X - 80 && px <= b.Right + 80 && py >= b.Y - 80 && py <= b.Bottom + 80;

    private PixelPoint Snap(PixelPoint p)
    {
        var screen = Screens.ScreenFromPoint(p) ?? Screens.Primary;
        if (screen is null) return p;
        var scale = screen.Scaling;
        var grid = Math.Max(1, (int)Math.Round(GridSize * scale));
        var magnet = (int)(EdgeMagnet * scale);
        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(Bounds.Size, scale);

        int x = (int)Math.Round((double)p.X / grid) * grid;
        int y = (int)Math.Round((double)p.Y / grid) * grid;

        // The card has a transparent margin for its shadow; line the visible card up with the edge.
        var inset = (int)(_card.Margin.Left * scale);
        if (Math.Abs(p.X + inset - area.X) < magnet) x = area.X - inset;
        if (Math.Abs(p.Y + inset - area.Y) < magnet) y = area.Y - inset;
        if (Math.Abs(p.X + size.Width - inset - area.Right) < magnet) x = area.Right - size.Width + inset;
        if (Math.Abs(p.Y + size.Height - inset - area.Bottom) < magnet) y = area.Bottom - size.Height + inset;

        return new PixelPoint(x, y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Let buttons inside widgets (play/pause) work even when unlocked.
        if (e.Source is Visual v && (v.FindAncestorOfType<Button>(includeSelf: true) is not null ||
                                     v.FindAncestorOfType<TextBox>(includeSelf: true) is not null)) return;
        _dragged = true;
        BeginMoveDrag(e);
    }

    private void ApplyDesktopBehavior()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.MakeToolWindow(hwnd);
        DesktopLayer.Pin(hwnd); // stays under every normal window, even when clicked or re-shown
    }

    private void SendToBottom()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero) NativeMethods.SendToBottom(hwnd);
    }
}
