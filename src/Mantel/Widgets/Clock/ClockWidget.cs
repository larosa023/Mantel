using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Mantel.Core;

namespace Mantel.Widgets.Clock;

/// <summary>
/// Time and date. Options:
///   "format24h" = "true"  — 24-hour time
///   "timeZone"  = an IANA or Windows time zone id (e.g. "America/Los_Angeles") — world clock
///   "label"     = text shown above the time (e.g. "LOS ANGELES")
/// </summary>
public sealed class ClockWidget : TimedWidget
{
    private TextBlock _time = null!;
    private TextBlock _period = null!;
    private TextBlock _date = null!;
    private TextBlock _label = null!;
    private string _lastMinute = "";

    public ClockWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(1)) { }

    private bool Use24h => Settings.Options.TryGetValue("format24h", out var v) && v == "true";

    private TimeZoneInfo Zone
    {
        get
        {
            if (Settings.Options.TryGetValue("timeZone", out var id) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz))
                return tz;
            return TimeZoneInfo.Local;
        }
    }

    protected override Control BuildView()
    {
        _label = new TextBlock { IsVisible = false };
        _label.Classes.Add("label");

        _time = new TextBlock();
        _time.Classes.Add("big");

        _period = new TextBlock { VerticalAlignment = VerticalAlignment.Bottom, Margin = new(6, 0, 0, 8) };
        _period.Classes.Add("muted");

        _date = new TextBlock { Margin = new(2, 2, 0, 0) };
        _date.Classes.Add("muted");

        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _time, _period } };
        return new StackPanel { MinWidth = 200, Children = { _label, timeRow, _date } };
    }

    protected override void Refresh()
    {
        var now = TimeZoneInfo.ConvertTime(DateTime.Now, Zone);
        var minuteKey = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        if (minuteKey == _lastMinute) return; // only touch the UI once a minute
        _lastMinute = minuteKey;

        var culture = CultureInfo.CurrentCulture;
        if (Use24h)
        {
            _time.Text = now.ToString("HH:mm", culture);
            _period.Text = "";
        }
        else
        {
            _time.Text = now.ToString("h:mm", culture);
            _period.Text = now.ToString("tt", culture);
        }
        _date.Text = now.ToString("dddd, MMMM d", culture);

        var hasLabel = Settings.Options.TryGetValue("label", out var label) && !string.IsNullOrWhiteSpace(label);
        _label.IsVisible = hasLabel;
        _label.Text = hasLabel ? label!.ToUpperInvariant() : "";
    }
}
