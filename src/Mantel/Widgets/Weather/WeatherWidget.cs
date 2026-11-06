using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Mantel.Core;

namespace Mantel.Widgets.Weather;

/// <summary>
/// Current conditions and a 3-day forecast from Open-Meteo.
/// Options: "place" (display name), "lat", "lon", "units" ("f" or "c").
/// </summary>
public sealed class WeatherWidget : IWidget
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(2);

    private readonly WidgetInstanceSettings _settings;
    private readonly ContentControl _icon = new();
    private readonly TextBlock _temp = new();
    private readonly TextBlock _condition = new();
    private readonly TextBlock _range = new();
    private readonly TextBlock _place = new();
    private readonly Grid _days = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
    private readonly Grid _hours = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*") };
    private readonly StackPanel _forecastRow = new() { Spacing = 0 };
    private Control? _view;
    private CancellationTokenSource? _cts;
    private WeatherReport? _last;

    public WeatherWidget(WidgetInstanceSettings settings) => _settings = settings;

    private double? Lat => ParseDouble("lat");
    private double? Lon => ParseDouble("lon");
    private string PlaceName => _settings.Options.TryGetValue("place", out var p) ? p : "";

    private bool Fahrenheit => _settings.Options.TryGetValue("units", out var u)
        ? u == "f"
        : !RegionInfo.CurrentRegion.IsMetric;

    private double? ParseDouble(string key) =>
        _settings.Options.TryGetValue(key, out var v) &&
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    public Control View => _view ??= BuildView();

    private Control BuildView()
    {
        _temp.Classes.Add("big");
        _condition.Classes.Add("strong");
        _range.Classes.Add("muted");
        _place.Classes.Add("label");

        var info = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(12, 0, 0, 0),
            Spacing = 2,
            Children = { _condition, _range },
        };

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _icon, _temp, info },
        };
        _icon.Margin = new(0, 0, 10, 0);
        _icon.VerticalAlignment = VerticalAlignment.Center;

        var divider = new Border { Margin = new(0, 12, 0, 10) };
        divider.Classes.Add("divider");
        _forecastRow.Children.Add(divider);
        _forecastRow.Children.Add(_hours);
        var divider2 = new Border { Margin = new(0, 12, 0, 10) };
        divider2.Classes.Add("divider");
        _forecastRow.Children.Add(divider2);
        _forecastRow.Children.Add(_days);

        return new StackPanel { Width = Mantel.UI.WidgetCard.ContentWidth, Children = { _place, top, _forecastRow } };
    }

    public void Start()
    {
        _ = View;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        if (Lat is null || Lon is null)
        {
            ShowMessage("Set a location in Mantel settings");
            return;
        }

        if (_last is null) ShowMessage("Loading weather…");
        _ = LoopAsync(_cts.Token);
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose() => Stop();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                var report = await WeatherClient.GetAsync(Lat!.Value, Lon!.Value, Fahrenheit, ct);
                _last = report;
                Dispatcher.UIThread.Post(() => Render(report, stale: false));
                wait = RefreshEvery;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Error("Weather fetch failed.", ex);
                var last = _last;
                Dispatcher.UIThread.Post(() =>
                {
                    if (last is not null) Render(last, stale: true);
                    else ShowMessage("Weather unavailable. Retrying…");
                });
                wait = RetryAfterError;
            }

            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void ShowMessage(string text)
    {
        _place.Text = string.IsNullOrEmpty(PlaceName) ? "WEATHER" : PlaceName.ToUpperInvariant();
        _icon.Content = WeatherIcon.Create(WeatherKind.PartlyCloudy, isDay: true, 40);
        _temp.Text = "";
        _condition.Text = text;
        _range.Text = "";
        _forecastRow.IsVisible = false;
    }

    private void Render(WeatherReport r, bool stale)
    {
        var unit = Fahrenheit ? "mph" : "km/h";
        _place.Text = (string.IsNullOrEmpty(PlaceName) ? "WEATHER" : PlaceName.ToUpperInvariant())
                      + (stale ? " · OFFLINE" : "");
        _icon.Content = WeatherIcon.Create(r.Kind, r.IsDay, 46);
        _temp.Text = $"{r.Temperature:0}°";
        _condition.Text = r.Condition;

        var today = r.Days.FirstOrDefault();
        _range.Text = today is null
            ? $"Feels {r.FeelsLike:0}°"
            : $"H {today.High:0}°  L {today.Low:0}° · Feels {r.FeelsLike:0}° · {r.Wind:0} {unit}";

        // Next six hours: time, icon, temperature (and rain chance when it's likely).
        _hours.Children.Clear();
        var now = DateTime.Now;
        var nextHours = r.Hours.Where(h => h.Time > now.AddMinutes(-30)).Take(6).ToList();
        for (var i = 0; i < nextHours.Count; i++)
        {
            var h = nextHours[i];
            var label = new TextBlock
            {
                Text = i == 0 ? "NOW" : h.Time.ToString("h tt", CultureInfo.CurrentCulture).Replace(" ", "").ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.Classes.Add("label");
            var icon = WeatherIcon.Create(h.Kind, h.IsDay, 22);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var temp = new TextBlock { Text = $"{h.Temperature:0}°", HorizontalAlignment = HorizontalAlignment.Center };
            temp.Classes.Add("value");
            var cell = new StackPanel { Spacing = 4, Children = { label, icon, temp } };
            if (h.RainChance >= 30)
            {
                var rain = new TextBlock { Text = $"{h.RainChance}%", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush.Parse("#6FA8E8") };
                cell.Children.Add(rain);
            }
            Grid.SetColumn(cell, i);
            _hours.Children.Add(cell);
        }
        _hours.IsVisible = nextHours.Count > 0;

        _days.Children.Clear();
        var upcoming = r.Days.Skip(1).Take(3).ToList();
        for (var i = 0; i < upcoming.Count; i++)
        {
            var d = upcoming[i];
            var name = new TextBlock
            {
                Text = d.Date.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            name.Classes.Add("label");
            var temps = new TextBlock
            {
                Text = $"{d.High:0}° / {d.Low:0}°",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            temps.Classes.Add("muted");
            var icon = WeatherIcon.Create(d.Kind, isDay: true, 24);
            icon.HorizontalAlignment = HorizontalAlignment.Center;

            var cell = new StackPanel { Spacing = 4, Children = { name, icon, temps } };
            Grid.SetColumn(cell, i);
            _days.Children.Add(cell);
        }
        _forecastRow.IsVisible = upcoming.Count > 0;
    }

    // --- Settings panel ------------------------------------------------------

    public static Control BuildOptions(WidgetOptionsContext ctx)
    {
        var current = new TextBlock
        {
            Text = ctx.Get("place") is { Length: > 0 } p ? p : "No location set",
            FontSize = 13,
        };
        var search = new TextBox { Watermark = "Search for a city or ZIP…" };
        var results = new ListBox { MaxHeight = 200, IsVisible = false };
        var status = new TextBlock { IsVisible = false };
        status.Classes.Add("muted");
        CancellationTokenSource? pending = null;

        async Task RunSearchAsync()
        {
            var q = search.Text?.Trim();
            if (string.IsNullOrEmpty(q) || q.Length < 2) { results.IsVisible = false; return; }

            pending?.Cancel();
            var cts = pending = new CancellationTokenSource();
            try
            {
                status.Text = "Searching…";
                status.IsVisible = true;
                var places = await WeatherClient.SearchAsync(q, cts.Token);
                if (cts.IsCancellationRequested) return;

                results.Items.Clear();
                foreach (var place in places) results.Items.Add(new ListBoxItem { Content = place.Display, Tag = place });
                results.IsVisible = places.Count > 0;
                status.Text = places.Count == 0 ? "No matches." : "";
                status.IsVisible = places.Count == 0;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error("Place search failed.", ex);
                status.Text = "Couldn't search right now.";
                status.IsVisible = true;
            }
        }

        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        debounce.Tick += async (_, _) => { debounce.Stop(); await RunSearchAsync(); };
        search.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };

        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not ListBoxItem { Tag: Place place }) return;
            var inv = CultureInfo.InvariantCulture;
            ctx.Options["lat"] = place.Latitude.ToString(inv);
            ctx.Options["lon"] = place.Longitude.ToString(inv);
            ctx.Set("place", place.Name, immediate: true);
            current.Text = place.Display;
            results.IsVisible = false;
            search.Text = "";
        };

        var defaultUnits = RegionInfo.CurrentRegion.IsMetric ? "c" : "f";
        return OptionsUi.Panel(
            OptionsUi.Labeled("Location", new StackPanel { Spacing = 6, Children = { current, search, status, results } }),
            OptionsUi.Choice(ctx, "units", "Units", new[] { ("f", "Fahrenheit, mph"), ("c", "Celsius, km/h") }, defaultUnits));
    }

    public static string? Subtitle(WidgetInstanceSettings s) =>
        s.Options.TryGetValue("place", out var p) && !string.IsNullOrEmpty(p) ? p : "No location set";
}
