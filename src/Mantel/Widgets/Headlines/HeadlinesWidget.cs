using System.Globalization;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Headlines;

/// <summary>
/// The latest headlines from any RSS or Atom feed. Click one to read it. Refreshes every 15 minutes.
/// Options: "feed" (a preset id or a feed URL), "count".
/// </summary>
public sealed class HeadlinesWidget : IWidget
{
    public static readonly (string Id, string Name, string Url)[] Presets =
    {
        ("yahoo-nba", "Yahoo Sports NBA", "https://sports.yahoo.com/nba/rss/"),
        ("cbs-nba", "CBS Sports NBA", "https://www.cbssports.com/rss/headlines/nba/"),
        ("espn-nba", "ESPN NBA", "https://www.espn.com/espn/rss/nba/news"),
        ("espn", "ESPN top stories", "https://www.espn.com/espn/rss/news"),
        ("verge", "The Verge", "https://www.theverge.com/rss/index.xml"),
        ("bbc", "BBC News", "https://feeds.bbci.co.uk/news/rss.xml"),
        ("npr", "NPR News", "https://feeds.npr.org/1001/rss.xml"),
        ("hn", "Hacker News", "https://hnrss.org/frontpage"),
        ("enr", "ENR (construction)", "https://www.enr.com/rss/articles"),
    };

    private static readonly HttpClient Http = CreateClient();

    private readonly WidgetInstanceSettings _settings;
    private readonly TextBlock _source = new();
    private readonly StackPanel _items = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private Control? _view;
    private CancellationTokenSource? _cts;

    public HeadlinesWidget(WidgetInstanceSettings settings) => _settings = settings;

    private static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        // Some publishers (ESPN) send an empty page to anything that doesn't look like a browser.
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
        return http;
    }

    private string FeedSetting => _settings.Options.TryGetValue("feed", out var f) && !string.IsNullOrWhiteSpace(f) ? f.Trim() : "yahoo-nba";
    private int Count => int.TryParse(_settings.Options.TryGetValue("count", out var c) ? c : "5", out var n) ? Math.Clamp(n, 1, 12) : 5;

    private (string Name, string Url) Feed
    {
        get
        {
            var preset = Presets.FirstOrDefault(p => p.Id == FeedSetting);
            if (preset.Url is not null) return (preset.Name, preset.Url);
            var name = Uri.TryCreate(FeedSetting, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : "Headlines";
            return (name, FeedSetting);
        }
    }

    public Control View => _view ??= BuildView();

    private Control BuildView()
    {
        _source.Classes.Add("label");
        _source.Margin = new Thickness(0, 0, 0, 8);
        _source.Text = Feed.Name.ToUpperInvariant();
        _status.Classes.Add("muted");
        _status.Text = "Loading headlines…";
        return new StackPanel { Width = WidgetCard.ContentWidth, Children = { _source, _items, _status } };
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

    private sealed record Item(string Title, string Link, DateTimeOffset? Published);

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var wait = TimeSpan.FromMinutes(15);
            try
            {
                var xml = await Http.GetStringAsync(Feed.Url, ct);
                var items = Parse(xml).Take(Count).ToList();
                Dispatcher.UIThread.Post(() => Render(items));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Error($"Headlines from {Feed.Url} failed.", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_items.Children.Count > 0) return;
                    _status.Text = "Couldn't load this feed. Check the address in Settings.";
                    _status.IsVisible = true;
                });
                wait = TimeSpan.FromMinutes(3);
            }
            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>RSS 2.0 (&lt;item&gt;) and Atom (&lt;entry&gt;), whichever the feed uses.</summary>
    private static IEnumerable<Item> Parse(string xml)
    {
        xml = xml.Trim().TrimStart('\uFEFF');
        if (xml.Length == 0) throw new InvalidDataException("The feed came back empty.");
        var doc = XDocument.Parse(xml);
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string? Child(string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value?.Trim();
            var title = System.Net.WebUtility.HtmlDecode(Child("title") ?? "");
            var link = Child("link");
            if (string.IsNullOrEmpty(link))
                link = e.Elements().FirstOrDefault(c => c.Name.LocalName == "link")?.Attribute("href")?.Value;
            var date = Child("pubDate") ?? Child("published") ?? Child("updated");
            DateTimeOffset? published = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null;
            if (title.Length > 0 && !string.IsNullOrEmpty(link)) yield return new Item(title, link!, published);
        }
    }

    private void Render(IReadOnlyList<Item> items)
    {
        _items.Children.Clear();
        foreach (var item in items)
        {
            var title = new TextBlock
            {
                Text = item.Title,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13.5,
            };
            title.Classes.Add("value");
            var stack = new StackPanel { Spacing = 2, Children = { title } };
            if (item.Published is { } p)
            {
                var when = new TextBlock { Text = Ago(p), FontSize = 11 };
                when.Classes.Add("label");
                stack.Children.Add(when);
            }
            var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Classes.Add("row");
            var link = item.Link;
            button.Click += (_, _) => AppInfo.OpenUrl(link);
            _items.Children.Add(button);
        }
        _status.IsVisible = items.Count == 0;
        _status.Text = "No headlines right now.";
    }

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 1) return "JUST NOW";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} MIN AGO";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} HR AGO";
        return when.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture).ToUpperInvariant();
    }

    public static Control BuildOptions(WidgetOptionsContext ctx)
    {
        var choices = Presets.Select(p => (p.Id, p.Name)).ToList();
        choices.Add(("custom", "Custom feed address…"));
        var current = ctx.Get("feed", "yahoo-nba");
        var isCustom = !Presets.Any(p => p.Id == current);

        var custom = new TextBox { Text = isCustom ? current : "", Watermark = "https://example.com/feed.xml", IsVisible = isCustom };
        custom.TextChanged += (_, _) =>
        {
            var v = custom.Text?.Trim();
            if (!string.IsNullOrEmpty(v) && Uri.TryCreate(v, UriKind.Absolute, out _)) ctx.Set("feed", v);
        };

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var c in choices) combo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        combo.SelectedIndex = isCustom ? choices.Count - 1 : Math.Max(0, choices.FindIndex(c => c.Id == current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            custom.IsVisible = id == "custom";
            if (id != "custom") ctx.Set("feed", id, immediate: true);
        };

        return OptionsUi.Panel(
            OptionsUi.Labeled("Feed", new StackPanel { Spacing = 6, Children = { combo, custom } }, "Any RSS or Atom feed works."),
            OptionsUi.Choice(ctx, "count", "Headlines", new[] { ("3", "3"), ("5", "5"), ("8", "8"), ("12", "12") }, "5"));
    }

    public static string? Subtitle(WidgetInstanceSettings s)
    {
        var feed = s.Options.TryGetValue("feed", out var f) ? f : "yahoo-nba";
        return Presets.FirstOrDefault(p => p.Id == feed).Name ?? (Uri.TryCreate(feed, UriKind.Absolute, out var u) ? u.Host : "Headlines");
    }
}
