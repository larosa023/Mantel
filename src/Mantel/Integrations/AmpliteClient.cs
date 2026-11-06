using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel.Core;

namespace Mantel.Integrations;

public sealed record AmpliteEqPreset(string Id, string Name);

public sealed record AmpliteEq(bool Available, bool On, string Preset, IReadOnlyList<AmpliteEqPreset> Presets)
{
    public string PresetName => Presets.FirstOrDefault(p => p.Id == Preset)?.Name ?? "Custom";
}

public sealed record AmpliteTrack(string Uri, string Title, string Artist, string Album, string? Image, long DurationMs);

public sealed record AmpliteState(
    bool Playing,
    AmpliteTrack? Track,
    long PositionMs,
    DateTimeOffset At,
    int? Volume,
    string? Accent,
    AmpliteEq? Eq,
    string Version)
{
    /// <summary>Where the song is now, extrapolated from Amplite's last report.</summary>
    public long PositionNowMs => Playing ? PositionMs + (long)(DateTimeOffset.Now - At).TotalMilliseconds : PositionMs;
}

/// <summary>
/// Talks to Amplite's local API (127.0.0.1:43821, the same one its Stream Deck plugin uses).
/// It only polls while something is listening, about once a second while Amplite runs and
/// every 5 seconds while it doesn't. Every event fires on the UI thread.
/// </summary>
public sealed class AmpliteClient
{
    private const string Api = "http://127.0.0.1:43821/api";

    public static AmpliteClient Shared { get; } = new();

    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromMilliseconds(1500),
    };

    private static readonly HttpClient ArtHttp = CreateArtClient();

    private readonly Dictionary<string, Bitmap> _art = new();
    private int _listeners;
    private CancellationTokenSource? _cts;
    private string _lastKey = "";

    /// <summary>Raised when what Amplite reports changes (track, play state, volume, EQ, accent).</summary>
    public event Action? Changed;

    /// <summary>Null when Amplite isn't running (or is too old to answer).</summary>
    public AmpliteState? State { get; private set; }

    public bool IsRunning => State is not null;

    private static HttpClient CreateArtClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mantel", AppInfo.Version));
        return http;
    }

    /// <summary>Start listening. Pair every call with <see cref="Release"/>.</summary>
    public void Acquire()
    {
        if (_listeners++ > 0) return;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Release()
    {
        if (--_listeners > 0) return;
        _listeners = 0;
        _cts?.Cancel();
        _cts = null;
    }

    /// <summary>Sends a command: toggle, next, prev, like, vol:+5, seek:-10000, eq:on, eq:off, eq:bass…</summary>
    public async Task<bool> SendAsync(string cmd)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Api + "/cmd")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { cmd }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Amplite", "1");
            using var res = await Http.SendAsync(req);
            // Ask again right away so the widget reflects the change without waiting a poll.
            _ = PollOnceAsync(CancellationToken.None);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error($"Amplite command '{cmd}' failed.", ex);
            return false;
        }
    }

    /// <summary>Album art, downloaded once per cover and kept (small; a few covers per session).</summary>
    public async Task<Bitmap?> GetArtAsync(string? url, int width)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (_art.TryGetValue(url, out var cached)) return cached;
        try
        {
            var bytes = await ArtHttp.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = Bitmap.DecodeToWidth(ms, width);
            if (_art.Count > 40) _art.Clear(); // simple cap; covers are cheap to refetch
            _art[url] = bmp;
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load album art from Amplite.", ex);
            return null;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync(ct);
            try { await Task.Delay(IsRunning ? 1000 : 5000, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        AmpliteState? state = null;
        string key = "";
        try
        {
            var json = await Http.GetStringAsync(Api + "/now", ct);
            state = Parse(json);
            // Change detection ignores the position, which moves every poll.
            key = json.Length > 0 ? ChangeKey(state) : "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch { /* not running: connection refused, fast and normal */ }

        Dispatcher.UIThread.Post(() =>
        {
            var wasRunning = IsRunning;
            State = state;
            if (key != _lastKey || wasRunning != (state is not null))
            {
                _lastKey = key;
                Changed?.Invoke();
            }
        });
    }

    private static string ChangeKey(AmpliteState? s) =>
        s is null ? "" :
        $"{s.Playing}|{s.Track?.Uri}|{s.Volume}|{s.Accent}|{s.Eq?.On}|{s.Eq?.Preset}|{s.Eq?.Available}|{s.PositionMs / 5000}";

    private static AmpliteState? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;

        AmpliteTrack? track = null;
        if (r.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            track = new AmpliteTrack(
                Str(t, "uri") ?? "",
                Str(t, "title") ?? "",
                Str(t, "artist") ?? "",
                Str(t, "album") ?? "",
                Str(t, "image"),
                t.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0);
        }

        AmpliteEq? eq = null;
        if (r.TryGetProperty("eq", out var e) && e.ValueKind == JsonValueKind.Object)
        {
            var presets = new List<AmpliteEqPreset>();
            if (e.TryGetProperty("presets", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray())
                    if (Str(p, "id") is { } id) presets.Add(new AmpliteEqPreset(id, Str(p, "name") ?? id));
            eq = new AmpliteEq(
                Bool(e, "available"), Bool(e, "on"), Str(e, "preset") ?? "custom", presets);
        }

        return new AmpliteState(
            Bool(r, "playing"),
            track,
            r.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Number ? pos.GetInt64() : 0,
            r.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(at.GetInt64()) : DateTimeOffset.Now,
            r.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null,
            Str(r, "accent") is { Length: 7 } a ? a : null,
            eq,
            Str(r, "version") ?? "");
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
