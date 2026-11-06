using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Mantel.Core;

namespace Mantel.Widgets.Weather;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

public sealed record DayForecast(DateOnly Date, WeatherKind Kind, double High, double Low);

public sealed record WeatherReport(
    double Temperature,
    double FeelsLike,
    double Wind,
    WeatherKind Kind,
    string Condition,
    bool IsDay,
    IReadOnlyList<DayForecast> Days);

public sealed record Place(string Name, string Region, double Latitude, double Longitude)
{
    public string Display => string.IsNullOrEmpty(Region) ? Name : $"{Name}, {Region}";
}

/// <summary>Open-Meteo: free, no API key, generous limits. Mantel polls every 20 minutes at most.</summary>
public static class WeatherClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mantel", AppInfo.Version));
        return http;
    }

    public static async Task<WeatherReport> GetAsync(double lat, double lon, bool fahrenheit, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                  "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                  $"&temperature_unit={(fahrenheit ? "fahrenheit" : "celsius")}" +
                  $"&wind_speed_unit={(fahrenheit ? "mph" : "kmh")}" +
                  "&timezone=auto&forecast_days=4";

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        var root = doc.RootElement;
        var current = root.GetProperty("current");
        var code = current.GetProperty("weather_code").GetInt32();

        var daily = root.GetProperty("daily");
        var times = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var highs = daily.GetProperty("temperature_2m_max");
        var lows = daily.GetProperty("temperature_2m_min");
        var days = new List<DayForecast>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            days.Add(new DayForecast(
                DateOnly.Parse(times[i].GetString()!, inv),
                KindOf(codes[i].GetInt32()),
                highs[i].GetDouble(),
                lows[i].GetDouble()));
        }

        return new WeatherReport(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            current.GetProperty("wind_speed_10m").GetDouble(),
            KindOf(code),
            ConditionOf(code),
            current.GetProperty("is_day").GetInt32() == 1,
            days);
    }

    public static async Task<IReadOnlyList<Place>> SearchAsync(string query, CancellationToken ct)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search" +
                  $"?name={Uri.EscapeDataString(query)}&count=6&language=en&format=json";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("results", out var results)) return Array.Empty<Place>();

        return results.EnumerateArray().Select(r =>
        {
            var region = new[] { Str(r, "admin1"), Str(r, "country_code") }.Where(s => !string.IsNullOrEmpty(s));
            return new Place(
                Str(r, "name") ?? "?",
                string.Join(", ", region),
                r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble());
        }).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // WMO weather interpretation codes, as used by Open-Meteo.
    public static WeatherKind KindOf(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        >= 61 and <= 67 or >= 80 and <= 82 => WeatherKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 95 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy,
    };

    private static string ConditionOf(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 or 81 => "Showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorms",
        96 or 99 => "Thunderstorms, hail",
        _ => "—",
    };
}
