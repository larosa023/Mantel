using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// Tells Mantel when nobody can see the desktop: the PC is locked, or the screens are off.
/// Windows sends these as messages to a hidden window, so watching costs nothing.
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    private const uint WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_POWERBROADCAST = 0x0218;
    private const int WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint NOTIFY_FOR_THIS_SESSION = 0;
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    // GUID_CONSOLE_DISPLAY_STATE: 0 = off, 1 = on, 2 = dimmed.
    private static readonly Guid DisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly SubclassProc _proc;
    private Window? _sink;
    private IntPtr _hwnd;
    private IntPtr _powerHandle;

    public bool IsLocked { get; private set; }
    public bool IsDisplayOff { get; private set; }

    /// <summary>True while the desktop can't be seen (locked or screens off).</summary>
    public bool IsAway => IsLocked || IsDisplayOff;

    public event Action? Changed;

    public SessionWatcher() => _proc = WndProc;

    public void Start()
    {
        if (!OperatingSystem.IsWindows() || _hwnd != IntPtr.Zero) return;
        try
        {
            _sink = new Window { Width = 1, Height = 1, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None };
            _hwnd = _sink.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_hwnd == IntPtr.Zero) return;
            SetWindowSubclass(_hwnd, _proc, new UIntPtr(0x53455353), UIntPtr.Zero); // "SESS"
            WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION);
            var guid = DisplayState;
            _powerHandle = RegisterPowerSettingNotification(_hwnd, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't watch for lock and screen-off; widgets will keep running then.", ex);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_WTSSESSION_CHANGE)
        {
            var code = wParam.ToInt32();
            if (code == WTS_SESSION_LOCK) Set(locked: true, displayOff: IsDisplayOff);
            else if (code == WTS_SESSION_UNLOCK) Set(locked: false, displayOff: IsDisplayOff);
        }
        else if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == DisplayState)
                Set(locked: IsLocked, displayOff: setting.Data == 0);
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void Set(bool locked, bool displayOff)
    {
        if (locked == IsLocked && displayOff == IsDisplayOff) return;
        var wasAway = IsAway;
        IsLocked = locked;
        IsDisplayOff = displayOff;
        if (wasAway != IsAway)
        {
            Log.Info(IsAway ? $"Pausing widgets ({(IsLocked ? "locked" : "screens off")})." : "Resuming widgets.");
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }
    }

    public void Dispose()
    {
        try
        {
            if (_hwnd != IntPtr.Zero) WTSUnRegisterSessionNotification(_hwnd);
            if (_powerHandle != IntPtr.Zero) UnregisterPowerSettingNotification(_powerHandle);
        }
        catch { /* shutting down */ }
        _sink?.Close();
    }
}
