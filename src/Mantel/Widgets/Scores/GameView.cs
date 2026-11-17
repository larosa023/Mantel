using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Scores;

/// <summary>
/// One game, live, inside the Scores widget: the score, win probability, line score by period,
/// and tabs for each team's box score, team stats side by side, and the latest plays. Data comes
/// from Fullcourt's /api/game and refreshes every 15 seconds while the game is on.
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
    private readonly Action<string> _openFullcourt;

    private readonly StackPanel _root = new() { Width = Width };
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _cts;
    private Game? _game;
    private string _tab = "box";
    private string _boxSide = "";

    public GameView(HttpClient http, Dictionary<string, Bitmap> logos, string site, string league, string id,
                    Action back, Action<string> openFullcourt)
    {
        _http = http;
        _logos = logos;
        _site = site;
        _league = league;
        _id = id;
        _back = back;
        _openFullcourt = openFullcourt;

        var backButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { Icons.Create(Icons.ChevronLeft, 14), new TextBlock { Text = "All games", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center } },
            },
            Padding = new Thickness(4, 3, 8, 3),
        };
        backButton.Classes.Add("row");
        backButton.Margin = new Thickness(-4, 0, 0, 0);
        backButton.HorizontalAlignment = HorizontalAlignment.Left;
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
        };
        open.Classes.Add("row");
        open.Margin = new Thickness(0, 0, -4, 0);
        open.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTip.SetTip(open, "Open this game in Fullcourt");
        open.Click += (_, _) => _openFullcourt(_game?.Url is { Length: > 0 } u ? u : $"{_site}/?league={_league}&game={_id}");

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
    private sealed record Stat(string Label, string Kind, string Away, string Home);
    private sealed record Player(string Short, string Name, string Pos, bool Starter, int Min, int Pts, int Reb, int Ast, int Stl, int Blk, int To, string Fg, string Three, string Ft, string Pm);
    private sealed record Play(int Period, string Clock, string Text, string? Team, bool Scoring, int? Away, int? Home);
    private sealed record Game(string State, string Status, string Tv, Team Away, Team Home, List<Stat> Stats,
                               List<Player> AwayPlayers, List<Player> HomePlayers, List<Play> Plays, double? HomeWinPct, int Refresh, string Url);

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(30);
            try
            {
                var json = await _http.GetStringAsync($"{_site}/api/game?league={_league}&id={_id}", ct);
                var game = Parse(json);
                if (game is null) throw new InvalidDataException("No game in the response.");
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

    private static Game? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;

        static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static int? I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        static bool B(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

        Team T(JsonElement t) => new(S(t, "abbr"), S(t, "name"), S(t, "logo") is { Length: > 0 } l ? l : null,
            S(t, "color") is { Length: > 0 } c ? c : null, I(t, "score"), S(t, "record"),
            t.TryGetProperty("linescores", out var ls) ? ls.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0).ToArray() : Array.Empty<int>(),
            B(t, "winner"));

        List<Player> Players(JsonElement arr) => arr.EnumerateArray().Select(p => new Player(
            S(p, "short"), S(p, "name"), S(p, "pos"), B(p, "starter"), I(p, "min") ?? 0, I(p, "pts") ?? 0, I(p, "reb") ?? 0,
            I(p, "ast") ?? 0, I(p, "stl") ?? 0, I(p, "blk") ?? 0, I(p, "to") ?? 0, S(p, "fg"), S(p, "three"), S(p, "ft"), S(p, "pm"))).ToList();

        var players = r.GetProperty("players");
        return new Game(
            S(r, "state"), S(r, "status"), S(r, "tv"),
            T(r.GetProperty("away")), T(r.GetProperty("home")),
            r.GetProperty("stats").EnumerateArray().Select(s => new Stat(S(s, "label"), S(s, "kind"), S(s, "away"), S(s, "home"))).ToList(),
            Players(players.GetProperty("away")), Players(players.GetProperty("home")),
            r.GetProperty("plays").EnumerateArray().Select(p => new Play(I(p, "period") ?? 0, S(p, "clock"), S(p, "text"),
                S(p, "team") is { Length: > 0 } tm ? tm : null, B(p, "scoring"), I(p, "away"), I(p, "home"))).ToList(),
            r.TryGetProperty("homeWinPct", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetDouble() : null,
            I(r, "refresh") ?? 0, S(r, "url"));
    }

    // --- Display ------------------------------------------------------------------------------------

    private readonly Dictionary<string, List<Image>> _logoSlots = new();

    private void Render()
    {
        if (_game is not { } g) return;
        _status.IsVisible = false;
        _body.Children.Clear();
        _logoSlots.Clear();
        if (_boxSide.Length == 0) _boxSide = g.Away.Abbr;

        _body.Children.Add(Scoreboard(g));
        if (g.State == "in" && g.HomeWinPct is { } wp) _body.Children.Add(WinBar(g, wp));
        if (g.Away.Lines.Length > 0) _body.Children.Add(LineScore(g));

        // Before tip-off there's no box score, stats or plays yet.
        if (g.State == "pre")
        {
            var note = new TextBlock { Text = "Box score, stats and plays show up once the game starts.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            note.Classes.Add("muted");
            _body.Children.Add(note);
            return;
        }

        _body.Children.Add(Tabs());
        _body.Children.Add(_tab switch
        {
            "team" => TeamStats(g),
            "plays" => Plays(g),
            _ => BoxScore(g),
        });
    }

    private Control Scoreboard(Game g)
    {
        var live = g.State == "in";
        var final = g.State == "post";

        Control Side(Team t, bool right)
        {
            var logo = Logo(t.Logo, 30);
            var abbr = new TextBlock { Text = t.Abbr, FontWeight = FontWeight.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            var record = new TextBlock { Text = t.Record, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
            record.Classes.Add("label");
            var id = new StackPanel { Spacing = 2, Children = { logo, abbr, record }, VerticalAlignment = VerticalAlignment.Center };
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

        var status = new TextBlock
        {
            Text = g.Status.Replace(" - ", "\n"),
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            FontWeight = live ? FontWeight.SemiBold : FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        status.Classes.Add(live ? "accent" : "muted");
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

    /// <summary>Who's likely to win, as one bar in the two teams' colors.</summary>
    private static Control WinBar(Game g, double homePct)
    {
        IBrush Color(string? hex, string fallback) => Brush.Parse(hex is { Length: 7 } ? hex : fallback);
        var awayPct = 100 - homePct;
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Math.Max(awayPct, 1):0.#}*,{Math.Max(homePct, 1):0.#}*"),
            Height = 5,
            ClipToBounds = true,
        };
        var a = new Border { Background = Color(g.Away.Color, "#8C8A86"), CornerRadius = new CornerRadius(3, 0, 0, 3) };
        var h = new Border { Background = Color(g.Home.Color, "#E8A15A"), CornerRadius = new CornerRadius(0, 3, 3, 0), Margin = new Thickness(2, 0, 0, 0) };
        bar.Children.Add(a);
        Grid.SetColumn(h, 1);
        bar.Children.Add(h);

        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
        var al = new TextBlock { Text = $"{g.Away.Abbr} {awayPct:0}%", FontSize = 11 };
        var mid = new TextBlock { Text = "WIN PROBABILITY", FontSize = 10 };
        var hl = new TextBlock { Text = $"{homePct:0}% {g.Home.Abbr}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        al.Classes.Add("muted");
        hl.Classes.Add("muted");
        mid.Classes.Add("label");
        labels.Children.Add(al);
        Grid.SetColumn(mid, 1);
        labels.Children.Add(mid);
        Grid.SetColumn(hl, 2);
        labels.Children.Add(hl);

        return new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { bar, labels } };
    }

    /// <summary>Points by period: 1 2 3 4 (OT…) and the total.</summary>
    private static Control LineScore(Game g)
    {
        var periods = Math.Max(4, Math.Max(g.Away.Lines.Length, g.Home.Lines.Length));
        var cols = "48," + string.Join(",", Enumerable.Repeat("*", periods)) + ",36";
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };

        void Cell(string text, int row, int col, string cls, bool bold = false, bool right = false)
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
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }

        for (var p = 0; p < periods; p++) Cell(p < 4 ? (p + 1).ToString() : p == 4 ? "OT" : $"{p - 3}OT", 0, p + 1, "label");
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
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (id, label) in new[] { ("box", "Box score"), ("team", "Team stats"), ("plays", "Plays") })
        {
            var b = new Button { Content = label, Padding = new Thickness(10, 4) };
            b.Classes.Add("chip");
            b.Classes.Set("on", _tab == id);
            var tab = id;
            b.Click += (_, _) => { _tab = tab; Render(); };
            row.Children.Add(b);
        }
        return row;
    }

    private Control BoxScore(Game g)
    {
        var stack = new StackPanel();

        // Which team: two chips with the abbreviations.
        var pick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var t in new[] { g.Away, g.Home })
        {
            var b = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Logo(t.Logo, 14), new TextBlock { Text = t.Abbr, VerticalAlignment = VerticalAlignment.Center } } },
                Padding = new Thickness(8, 3),
            };
            b.Classes.Add("chip");
            b.Classes.Set("on", _boxSide == t.Abbr);
            var abbr = t.Abbr;
            b.Click += (_, _) => { _boxSide = abbr; Render(); };
            pick.Children.Add(b);
        }
        stack.Children.Add(pick);

        var players = _boxSide == g.Home.Abbr ? g.HomePlayers : g.AwayPlayers;
        if (players.Count == 0)
        {
            var none = new TextBlock { Text = "No box score yet." };
            none.Classes.Add("muted");
            stack.Children.Add(none);
            return stack;
        }

        const string cols = "*,30,32,30,30";
        Grid Row(string name, string min, string pts, string reb, string ast, string cls, bool header = false)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Margin = new Thickness(0, header ? 0 : 1) };
            var values = new[] { name, min, pts, reb, ast };
            for (var i = 0; i < values.Length; i++)
            {
                var t = new TextBlock
                {
                    Text = values[i],
                    FontSize = header ? 10.5 : 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    FontWeight = !header && i == 2 ? FontWeight.SemiBold : FontWeight.Normal,
                };
                t.Classes.Add(header ? "label" : i == 0 || i == 2 ? "value" : cls);
                Grid.SetColumn(t, i);
                grid.Children.Add(t);
            }
            return grid;
        }

        stack.Children.Add(Row("STARTERS", "MIN", "PTS", "REB", "AST", "label", header: true));
        var benchShown = false;
        foreach (var p in players)
        {
            if (!p.Starter && !benchShown)
            {
                benchShown = true;
                var bench = Row("BENCH", "", "", "", "", "label", header: true);
                bench.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(bench);
            }
            var row = Row(p.Short + (p.Pos.Length > 0 ? $"  {p.Pos}" : ""), p.Min.ToString(), p.Pts.ToString(), p.Reb.ToString(), p.Ast.ToString(), "muted");
            ToolTip.SetTip(row, $"{p.Name}\nFG {p.Fg} · 3PT {p.Three} · FT {p.Ft}\nSTL {p.Stl} · BLK {p.Blk} · TO {p.To}" + (p.Pm.Length > 0 ? $" · +/- {p.Pm}" : ""));
            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>Team stats side by side; the better number in each row is brighter.</summary>
    private static Control TeamStats(Game g)
    {
        var stack = new StackPanel { Spacing = 3 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 0, 0, 4) };
        var a = new TextBlock { Text = g.Away.Abbr, FontWeight = FontWeight.SemiBold };
        var h = new TextBlock { Text = g.Home.Abbr, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        head.Children.Add(a);
        Grid.SetColumn(h, 2);
        head.Children.Add(h);
        stack.Children.Add(head);

        foreach (var s in g.Stats)
        {
            var lowerIsBetter = s.Label == "Turnovers";
            var av = Number(s.Away);
            var hv = Number(s.Home);
            var awayBetter = av is not null && hv is not null && (lowerIsBetter ? av < hv : av > hv);
            var homeBetter = av is not null && hv is not null && (lowerIsBetter ? hv < av : hv > av);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            var at = new TextBlock { Text = s.Away, FontSize = 13 };
            var label = new TextBlock { Text = s.Label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
            var ht = new TextBlock { Text = s.Home, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
            label.Classes.Add("muted");
            at.Classes.Add(awayBetter ? "value" : "muted");
            ht.Classes.Add(homeBetter ? "value" : "muted");
            if (awayBetter) at.FontWeight = FontWeight.SemiBold;
            if (homeBetter) ht.FontWeight = FontWeight.SemiBold;
            grid.Children.Add(at);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            Grid.SetColumn(ht, 2);
            grid.Children.Add(ht);
            stack.Children.Add(grid);
        }
        if (g.Stats.Count == 0)
        {
            var none = new TextBlock { Text = "No team stats yet." };
            none.Classes.Add("muted");
            stack.Children.Add(none);
        }
        return stack;
    }

    private static double? Number(string s)
    {
        var first = s.Split('-')[0];
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Latest plays, newest first; scoring plays get the accent and the new score.</summary>
    private static Control Plays(Game g)
    {
        var stack = new StackPanel { Spacing = 7 };
        foreach (var p in g.Plays.Take(12))
        {
            var when = new TextBlock
            {
                Text = $"{(p.Period <= 4 ? $"Q{p.Period}" : p.Period == 5 ? "OT" : $"{p.Period - 4}OT")} {p.Clock}",
                FontSize = 10.5,
                Width = 62,
            };
            when.Classes.Add("label");
            var text = new TextBlock { Text = p.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            text.Classes.Add(p.Scoring ? "value" : "muted");
            var content = new StackPanel { Spacing = 1, Children = { text } };
            if (p.Scoring && p.Away is not null && p.Home is not null)
            {
                var score = new TextBlock { Text = $"{g.Away.Abbr} {p.Away} – {p.Home} {g.Home.Abbr}", FontSize = 11, FontWeight = FontWeight.SemiBold };
                score.Classes.Add("accent");
                content.Children.Add(score);
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(when);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            stack.Children.Add(row);
        }
        if (g.Plays.Count == 0)
        {
            var none = new TextBlock { Text = "No plays yet." };
            none.Classes.Add("muted");
            stack.Children.Add(none);
        }
        return stack;
    }

    private Image Logo(string? url, double size)
    {
        var img = new Image { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
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
