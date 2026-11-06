using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// What a widget's settings panel gets: the instance's options and a way to say "I changed
/// something". Changes are debounced, then the widget is rebuilt with the new options.
/// </summary>
public sealed class WidgetOptionsContext
{
    private readonly Action _commit;
    private readonly DispatcherTimer _debounce;

    public WidgetOptionsContext(WidgetInstanceSettings instance, Action commit)
    {
        Instance = instance;
        _commit = commit;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _commit(); };
    }

    public WidgetInstanceSettings Instance { get; }

    public Dictionary<string, string> Options => Instance.Options;

    public string Get(string key, string fallback = "") =>
        Options.TryGetValue(key, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        Options.TryGetValue(key, out var v) ? v == "true" : fallback;

    /// <summary>Sets a value and schedules a commit. Null or empty removes the key.</summary>
    public void Set(string key, string? value, bool immediate = false)
    {
        if (string.IsNullOrEmpty(value)) Options.Remove(key);
        else Options[key] = value;

        if (immediate) { _debounce.Stop(); _commit(); }
        else { _debounce.Stop(); _debounce.Start(); }
    }

    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false", immediate: true);
}

/// <summary>Small builders so every widget's settings panel looks the same.</summary>
public static class OptionsUi
{
    public static StackPanel Panel(params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var r in rows) panel.Children.Add(r);
        return panel;
    }

    public static Control Labeled(string label, Control control, string? hint = null)
    {
        var text = new TextBlock { Text = label, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
        var stack = new StackPanel { Children = { text, control } };
        if (hint is not null)
        {
            var h = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            h.Classes.Add("muted");
            stack.Children.Add(h);
        }
        return stack;
    }

    public static Control Text(WidgetOptionsContext ctx, string key, string label, string? placeholder = null, string? hint = null)
    {
        var box = new TextBox { Text = ctx.Get(key), Watermark = placeholder };
        box.TextChanged += (_, _) => ctx.Set(key, box.Text?.Trim());
        return Labeled(label, box, hint);
    }

    public static Control Toggle(WidgetOptionsContext ctx, string key, string label, bool fallback = false)
    {
        var toggle = new ToggleSwitch { IsChecked = ctx.GetBool(key, fallback), OnContent = null, OffContent = null };
        toggle.IsCheckedChanged += (_, _) => ctx.SetBool(key, toggle.IsChecked == true);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    public static Control Choice(WidgetOptionsContext ctx, string key, string label,
        IReadOnlyList<(string Value, string Display)> choices, string fallback)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 };
        foreach (var c in choices) combo.Items.Add(new ComboBoxItem { Content = c.Display, Tag = c.Value });
        var current = ctx.Get(key, fallback);
        combo.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(c => c.Value == current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string v }) ctx.Set(key, v, immediate: true);
        };
        return Labeled(label, combo);
    }
}
