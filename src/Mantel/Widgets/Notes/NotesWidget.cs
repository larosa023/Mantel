using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Notes;

/// <summary>
/// A sticky note right on the desktop. Click and type; it saves itself a moment after you stop.
/// Options: "title", "text" (the note itself), "size" ("small", "medium", "large").
/// </summary>
public sealed class NotesWidget : IWidget
{
    private readonly WidgetInstanceSettings _settings;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private TextBox? _box;
    private Control? _view;

    public NotesWidget(WidgetInstanceSettings settings)
    {
        _settings = settings;
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            SettingsStore.Current?.Save();
        };
    }

    private string Title => _settings.Options.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t) ? t : "Notes";

    private double Height => (_settings.Options.TryGetValue("size", out var s) ? s : "medium") switch
    {
        "small" => 90,
        "large" => 280,
        _ => 170,
    };

    public Control View => _view ??= BuildView();

    private Control BuildView()
    {
        var title = new TextBlock { Text = Title.ToUpperInvariant(), Margin = new Thickness(0, 0, 0, 6) };
        title.Classes.Add("label");

        _box = new TextBox
        {
            Text = _settings.Options.TryGetValue("text", out var text) ? text : "",
            Watermark = "Click to write a note…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = Height,
            Width = WidgetCard.ContentWidth,
            Padding = new Thickness(0),
            FontSize = 14,
        };
        _box.Classes.Add("note");
        _box.TextChanged += (_, _) =>
        {
            _settings.Options["text"] = _box.Text ?? "";
            _save.Stop();
            _save.Start();
        };

        return new StackPanel { Children = { title, _box } };
    }

    public void Start() => _ = View;

    public void Stop()
    {
        // Don't lose the last few keystrokes when the widget closes.
        if (_save.IsEnabled) { _save.Stop(); SettingsStore.Current?.Save(); }
    }

    public void Dispose() => Stop();

    public static Control BuildOptions(WidgetOptionsContext ctx) =>
        OptionsUi.Panel(
            OptionsUi.Text(ctx, "title", "Title", "Notes"),
            OptionsUi.Choice(ctx, "size", "Size", new[] { ("small", "Small"), ("medium", "Medium"), ("large", "Large") }, "medium"));

    public static string? Subtitle(WidgetInstanceSettings s)
    {
        var text = s.Options.TryGetValue("text", out var t) ? t.Trim() : "";
        if (text.Length == 0) return "Empty";
        var line = text.Split('\n')[0].Trim();
        return line.Length > 40 ? line[..40] + "…" : line;
    }
}
