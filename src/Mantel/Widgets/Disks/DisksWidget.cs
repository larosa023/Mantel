using Avalonia.Controls;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Disks;

/// <summary>Space used on each fixed drive. Checks once a minute; disk space changes slowly.</summary>
public sealed class DisksWidget : TimedWidget
{
    private const double Width = WidgetCard.ContentWidth;

    private readonly StackPanel _list = new() { Spacing = 14 };
    private readonly Dictionary<string, Meter> _meters = new();

    public DisksWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromMinutes(1)) { }

    protected override Control BuildView() => _list;

    protected override void Refresh()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && SafeIsReady(d))
            .OrderBy(d => d.Name)
            .ToList();

        // Drives come and go (external SSDs); rebuild only when the set changes.
        var names = drives.Select(d => d.Name).ToList();
        if (!names.SequenceEqual(_meters.Keys))
        {
            _meters.Clear();
            _list.Children.Clear();
            foreach (var d in drives)
            {
                var label = string.IsNullOrWhiteSpace(SafeLabel(d))
                    ? d.Name.TrimEnd('\\')
                    : $"{d.Name.TrimEnd('\\')}  {SafeLabel(d)!.ToUpperInvariant()}";
                var meter = new Meter(label, Width, hotAt: 0.92);
                _meters[d.Name] = meter;
                _list.Children.Add(meter.View);
            }
        }

        foreach (var d in drives)
        {
            if (!_meters.TryGetValue(d.Name, out var meter)) continue;
            try
            {
                var total = d.TotalSize;
                var free = d.AvailableFreeSpace;
                if (total <= 0) continue;
                meter.Set((double)(total - free) / total, $"{FormatSize(free)} free");
            }
            catch (IOException) { /* drive went away mid-read; next tick rebuilds the list */ }
        }
    }

    private static bool SafeIsReady(DriveInfo d)
    {
        try { return d.IsReady; } catch { return false; }
    }

    private static string? SafeLabel(DriveInfo d)
    {
        try { return d.VolumeLabel; } catch { return null; }
    }

    private static string FormatSize(long bytes)
    {
        var gb = bytes / 1024d / 1024d / 1024d;
        return gb >= 1000 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
    }
}
