using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Layout;
using Mantel.Core;

namespace Mantel.Widgets.Network;

/// <summary>Live download and upload speed across all active network adapters.</summary>
public sealed class NetworkWidget : TimedWidget
{
    private readonly TextBlock _down = new();
    private readonly TextBlock _up = new();
    private readonly TextBlock _adapter = new();
    private long _lastReceived = -1, _lastSent = -1;
    private DateTime _lastSample;

    public NetworkWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(2)) { }

    protected override Control BuildView()
    {
        _adapter.Classes.Add("label");
        return new StackPanel
        {
            Width = 220,
            Spacing = 8,
            Children =
            {
                _adapter,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    Children = { Stat("DOWN", _down, 0), Stat("UP", _up, 1) },
                },
            },
        };
    }

    private static Control Stat(string label, TextBlock value, int column)
    {
        var title = new TextBlock { Text = label };
        title.Classes.Add("label");
        value.FontSize = 22;
        value.FontWeight = Avalonia.Media.FontWeight.Light;
        value.Foreground = Avalonia.Media.Brush.Parse("#F0ECE4");
        var stack = new StackPanel { Spacing = 2, Children = { title, value }, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(stack, column);
        return stack;
    }

    protected override void Refresh()
    {
        long received = 0, sent = 0;
        string? primaryName = null;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var stats = nic.GetIPStatistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
            // Name the adapter with a gateway: that's the one actually carrying traffic.
            if (primaryName is null && nic.GetIPProperties().GatewayAddresses.Count > 0)
                primaryName = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "WI-FI" : "ETHERNET";
        }

        var now = DateTime.UtcNow;
        if (_lastReceived >= 0)
        {
            var seconds = (now - _lastSample).TotalSeconds;
            if (seconds > 0)
            {
                _down.Text = Rate(Math.Max(0, received - _lastReceived) / seconds);
                _up.Text = Rate(Math.Max(0, sent - _lastSent) / seconds);
            }
        }
        else
        {
            _down.Text = "—";
            _up.Text = "—";
        }

        _adapter.Text = primaryName ?? "OFFLINE";
        _lastReceived = received;
        _lastSent = sent;
        _lastSample = now;
    }

    /// <summary>Bits per second, the way speed tests and ISPs quote it.</summary>
    private static string Rate(double bytesPerSecond)
    {
        var bits = bytesPerSecond * 8;
        return bits switch
        {
            >= 1_000_000_000 => $"{bits / 1_000_000_000:0.0} Gb/s",
            >= 1_000_000 => $"{bits / 1_000_000:0.#} Mb/s",
            >= 1_000 => $"{bits / 1_000:0} Kb/s",
            _ => $"{bits:0} b/s",
        };
    }
}
