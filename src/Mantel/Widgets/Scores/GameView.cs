using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Scores;

/// <summary>
/// One game, inside the Scores widget. Before tip-off: the matchup (odds, projection, season
/// leaders, recent form, injuries, season series). During and after: score, win probability, line
/// score, and tabs for an overview (top performers, game flow chart, lead changes, game info), each
/// team's full box score, team stats side by side, and the plays. Data comes from Fullcourt's
/// /api/game and refreshes every 15 seconds while the game is on.
/// </summary>
internal sealed class GameView
{
    private const double Width = WidgetCard.ContentWidth;

    private readonly HttpClient _http;
    private readonly Dictionary<string, Bitmap> _logos;
    private readonly string _site;
    private readonly string _league;
    private readonly string _id;
    private readonly Action _back;

    private readonly StackPanel _root = new() { Width = Width };
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _cts;
    private Game? _game;

    // What the viewer picked, kept across refreshes.
    private string _tab = "overview";
    private string _boxSide = "";
    private readonly HashSet<string> _expanded = new();
    private bool _scoringOnly;
    private int _playsShown = 25;

    public GameView(HttpClient http, Dictionary<string, Bitmap> logos, string site, string league, string id,
                    Action back, Action<string> openFullcourt)
    {
        _http = http;
        _logos = logos;
        _site = site;
        _league = league;
        _id = id;
        _back = back;

        var backButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { Icons.Create(Icons.ChevronLeft, 14), new TextBlock { Text = "All games", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center } },
            },
            Padding = new Thickness(4, 3, 8, 3),
            Margin = new Thickness(-4, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        backButton.Classes.Add("row");
        backButton.Click += (_, _) => _back();

        var open = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new TextBlock { Text = "Fullcourt", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }, Icons.Create(Icons.Open, 13) },
            },
            Padding = new Thickness(8, 3, 4, 3),
            Margin = new Thickness(0, 0, -4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        open.Classes.Add("row");
        ToolTip.SetTip(open, "Open this game in Fullcourt");
        open.Click += (_, _) => openFullcourt(_game?.Url is { Length: > 0 } u ? u : $"{_site}/?league={_league}&game={_id}");

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(backButton);
        Grid.SetColumn(open, 1);
        bar.Children.Add(open);

        _status.Classes.Add("muted");
        _status.Text = "Loading game…";
        _root.Children.Add(bar);
        _root.Children.Add(_body);
        _root.Children.Add(_status);
    }

    public Control View => _root;

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop() => _cts?.Cancel();

    // --- Data ---------------------------------------------------------------------------------------

    private sealed record Team(string Abbr, string Name, string? Logo, string? Color, int? Score, string Record, int[] Lines, bool Winner);
    private sealed record Stat(string Label, string Away, string Home);
    private sealed record Player(string Short, string Name, string Pos, bool Starter, int Min, int Pts, int Reb, int Ast, int Stl, int Blk,
                                 int To, int Oreb, int Dreb, int Pf, string Fg, string Three, string Ft, string Pm);
    private sealed record Play(int Period, string Clock, string Text, string? Team, bool Scoring, int Points, int? Away, int? Home);
    private sealed record Leader(string Stat, string Name, string Value);
    private sealed record Result(string Outcome, string Score, string Opp, string At);
    private sealed record Injury(string Name, string Pos, string Status, string Detail);
    private sealed record Odds(string Provider, string Details, double? OverUnder, int? AwayMoneyline, int? HomeMoneyline);

    private sealed class Game
    {
        public string State = "", Status = "", Tv = "", Url = "", Venue = "", City = "", Series = "";
        public DateTimeOffset? Start;
        public int? Attendance;
        public List<string> Officials = new();
        public Team Away = null!, Home = null!;
        public List<Stat> Stats = new();
        public List<Player> AwayPlayers = new(), HomePlayers = new();
        public List<Play> Plays = new();
        public List<(double T, int M)> Flow = new();
        public double Length, PeriodLength = 720;
        public int PeriodCount = 4;
        public int LeadChanges, TimesTied;
        public Dictionary<string, int> LargestLead = new();
        public (string Team, int Points)? Run;
        public double? HomeWinPct;
        public Odds? Odds;
        public (double Away, double Home)? Projection;
        public Dictionary<string, List<Leader>> Leaders = new();
        public Dictionary<string, List<Result>> Form = new();
        public Dictionary<string, List<Injury>> Injuries = new();
        public int Refresh;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(30);
            try
            {
                var json = await _http.GetStringAsync($"{_site}/api/game?league={_league}&id={_id}", ct);
                var game = Parse(json) ?? throw new InvalidDataException("No game in the response.");
                await Dispatcher.UIThread.InvokeAsync(() => { _game = game; Render(); });
                _ = LoadLogosAsync(game, ct);
                if (game.Refresh <= 0) return; // finished: nothing more will change
                wait = TimeSpan.FromSeconds(Math.Clamp(game.Refresh, 10, 600));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Error("Game view fetch failed.", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_game is not null) return; // keep showing the last good data
                    _status.Text = "Couldn't load this game. Retrying…";
                    _status.IsVisible = true;
                });
            }
            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int? I(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
    private static double? D(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static bool B(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static JsonElement O(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v : default;
    private static IEnumerable<JsonElement> A(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static Game? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!B(r, "ok")) return null;

        Team T(JsonElement t) => new(S(t, "abbr"), S(t, "name"), S(t, "logo") is { Length: > 0 } l ? l : null,
            S(t, "color") is { Length: > 0 } c ? c : null, I(t, "score"), S(t, "record"),
            A(O(t, "linescores")).Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0).ToArray(), B(t, "winner"));

        List<Player> Players(JsonElement arr) => A(arr).Select(p => new Player(
            S(p, "short"), S(p, "name"), S(p, "pos"), B(p, "starter"), I(p, "min") ?? 0, I(p, "pts") ?? 0, I(p, "reb") ?? 0,
            I(p, "ast") ?? 0, I(p, "stl") ?? 0, I(p, "blk") ?? 0, I(p, "to") ?? 0, I(p, "oreb") ?? 0, I(p, "dreb") ?? 0, I(p, "pf") ?? 0,
            S(p, "fg"), S(p, "three"), S(p, "ft"), S(p, "pm"))).ToList();

        Dictionary<string, List<TItem>> BySide<TItem>(JsonElement obj, Func<JsonElement, TItem> map)
        {
            var d = new Dictionary<string, List<TItem>>();
            if (obj.ValueKind != JsonValueKind.Object) return d;
            foreach (var p in obj.EnumerateObject()) d[p.Name] = A(p.Value).Select(map).ToList();
            return d;
        }

        var g = new Game
        {
            State = S(r, "state"),
            Status = S(r, "status"),
            Tv = S(r, "tv"),
            Url = S(r, "url"),
            Venue = S(r, "venue"),
            City = S(r, "city"),
            Attendance = I(r, "attendance"),
            Officials = A(O(r, "officials")).Select(o => o.GetString() ?? "").Where(o => o.Length > 0).ToList(),
            Start = DateTimeOffset.TryParse(S(r, "start"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var st) ? st : null,
            Away = T(O(r, "away")),
            Home = T(O(r, "home")),
            Stats = A(O(r, "stats")).Select(s => new Stat(S(s, "label"), S(s, "away"), S(s, "home"))).ToList(),
            AwayPlayers = Players(O(O(r, "players"), "away")),
            HomePlayers = Players(O(O(r, "players"), "home")),
            Plays = A(O(r, "plays")).Select(p => new Play(I(p, "period") ?? 0, S(p, "clock"), S(p, "text"),
                S(p, "team") is { Length: > 0 } tm ? tm : null, B(p, "scoring"), I(p, "points") ?? 0, I(p, "away"), I(p, "home"))).ToList(),
            Flow = A(O(r, "flow")).Select(p => (D(p, "t") ?? 0, I(p, "m") ?? 0)).ToList(),
            Length = D(r, "length") ?? 2880,
            PeriodLength = D(r, "periodLength") ?? 720,
            PeriodCount = I(r, "periodCount") ?? 4,
            HomeWinPct = D(r, "homeWinPct"),
            Series = S(O(r, "series"), "summary"),
            Refresh = I(r, "refresh") ?? 0,
            Form = BySide(O(r, "form"), e => new Result(S(e, "result"), S(e, "score"), S(e, "opp"), S(e, "at"))),
            Injuries = BySide(O(r, "injuries"), e => new Injury(S(e, "name"), S(e, "pos"), S(e, "status"), S(e, "detail"))),
        };

        var summary = O(r, "summary");
        g.LeadChanges = I(summary, "leadChanges") ?? 0;
        g.TimesTied = I(summary, "timesTied") ?? 0;
        if (O(summary, "largestLead") is { ValueKind: JsonValueKind.Object } ll)
            foreach (var p in ll.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number) g.LargestLead[p.Name] = p.Value.GetInt32();
        if (O(summary, "run") is { ValueKind: JsonValueKind.Object } run && I(run, "points") is { } pts)
            g.Run = (S(run, "team"), pts);

        var pre = O(r, "pre");
        if (O(pre, "odds") is { ValueKind: JsonValueKind.Object } odds)
            g.Odds = new Odds(S(odds, "provider"), S(odds, "details"), D(odds, "overUnder"), I(odds, "awayMoneyline"), I(odds, "homeMoneyline"));
        if (O(pre, "projection") is { ValueKind: JsonValueKind.Object } proj && D(proj, "home") is { } ph && D(proj, "away") is { } pa)
            g.Projection = (pa, ph);
        g.Leaders = BySide(O(pre, "leaders"), e => new Leader(S(e, "stat"), S(e, "name"), S(e, "value")));
        return g;
    }

    // --- Display ------------------------------------------------------------------------------------

    private readonly Dictionary<string, List<Image>> _logoSlots = new();

    private bool Halves => _game?.PeriodCount == 2;

    private void Render()
    {
        if (_game is not { } g) return;
        _status.IsVisible = false;
        _body.Children.Clear();
        _logoSlots.Clear();
        if (_boxSide.Length == 0) _boxSide = "away";

        _body.Children.Add(Scoreboard(g));
        if (g.State == "in" && g.HomeWinPct is { } wp) _body.Children.Add(WinBar(g, wp, "WIN PROBABILITY"));
        if (g.State == "in" && g.Run is { } run) _body.Children.Add(RunBanner(run));
        if (g.State != "pre" && g.Away.Lines.Length > 0) _body.Children.Add(LineScore(g));

        if (g.State == "pre")
        {
            _body.Children.Add(Matchup(g));
            return;
        }

        _body.Children.Add(Tabs());
        _body.Children.Add(_tab switch
        {
            "box" => BoxScore(g),
            "team" => TeamStats(g),
            "plays" => Plays(g),
            _ => Overview(g),
        });
    }

    // Small building blocks ---------------------------------------------------------------------------

    private static TextBlock Text(string text, string cls, double size = 12.5, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (weight is { } w) t.FontWeight = w;
        t.Classes.Add(cls);
        if (cls == "muted" || cls == "value") t.FontSize = size; // the class sets its own size; keep ours
        return t;
    }

    private static TextBlock Section(string title)
    {
        var t = new TextBlock { Text = title.ToUpperInvariant(), Margin = new Thickness(0, 14, 0, 6), FontSize = 10.5 };
        t.Classes.Add("label");
        return t;
    }

    /// <summary>"away value · label · home value" in three columns, label centered.</summary>
    private static Grid Compare(Control away, string label, Control home)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 1) };
        away.HorizontalAlignment = HorizontalAlignment.Left;
        home.HorizontalAlignment = HorizontalAlignment.Right;
        var l = Text(label, "muted", 11.5);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.Margin = new Thickness(6, 0);
        grid.Children.Add(away);
        Grid.SetColumn(l, 1);
        grid.Children.Add(l);
        Grid.SetColumn(home, 2);
        grid.Children.Add(home);
        return grid;
    }

    private static Grid KeyValue(string key, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("88,*"), Margin = new Thickness(0, 1) };
        var k = Text(key, "muted", 12);
        var v = new TextBlock { Text = value, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        v.Classes.Add("value");
        v.FontSize = 12.5;
        grid.Children.Add(k);
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    private Control Chip(string label, bool on, Action click, Control? icon = null)
    {
        var b = new Button { Padding = new Thickness(9, 3) };
        b.Content = icon is null ? label : new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { icon, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
        };
        b.Classes.Add("chip");
        b.Classes.Set("on", on);
        b.Click += (_, _) => click();
        return b;
    }

    private string PeriodShort(int p) =>
        p <= (_game?.PeriodCount ?? 4) ? (Halves ? $"H{p}" : $"Q{p}") : p - (_game?.PeriodCount ?? 4) == 1 ? "OT" : $"{p - (_game?.PeriodCount ?? 4)}OT";

    private string PeriodLong(int p)
    {
        var count = _game?.PeriodCount ?? 4;
        if (p > count) return p - count == 1 ? "OVERTIME" : $"{p - count}{Ordinal(p - count)} OVERTIME".ToUpperInvariant();
        return $"{p}{Ordinal(p)} {(Halves ? "HALF" : "QUARTER")}".ToUpperInvariant();
    }

    private static string Ordinal(int n) => (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

    // Top of the view ----------------------------------------------------------------------------------

    private Control Scoreboard(Game g)
    {
        var live = g.State == "in";
        var final = g.State == "post";
        var pre = g.State == "pre";

        Control Side(Team t, bool right)
        {
            var logo = Logo(t.Logo, 30);
            var abbr = new TextBlock { Text = t.Abbr, FontWeight = FontWeight.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            var record = new TextBlock { Text = t.Record, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
            record.Classes.Add("label");
            var id = new StackPanel { Spacing = 2, Children = { logo, abbr, record }, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(id, t.Name);
            if (pre) return new StackPanel { Children = { id }, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(right ? 0 : 8, 0, right ? 8 : 0, 0) };

            var score = new TextBlock
            {
                Text = t.Score?.ToString() ?? "–",
                FontSize = 32,
                FontWeight = final && t.Winner ? FontWeight.Bold : FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(right ? 0 : 10, 0, right ? 10 : 0, 0),
            };
            if (final && !t.Winner) score.Opacity = 0.55;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            if (right) { row.Children.Add(score); row.Children.Add(id); }
            else { row.Children.Add(id); row.Children.Add(score); }
            return row;
        }

        var statusText = pre && g.Start is { } start
            ? start.ToLocalTime().ToString("ddd, MMM d", CultureInfo.CurrentCulture) + "\n" + start.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture)
            : g.Status.Replace(" - ", "\n");
        var status = new TextBlock
        {
            Text = statusText,
            TextAlignment = TextAlignment.Center,
            FontSize = pre ? 13 : 12,
            FontWeight = live || pre ? FontWeight.SemiBold : FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        status.Classes.Add(live ? "accent" : pre ? "value" : "muted");
        status.FontSize = pre ? 13 : 12;
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3, Children = { status } };
        if (live)
        {
            var pill = new Border { Child = new TextBlock { Text = "LIVE" }, HorizontalAlignment = HorizontalAlignment.Center };
            pill.Classes.Add("pill");
            middle.Children.Insert(0, pill);
        }
        if (g.Tv.Length > 0 && !final)
        {
            var tv = new TextBlock { Text = g.Tv.Split(" · ")[0], FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
            tv.Classes.Add("label");
            ToolTip.SetTip(tv, g.Tv);
            middle.Children.Add(tv);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 0, 0, 10) };
        grid.Children.Add(Side(g.Away, right: false));
        Grid.SetColumn(middle, 1);
        middle.Margin = new Thickness(6, 0);
        grid.Children.Add(middle);
        var home = Side(g.Home, right: true);
        Grid.SetColumn(home, 2);
        grid.Children.Add(home);
        return grid;
    }

    private static IBrush TeamBrush(string? hex, string fallback) =>
        Brush.Parse(hex is { Length: 7 } ? hex : fallback);

    private static Color TeamColor(string? hex, string fallback) =>
        Color.TryParse(hex is { Length: 7 } ? hex : fallback, out var c) ? c : Colors.Gray;

    /// <summary>Who's likely to win, as one bar in the two teams' colors.</summary>
    private static Control WinBar(Game g, double homePct, string caption)
    {
        var awayPct = 100 - homePct;
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                $"{Math.Max(awayPct, 1).ToString("0.#", CultureInfo.InvariantCulture)}*,{Math.Max(homePct, 1).ToString("0.#", CultureInfo.InvariantCulture)}*"),
            Height = 5,
        };
        var a = new Border { Background = TeamBrush(g.Away.Color, "#8C8A86"), CornerRadius = new CornerRadius(3, 0, 0, 3) };
        var h = new Border { Background = TeamBrush(g.Home.Color, "#E8A15A"), CornerRadius = new CornerRadius(0, 3, 3, 0), Margin = new Thickness(2, 0, 0, 0) };
        bar.Children.Add(a);
        Grid.SetColumn(h, 1);
        bar.Children.Add(h);

        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
        var al = Text($"{g.Away.Abbr} {awayPct:0}%", "muted", 11);
        var mid = Text(caption, "label", 10);
        var hl = Text($"{homePct:0}% {g.Home.Abbr}", "muted", 11);
        hl.HorizontalAlignment = HorizontalAlignment.Right;
        labels.Children.Add(al);
        Grid.SetColumn(mid, 1);
        labels.Children.Add(mid);
        Grid.SetColumn(hl, 2);
        labels.Children.Add(hl);

        return new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { bar, labels } };
    }

    private static Control RunBanner((string Team, int Points) run)
    {
        var t = new TextBlock { Text = $"{run.Team} on a {run.Points}-0 run", FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        t.Classes.Add("accent");
        var b = new Border { Child = t, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, -4, 0, 10) };
        b.Classes.Add("inset");
        return b;
    }

    /// <summary>Points by period and the total.</summary>
    private Control LineScore(Game g)
    {
        var periods = Math.Max(g.PeriodCount, Math.Max(g.Away.Lines.Length, g.Home.Lines.Length));
        var cols = "48," + string.Join(",", Enumerable.Repeat("*", periods)) + ",36";
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };

        void Cell(string text, int row, int col, string cls, bool bold = false)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = row == 0 ? 10.5 : 12.5,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                Margin = new Thickness(0, 2),
            };
            t.Classes.Add(cls);
            t.FontSize = row == 0 ? 10.5 : 12.5;
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }

        for (var p = 0; p < periods; p++)
            Cell(p < g.PeriodCount ? (p + 1).ToString() : p == g.PeriodCount ? "OT" : $"{p - g.PeriodCount + 1}OT", 0, p + 1, "label");
        Cell("T", 0, periods + 1, "label");

        void Row(Team t, int row)
        {
            Cell(t.Abbr, row, 0, "value", bold: true);
            for (var p = 0; p < periods; p++) Cell(p < t.Lines.Length ? t.Lines[p].ToString() : "", row, p + 1, "muted");
            Cell(t.Score?.ToString() ?? "", row, periods + 1, "value", bold: true);
        }
        Row(g.Away, 1);
        Row(g.Home, 2);

        var box = new Border { Child = grid, Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 12) };
        box.Classes.Add("inset");
        return box;
    }

    private Control Tabs()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (id, label) in new[] { ("overview", "Overview"), ("box", "Box score"), ("team", "Team"), ("plays", "Plays") })
        {
            var tab = id;
            var chip = Chip(label, _tab == id, () => { _tab = tab; Render(); });
            chip.Margin = new Thickness(0, 0, 6, 6);
            row.Children.Add(chip);
        }
        return row;
    }

    // Overview -----------------------------------------------------------------------------------------

    private Control Overview(Game g)
    {
        var stack = new StackPanel();

        // Top performers, worked out from the box score.
        if (g.AwayPlayers.Count > 0 || g.HomePlayers.Count > 0)
        {
            stack.Children.Add(Section("Top performers"));
            foreach (var (label, pick) in new (string, Func<Player, int>)[] { ("PTS", p => p.Pts), ("REB", p => p.Reb), ("AST", p => p.Ast) })
            {
                Control Best(List<Player> players, bool right)
                {
                    var best = players.OrderByDescending(pick).ThenByDescending(p => p.Min).FirstOrDefault();
                    if (best is null) return new TextBlock();
                    var value = Text(pick(best).ToString(), "value", 14, FontWeight.Bold);
                    value.FontSize = 14;
                    var name = Text(best.Short, "muted", 12);
                    name.VerticalAlignment = VerticalAlignment.Center;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    if (right) { row.Children.Add(name); row.Children.Add(value); }
                    else { row.Children.Add(value); row.Children.Add(name); }
                    ToolTip.SetTip(row, $"{best.Name}: {best.Pts} PTS, {best.Reb} REB, {best.Ast} AST in {best.Min} min");
                    return row;
                }
                stack.Children.Add(Compare(Best(g.AwayPlayers, false), label, Best(g.HomePlayers, true)));
            }
        }

        // Game flow chart.
        if (g.Flow.Count > 1)
        {
            stack.Children.Add(Section("Game flow"));
            var chart = new FlowChart(g.Flow, g.Length, g.PeriodLength, g.PeriodCount,
                TeamColor(g.Home.Color, "#E8A15A"), TeamColor(g.Away.Color, "#8C8A86")) { Height = 86 };
            var homeTag = Text(g.Home.Abbr, "label", 9.5);
            homeTag.VerticalAlignment = VerticalAlignment.Top;
            homeTag.Margin = new Thickness(3, 1);
            var awayTag = Text(g.Away.Abbr, "label", 9.5);
            awayTag.VerticalAlignment = VerticalAlignment.Bottom;
            awayTag.Margin = new Thickness(3, 1);
            var chartBox = new Border
            {
                Child = new Panel { Children = { chart, homeTag, awayTag } },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                ClipToBounds = true,
            };
            chartBox.Classes.Add("inset");
            ToolTip.SetTip(chartBox, $"Above the line {g.Home.Abbr} leads, below it {g.Away.Abbr} leads.");
            stack.Children.Add(chartBox);

            var facts = new List<string>();
            facts.Add(g.LeadChanges == 1 ? "1 lead change" : $"{g.LeadChanges} lead changes");
            facts.Add(g.TimesTied == 1 ? "tied once" : $"tied {g.TimesTied} times");
            var leads = string.Join(", ", new[] { g.Away.Abbr, g.Home.Abbr }
                .Where(a => g.LargestLead.TryGetValue(a, out var v) && v > 0).Select(a => $"{a} {g.LargestLead[a]}"));
            var line = new TextBlock
            {
                Text = string.Join(" · ", facts) + (leads.Length > 0 ? $"\nLargest lead: {leads}" : ""),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
            };
            line.Classes.Add("muted");
            line.FontSize = 12;
            stack.Children.Add(line);
        }

        stack.Children.Add(GameInfo(g));
        return stack;
    }

    private Control GameInfo(Game g)
    {
        var stack = new StackPanel();
        stack.Children.Add(Section("Game info"));
        if (g.Venue.Length > 0) stack.Children.Add(KeyValue("Arena", g.Venue + (g.City.Length > 0 ? $", {g.City}" : "")));
        if (g.Start is { } start && g.State == "pre")
            stack.Children.Add(KeyValue("Tip-off", start.ToLocalTime().ToString("dddd, MMM d · h:mm tt", CultureInfo.CurrentCulture)));
        if (g.Tv.Length > 0) stack.Children.Add(KeyValue("TV", g.Tv));
        if (g.Attendance is { } att && att > 0) stack.Children.Add(KeyValue("Attendance", att.ToString("N0", CultureInfo.CurrentCulture)));
        if (g.Officials.Count > 0) stack.Children.Add(KeyValue("Officials", string.Join(", ", g.Officials)));
        if (g.Series.Length > 0) stack.Children.Add(KeyValue("Season series", g.Series));
        return stack.Children.Count > 1 ? stack : new Panel();
    }

    // Before tip-off -----------------------------------------------------------------------------------

    private Control Matchup(Game g)
    {
        var stack = new StackPanel();

        if (g.Projection is { } proj) stack.Children.Add(WinBar(g, proj.Home, "PROJECTION"));

        if (g.Odds is { } odds && (odds.Details.Length > 0 || odds.OverUnder is not null))
        {
            stack.Children.Add(Section(odds.Provider.Length > 0 ? $"Odds · {odds.Provider}" : "Odds"));
            if (odds.Details.Length > 0) stack.Children.Add(KeyValue("Spread", odds.Details));
            if (odds.OverUnder is { } ou) stack.Children.Add(KeyValue("Total", $"O/U {ou.ToString("0.#", CultureInfo.InvariantCulture)}"));
            if (odds.AwayMoneyline is { } am && odds.HomeMoneyline is { } hm)
                stack.Children.Add(KeyValue("Moneyline", $"{g.Away.Abbr} {Signed(am)} · {g.Home.Abbr} {Signed(hm)}"));
        }

        var leadersAway = g.Leaders.GetValueOrDefault("away") ?? new();
        var leadersHome = g.Leaders.GetValueOrDefault("home") ?? new();
        if (leadersAway.Count > 0 || leadersHome.Count > 0)
        {
            stack.Children.Add(Section("Season leaders"));
            var stats = leadersAway.Select(l => l.Stat).Concat(leadersHome.Select(l => l.Stat)).Distinct().Take(3);
            foreach (var stat in stats)
            {
                Control Side(List<Leader> list, bool right)
                {
                    var l = list.FirstOrDefault(x => x.Stat == stat);
                    if (l is null) return new TextBlock();
                    var value = Text(l.Value, "value", 13.5, FontWeight.Bold);
                    value.FontSize = 13.5;
                    var name = Text(l.Name, "muted", 12);
                    name.VerticalAlignment = VerticalAlignment.Center;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    if (right) { row.Children.Add(name); row.Children.Add(value); }
                    else { row.Children.Add(value); row.Children.Add(name); }
                    return row;
                }
                stack.Children.Add(Compare(Side(leadersAway, false), stat, Side(leadersHome, true)));
            }
        }

        if (g.Form.Count > 0)
        {
            stack.Children.Add(Section("Last 5 games"));
            foreach (var (side, team) in new[] { ("away", g.Away), ("home", g.Home) })
            {
                if (!g.Form.TryGetValue(side, out var games) || games.Count == 0) continue;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 2) };
                var name = Text(team.Abbr, "value", 12.5, FontWeight.SemiBold);
                name.Width = 44;
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                foreach (var r in games)
                {
                    var win = r.Outcome == "W";
                    var t = new TextBlock { Text = r.Outcome.Length > 0 ? r.Outcome : "–", FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
                    var b = new Border { Width = 22, Height = 20, CornerRadius = new CornerRadius(5), Child = t };
                    t.VerticalAlignment = VerticalAlignment.Center;
                    if (win) { b.Classes.Add("pill"); b.Padding = new Thickness(0); b.CornerRadius = new CornerRadius(5); }
                    else { b.Classes.Add("inset"); t.Classes.Add("muted"); t.FontSize = 11; }
                    ToolTip.SetTip(b, $"{(win ? "Won" : "Lost")} {r.Score} {(r.At == "@" ? "at" : "vs")} {r.Opp}");
                    row.Children.Add(b);
                }
                var wins = games.Count(x => x.Outcome == "W");
                var tally = Text($"{wins}-{games.Count - wins}", "muted", 12);
                tally.VerticalAlignment = VerticalAlignment.Center;
                tally.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(tally);
                stack.Children.Add(row);
            }
        }

        var anyInjuries = g.Injuries.Values.Any(l => l.Count > 0);
        if (anyInjuries)
        {
            stack.Children.Add(Section("Injuries"));
            foreach (var (side, team) in new[] { ("away", g.Away), ("home", g.Home) })
            {
                if (!g.Injuries.TryGetValue(side, out var list) || list.Count == 0) continue;
                var head = Text(team.Abbr, "value", 12.5, FontWeight.SemiBold);
                head.Margin = new Thickness(0, 4, 0, 2);
                stack.Children.Add(head);
                foreach (var i in list)
                {
                    var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 1) };
                    var who = Text(i.Name + (i.Pos.Length > 0 ? $"  {i.Pos}" : ""), "muted", 12);
                    var status = Text(i.Status, i.Status.Equals("Out", StringComparison.OrdinalIgnoreCase) ? "accent" : "muted", 12, FontWeight.SemiBold);
                    status.FontSize = 12;
                    grid.Children.Add(who);
                    Grid.SetColumn(status, 1);
                    grid.Children.Add(status);
                    if (i.Detail.Length > 0) ToolTip.SetTip(grid, i.Detail);
                    stack.Children.Add(grid);
                }
            }
        }

        stack.Children.Add(GameInfo(g));

        var note = new TextBlock { Text = "Box score, game flow and plays show up once the game starts.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 11.5 };
        note.Classes.Add("label");
        note.LetterSpacing = 0;
        stack.Children.Add(note);
        return stack;
    }

    private static string Signed(int n) => n > 0 ? $"+{n}" : n.ToString(CultureInfo.InvariantCulture);

    // Box score ----------------------------------------------------------------------------------------

    private Control BoxScore(Game g)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var pick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (side, t) in new[] { ("away", g.Away), ("home", g.Home) })
        {
            var s = side;
            pick.Children.Add(Chip(t.Abbr, _boxSide == side, () => { _boxSide = s; Render(); }, Logo(t.Logo, 14)));
        }
        stack.Children.Add(pick);

        var players = _boxSide == "home" ? g.HomePlayers : g.AwayPlayers;
        if (players.Count == 0)
        {
            stack.Children.Add(Text("No box score yet.", "muted"));
            return stack;
        }

        const string cols = "*,28,32,30,30";
        Grid Row(string name, string min, string pts, string reb, string ast, bool header = false)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
            var values = new[] { name, min, pts, reb, ast };
            for (var i = 0; i < values.Length; i++)
            {
                var t = new TextBlock
                {
                    Text = values[i],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    FontWeight = !header && i == 2 ? FontWeight.SemiBold : FontWeight.Normal,
                };
                t.Classes.Add(header ? "label" : i == 0 || i == 2 ? "value" : "muted");
                t.FontSize = header ? 10.5 : 12.5;
                Grid.SetColumn(t, i);
                grid.Children.Add(t);
            }
            return grid;
        }

        stack.Children.Add(Row("STARTERS", "MIN", "PTS", "REB", "AST", header: true));
        var benchShown = false;
        foreach (var p in players)
        {
            if (!p.Starter && !benchShown)
            {
                benchShown = true;
                var bench = Row("BENCH", "", "", "", "", header: true);
                bench.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(bench);
            }
            stack.Children.Add(PlayerRow(p, Row(p.Short + (p.Pos.Length > 0 ? $"  {p.Pos}" : ""), p.Min.ToString(), p.Pts.ToString(), p.Reb.ToString(), p.Ast.ToString())));
        }

        var totals = Row("TEAM", "", players.Sum(p => p.Pts).ToString(), players.Sum(p => p.Reb).ToString(), players.Sum(p => p.Ast).ToString());
        totals.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 0), Classes = { "divider" } });
        stack.Children.Add(totals);

        var hint = Text("Click a player for their full line.", "label", 10.5);
        hint.LetterSpacing = 0;
        hint.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(hint);
        return stack;
    }

    /// <summary>A player's row; click it to open their full stat line underneath.</summary>
    private Control PlayerRow(Player p, Grid summary)
    {
        var key = p.Name;
        var open = _expanded.Contains(key);
        var stack = new StackPanel { Children = { summary } };
        if (open)
        {
            var detail = new TextBlock
            {
                Text = $"FG {p.Fg} · 3PT {p.Three} · FT {p.Ft}\n" +
                       $"OREB {p.Oreb} · DREB {p.Dreb} · STL {p.Stl} · BLK {p.Blk}\n" +
                       $"TO {p.To} · PF {p.Pf}" + (p.Pm.Length > 0 ? $" · +/- {p.Pm}" : ""),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 2),
                LineHeight = 17,
            };
            detail.Classes.Add("muted");
            detail.FontSize = 11.5;
            var box = new Border { Child = detail, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 2, 0, 4) };
            box.Classes.Add("inset");
            stack.Children.Add(box);
        }

        var hit = new Border
        {
            Child = stack,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(0, 1),
        };
        ToolTip.SetTip(hit, p.Name);
        hit.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if (!_expanded.Remove(key)) _expanded.Add(key);
            Render();
        };
        return hit;
    }

    // Team stats ---------------------------------------------------------------------------------------

    private Control TeamStats(Game g)
    {
        var stack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 6, 0, 0) };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 4) };
        var a = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Logo(g.Away.Logo, 16), Text(g.Away.Abbr, "value", 13, FontWeight.SemiBold) } };
        var h = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { Text(g.Home.Abbr, "value", 13, FontWeight.SemiBold), Logo(g.Home.Logo, 16) } };
        head.Children.Add(a);
        Grid.SetColumn(h, 1);
        head.Children.Add(h);
        stack.Children.Add(head);

        foreach (var s in g.Stats)
        {
            var lowerIsBetter = s.Label is "Turnovers" or "Fouls" or "Technicals";
            var av = Number(s.Away);
            var hv = Number(s.Home);
            var awayBetter = av is not null && hv is not null && (lowerIsBetter ? av < hv : av > hv);
            var homeBetter = av is not null && hv is not null && (lowerIsBetter ? hv < av : hv > av);
            var at = Text(s.Away, awayBetter ? "value" : "muted", 13, awayBetter ? FontWeight.SemiBold : FontWeight.Normal);
            var ht = Text(s.Home, homeBetter ? "value" : "muted", 13, homeBetter ? FontWeight.SemiBold : FontWeight.Normal);
            at.FontSize = ht.FontSize = 13;
            stack.Children.Add(Compare(at, s.Label, ht));
        }
        if (g.Stats.Count == 0) stack.Children.Add(Text("No team stats yet.", "muted"));
        return stack;
    }

    private static double? Number(string s)
    {
        var first = s.Split('-')[0];
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // Plays --------------------------------------------------------------------------------------------

    private Control Plays(Game g)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var filter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
        filter.Children.Add(Chip("All plays", !_scoringOnly, () => { _scoringOnly = false; _playsShown = 25; Render(); }));
        filter.Children.Add(Chip("Scoring", _scoringOnly, () => { _scoringOnly = true; _playsShown = 25; Render(); }));
        stack.Children.Add(filter);

        var plays = g.Plays.Where(p => !_scoringOnly || p.Scoring).ToList();
        var period = -1;
        foreach (var p in plays.Take(_playsShown))
        {
            if (p.Period != period)
            {
                period = p.Period;
                stack.Children.Add(Section(PeriodLong(p.Period)));
            }

            var when = Text($"{PeriodShort(p.Period)} {p.Clock}", "label", 10.5);
            when.Width = 58;
            when.LetterSpacing = 0.5;
            var text = new TextBlock { Text = p.Text, TextWrapping = TextWrapping.Wrap };
            text.Classes.Add(p.Scoring ? "value" : "muted");
            text.FontSize = 12.5;
            if (p.Scoring) text.FontWeight = FontWeight.Medium;
            var content = new StackPanel { Spacing = 1, Children = { text } };
            if (p.Scoring && p.Away is not null && p.Home is not null)
            {
                var score = new TextBlock { Text = $"{g.Away.Abbr} {p.Away} – {p.Home} {g.Home.Abbr}", FontSize = 11, FontWeight = FontWeight.SemiBold };
                score.Classes.Add("accent");
                content.Children.Add(score);
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 7) };
            row.Children.Add(when);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            stack.Children.Add(row);
        }

        if (plays.Count > _playsShown)
        {
            var more = new Button { Content = $"Show {Math.Min(25, plays.Count - _playsShown)} more", HorizontalAlignment = HorizontalAlignment.Left };
            more.Classes.Add("row");
            more.Click += (_, _) => { _playsShown += 25; Render(); };
            stack.Children.Add(more);
        }
        if (plays.Count == 0) stack.Children.Add(Text(_scoringOnly ? "No scoring plays yet." : "No plays yet.", "muted"));
        return stack;
    }

    // Logos --------------------------------------------------------------------------------------------

    private Image Logo(string? url, double size)
    {
        var img = new Image { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (url is null) return img;
        if (_logos.TryGetValue(url, out var bmp)) img.Source = bmp;
        else (_logoSlots.TryGetValue(url, out var list) ? list : _logoSlots[url] = new()).Add(img);
        return img;
    }

    private async Task LoadLogosAsync(Game g, CancellationToken ct)
    {
        foreach (var url in new[] { g.Away.Logo, g.Home.Logo }.Where(u => u is not null && !_logos.ContainsKey(u)).Distinct())
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url!, ct);
                using var ms = new MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 80);
                Dispatcher.UIThread.Post(() =>
                {
                    _logos[url!] = bmp;
                    if (_logoSlots.TryGetValue(url!, out var slots)) foreach (var i in slots) i.Source = bmp;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Log.Error("Team logo failed to load.", ex); }
        }
    }
}
