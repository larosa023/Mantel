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

    private readonly TextBlock _uptime = new();
    private readonly StackPanel _processes = new() { Spacing = 3 };
    private DateTime _lastProcessScan;

    private bool ShowUptime => Settings.Options.TryGetValue("uptime", out var v) && v == "true";
    private int TopProcesses => int.TryParse(Settings.Options.TryGetValue("processes", out var v) ? v : "0", out var n) ? Math.Clamp(n, 0, 8) : 0;

    protected override Control BuildView()
    {
        _uptime.Classes.Add("muted");
        _uptime.IsVisible = ShowUptime;
        var processesTitle = new TextBlock { Text = "TOP APPS BY MEMORY", Margin = new Avalonia.Thickness(0, 4, 0, 2) };
        processesTitle.Classes.Add("label");
        var processesBlock = new StackPanel { IsVisible = TopProcesses > 0, Children = { processesTitle, _processes } };
        return new StackPanel { Spacing = 12, Children = { _cpu.View, _cpuHistory, _ram.View, processesBlock, _uptime } };
    }

    /// <summary>Biggest apps by memory, grouped by name (Chrome's many processes count as one).</summary>
    private void ScanProcesses()
    {
        _lastProcessScan = DateTime.UtcNow;
        var groups = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0) continue;
                groups[p.ProcessName] = groups.GetValueOrDefault(p.ProcessName) + p.WorkingSet64;
            }
            catch { /* exited or protected */ }
            finally { p.Dispose(); }
        }

        _processes.Children.Clear();
        foreach (var (name, bytes) in groups.OrderByDescending(g => g.Value).Take(TopProcesses))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var label = new TextBlock { Text = name, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            label.Classes.Add("value");
            var size = new TextBlock { Text = bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes >> 20} MB" };
            size.Classes.Add("muted");
            Grid.SetColumn(size, 1);
            row.Children.Add(label);
            row.Children.Add(size);
            _processes.Children.Add(row);
        }
    }

    private static string Uptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return up.TotalDays >= 1
            ? $"Up {(int)up.TotalDays} day{((int)up.TotalDays == 1 ? "" : "s")}, {up.Hours} hr{(up.Hours == 1 ? "" : "s")}"
            : $"Up {up.Hours} hr{(up.Hours == 1 ? "" : "s")}, {up.Minutes} min";
    }

    public static Control BuildOptions(WidgetOptionsContext ctx) =>
        OptionsUi.Panel(
            OptionsUi.Choice(ctx, "processes", "Top apps by memory", new[] { ("0", "Off"), ("3", "Top 3"), ("5", "Top 5"), ("8", "Top 8") }, "0"),
            OptionsUi.Toggle(ctx, "uptime", "Show how long the PC has been on"));

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

        if (ShowUptime) _uptime.Text = Uptime();
        // Listing every process costs more than the rest of this widget; do it every 10 seconds.
        if (TopProcesses > 0 && DateTime.UtcNow - _lastProcessScan > TimeSpan.FromSeconds(10)) ScanProcesses();

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
