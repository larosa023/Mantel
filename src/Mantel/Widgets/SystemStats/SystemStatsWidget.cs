using Avalonia.Controls;
using Avalonia.Layout;
using Mantel.Core;

namespace Mantel.Widgets.SystemStats;

/// <summary>
/// CPU and memory usage straight from the Windows kernel (no performance-counter
/// overhead, no admin rights). Temperatures and GPU come later through a small
/// elevated helper, so this widget never needs admin.
/// </summary>
public sealed class SystemStatsWidget : TimedWidget
{
    private const double BarWidth = 200;

    private readonly Meter _cpu = new("CPU");
    private readonly Meter _ram = new("MEMORY");
    private ulong _lastIdle, _lastTotal;

    public SystemStatsWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(2)) { }

    protected override Control BuildView() =>
        new StackPanel { Spacing = 12, Width = BarWidth, Children = { _cpu.View, _ram.View } };

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
            var fraction = (double)used / mem.ullTotalPhys;
            _ram.Set(fraction, $"{Gb(used):0.0} / {Gb(mem.ullTotalPhys):0} GB");
        }
    }

    private static double Gb(ulong bytes) => bytes / 1024d / 1024d / 1024d;

    /// <summary>A label, a value, and a thin bar.</summary>
    private sealed class Meter
    {
        private readonly TextBlock _value = new() { HorizontalAlignment = HorizontalAlignment.Right };
        private readonly Border _fill = new() { Width = 0 };

        public Meter(string label)
        {
            var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            title.Classes.Add("label");
            _value.Classes.Add("value");
            _fill.Classes.Add("fill");

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(title);
            Grid.SetColumn(_value, 1);
            header.Children.Add(_value);

            var track = new Border { Child = _fill };
            track.Classes.Add("track");

            View = new StackPanel { Spacing = 6, Children = { header, track } };
        }

        public Control View { get; }

        public void Set(double fraction, string text)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            _value.Text = text;
            _fill.Width = BarWidth * fraction;
            _fill.Classes.Set("hot", fraction >= 0.9);
        }
    }
}
