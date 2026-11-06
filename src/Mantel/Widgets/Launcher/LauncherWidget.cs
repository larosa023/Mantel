using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Launcher;

/// <summary>
/// A row of your favorite apps, folders or files, one click away. Each gets a lettered tile in
/// the theme's colors. Options: "apps" (one "Name|path" per line), "title".
/// </summary>
public sealed class LauncherWidget : IWidget
{
    private const int PerRow = 4;

    private readonly WidgetInstanceSettings _settings;
    private Control? _view;

    public LauncherWidget(WidgetInstanceSettings settings) => _settings = settings;

    public Control View => _view ??= BuildView();

    public sealed record App(string Name, string Path);

    public static List<App> ReadApps(WidgetInstanceSettings s) =>
        (s.Options.TryGetValue("apps", out var v) ? v : "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', 2))
            .Where(p => p.Length == 2 && p[1].Length > 0)
            .Select(p => new App(p[0], p[1]))
            .ToList();

    public static void WriteApps(Dictionary<string, string> options, IEnumerable<App> apps) =>
        options["apps"] = string.Join("\n", apps.Select(a => $"{a.Name.Replace("|", " ")}|{a.Path}"));

    private Control BuildView()
    {
        var apps = ReadApps(_settings);
        var root = new StackPanel { Width = WidgetCard.ContentWidth };

        if (_settings.Options.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            var label = new TextBlock { Text = title.ToUpperInvariant(), Margin = new Thickness(0, 0, 0, 10) };
            label.Classes.Add("label");
            root.Children.Add(label);
        }

        if (apps.Count == 0)
        {
            var hint = new TextBlock { Text = "Add apps, folders or files in Mantel settings.", TextWrapping = TextWrapping.Wrap };
            hint.Classes.Add("muted");
            root.Children.Add(hint);
            return root;
        }

        var grid = new UniformGrid { Columns = PerRow };
        foreach (var app in apps) grid.Children.Add(Tile(app));
        root.Children.Add(grid);
        return root;
    }

    private static Control Tile(App app)
    {
        var letter = new TextBlock
        {
            Text = Monogram(app.Name),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            Width = 46, Height = 46,
            CornerRadius = new CornerRadius(13),
            Child = letter,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        badge.Classes.Add("launcher-tile");

        var name = new TextBlock
        {
            Text = app.Name,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 68,
            Margin = new Thickness(0, 6, 0, 0),
        };
        name.Classes.Add("muted");

        var button = new Button
        {
            Content = new StackPanel { Children = { badge, name } },
            Padding = new Thickness(2, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        button.Classes.Add("row");
        ToolTip.SetTip(button, app.Path);
        button.Click += (_, _) => Launch(app);
        return button;
    }

    private static string Monogram(string name)
    {
        var words = name.Split(' ', '-', '_').Where(w => w.Length > 0).ToList();
        if (words.Count >= 2 && char.IsLetterOrDigit(words[0][0]) && char.IsLetterOrDigit(words[1][0]))
            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        return name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
    }

    private static void Launch(App app)
    {
        try { Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"Couldn't open {app.Path}.", ex); }
    }

    public void Start() => _ = View;
    public void Stop() { }
    public void Dispose() { }

    // --- Settings panel -----------------------------------------------------------------------

    public static Control BuildOptions(WidgetOptionsContext ctx)
    {
        var list = new StackPanel { Spacing = 6 };

        void Refresh()
        {
            list.Children.Clear();
            var apps = ReadApps(ctx.Instance);
            if (apps.Count == 0)
            {
                var none = new TextBlock { Text = "No apps yet." };
                none.Classes.Add("muted");
                list.Children.Add(none);
            }
            for (var i = 0; i < apps.Count; i++)
            {
                var index = i;
                var app = apps[i];
                var name = new TextBox { Text = app.Name, Width = 160 };
                name.LostFocus += (_, _) =>
                {
                    var current = ReadApps(ctx.Instance);
                    if (index >= current.Count || string.IsNullOrWhiteSpace(name.Text)) return;
                    current[index] = current[index] with { Name = name.Text.Trim() };
                    WriteApps(ctx.Options, current);
                    ctx.Set("apps", ctx.Options["apps"], immediate: true);
                };
                var path = new TextBlock { Text = app.Path, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
                path.Classes.Add("muted");
                var remove = new Button { Content = Icons.Create(Icons.Trash, 15), Padding = new Thickness(6) };
                remove.Classes.Add("ghost");
                ToolTip.SetTip(remove, "Remove");
                remove.Click += (_, _) =>
                {
                    var current = ReadApps(ctx.Instance);
                    if (index < current.Count) current.RemoveAt(index);
                    WriteApps(ctx.Options, current);
                    ctx.Set("apps", ctx.Options["apps"], immediate: true);
                    Refresh();
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                row.Children.Add(name);
                Grid.SetColumn(path, 1);
                row.Children.Add(path);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
        }

        var add = new Button { Content = "Add app or file…" };
        add.Classes.Add("primary");
        add.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(add);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add to launcher",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Apps and shortcuts") { Patterns = new[] { "*.exe", "*.lnk", "*.url", "*.bat", "*.cmd" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 0) return;
            var current = ReadApps(ctx.Instance);
            foreach (var f in files)
            {
                var path = f.TryGetLocalPath();
                if (path is null) continue;
                current.Add(new App(System.IO.Path.GetFileNameWithoutExtension(path), path));
            }
            WriteApps(ctx.Options, current);
            ctx.Set("apps", ctx.Options["apps"], immediate: true);
            Refresh();
        };

        var addFolder = new Button { Content = "Add folder…" };
        addFolder.Classes.Add("ghost");
        addFolder.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(addFolder);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add folder to launcher" });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path is null) return;
            var current = ReadApps(ctx.Instance);
            current.Add(new App(new DirectoryInfo(path).Name, path));
            WriteApps(ctx.Options, current);
            ctx.Set("apps", ctx.Options["apps"], immediate: true);
            Refresh();
        };

        Refresh();
        return OptionsUi.Panel(
            OptionsUi.Text(ctx, "title", "Title", "Optional, e.g. Work apps"),
            OptionsUi.Labeled("Apps", list),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, addFolder } });
    }

    public static string? Subtitle(WidgetInstanceSettings s)
    {
        var n = ReadApps(s).Count;
        return n == 0 ? "No apps yet" : $"{n} item{(n == 1 ? "" : "s")}";
    }
}
