using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Mantel.Core;

namespace Mantel.Widgets.Countdown;

/// <summary>
/// Counts down to a date and time (a bid deadline, a game, a trip). Add as many as you like.
/// Options: "title", "target" (ISO 8601 local time, e.g. 2026-12-18T14:00).
/// </summary>
public sealed class CountdownWidget : TimedWidget
{
    private readonly TextBlock _title = new();
    private readonly TextBlock _big = new();
    private readonly TextBlock _unit = new();
    private readonly TextBlock _detail = new();

    public CountdownWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(30)) { }

    private string Title => Settings.Options.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t) ? t : "Countdown";

    private DateTime? Target => ParseTarget(Settings);

    private static DateTime? ParseTarget(WidgetInstanceSettings s) =>
        s.Options.TryGetValue("target", out var v) &&
        DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt) ? dt : null;

    protected override Control BuildView()
    {
        _title.Classes.Add("label");
        _big.Classes.Add("big");
        _unit.Classes.Add("muted");
        _unit.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.Margin = new(8, 0, 0, 8);
        _detail.Classes.Add("muted");
        _detail.Margin = new(2, 2, 0, 0);

        return new StackPanel
        {
            Width = Mantel.UI.WidgetCard.ContentWidth,
            Children =
            {
                _title,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _big, _unit } },
                _detail,
            },
        };
    }

    protected override void Refresh()
    {
        _title.Text = Title.ToUpperInvariant();

        if (Target is not { } target)
        {
            _big.Text = "—";
            _unit.Text = "";
            _detail.Text = "Set a date in Settings";
            return;
        }

        var left = target - DateTime.Now;
        _detail.Text = target.ToString("ddd, MMM d · h:mm tt", CultureInfo.CurrentCulture);

        if (left <= TimeSpan.Zero)
        {
            var ago = -left;
            _big.Text = ago.TotalMinutes < 1 ? "Now" : Big(ago).Value;
            _unit.Text = ago.TotalMinutes < 1 ? "" : Big(ago).Unit + " ago";
            Interval = TimeSpan.FromMinutes(1);
            return;
        }

        var (value, unit) = Big(left);
        _big.Text = value;
        _unit.Text = unit + Remainder(left);

        // Tick faster as it gets close, so the last hour actually counts down.
        Interval = left.TotalHours < 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30);
    }

    /// <summary>The headline number: days if far out, hours if today, minutes in the last hour.</summary>
    private static (string Value, string Unit) Big(TimeSpan t)
    {
        if (t.TotalDays >= 2) return (((int)t.TotalDays).ToString(), "days");
        if (t.TotalDays >= 1) return ("1", "day");
        if (t.TotalHours >= 1) return (((int)t.TotalHours).ToString(), (int)t.TotalHours == 1 ? "hr" : "hrs");
        return ($"{(int)t.TotalMinutes}:{t.Seconds:00}", "min");
    }

    private static string Remainder(TimeSpan t)
    {
        if (t.TotalDays >= 1 && t.Hours > 0) return $" {t.Hours} hr{(t.Hours == 1 ? "" : "s")}";
        if (t.TotalHours >= 1 && t.TotalDays < 1 && t.Minutes > 0) return $" {t.Minutes} min";
        return "";
    }

    public static Control BuildOptions(WidgetOptionsContext ctx)
    {
        var existing = ParseTarget(ctx.Instance) ?? DateTime.Today.AddDays(7).AddHours(17);

        var date = new CalendarDatePicker
        {
            SelectedDate = existing.Date,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var time = new TimePicker { SelectedTime = existing.TimeOfDay, MinuteIncrement = 5 };

        void Commit()
        {
            if (date.SelectedDate is not { } d) return;
            var t = time.SelectedTime ?? TimeSpan.Zero;
            ctx.Set("target", d.Date.Add(t).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), immediate: true);
        }

        date.SelectedDateChanged += (_, _) => Commit();
        time.SelectedTimeChanged += (_, _) => Commit();
        if (!ctx.Options.ContainsKey("target")) Commit();

        return OptionsUi.Panel(
            OptionsUi.Text(ctx, "title", "Name", "e.g. Bid due, Trip, Tip-off"),
            OptionsUi.Labeled("Date", date),
            OptionsUi.Labeled("Time", time));
    }

    public static string? Subtitle(WidgetInstanceSettings s)
    {
        var title = s.Options.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t) ? t : null;
        var target = ParseTarget(s);
        var when = target?.ToString("MMM d", CultureInfo.CurrentCulture);
        return (title, when) switch
        {
            (not null, not null) => $"{title} · {when}",
            (not null, null) => title,
            (null, not null) => when,
            _ => "Not set yet",
        };
    }
}
