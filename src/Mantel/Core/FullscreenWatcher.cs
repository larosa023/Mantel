using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// Polls Windows every few seconds for "a full-screen app is running" (games, videos,
/// presentations). One cheap shell call per poll; nothing runs while the feature is off.
/// </summary>
public sealed class FullscreenWatcher
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public FullscreenWatcher()
    {
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsFullscreen { get; private set; }

    public event Action<bool>? Changed;

    public void SetEnabled(bool enabled)
    {
        if (enabled) { _timer.Start(); Poll(); }
        else
        {
            _timer.Stop();
            Set(false);
        }
    }

    private void Poll()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out var state) != 0) return;
            Set(state is NativeMethods.QUNS_BUSY
                      or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
                      or NativeMethods.QUNS_PRESENTATION_MODE);
        }
        catch (Exception ex)
        {
            Log.Error("Full-screen check failed; turning it off for this session.", ex);
            _timer.Stop();
        }
    }

    private void Set(bool value)
    {
        if (IsFullscreen == value) return;
        IsFullscreen = value;
        Changed?.Invoke(value);
    }
}
