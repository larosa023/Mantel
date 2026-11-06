using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mantel.Core;
using Mantel.Theme;
using Mantel.Updates;

namespace Mantel.UI;

/// <summary>Appearance, desktop widgets, flyouts, layout, startup and updates.</summary>
public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly WidgetManager _widgets;
    private readonly UpdateService _updates;
    private readonly Action _appearanceChanged;

    private readonly StackPanel _themeSection = new();
    private readonly StackPanel _widgetList = new() { Spacing = 2 };
    private readonly StackPanel _flyoutList = new() { Spacing = 2 };
    private readonly TextBlock _updateStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _updateButton = new();
    private readonly ToggleSwitch _lockToggle = new();
    private readonly Dictionary<string, ToggleSwitch> _widgetToggles = new();
    private bool _syncing;

    private static readonly string[] AccentPresets =
        { "#E8A15A", "#E8665A", "#FF3CAC", "#B18CFF", "#6FA8E8", "#88C0D0", "#5AD19A", "#F2C94C" };

    public SettingsWindow(SettingsStore store, WidgetManager widgets, UpdateService updates, Action appearanceChanged)
    {
        _store = store;
        _widgets = widgets;
        _updates = updates;
        _appearanceChanged = appearanceChanged;

        Title = "Mantel settings";
        Width = 540;
        Height = 800;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#141519"));
        Classes.Add("settings");
        Icon = TrayHost.LoadIcon("mantel");

        var snap = new ToggleSwitch { IsChecked = _widgets.SnapToGrid };
        snap.IsCheckedChanged += (_, _) => _widgets.SetSnapToGrid(snap.IsChecked == true);

        var fullscreen = new ToggleSwitch { IsChecked = _widgets.HideOnFullscreen };
        fullscreen.IsCheckedChanged += (_, _) => _widgets.SetHideOnFullscreen(fullscreen.IsChecked == true);

        var addWidget = AddWidgetButton("+ Add a widget to the desktop", flyoutId: null);
        var addFlyout = new Button { Content = "+ New flyout" };
        addFlyout.Classes.Add("link");
        addFlyout.Click += (_, _) => _widgets.AddFlyout();

        var flyoutHint = Muted("Panels of widgets that slide in from a screen edge or open from their own taskbar icon.");

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 8, 24, 24),
                Children =
                {
                    Section("APPEARANCE"),
                    _themeSection,
                    Section("DESKTOP WIDGETS"),
                    _widgetList,
                    addWidget,
                    Section("FLYOUTS"),
                    flyoutHint,
                    _flyoutList,
                    addFlyout,
                    Section("LAYOUT"),
                    Row("Lock widget positions", "Turn off to drag widgets around", _lockToggle),
                    Row("Snap to grid and screen edges", null, snap),
                    Row("Hide during full-screen apps", "Games, videos and presentations. Widgets pause while hidden.", fullscreen),
                    Section("STARTUP"),
                    Row("Start with Windows", null, BuildStartupToggle()),
                    Section("UPDATES"),
                    BuildUpdates(),
                    Section("ABOUT"),
                    BuildAbout(),
                },
            },
        };

        _lockToggle.IsChecked = _widgets.Locked;
        _lockToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing) _widgets.SetLocked(_lockToggle.IsChecked == true);
        };

        BuildAppearance();
        BuildLists();

        _widgets.Changed += SyncFromModel;
        _widgets.StructureChanged += BuildLists;
        _updates.StateChanged += SyncUpdateStatus;
        Closed += (_, _) =>
        {
            _widgets.Changed -= SyncFromModel;
            _widgets.StructureChanged -= BuildLists;
            _updates.StateChanged -= SyncUpdateStatus;
        };

        SyncUpdateStatus();
    }

    // --- Appearance ---------------------------------------------------------------------------

    private void BuildAppearance()
    {
        _themeSection.Children.Clear();
        var settings = _store.Settings;

        var swatches = new WrapPanel();
        foreach (var t in ThemeManager.All)
        {
            var button = new Button { Content = ThemePreview(t) };
            button.Classes.Add("swatch");
            button.Classes.Set("selected", t.Id == settings.Theme);
            ToolTip.SetTip(button, t.Description);
            var id = t.Id;
            button.Click += (_, _) =>
            {
                settings.Theme = id;
                _store.Save();
                _appearanceChanged();
                BuildAppearance();
            };
            swatches.Children.Add(button);
        }
        _themeSection.Children.Add(swatches);

        // Accent: the theme's own, follow Amplite, or a custom color.
        var mode = settings.Accent switch { "" => 0, "amplite" => 1, _ => 2 };
        var accentChoice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = mode };
        accentChoice.Items.Add(new ComboBoxItem { Content = "Theme's own accent" });
        accentChoice.Items.Add(new ComboBoxItem { Content = "Match Amplite's accent" });
        accentChoice.Items.Add(new ComboBoxItem { Content = "Custom color" });

        var customRow = new WrapPanel { IsVisible = mode == 2, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var hex in AccentPresets)
        {
            var dot = new Button
            {
                Width = 26, Height = 26, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(settings.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase) ? 2 : 0),
                BorderBrush = Brushes.White,
                Margin = new Thickness(0, 0, 8, 8),
            };
            var h = hex;
            dot.Click += (_, _) => { SetAccent(h); BuildAppearance(); };
            customRow.Children.Add(dot);
        }
        var hexBox = new TextBox
        {
            Text = mode == 2 ? settings.Accent : "",
            Watermark = "#RRGGBB",
            Width = 110,
            Margin = new Thickness(0, 0, 0, 8),
        };
        hexBox.TextChanged += (_, _) =>
        {
            var v = hexBox.Text?.Trim() ?? "";
            if (!v.StartsWith('#')) v = "#" + v;
            if (ThemeManager.ParseAccent(v) is not null) SetAccent(v.ToUpperInvariant());
        };
        customRow.Children.Add(hexBox);

        accentChoice.SelectionChanged += (_, _) =>
        {
            switch (accentChoice.SelectedIndex)
            {
                case 0: SetAccent(""); customRow.IsVisible = false; break;
                case 1: SetAccent("amplite"); customRow.IsVisible = false; break;
                case 2:
                    customRow.IsVisible = true;
                    if (ThemeManager.ParseAccent(settings.Accent) is null) SetAccent(AccentPresets[0]);
                    break;
            }
        };

        _themeSection.Children.Add(new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Children = { new TextBlock { Text = "Accent color", FontSize = 13, Margin = new Thickness(0, 0, 0, 4) }, accentChoice, customRow },
        });
    }

    private void SetAccent(string value)
    {
        _store.Settings.Accent = value;
        _store.Save();
        _appearanceChanged();
    }

    /// <summary>A tiny card in the theme's colors, over a dark "wallpaper" so see-through themes read right.</summary>
    private static Control ThemePreview(ThemeDef t)
    {
        IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

        var time = new TextBlock
        {
            Text = "9:41",
            FontSize = t.Bare ? 26 : 20,
            FontWeight = t.BigWeight,
            Foreground = B(t.Text),
            FontFamily = t.FontFamily is { } f ? new FontFamily(f) : FontFamily.Default,
        };
        var bar = new Border
        {
            Height = 4, Width = 44, CornerRadius = new CornerRadius(2),
            Background = B(t.Accent), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var card = new Border
        {
            Background = B(t.CardBackground),
            BorderBrush = B(t.CardBorder),
            BorderThickness = new Thickness(t.CardBorderThickness),
            CornerRadius = new CornerRadius(Math.Min(t.CardRadius, 10)),
            Padding = new Thickness(10, 6),
            Child = new StackPanel { Children = { time, bar } },
        };
        var wallpaper = new Border
        {
            Width = 112, Height = 76,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(t.IsLight ? "#C9D6E3" : "#2B3A4F"), 0),
                    new GradientStop(Color.Parse(t.IsLight ? "#E9D8C4" : "#4A2F3D"), 1),
                },
            },
            Child = new Border { Child = card, Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Center },
        };
        var name = new TextBlock { Text = t.Name, FontSize = 12, Margin = new Thickness(2, 4, 0, 2) };
        return new StackPanel { Children = { wallpaper, name } };
    }

    // --- Widgets and flyouts ---------------------------------------------------------------------

    private void BuildLists()
    {
        _widgetToggles.Clear();

        _widgetList.Children.Clear();
        foreach (var d in WidgetRegistry.All)
            foreach (var instance in _widgets.Instances.Where(i => i.Type == d.Type && i.FlyoutId is null))
                _widgetList.Children.Add(WidgetEntry(d, instance));

        _flyoutList.Children.Clear();
        foreach (var f in _widgets.FlyoutList)
            _flyoutList.Children.Add(FlyoutEntry(f));
    }

    private Button AddWidgetButton(string label, string? flyoutId)
    {
        var button = new Button { Content = label };
        button.Classes.Add("link");
        button.Margin = new Thickness(0, 4, 0, 6);
        button.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            foreach (var d in WidgetRegistry.All)
            {
                var item = new MenuItem { Header = d.Name };
                ToolTip.SetTip(item, d.Description);
                var type = d.Type;
                item.Click += (_, _) => _widgets.AddInstance(type, flyoutId);
                menu.Items.Add(item);
            }
            menu.ShowAt(button);
        };
        return button;
    }

    private Control WidgetEntry(WidgetDescriptor d, WidgetInstanceSettings instance)
    {
        var id = instance.Id;
        var toggle = new ToggleSwitch { IsChecked = instance.Enabled, OnContent = null, OffContent = null };
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing) _widgets.SetEnabled(id, toggle.IsChecked == true);
        };
        _widgetToggles[id] = toggle;

        var subtitle = Muted(d.Subtitle?.Invoke(instance) ?? d.Description);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = d.Name, FontSize = 14 }, subtitle },
        });
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);

        var body = new StackPanel { Spacing = 10 };

        // Where it lives: the desktop or one of the flyouts.
        var places = new List<(string? Id, string Name)> { (null, "Desktop") };
        places.AddRange(_widgets.FlyoutList.Select(f => ((string?)f.Id, $"{f.Name} flyout")));
        var where = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var p in places) where.Items.Add(new ComboBoxItem { Content = p.Name });
        where.SelectedIndex = Math.Max(0, places.FindIndex(p => p.Id == instance.FlyoutId));
        where.SelectionChanged += (_, _) =>
        {
            if (where.SelectedIndex >= 0) _widgets.SetPlacement(id, places[where.SelectedIndex].Id);
        };
        body.Children.Add(OptionsUi.Labeled("Show on", where));

        if (d.BuildOptions is not null)
        {
            var ctx = new WidgetOptionsContext(instance, () =>
            {
                _widgets.Reload(id);
                subtitle.Text = d.Subtitle?.Invoke(instance) ?? d.Description;
            });
            try { body.Children.Add(d.BuildOptions(ctx)); }
            catch (Exception ex)
            {
                Log.Error($"Options panel for '{d.Type}' failed.", ex);
                body.Children.Add(new TextBlock { Text = "These options couldn't load." });
            }
        }

        var remove = new Button { Content = "Remove this widget" };
        remove.Classes.Add("link");
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.Click += (_, _) => _widgets.RemoveInstance(id);
        body.Children.Add(remove);

        return new Expander { Header = header, Content = body };
    }

    private Control FlyoutEntry(FlyoutSettings f)
    {
        var toggle = new ToggleSwitch { IsChecked = f.Enabled, OnContent = null, OffContent = null };
        toggle.IsCheckedChanged += (_, _) =>
        {
            f.Enabled = toggle.IsChecked == true;
            _widgets.FlyoutChanged(structural: true);
        };

        var count = _widgets.Instances.Count(w => w.FlyoutId == f.Id);
        var opens = new List<string>();
        if (f.TrayIcon) opens.Add("taskbar icon");
        if (f.Edge is "left" or "right" or "top") opens.Add($"{f.Edge} edge");
        var subtitle = Muted($"{count} widget{(count == 1 ? "" : "s")} · " +
                             (opens.Count > 0 ? "opens from " + string.Join(" or ", opens) : "no way to open it yet"));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = f.Name, FontSize = 14 }, subtitle },
        });
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);

        // Name
        var name = new TextBox { Text = f.Name };
        name.LostFocus += (_, _) =>
        {
            var v = name.Text?.Trim();
            if (string.IsNullOrEmpty(v) || v == f.Name) return;
            f.Name = v;
            _widgets.FlyoutChanged(structural: true);
        };

        // Triggers
        var tray = new ToggleSwitch { IsChecked = f.TrayIcon, OnContent = null, OffContent = null };
        tray.IsCheckedChanged += (_, _) => { f.TrayIcon = tray.IsChecked == true; _widgets.FlyoutChanged(structural: true); };

        var edges = new[] { ("none", "No screen edge"), ("right", "Right edge"), ("left", "Left edge"), ("top", "Top edge") };
        var edge = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, label) in edges) edge.Items.Add(new ComboBoxItem { Content = label });
        edge.SelectedIndex = Math.Max(0, Array.FindIndex(edges, e => e.Item1 == f.Edge));
        edge.SelectionChanged += (_, _) =>
        {
            if (edge.SelectedIndex < 0) return;
            f.Edge = edges[edge.SelectedIndex].Item1;
            _widgets.FlyoutChanged(structural: true);
        };

        var icons = new[] { ("mantel", "Mantel"), ("music", "Music note"), ("scores", "Basketball"), ("glance", "Glance") };
        var icon = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, label) in icons) icon.Items.Add(new ComboBoxItem { Content = label });
        icon.SelectedIndex = Math.Max(0, Array.FindIndex(icons, i => i.Item1 == f.Icon));
        icon.SelectionChanged += (_, _) =>
        {
            if (icon.SelectedIndex < 0) return;
            f.Icon = icons[icon.SelectedIndex].Item1;
            _widgets.FlyoutChanged();
        };

        // Its widgets
        var widgetList = new StackPanel { Spacing = 2 };
        foreach (var instance in _widgets.Instances.Where(w => w.FlyoutId == f.Id))
            if (WidgetRegistry.Find(instance.Type) is { } d) widgetList.Children.Add(WidgetEntry(d, instance));

        var tryIt = new Button { Content = "Open it now", IsEnabled = f.Enabled };
        tryIt.Click += (_, _) => _widgets.Flyouts.Open(f.Id);

        var delete = new Button { Content = "Delete this flyout" };
        delete.Classes.Add("link");
        delete.Click += (_, _) => _widgets.RemoveFlyout(f.Id);

        var body = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                OptionsUi.Labeled("Name", name),
                Row("Taskbar icon", "An icon by the clock that opens this flyout", tray),
                OptionsUi.Labeled("Screen edge", edge, "Push the mouse against this edge to slide it out."),
                OptionsUi.Labeled("Taskbar icon picture", icon),
                new TextBlock { Text = "Widgets in this flyout", FontSize = 13, Margin = new Thickness(0, 6, 0, 0) },
                widgetList,
                AddWidgetButton("+ Add a widget to this flyout", f.Id),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { tryIt, delete } },
            },
        };

        return new Expander { Header = header, Content = body };
    }

    // --- Startup, updates, about ----------------------------------------------------------------

    private static ToggleSwitch BuildStartupToggle()
    {
        var toggle = new ToggleSwitch { IsChecked = StartupRegistration.IsEnabled };
        toggle.IsCheckedChanged += (_, _) => StartupRegistration.Set(toggle.IsChecked == true);
        return toggle;
    }

    private Control BuildUpdates()
    {
        var version = new TextBlock { Text = $"Mantel {AppInfo.Version}", FontSize = 15 };
        _updateStatus.Classes.Add("muted");

        _updateButton.Click += async (_, _) =>
        {
            if (_updates.State == UpdateState.ReadyToRestart) _updates.RestartAndApply();
            else await _updates.CheckAndDownloadAsync();
        };

        var auto = new ToggleSwitch { IsChecked = _store.Settings.AutoUpdate };
        auto.IsCheckedChanged += (_, _) => { _store.Settings.AutoUpdate = auto.IsChecked == true; _store.Save(); };

        var beta = new ToggleSwitch { IsChecked = _store.Settings.BetaUpdates };
        beta.IsCheckedChanged += (_, _) => { _store.Settings.BetaUpdates = beta.IsChecked == true; _store.Save(); };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 8) };
        header.Children.Add(new StackPanel { Spacing = 2, Children = { version, _updateStatus } });
        _updateButton.VerticalAlignment = VerticalAlignment.Center;
        _updateButton.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(_updateButton, 1);
        header.Children.Add(_updateButton);

        return new StackPanel
        {
            Children =
            {
                header,
                Row("Update automatically", "Downloads in the background and applies when you're away", auto),
                Row("Beta updates", "Get new features early. Can be rougher around the edges.", beta),
            },
        };
    }

    private static Control BuildAbout()
    {
        var notes = new Button { Content = "Release notes" };
        notes.Classes.Add("link");
        notes.Click += (_, _) => AppInfo.OpenUrl($"{AppInfo.RepoUrl}/releases");

        var logs = new Button { Content = "Open log folder" };
        logs.Classes.Add("link");
        logs.Click += (_, _) => AppInfo.OpenUrl(SettingsStore.Folder);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(-4, 0, 0, 0),
            Children = { notes, logs },
        };
    }

    // --- Sync --------------------------------------------------------------------------------------

    private void SyncFromModel()
    {
        _syncing = true;
        try
        {
            _lockToggle.IsChecked = _widgets.Locked;
            foreach (var (id, toggle) in _widgetToggles) toggle.IsChecked = _widgets.IsEnabled(id);
        }
        finally { _syncing = false; }
    }

    private void SyncUpdateStatus()
    {
        _updateStatus.Text = _updates.State switch
        {
            UpdateState.NotInstalled => "Dev build: updates are off. Install from GitHub Releases to get them.",
            UpdateState.Checking => "Checking for updates…",
            UpdateState.Downloading => "Downloading update…",
            UpdateState.ReadyToRestart => $"Version {_updates.PendingVersion} is ready. It installs when you're away, or restart now.",
            UpdateState.UpToDate => $"Up to date. Checked {_updates.LastChecked:h:mm tt}.",
            UpdateState.Failed => "Couldn't reach the update server. Will try again later.",
            _ => "Checks for updates automatically.",
        };

        _updateButton.Content = _updates.State == UpdateState.ReadyToRestart ? "Restart now" : "Check now";
        _updateButton.Classes.Set("accent", _updates.State == UpdateState.ReadyToRestart);
        _updateButton.IsEnabled = _updates.IsInstalled &&
                                  _updates.State is not (UpdateState.Checking or UpdateState.Downloading);
    }

    // --- Layout helpers ------------------------------------------------------------------------------

    private static TextBlock Section(string text)
    {
        var t = new TextBlock { Text = text };
        t.Classes.Add("section");
        return t;
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.Classes.Add("muted");
        return t;
    }

    private static Control Row(string title, string? subtitle, Control control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        if (subtitle is not null) text.Children.Add(Muted(subtitle));

        // ToggleSwitch shows "On"/"Off" text by default; the row already says what it does.
        if (control is ToggleSwitch ts)
        {
            ts.OnContent = null;
            ts.OffContent = null;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6) };
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }
}
