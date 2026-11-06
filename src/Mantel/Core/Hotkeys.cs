using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// System-wide keyboard shortcuts (Ctrl+Alt+key), via Windows' RegisterHotKey. A hidden window
/// receives the presses; nothing polls. Shortcuts another app already owns are skipped and logged.
/// </summary>
public sealed class Hotkeys : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly SubclassProc _proc;
    private readonly Dictionary<int, Action> _actions = new();
    private Window? _sink;
    private IntPtr _hwnd;
    private int _nextId = 1;

    public Hotkeys() => _proc = WndProc;

    /// <summary>The keys a shortcut can use (with Ctrl+Alt).</summary>
    public static IReadOnlyList<string> Keys { get; } =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()).Concat(Enumerable.Range(1, 9).Select(i => i.ToString())).ToList();

    public static string Describe(string key) => string.IsNullOrEmpty(key) ? "None" : $"Ctrl + Alt + {key}";

    /// <summary>Replaces every registered shortcut with this set.</summary>
    public void SetAll(IEnumerable<(string Key, Action Action)> shortcuts)
    {
        if (!OperatingSystem.IsWindows()) return;
        Clear();
        if (!EnsureSink()) return;

        foreach (var (key, action) in shortcuts)
        {
            if (key.Length != 1 || !char.IsLetterOrDigit(key[0])) continue;
            var id = _nextId++;
            var vk = (uint)char.ToUpperInvariant(key[0]); // VK codes for A–Z and 0–9 are their ASCII codes
            if (RegisterHotKey(_hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk)) _actions[id] = action;
            else Log.Info($"Shortcut {Describe(key)} is already used by another app; skipped.");
        }
    }

    private void Clear()
    {
        if (_hwnd != IntPtr.Zero)
            foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    private bool EnsureSink()
    {
        if (_hwnd != IntPtr.Zero) return true;
        try
        {
            // A never-shown window just to receive WM_HOTKEY. Its handle exists from construction.
            _sink = new Window { Width = 1, Height = 1, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None };
            _hwnd = _sink.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_hwnd == IntPtr.Zero) return false;
            SetWindowSubclass(_hwnd, _proc, new UIntPtr(0x484B4559), UIntPtr.Zero); // "HKEY"
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard shortcuts are unavailable.", ex);
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { action(); }
                catch (Exception ex) { Log.Error("Shortcut action failed.", ex); }
            });
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Clear();
        _sink?.Close();
    }
}
