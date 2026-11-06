using System.Runtime.InteropServices;

namespace Mantel.Core;

/// <summary>
/// Keeps a window pinned to the bottom of the window stack, just above the wallpaper, no matter
/// what: every time Windows is about to change its z-order (it's shown, clicked, another app
/// asks), the change is rewritten to "put it at the bottom". This is what stops widgets from
/// drawing over normal windows. Same approach Rainmeter's "on desktop" mode uses.
/// </summary>
internal static class DesktopLayer
{
    private const uint WM_WINDOWPOSCHANGING = 0x0046;
    private const uint WM_NCDESTROY = 0x0082;
    private const uint SWP_NOZORDER = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // One shared callback, kept alive for the life of the process so it's never garbage collected.
    private static readonly SubclassProc Proc = WndProc;
    private static readonly UIntPtr Id = new(0x4D414E54); // "MANT"

    public static void Pin(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        try
        {
            SetWindowSubclass(hwnd, Proc, Id, UIntPtr.Zero);
            NativeMethods.SendToBottom(hwnd);
        }
        catch (Exception ex) { Log.Error("Couldn't pin a widget to the desktop layer.", ex); }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
            pos.flags &= ~SWP_NOZORDER;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        else if (msg == WM_NCDESTROY)
        {
            RemoveWindowSubclass(hWnd, Proc, Id);
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
