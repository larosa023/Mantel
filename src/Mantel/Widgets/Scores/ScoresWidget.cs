using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel.Core;

namespace Mantel.Widgets.Scores;

/// <summary>
/// Live scores from Fullcourt's /api/scores. Polls as often as Fullcourt says to (every 20
/// seconds during games, every 10 minutes on quiet days). Click a game to open it in Fullcourt.
/// Options: "league" (nba, wnba, ncaam, ncaaw), "teams" (e.g. "BOS,NY"), "max", "site", "hideWhenEmpty".
/// </summary>
public sealed class ScoresWidget : IWidget
{
    public const string DefaultSite = "https://nbafullcourty.netlify.app";
    private const double Width = Mantel.UI.WidgetCard.ContentWidth;

    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, Bitmap> Logos = new();

    private readonly WidgetInstanceSettings _settings;
    private readonly TextBlock _header = new();
    private readonly Border _livePill = new() { IsVisible = false };
    private readonly StackPanel _games = new() { Spacing = 2 };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap };
    private Control? _view;
    private CancellationTokenSource? _cts;

    public ScoresWidget(WidgetInstanceSettings settings) => _settings = settings;

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mantel", AppInfo.Version));
        return http;
    }

    private string League => Opt("league", "nba");
    private string Teams => Opt("teams", "").Replace(" ", "").ToUpperInvariant();
    private int Max => int.TryParse(Opt("max", "6"), out var m) ? Math.Clamp(m, 1, 15) : 6;
    private string Site => Opt("site", DefaultSite).TrimEnd('/');
    private bool HideWhenEmpty => Opt("hideWhenEmpty", "false") == "true";

    /// <summary>"web" opens games in the browser; "app" in the installed Fullcourt app.</summary>
    private string OpenIn => Opt("openIn", "web");

    private string Opt(string key, string fallback) =>
        _settings.Options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string LeagueName(string league) => league switch
    {
        "wnba" => "WNBA",
        "ncaam" => "MEN'S COLLEGE",
        "ncaaw" => "WOMEN'S COLLEGE",
        _ => "NBA",
    };

    public Control View => _view ??= BuildView();

    private Control BuildView()
    {
        _header.Classes.Add("label");
        _header.VerticalAlignment = VerticalAlignment.Center;
        _livePill.Classes.Add("pill");
        _livePill.Child = new TextBlock { Text = "LIVE" };
        _livePill.Margin = new Thickness(8, 0, 0, 0);
        _livePill.VerticalAlignment = VerticalAlignment.Center;
        _empty.Classes.Add("muted");

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { _header, _livePill },
        };
        _header.Text = LeagueName(League);
        _empty.Text = "Loading scores…";
        return new StackPanel { Width = Width, Children = { header, _games, _empty } };
    }

    public void Start()
    {
        _ = View;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose() => Stop();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var wait = TimeSpan.FromMinutes(2);
            try
            {
                var url = $"{Site}/api/scores?league={League}" +
                          (Teams.Length > 0 ? $"&teams={Uri.EscapeDataString(Teams)}" : "");
                var json = await Http.GetStringAsync(url, ct);
                var board = Parse(json);
                if (board is not null)
                {
                    wait = TimeSpan.FromSeconds(Math.Clamp(board.Refresh, 15, 900));
                    await Dispatcher.UIThread.InvokeAsync(() => Render(board));
                    _ = LoadLogosAsync(board, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Error("Fullcourt scores fetch failed.", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_games.Children.Count == 0)
                    {
                        _empty.Text = "Can't reach Fullcourt right now. Retrying…";
                        _empty.IsVisible = true;
                    }
                });
            }

            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // --- Model ----------------------------------------------------------------------

    private sealed record Team(string Abbr, string Name, int? Score, string Record, int? Rank, string? Logo, bool Winner);
    private sealed record Game(string Id, string State, string Status, DateTimeOffset Start, string Tv, Team Home, Team Away, string Url);
    private sealed record Board(bool Live, int Refresh, IReadOnlyList<Game> Games);

    private static Board? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;

        static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static int? I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        static Team T(JsonElement e) => new(S(e, "abbr"), S(e, "name"), I(e, "score"), S(e, "record"), I(e, "rank"),
            S(e, "logo") is { Length: > 0 } l ? l : null, e.TryGetProperty("winner", out var w) && w.ValueKind == JsonValueKind.True);

        var games = r.GetProperty("games").EnumerateArray().Select(g => new Game(
            S(g, "id"), S(g, "state"), S(g, "status"),
            DateTimeOffset.TryParse(S(g, "start"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var st) ? st : DateTimeOffset.MinValue,
            S(g, "tv"), T(g.GetProperty("home")), T(g.GetProperty("away")), S(g, "url"))).ToList();

        return new Board(r.TryGetProperty("live", out var lv) && lv.ValueKind == JsonValueKind.True, I(r, "refresh") ?? 120, games);
    }

    // --- Display ---------------------------------------------------------------------

    private readonly Dictionary<string, List<Image>> _logoSlots = new();

    private void Render(Board board)
    {
        _header.Text = LeagueName(League) + (Teams.Length > 0 ? $" · {Teams.Replace(",", ", ")}" : "");
        _livePill.IsVisible = board.Live;
        _games.Children.Clear();
        _logoSlots.Clear();

        var shown = board.Games.Take(Max).ToList();
        foreach (var g in shown) _games.Children.Add(GameRow(g));

        var more = board.Games.Count - shown.Count;
        if (more > 0)
        {
            var all = new Button { Content = $"+{more} more on Fullcourt" };
            all.Classes.Add("row");
            all.Click += (_, _) => Mantel.Integrations.FullcourtLauncher.Open($"{Site}/?league={League}", OpenIn);
            _games.Children.Add(all);
        }

        _empty.IsVisible = shown.Count == 0;
        var friendly = League switch { "wnba" => "WNBA", "ncaam" => "men's college", "ncaaw" => "women's college", _ => "NBA" };
        _empty.Text = Teams.Length > 0 ? $"No games today for {Teams.Replace(",", ", ")}." : $"No {friendly} games today.";
        View.IsVisible = !(HideWhenEmpty && shown.Count == 0);
    }

    private Control GameRow(Game g)
    {
        var live = g.State == "in";
        var final = g.State == "post";

        Control TeamLine(Team t, Team other)
        {
            var logo = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
            if (t.Logo is { } url)
            {
                if (Logos.TryGetValue(url, out var bmp)) logo.Source = bmp;
                else (_logoSlots.TryGetValue(url, out var list) ? list : _logoSlots[url] = new()).Add(logo);
            }

            var name = new TextBlock
            {
                Text = (t.Rank is { } rank ? $"{rank} " : "") + t.Abbr,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 64,
            };
            var record = new TextBlock { Text = t.Record, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            record.Classes.Add("label");

            var score = new TextBlock
            {
                Text = t.Score?.ToString() ?? "",
                FontSize = 15,
                FontWeight = final && t.Winner ? FontWeight.Bold : FontWeight.Medium,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // The loser of a final fades back a little, the way scoreboards do.
            if (final && !t.Winner && t.Score is not null) { name.Classes.Add("muted"); score.Classes.Add("muted"); }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
            grid.Children.Add(logo);
            Grid.SetColumn(name, 1); grid.Children.Add(name);
            Grid.SetColumn(record, 2); grid.Children.Add(record);
            Grid.SetColumn(score, 3); grid.Children.Add(score);
            return grid;
        }

        var teams = new StackPanel { Spacing = 3, Children = { TeamLine(g.Away, g.Home), TeamLine(g.Home, g.Away) } };

        var statusText = g.State == "pre"
            ? g.Start.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture)
            : g.Status;
        var status = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.Classes.Add(live ? "accent" : "muted");
        if (live) status.FontWeight = FontWeight.SemiBold;

        var statusStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 78, Margin = new Thickness(12, 0, 0, 0) };
        statusStack.Children.Add(status);
        if (g.State == "pre" && g.Tv.Length > 0)
        {
            var tv = new TextBlock
            {
                Text = g.Tv.Split(" · ")[0],
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            tv.Classes.Add("label");
            statusStack.Children.Add(tv);
        }

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(teams);
        Grid.SetColumn(statusStack, 1);
        row.Children.Add(statusStack);

        var button = new Button { Content = row };
        button.Classes.Add("row");
        if (g.Url.Length > 0) button.Click += (_, _) => Mantel.Integrations.FullcourtLauncher.Open(g.Url, OpenIn);
        ToolTip.SetTip(button, $"{g.Away.Name} at {g.Home.Name}" + (g.Tv.Length > 0 ? $" · {g.Tv}" : ""));
        return button;
    }

    private async Task LoadLogosAsync(Board board, CancellationToken ct)
    {
        var urls = board.Games.Take(Max).SelectMany(g => new[] { g.Home.Logo, g.Away.Logo })
            .Where(u => u is not null && !Logos.ContainsKey(u)).Distinct().ToList();
        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url!, ct);
                using var ms = new MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 40);
                Dispatcher.UIThread.Post(() =>
                {
                    Logos[url!] = bmp;
                    if (_logoSlots.TryGetValue(url!, out var slots)) foreach (var img in slots) img.Source = bmp;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Log.Error("Team logo failed to load.", ex); }
        }
    }

    // --- Settings panel ---------------------------------------------------------------

    public static Control BuildOptions(WidgetOptionsContext ctx) =>
        OptionsUi.Panel(
            OptionsUi.Choice(ctx, "league", "League", new[]
            {
                ("nba", "NBA"), ("wnba", "WNBA"), ("ncaam", "Men's college (Top 25)"), ("ncaaw", "Women's college (Top 25)"),
            }, "nba"),
            OptionsUi.Text(ctx, "teams", "Only these teams", "e.g. BOS, NY", "Team abbreviations, separated by commas. Leave empty for every game."),
            OptionsUi.Choice(ctx, "max", "Games to show", new[] { ("3", "3"), ("4", "4"), ("6", "6"), ("8", "8"), ("12", "12") }, "6"),
            OptionsUi.Toggle(ctx, "hideWhenEmpty", "Hide when there are no games"),
            OpenInOption(ctx),
            OptionsUi.Text(ctx, "site", "Fullcourt address", DefaultSite, "Change this only if you move Fullcourt to a new host."));

    /// <summary>Website or app, plus a line saying whether the app is actually installed.</summary>
    private static Control OpenInOption(WidgetOptionsContext ctx)
    {
        var choice = OptionsUi.Choice(ctx, "openIn", "Open games in", new[]
        {
            ("web", "Website (your browser)"),
            ("app", "Fullcourt app"),
        }, "web");

        var installed = Mantel.Integrations.FullcourtLauncher.FindInstalledApp();
        var status = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Text = installed is not null
                ? "Fullcourt app found. Games open in its own window."
                : "Fullcourt isn't installed as an app yet, so games open in an app-style Edge window. " +
                  "To install it: open Fullcourt in Edge or Chrome, then choose \"Install Fullcourt\" from the browser's menu (Apps).",
        };
        status.Classes.Add("muted");

        var install = new Button { Content = "Open Fullcourt to install it", Margin = new Thickness(0, 8, 0, 0), IsVisible = installed is null };
        install.Classes.Add("ghost");
        install.Click += (_, _) => AppInfo.OpenUrl(ctx.Get("site", DefaultSite));

        return new StackPanel { Children = { choice, status, install } };
    }

    public static string? Subtitle(WidgetInstanceSettings s)
    {
        var league = s.Options.TryGetValue("league", out var l) ? l : "nba";
        var name = league switch { "wnba" => "WNBA", "ncaam" => "Men's college", "ncaaw" => "Women's college", _ => "NBA" };
        return s.Options.TryGetValue("teams", out var t) && !string.IsNullOrWhiteSpace(t) ? $"{name} · {t}" : name;
    }
}
