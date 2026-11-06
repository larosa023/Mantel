using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Mantel.Core;

namespace Mantel.Widgets.Clock;

/// <summary>
/// Time and date. Add several for world clocks. Options:
///   "format24h" = "true"  — 24-hour time
///   "timeZone"  = a Windows/IANA time zone id; empty = this PC's time zone
///   "label"     = text shown above the time (defaults to the city for other time zones)
///   "seconds"   = "true"  — show seconds
/// </summary>
public sealed class ClockWidget : TimedWidget
{
    private TextBlock _time = null!;
    private TextBlock _period = null!;
    private TextBlock _date = null!;
    private TextBlock _label = null!;
    private string _lastKey = "";

    public ClockWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(1)) { }

    private bool Use24h => Settings.Options.TryGetValue("format24h", out var v) && v == "true";
    private bool ShowSeconds => Settings.Options.TryGetValue("seconds", out var v) && v == "true";

    private TimeZoneInfo Zone => ResolveZone(Settings) ?? TimeZoneInfo.Local;

    private static TimeZoneInfo? ResolveZone(WidgetInstanceSettings s) =>
        s.Options.TryGetValue("timeZone", out var id) && !string.IsNullOrEmpty(id) &&
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : null;

    protected override Control BuildView()
    {
        _label = new TextBlock { IsVisible = false, Margin = new(2, 0, 0, 2) };
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
        var key = now.ToString(ShowSeconds ? "yyyyMMddHHmmss" : "yyyyMMddHHmm", CultureInfo.InvariantCulture);
        if (key == _lastKey) return; // only touch the UI when the display actually changes
        _lastKey = key;

        var culture = CultureInfo.CurrentCulture;
        var seconds = ShowSeconds ? ":ss" : "";
        if (Use24h)
        {
            _time.Text = now.ToString("HH:mm" + seconds, culture);
            _period.Text = "";
        }
        else
        {
            _time.Text = now.ToString("h:mm" + seconds, culture);
            _period.Text = now.ToString("tt", culture);
        }

        var date = now.ToString("dddd, MMMM d", culture);
        // For another time zone, say how far ahead/behind it is: "Tomorrow, +5 hrs".
        if (ResolveZone(Settings) is { } zone && zone.Id != TimeZoneInfo.Local.Id)
            date += " · " + Offset(zone);
        _date.Text = date;

        var label = Settings.Options.TryGetValue("label", out var l) && !string.IsNullOrWhiteSpace(l)
            ? l
            : ResolveZone(Settings) is { } z && z.Id != TimeZoneInfo.Local.Id ? CityOf(z) : null;
        _label.IsVisible = label is not null;
        _label.Text = label?.ToUpperInvariant() ?? "";
    }

    private static string Offset(TimeZoneInfo zone)
    {
        var diff = zone.GetUtcOffset(DateTime.UtcNow) - TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        if (diff == TimeSpan.Zero) return "same time";
        var hours = diff.TotalHours;
        var text = hours % 1 == 0 ? $"{Math.Abs(hours):0}" : $"{Math.Abs(hours):0.#}";
        return $"{(hours > 0 ? "+" : "−")}{text} hr{(Math.Abs(hours) == 1 ? "" : "s")}";
    }

    /// <summary>"(UTC-08:00) Pacific Time (US &amp; Canada)" -> "Pacific Time".</summary>
    private static string CityOf(TimeZoneInfo zone)
    {
        var name = zone.DisplayName;
        var close = name.IndexOf(')');
        if (close >= 0) name = name[(close + 1)..].Trim();
        var paren = name.IndexOf('(');
        if (paren > 0) name = name[..paren].Trim();
        var comma = name.IndexOf(',');
        return comma > 0 ? name[..comma].Trim() : name;
    }

    public static Control BuildOptions(WidgetOptionsContext ctx)
    {
        var zones = new List<(string, string)> { ("", "This PC's time zone") };
        zones.AddRange(TimeZoneInfo.GetSystemTimeZones().Select(z => (z.Id, z.DisplayName)));

        return OptionsUi.Panel(
            OptionsUi.Choice(ctx, "timeZone", "Time zone", zones, ""),
            OptionsUi.Text(ctx, "label", "Label", "e.g. Tokyo office", "Shown above the time. Leave empty for the time zone's name."),
            OptionsUi.Toggle(ctx, "format24h", "24-hour time"),
            OptionsUi.Toggle(ctx, "seconds", "Show seconds"));
    }

    public static string? Subtitle(WidgetInstanceSettings s)
    {
        if (s.Options.TryGetValue("label", out var l) && !string.IsNullOrWhiteSpace(l)) return l;
        return ResolveZone(s) is { } z ? CityOf(z) : "Local time";
    }
}
