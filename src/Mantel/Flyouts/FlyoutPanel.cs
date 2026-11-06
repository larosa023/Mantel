using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Flyouts;

/// <summary>
/// The inside of a flyout: one panel with a header (the flyout's icon, name and today's date,
/// plus a shortcut to Settings) and each widget in its own soft section below.
/// Used by <see cref="FlyoutWindow"/> and by the screenshot renderer.
/// </summary>
public sealed class FlyoutPanel : UserControl
{
    private const double SectionPadding = 16;
    public const double PanelWidth = WidgetCard.ContentWidth + SectionPadding * 2 + 28;

    private readonly FlyoutSettings _flyout;
    private readonly IReadOnlyList<WidgetInstanceSettings> _instances;
    private readonly Func<WidgetInstanceSettings, ContextMenu> _menuFor;
    private readonly Action? _openSettings;
    private readonly List<IWidget> _widgets = new();
    private readonly StackPanel _sections = new() { Spacing = 10 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _subtitle = new();
    private readonly Border _surface;
    private bool _built;

    public FlyoutPanel(FlyoutSettings flyout, IReadOnlyList<WidgetInstanceSettings> instances,
                       Func<WidgetInstanceSettings, ContextMenu> menuFor, Action? openSettings = null)
    {
        _flyout = flyout;
        _instances = instances;
        _menuFor = menuFor;
        _openSettings = openSettings;

        _scroll = new ScrollViewer
        {
            Content = _sections,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        var body = new DockPanel { Margin = new Thickness(14), LastChildFill = true };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(_scroll);

        _surface = new Border { Child = body };
        _surface.Classes.Add("flyout");
        Content = _surface;
        RefreshHeader();
    }

    public IReadOnlyList<IWidget> Widgets => _widgets;

    /// <summary>Lay sections side by side (top-edge flyouts) instead of stacked.</summary>
    public bool Horizontal
    {
        set
        {
            _sections.Orientation = value ? Orientation.Horizontal : Orientation.Vertical;
            _scroll.HorizontalScrollBarVisibility = value
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            _scroll.VerticalScrollBarVisibility = value
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            _surface.Width = value ? double.NaN : PanelWidth;
        }
    }

    /// <summary>Pins the panel to an exact height (edge flyouts fill the screen top to bottom).</summary>
    public void SetHeight(double? height)
    {
        if (height is { } h) _surface.Height = h;
        else _surface.Height = double.NaN;
    }

    public void SetMaxHeight(double height) => _surface.MaxHeight = height;

    /// <summary>Creates the widgets the first time; later calls do nothing.</summary>
    public void BuildWidgets()
    {
        if (_built) return;
        _built = true;
        _surface.Width = PanelWidth;

        foreach (var s in _instances)
        {
            var d = WidgetRegistry.Find(s.Type);
            if (d is null) continue;
            try
            {
                var widget = d.Create(s);
                _widgets.Add(widget);
                _sections.Children.Add(Section(widget.View, _menuFor(s)));
            }
            catch (Exception ex) { Log.Error($"Flyout widget '{s.Type}' failed to start.", ex); }
        }

        if (_widgets.Count == 0) _sections.Children.Add(EmptyState());
    }

    public void RefreshHeader() =>
        _subtitle.Text = DateTime.Now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

    private Control BuildHeader()
    {
        var tile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Child = Icons.Create(Icons.ForFlyout(_flyout.Icon), 20, accent: true),
            Margin = new Thickness(0, 0, 12, 0),
        };
        tile.Classes.Add("flyout-section");
        tile.Padding = new Thickness(0);

        var name = new TextBlock { Text = _flyout.Name, FontSize = 16, FontWeight = FontWeight.SemiBold };
        _subtitle.Classes.Add("muted");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, _subtitle } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(2, 2, 0, 14) };
        grid.Children.Add(tile);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (_openSettings is not null)
        {
            var gear = new Button { Content = Icons.Create(Icons.Gear, 18), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
            gear.Classes.Add("icon");
            ToolTip.SetTip(gear, "Mantel settings");
            gear.Click += (_, _) => _openSettings();
            Grid.SetColumn(gear, 2);
            grid.Children.Add(gear);
        }
        return grid;
    }

    private static Control Section(Control view, ContextMenu menu)
    {
        var section = new Border { Child = view, ContextMenu = menu, IsVisible = view.IsVisible, Padding = new Thickness(SectionPadding, 14) };
        section.Classes.Add("flyout-section");
        // Widgets with nothing to show (Now Playing when idle) hide their section too.
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) section.IsVisible = view.IsVisible;
        };
        return section;
    }

    private Control EmptyState()
    {
        var title = new TextBlock { Text = "Nothing here yet", FontSize = 14, FontWeight = FontWeight.SemiBold };
        var hint = new TextBlock
        {
            Text = "Add widgets to this flyout in Mantel settings.",
            TextWrapping = TextWrapping.Wrap,
            Width = WidgetCard.ContentWidth,
        };
        hint.Classes.Add("muted");
        var stack = new StackPanel { Spacing = 6, Children = { title, hint } };
        if (_openSettings is not null)
        {
            var add = new Button { Content = "Open settings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            add.Classes.Add("chip");
            add.Classes.Add("on");
            add.Click += (_, _) => _openSettings();
            stack.Children.Add(add);
        }
        var section = new Border { Child = stack, Padding = new Thickness(SectionPadding, 14) };
        section.Classes.Add("flyout-section");
        return section;
    }
}
