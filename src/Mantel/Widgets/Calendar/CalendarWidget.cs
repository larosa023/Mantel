using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Calendar;

/// <summary>
/// A month at a glance with today highlighted. Arrows flip months; the title jumps back to today.
/// Options: "weekStart" ("sunday" or "monday"), "weekNumbers" ("true").
/// </summary>
public sealed class CalendarWidget : TimedWidget
{
    private readonly TextBlock _title = new();
    private readonly Grid _grid = new();
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _renderedFor;

    public CalendarWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromMinutes(1)) { }

    private bool MondayFirst => Settings.Options.TryGetValue("weekStart", out var v)
        ? v == "monday"
        : CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Monday;

    private bool WeekNumbers => Settings.Options.TryGetValue("weekNumbers", out var v) && v == "true";

    protected override Control BuildView()
    {
        _title.Classes.Add("title");
        _title.VerticalAlignment = VerticalAlignment.Center;
        var titleButton = new Button { Content = _title, Padding = new Thickness(0) };
        titleButton.Classes.Add("row");
        titleButton.Margin = new Thickness(0);
        titleButton.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(titleButton, "Back to today");
        titleButton.Click += (_, _) => { _month = new(DateTime.Today.Year, DateTime.Today.Month, 1); Render(); };

        var prev = Arrow(Icons.ChevronLeft, -1);
        var next = Arrow(Icons.ChevronRight, +1);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 0, -6, 10) };
        header.Children.Add(titleButton);
        Grid.SetColumn(prev, 1);
        header.Children.Add(prev);
        Grid.SetColumn(next, 2);
        header.Children.Add(next);

        return new StackPanel { Width = WidgetCard.ContentWidth, Children = { header, _grid } };
    }

    private Button Arrow(string icon, int delta)
    {
        var b = new Button { Content = Icons.Create(icon, 16), Padding = new Thickness(6) };
        b.Classes.Add("icon");
        b.Click += (_, _) => { _month = _month.AddMonths(delta); Render(); };
        return b;
    }

    protected override void Refresh()
    {
        // Redraw when the day changes (so "today" moves at midnight); otherwise nothing to do.
        if (_renderedFor == DateTime.Today) return;
        if (_renderedFor != default && _month == new DateTime(_renderedFor.Year, _renderedFor.Month, 1))
            _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        Render();
    }

    private void Render()
    {
        _renderedFor = DateTime.Today;
        var culture = CultureInfo.CurrentCulture;
        _title.Text = _month.ToString("MMMM yyyy", culture);

        var cols = WeekNumbers ? 8 : 7;
        _grid.Children.Clear();
        _grid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", cols)));
        _grid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto");

        var first = MondayFirst ? DayOfWeek.Monday : DayOfWeek.Sunday;
        var offset = WeekNumbers ? 1 : 0;

        // Weekday initials.
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            var label = new TextBlock
            {
                Text = culture.DateTimeFormat.GetShortestDayName(day).Substring(0, Math.Min(2, culture.DateTimeFormat.GetShortestDayName(day).Length)).ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            label.Classes.Add("label");
            Grid.SetColumn(label, i + offset);
            _grid.Children.Add(label);
        }

        var lead = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        var start = _month.AddDays(-lead);
        for (var week = 0; week < 6; week++)
        {
            var weekStart = start.AddDays(week * 7);
            if (week == 5 && weekStart.Month != _month.Month) break; // skip an all-next-month row

            if (WeekNumbers)
            {
                var wk = new TextBlock
                {
                    Text = ISOWeek.GetWeekOfYear(weekStart.AddDays(3)).ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 10,
                };
                wk.Classes.Add("label");
                Grid.SetRow(wk, week + 1);
                _grid.Children.Add(wk);
            }

            for (var d = 0; d < 7; d++)
            {
                var date = weekStart.AddDays(d);
                var inMonth = date.Month == _month.Month;
                var isToday = date == DateTime.Today;

                var text = new TextBlock
                {
                    Text = date.Day.ToString(),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (isToday)
                {
                    text.FontWeight = FontWeight.Bold;
                    text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Mantel.AccentText"));
                }
                else text.Classes.Add(inMonth ? "value" : "muted");
                if (!inMonth) text.Opacity = 0.45;

                var cell = new Border
                {
                    Width = 30, Height = 30,
                    CornerRadius = new CornerRadius(15),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1),
                    Child = text,
                };
                if (isToday) cell.Classes.Add("pill");
                if (isToday) cell.CornerRadius = new CornerRadius(15);
                Grid.SetRow(cell, week + 1);
                Grid.SetColumn(cell, d + offset);
                _grid.Children.Add(cell);
            }
        }
    }

    public static Control BuildOptions(WidgetOptionsContext ctx)
    {
        var defaultStart = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Monday ? "monday" : "sunday";
        return OptionsUi.Panel(
            OptionsUi.Choice(ctx, "weekStart", "Week starts on", new[] { ("sunday", "Sunday"), ("monday", "Monday") }, defaultStart),
            OptionsUi.Toggle(ctx, "weekNumbers", "Show week numbers"));
    }
}
