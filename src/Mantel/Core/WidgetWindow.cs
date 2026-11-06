using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// The borderless, transparent window that hosts one widget on the desktop.
/// It stays at the bottom of the window stack, out of the taskbar and Alt+Tab.
/// </summary>
public sealed class WidgetWindow : Window
{
    private readonly WidgetInstanceSettings _settings;
    private readonly Border _card;
    private readonly DispatcherTimer _saveDebounce;
    private bool _locked;

    /// <summary>Raised (debounced) after the user drags the widget somewhere new.</summary>
    public event Action? Moved;

    public WidgetWindow(IWidget widget, WidgetInstanceSettings settings, bool locked, ContextMenu menu)
    {
        _settings = settings;

        Title = "Mantel widget";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _card = new Border { Child = widget.View, ContextMenu = menu };
        _card.Classes.Add("card");
        Content = _card;

        SetLocked(locked);

        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            _settings.X = Position.X;
            _settings.Y = Position.Y;
            Moved?.Invoke();
        };

        PointerPressed += OnPointerPressed;
        PositionChanged += (_, _) => { if (IsVisible) _saveDebounce.Start(); };
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
        if (_settings.X is int x && _settings.Y is int y && IsOnSomeScreen(new PixelPoint(x, y)))
        {
            Position = new PixelPoint(x, y);
            return;
        }

        // Default: stack down the right side of the primary screen.
        var area = (Screens.Primary ?? Screens.All.FirstOrDefault())?.WorkingArea
                   ?? new PixelRect(0, 0, 1920, 1080);
        var scale = (Screens.Primary?.Scaling) ?? 1.0;
        var width = (int)(300 * scale);
        var step = (int)(150 * scale);
        Position = new PixelPoint(area.Right - width - (int)(24 * scale),
                                  area.Y + (int)(24 * scale) + fallbackIndex * step);
    }

    private bool IsOnSomeScreen(PixelPoint p) =>
        Screens.All.Any(s => s.Bounds.Contains(p));

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
    }

    private void ApplyDesktopBehavior()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.MakeToolWindow(hwnd);
        NativeMethods.SendToBottom(hwnd);
    }

    private void SendToBottom()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero) NativeMethods.SendToBottom(hwnd);
    }
}
