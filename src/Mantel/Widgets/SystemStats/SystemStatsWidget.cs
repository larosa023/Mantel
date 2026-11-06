using Avalonia.Controls;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.SystemStats;

/// <summary>
/// CPU and memory usage straight from the Windows kernel (no performance-counter
/// overhead, no admin rights). Temperatures and GPU come later through a small
/// elevated helper, so this widget never needs admin.
/// </summary>
public sealed class SystemStatsWidget : TimedWidget
{
    private const double Width = WidgetCard.ContentWidth;

    private readonly Meter _cpu = new("CPU", Width);
    private readonly Meter _ram = new("MEMORY", Width);
    private readonly Sparkline _cpuHistory = new(capacity: 60, height: 30) { Width = Width, Margin = new Avalonia.Thickness(0, 2, 0, 0) };
    private ulong _lastIdle, _lastTotal;

    public SystemStatsWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(2)) { }

    protected override Control BuildView() =>
        new StackPanel { Spacing = 12, Children = { _cpu.View, _cpuHistory, _ram.View } };

    protected override void Refresh()
    {
        if (!OperatingSystem.IsWindows()) return;

        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var idleNow = idle.Value;
            var totalNow = kernel.Value + user.Value; // kernel time already includes idle time
            var dIdle = idleNow - _lastIdle;
            var dTotal = totalNow - _lastTotal;
            if (_lastTotal != 0 && dTotal > 0)
            {
                var busy = 1.0 - (double)dIdle / dTotal;
                _cpu.Set(busy, $"{busy * 100:0}%");
                _cpuHistory.Push(busy);
            }
            _lastIdle = idleNow;
            _lastTotal = totalNow;
        }

        var mem = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };
        if (NativeMethods.GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            var used = mem.ullTotalPhys - mem.ullAvailPhys;
            _ram.Set((double)used / mem.ullTotalPhys, $"{Gb(used):0.0} / {Gb(mem.ullTotalPhys):0} GB");
        }
    }

    private static double Gb(ulong bytes) => bytes / 1024d / 1024d / 1024d;
}
