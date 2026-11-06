using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mantel.Core;
using Mantel.Theme;
using Mantel.Updates;

namespace Mantel.UI;

/// <summary>
/// Mantel's settings, laid out like a Windows 11 settings app: a sidebar of pages, and on each
/// page rows of setting cards. Widgets and flyouts open detail pages instead of expanding inline.
/// </summary>
public sealed class SettingsWindow : Window
{
    /// <summary>Every page, in sidebar order, plus the two detail pages (for screenshots).</summary>
    public static readonly string[] PageNames = { "widgets", "widget-detail", "flyouts", "flyout-detail", "appearance", "general", "updates" };

    private static readonly (string Id, string Title, string Icon)[] Nav =
    {
        ("widgets", "Widgets", Icons.Widgets),
        ("flyouts", "Flyouts", Icons.Flyout),
        ("appearance", "Appearance", Icons.Palette),
        ("general", "General", Icons.Sliders),
        ("updates", "Updates & about", Icons.Update),
    };

    private static readonly string[] AccentPresets =
        { "#E8A15A", "#E8665A", "#FF3CAC", "#B18CFF", "#6FA8E8", "#88C0D0", "#5AD19A", "#F2C94C" };

    private readonly SettingsStore _store;
    private readonly WidgetManager _widgets;
    private readonly UpdateService _updates;
    private readonly Action _appearanceChanged;

    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly List<Action> _syncers = new();

    // What's on screen: a page id, or a detail page for one widget or flyout.
    private string _page = "widgets";
    private string? _detailWidgetId;
    private string? _detailFlyoutId;
    private string? _detailReturnFlyoutId; // a widget detail opened from inside a flyout goes back there
    private bool _syncing;

    public SettingsWindow(SettingsStore store, WidgetManager widgets, UpdateService updates, Action appearanceChanged)
    {
        _store = store;
        _widgets = widgets;
        _updates = updates;
        _appearanceChanged = appearanceChanged;

        Title = "Mantel settings";
        Width = 1060;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Classes.Add("settings");
        Icon = TrayHost.LoadIcon("mantel");

        // Windows 11: Mica behind the window and a seamless title bar. Elsewhere: solid dark.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.None };
        Background = new SolidColorBrush(Color.Parse("#141519"));
        Opened += (_, _) =>
        {
            if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
                Background = new SolidColorBrush(Color.Parse("#B3141519"));
        };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("236,*") };
        root.Children.Add(BuildSidebar());
        // The page scrolls below the title-bar strip (caption buttons live there), never under it.
        var content = new Border { Child = _scroll, Margin = new Thickness(0, 40, 0, 0) };
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        Content = root;

        _widgets.Changed += SyncFromModel;
        _widgets.StructureChanged += Rerender;
        _updates.StateChanged += SyncFromModel;
        Closed += (_, _) =>
        {
            _widgets.Changed -= SyncFromModel;
            _widgets.StructureChanged -= Rerender;
            _updates.StateChanged -= SyncFromModel;
        };

        ShowPage("widgets");
    }

    // --- Navigation ----------------------------------------------------------------------------

    /// <summary>Shows a page by id (see <see cref="PageNames"/>).</summary>
    public void ShowPage(string page)
    {
        _detailWidgetId = null;
        _detailFlyoutId = null;
        _detailReturnFlyoutId = null;
        switch (page)
        {
            case "widget-detail":
                _page = "widgets";
                _detailWidgetId = _widgets.Instances.FirstOrDefault(w => w.FlyoutId is null && w.Type == "weather")?.Id
                                  ?? _widgets.Instances.FirstOrDefault()?.Id;
                break;
            case "flyout-detail":
                _page = "flyouts";
                _detailFlyoutId = _widgets.FlyoutList.FirstOrDefault(f => f.Name == "Glance")?.Id ?? _widgets.FlyoutList.FirstOrDefault()?.Id;
                break;
            default:
                _page = Nav.Any(n => n.Id == page) ? page : "widgets";
                break;
        }
        Render(resetScroll: true);
    }

    private void OpenWidget(string id, string? fromFlyout = null)
    {
        _detailWidgetId = id;
        _detailReturnFlyoutId = fromFlyout;
        _detailFlyoutId = null;
        Render(resetScroll: true);
    }

    private void OpenFlyout(string id)
    {
        _detailFlyoutId = id;
        _detailWidgetId = null;
        Render(resetScroll: true);
    }

    private void Back()
    {
        if (_detailWidgetId is not null && _detailReturnFlyoutId is { } flyout)
        {
            _page = "flyouts";
            OpenFlyout(flyout);
            return;
        }
        _detailWidgetId = null;
        _detailFlyoutId = null;
        Render(resetScroll: true);
    }

    private void Rerender() => Render(resetScroll: false);

    private void Render(bool resetScroll)
    {
        var offset = _scroll.Offset;
        _syncers.Clear();

        foreach (var (id, button) in _navButtons) button.Classes.Set("selected", id == _page);

        Control page;
        try
        {
            if (_detailWidgetId is { } wid && _widgets.Instances.FirstOrDefault(w => w.Id == wid) is { } instance)
                page = WidgetDetailPage(instance);
            else if (_detailFlyoutId is { } fid && _widgets.FlyoutList.FirstOrDefault(f => f.Id == fid) is { } flyout)
                page = FlyoutDetailPage(flyout);
            else
            {
                _detailWidgetId = _detailFlyoutId = null;
                page = _page switch
                {
                    "flyouts" => FlyoutsPage(),
                    "appearance" => AppearancePage(),
                    "general" => GeneralPage(),
                    "updates" => UpdatesPage(),
                    _ => WidgetsPage(),
                };
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Settings page '{_page}' failed to build.", ex);
            page = Muted("This page couldn't load. The log has details.");
        }

        // Pages fill the width up to a comfortable reading width, left-aligned.
        var column = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) { MaxWidth = 780 }, new ColumnDefinition(GridLength.Auto) },
        };
        column.Children.Add(page);
        _scroll.Content = new Border { Padding = new Thickness(40, 8, 40, 40), Child = column };
        if (!resetScroll) Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Loaded);
        else _scroll.Offset = default;
        SyncFromModel();
    }

    private Control BuildSidebar()
    {
        var logo = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#26E8A15A")),
            Child = Icons.Create(Icons.Mantel, 20, accent: true),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var name = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Mantel", FontSize = 16, FontWeight = FontWeight.SemiBold },
                Muted($"Version {AppInfo.Version}", 12),
            },
        };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 22), Children = { logo, name } };

        var nav = new StackPanel { Spacing = 2 };
        foreach (var (id, title, icon) in Nav)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { Icons.Create(icon, 18), new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } },
                },
            };
            button.Classes.Add("nav");
            var page = id;
            button.Click += (_, _) => ShowPage(page);
            _navButtons[id] = button;
            nav.Children.Add(button);
        }

        var stack = new StackPanel { Margin = new Thickness(12, 48, 12, 16), Children = { brand, nav } };
        var sidebar = new Border { Child = stack };
        sidebar.Classes.Add("sidebar");
        return sidebar;
    }

    // --- Widgets page -----------------------------------------------------------------------------

    private Control WidgetsPage()
    {
        var desktop = _widgets.Instances.Where(w => w.FlyoutId is null && WidgetRegistry.Find(w.Type) is not null).ToList();
        var page = Page("Widgets", "Widgets that sit on your desktop. Unlock positions to drag them anywhere.", AddWidgetButton(null, accent: true));

        var on = desktop.Where(w => w.Enabled).ToList();
        var off = desktop.Where(w => !w.Enabled).ToList();

        if (on.Count > 0)
        {
            page.Children.Add(Group("On your desktop"));
            page.Children.Add(Cards(on.Select(w => WidgetCardRow(w, null))));
        }
        if (off.Count > 0)
        {
            page.Children.Add(Group(on.Count > 0 ? "Turned off" : "Your widgets"));
            page.Children.Add(Cards(off.Select(w => WidgetCardRow(w, null))));
        }
        if (desktop.Count == 0)
            page.Children.Add(EmptyCard("No widgets on the desktop", "Add one with the button above, or put them in a flyout instead."));

        var lockToggle = Toggle(() => _widgets.Locked, v => _widgets.SetLocked(v));
        page.Children.Add(Group("Layout"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Widgets, "Lock widget positions", "Turn off to drag widgets around your desktop", lockToggle),
        }));
        return page;
    }

    private Control WidgetCardRow(WidgetInstanceSettings w, string? flyoutId, bool reorder = false)
    {
        var d = WidgetRegistry.Find(w.Type)!;
        var id = w.Id;
        var toggle = Toggle(() => _widgets.IsEnabled(id), v => _widgets.SetEnabled(id, v));
        var subtitle = d.Subtitle?.Invoke(w) ?? d.Description;

        Control trailing = toggle;
        if (reorder)
        {
            var up = IconButton(Icons.ArrowUp, "Move up", () => _widgets.MoveInFlyout(id, -1));
            var down = IconButton(Icons.ArrowDown, "Move down", () => _widgets.MoveInFlyout(id, +1));
            trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { up, down, toggle } };
        }
        return Card(Icons.ForWidget(w.Type), d.Name, subtitle, trailing, onClick: () => OpenWidget(id, flyoutId));
    }

    private Control WidgetDetailPage(WidgetInstanceSettings w)
    {
        var d = WidgetRegistry.Find(w.Type)!;
        var id = w.Id;
        var crumb = _detailReturnFlyoutId is { } fid && _widgets.FlyoutList.FirstOrDefault(f => f.Id == fid) is { } fly
            ? $"Flyouts  ›  {fly.Name}"
            : "Widgets";
        var page = DetailPage(crumb, Icons.ForWidget(w.Type), d.Name, d.Description);

        // Where it lives: the desktop or one of the flyouts.
        var places = new List<(string? Id, string Name)> { (null, "Desktop") };
        places.AddRange(_widgets.FlyoutList.Select(f => ((string?)f.Id, $"{f.Name} flyout")));
        var where = new ComboBox { MinWidth = 200 };
        foreach (var p in places) where.Items.Add(new ComboBoxItem { Content = p.Name });
        where.SelectedIndex = Math.Max(0, places.FindIndex(p => p.Id == w.FlyoutId));
        where.SelectionChanged += (_, _) =>
        {
            if (where.SelectedIndex < 0) return;
            var target = places[where.SelectedIndex].Id;
            if (target == w.FlyoutId) return;
            _detailReturnFlyoutId = target;
            _widgets.SetPlacement(id, target);
        };

        page.Children.Add(Cards(new[]
        {
            Card(Icons.Check, "Show this widget", null, Toggle(() => _widgets.IsEnabled(id), v => _widgets.SetEnabled(id, v))),
            Card(Icons.Flyout, "Show on", "The desktop, or inside one of your flyouts", where),
        }));

        if (d.BuildOptions is not null)
        {
            page.Children.Add(Group("Options"));
            Control options;
            try
            {
                options = d.BuildOptions(new WidgetOptionsContext(w, () => _widgets.Reload(id)));
            }
            catch (Exception ex)
            {
                Log.Error($"Options panel for '{d.Type}' failed.", ex);
                options = Muted("These options couldn't load.");
            }
            var panel = new Border { Child = options, Padding = new Thickness(20, 16) };
            panel.Classes.Add("setting-card");
            page.Children.Add(panel);
        }

        var remove = new Button { Content = "Remove this widget", Margin = new Thickness(0, 24, 0, 0) };
        remove.Classes.Add("danger");
        remove.Click += (_, _) =>
        {
            _widgets.RemoveInstance(id);
            Back();
        };
        page.Children.Add(remove);
        return page;
    }

    // --- Flyouts page -------------------------------------------------------------------------------

    private Control FlyoutsPage()
    {
        var add = new Button { Content = WithIcon(Icons.Plus, "New flyout") };
        add.Classes.Add("primary");
        add.Click += (_, _) =>
        {
            var f = _widgets.AddFlyout();
            OpenFlyout(f.Id);
        };

        var page = Page("Flyouts", "Panels of widgets that stay hidden until you want them: push the mouse to a screen edge, or click their taskbar icon.", add);

        if (_widgets.FlyoutList.Count == 0)
            page.Children.Add(EmptyCard("No flyouts yet", "Make one, then add widgets to it."));
        else
            page.Children.Add(Cards(_widgets.FlyoutList.Select(FlyoutCardRow)));
        return page;
    }

    private Control FlyoutCardRow(FlyoutSettings f)
    {
        var id = f.Id;
        var toggle = Toggle(() => _widgets.FlyoutList.FirstOrDefault(x => x.Id == id)?.Enabled == true, v =>
        {
            f.Enabled = v;
            _widgets.FlyoutChanged(structural: true);
        });
        return Card(Icons.ForFlyout(f.Icon), f.Name, FlyoutSummary(f), toggle, onClick: () => OpenFlyout(id));
    }

    private string FlyoutSummary(FlyoutSettings f)
    {
        var count = _widgets.Instances.Count(w => w.FlyoutId == f.Id);
        var opens = new List<string>();
        if (f.Edge is "left" or "right" or "top") opens.Add($"{Capitalize(f.Edge)} edge");
        if (f.TrayIcon) opens.Add("taskbar icon");
        return $"{count} widget{(count == 1 ? "" : "s")} · " + (opens.Count > 0 ? string.Join(" or ", opens) : "no way to open it yet");
    }

    private Control FlyoutDetailPage(FlyoutSettings f)
    {
        var id = f.Id;
        var page = DetailPage("Flyouts", Icons.ForFlyout(f.Icon), f.Name, FlyoutSummary(f));

        var on = Toggle(() => _widgets.FlyoutList.FirstOrDefault(x => x.Id == id)?.Enabled == true, v =>
        {
            f.Enabled = v;
            _widgets.FlyoutChanged(structural: true);
        });

        var tray = Toggle(() => f.TrayIcon, v => { f.TrayIcon = v; _widgets.FlyoutChanged(structural: true); });

        var edge = Segmented(new[] { ("none", "Off"), ("left", "Left"), ("right", "Right"), ("top", "Top") }, () => f.Edge, v =>
        {
            f.Edge = v;
            _widgets.FlyoutChanged(structural: true);
        });

        page.Children.Add(Cards(new[]
        {
            Card(Icons.Check, "Use this flyout", null, on),
        }));

        page.Children.Add(Group("Opens from"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Taskbar, "Taskbar icon", "An icon by the clock; click it to open the flyout", tray),
            Card(Icons.Edge, "Screen edge", "Push the mouse against this edge to slide it out", edge),
        }));

        // Look: name and icon.
        var name = new TextBox { Text = f.Name, MinWidth = 220 };
        void CommitName()
        {
            var v = name.Text?.Trim();
            if (string.IsNullOrEmpty(v) || v == f.Name) return;
            f.Name = v;
            _widgets.FlyoutChanged(structural: true);
        }
        name.LostFocus += (_, _) => CommitName();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitName(); };

        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (value, label) in new[] { ("mantel", "Mantel"), ("music", "Music"), ("scores", "Basketball"), ("glance", "Glance") })
        {
            var b = new Button { Content = Icons.Create(Icons.ForFlyout(value), 20), Padding = new Thickness(9) };
            b.Classes.Add("icon-choice");
            b.Classes.Set("selected", f.Icon == value);
            ToolTip.SetTip(b, label);
            var v = value;
            b.Click += (_, _) => { f.Icon = v; _widgets.FlyoutChanged(structural: true); };
            icons.Children.Add(b);
        }

        page.Children.Add(Group("Look"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Info, "Name", null, name),
            Card(Icons.Palette, "Icon", "Shown in the flyout's header and on its taskbar icon", icons),
        }));

        // Its widgets, in order, with reordering.
        var items = _widgets.Instances.Where(w => w.FlyoutId == id && WidgetRegistry.Find(w.Type) is not null).ToList();
        page.Children.Add(Group("Widgets in this flyout", AddWidgetButton(id, accent: false)));
        page.Children.Add(items.Count == 0
            ? EmptyCard("No widgets yet", "Add some with the button above.")
            : Cards(items.Select(w => WidgetCardRow(w, id, reorder: items.Count > 1))));

        var open = new Button { Content = WithIcon(Icons.Open, "Open it now"), IsEnabled = f.Enabled };
        open.Classes.Add("primary");
        open.Click += (_, _) => _widgets.Flyouts.Open(id);
        var delete = new Button { Content = "Delete this flyout" };
        delete.Classes.Add("danger");
        delete.Click += (_, _) =>
        {
            _widgets.RemoveFlyout(id);
            Back();
        };
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 24, 0, 0), Children = { open, delete } });
        return page;
    }

    // --- Appearance page -------------------------------------------------------------------------

    private Control AppearancePage()
    {
        var settings = _store.Settings;
        var page = Page("Appearance", "One look for every widget and flyout.");

        page.Children.Add(Group("Theme"));
        var gallery = new WrapPanel();
        foreach (var t in ThemeManager.All)
        {
            var b = new Button { Content = ThemeTile(t, t.Id == settings.Theme) };
            b.Classes.Add("theme-tile");
            b.Classes.Set("selected", t.Id == settings.Theme);
            var id = t.Id;
            b.Click += (_, _) =>
            {
                settings.Theme = id;
                _store.Save();
                _appearanceChanged();
                Rerender();
            };
            gallery.Children.Add(b);
        }
        page.Children.Add(gallery);

        // Accent: the theme's own, follow Amplite, or a custom color.
        var mode = settings.Accent switch { "" => "theme", "amplite" => "amplite", _ => "custom" };
        var modePicker = Segmented(new[] { ("theme", "Theme's own"), ("amplite", "Match Amplite"), ("custom", "Custom") }, () => mode, v =>
        {
            if (v == "theme") SetAccent("");
            else if (v == "amplite") SetAccent("amplite");
            else if (ThemeManager.ParseAccent(settings.Accent) is null) SetAccent(AccentPresets[0]);
            Rerender();
        });

        page.Children.Add(Group("Accent color"));
        var cards = new List<Control>
        {
            Card(Icons.Palette, "Accent", mode == "amplite" ? "Follows Amplite's theme color while it's running" : "Progress bars, highlights and live games", modePicker),
        };

        if (mode == "custom")
        {
            var swatches = new WrapPanel();
            foreach (var hex in AccentPresets)
            {
                var selected = settings.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase);
                var dot = new Button
                {
                    Width = 30, Height = 30, Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(15),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    BorderThickness = new Thickness(selected ? 3 : 0),
                    BorderBrush = Brushes.White,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                dot.Classes.Add("swatch");
                var h = hex;
                dot.Click += (_, _) => { SetAccent(h); Rerender(); };
                swatches.Children.Add(dot);
            }
            var hexBox = new TextBox { Text = settings.Accent, Watermark = "#RRGGBB", Width = 110, Margin = new Thickness(8, 0, 0, 0) };
            hexBox.TextChanged += (_, _) =>
            {
                var v = hexBox.Text?.Trim() ?? "";
                if (!v.StartsWith('#')) v = "#" + v;
                if (ThemeManager.ParseAccent(v) is not null && !v.Equals(settings.Accent, StringComparison.OrdinalIgnoreCase))
                    SetAccent(v.ToUpperInvariant());
            };
            swatches.Children.Add(hexBox);
            var panel = new Border { Child = swatches, Padding = new Thickness(20, 14) };
            panel.Classes.Add("setting-card");
            cards.Add(panel);
        }
        page.Children.Add(Cards(cards));
        return page;
    }

    private void SetAccent(string value)
    {
        _store.Settings.Accent = value;
        _store.Save();
        _appearanceChanged();
    }

    /// <summary>A mini desktop in the theme's colors: a clock card and a music card over a wallpaper.</summary>
    private static Control ThemeTile(ThemeDef t, bool selected)
    {
        IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        var font = t.FontFamily is { } f ? new FontFamily(f) : FontFamily.Default;

        Border MiniCard(Control content) => new()
        {
            Background = t.Bare ? Brushes.Transparent : B(t.CardBackground),
            BorderBrush = B(t.CardBorder),
            BorderThickness = new Thickness(t.Bare ? 0 : t.CardBorderThickness),
            CornerRadius = new CornerRadius(Math.Min(t.CardRadius, 10)),
            Padding = new Thickness(10, 7),
            Child = content,
        };

        var clock = MiniCard(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "9:41", FontSize = t.Bare ? 30 : 24, FontWeight = t.BigWeight, Foreground = B(t.Text), FontFamily = font },
                new TextBlock { Text = "Friday", FontSize = 10, Foreground = B(t.TextMuted), FontFamily = font },
            },
        });
        var bar = new Border { Height = 4, Width = 70, CornerRadius = new CornerRadius(2), Background = B(t.Track), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        bar.Child = new Border { Width = 40, CornerRadius = new CornerRadius(2), Background = B(t.Accent), HorizontalAlignment = HorizontalAlignment.Left };
        var music = MiniCard(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "NOW PLAYING", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = B(t.Accent), FontFamily = font },
                new TextBlock { Text = "Song title", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = B(t.Text), FontFamily = font },
                bar,
            },
        });

        var wallpaper = new Border
        {
            Width = 196, Height = 112,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#1B2536"), 0),
                    new GradientStop(Color.Parse("#3A2433"), 1),
                },
            },
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center, Children = { clock, music } },
        };

        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(2, 10, 0, 0),
            Children = { new TextBlock { Text = t.Name, FontSize = 14, FontWeight = FontWeight.SemiBold } },
        };
        if (selected) title.Children.Add(Icons.Create(Icons.Check, 16, accent: true));
        var desc = Muted(t.Description, 12);
        desc.Margin = new Thickness(2, 2, 0, 0);
        desc.Width = 196;
        return new StackPanel { Children = { wallpaper, title, desc } };
    }

    // --- General and updates -----------------------------------------------------------------------

    private Control GeneralPage()
    {
        var page = Page("General", "How widgets behave on your desktop.");

        page.Children.Add(Group("Layout"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Widgets, "Lock widget positions", "Turn off to drag widgets around your desktop", Toggle(() => _widgets.Locked, v => _widgets.SetLocked(v))),
            Card(Icons.Edge, "Snap to grid and screen edges", "Dragged widgets line up neatly", Toggle(() => _widgets.SnapToGrid, v => _widgets.SetSnapToGrid(v))),
            Card(Icons.Flyout, "Hide during full-screen apps", "Games, videos and presentations. Widgets pause while hidden.", Toggle(() => _widgets.HideOnFullscreen, v => _widgets.SetHideOnFullscreen(v))),
        }));

        page.Children.Add(Group("Startup"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Mantel, "Start with Windows", "Mantel opens quietly in the tray when you sign in", Toggle(() => StartupRegistration.IsEnabled, StartupRegistration.Set)),
        }));
        return page;
    }

    private Control UpdatesPage()
    {
        var page = Page("Updates & about", "Mantel updates itself in the background and installs while you're away.");

        var status = Muted("");
        var button = new Button();
        button.Classes.Add("primary");
        button.Click += async (_, _) =>
        {
            if (_updates.State == UpdateState.ReadyToRestart) _updates.RestartAndApply();
            else await _updates.CheckAndDownloadAsync();
        };
        _syncers.Add(() =>
        {
            status.Text = _updates.State switch
            {
                UpdateState.NotInstalled => "Development build: updates are off.",
                UpdateState.Checking => "Checking for updates…",
                UpdateState.Downloading => "Downloading update…",
                UpdateState.ReadyToRestart => $"Version {_updates.PendingVersion} is ready. It installs when you're away, or restart now.",
                UpdateState.UpToDate => $"You're up to date. Checked at {_updates.LastChecked:h:mm tt}.",
                UpdateState.Failed => "Couldn't reach the update server. Mantel will try again later.",
                _ => "Mantel checks for updates every few hours.",
            };
            button.Content = _updates.State == UpdateState.ReadyToRestart ? "Restart now" : "Check for updates";
            button.IsEnabled = _updates.IsInstalled && _updates.State is not (UpdateState.Checking or UpdateState.Downloading);
        });

        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var tile = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#26E8A15A")),
            Child = Icons.Create(Icons.Mantel, 30, accent: true),
            Margin = new Thickness(0, 0, 16, 0),
        };
        hero.Children.Add(tile);
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = $"Mantel {AppInfo.Version}", FontSize = 18, FontWeight = FontWeight.SemiBold }, status },
        };
        Grid.SetColumn(text, 1);
        hero.Children.Add(text);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 2);
        hero.Children.Add(button);
        var heroCard = new Border { Child = hero, Padding = new Thickness(20, 18) };
        heroCard.Classes.Add("setting-card");
        page.Children.Add(heroCard);

        page.Children.Add(Group("Preferences"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Update, "Update automatically", "Download in the background and install while you're away",
                Toggle(() => _store.Settings.AutoUpdate, v => { _store.Settings.AutoUpdate = v; _store.Save(); })),
            Card(Icons.Info, "Beta updates", "Get new features early. They can be rougher around the edges.",
                Toggle(() => _store.Settings.BetaUpdates, v => { _store.Settings.BetaUpdates = v; _store.Save(); })),
        }));

        page.Children.Add(Group("About"));
        page.Children.Add(Cards(new[]
        {
            Card(Icons.Open, "Release notes", "What changed in each version", null, onClick: () => AppInfo.OpenUrl($"{AppInfo.RepoUrl}/releases")),
            Card(Icons.Info, "Log folder", "Where Mantel writes its log and settings", null, onClick: () => AppInfo.OpenUrl(SettingsStore.Folder)),
        }));
        return page;
    }

    // --- Building blocks ---------------------------------------------------------------------------

    private void SyncFromModel()
    {
        _syncing = true;
        try { foreach (var s in _syncers) s(); }
        finally { _syncing = false; }
    }

    /// <summary>A page: big title, a line of explanation, and an optional button at the top right.</summary>
    private static StackPanel Page(string title, string subtitle, Control? action = null)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.SemiBold },
                Muted(subtitle, 14),
            },
        });
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Top;
            action.Margin = new Thickness(24, 6, 0, 0);
            Grid.SetColumn(action, 1);
            header.Children.Add(action);
        }
        return new StackPanel { Children = { header } };
    }

    /// <summary>A detail page: a back link with the breadcrumb, then the item's icon, name and description.</summary>
    private StackPanel DetailPage(string crumb, string icon, string title, string subtitle)
    {
        var back = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Icons.Create(Icons.ChevronLeft, 16), new TextBlock { Text = crumb, VerticalAlignment = VerticalAlignment.Center } },
            },
            Margin = new Thickness(-10, 0, 0, 14),
        };
        back.Classes.Add("ghost");
        back.Click += (_, _) => Back();

        var tile = IconTile(icon, 52, 26);
        tile.Margin = new Thickness(0, 0, 16, 0);
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
            Children = { new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeight.SemiBold }, Muted(subtitle, 14) },
        };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22), Children = { tile, text } };
        return new StackPanel { Children = { back, heading } };
    }

    private static Control Group(string text, Control? action = null)
    {
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
        label.Classes.Add("group");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(2, 26, 0, 8) };
        grid.Children.Add(label);
        if (action is not null)
        {
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
        }
        return grid;
    }

    private static Control Cards(IEnumerable<Control> cards)
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (var c in cards) stack.Children.Add(c);
        return stack;
    }

    /// <summary>
    /// One setting row: icon, title and subtitle on the left, a control on the right, and a
    /// chevron when the whole row opens something.
    /// </summary>
    private static Control Card(string icon, string title, string? subtitle, Control? control, Action? onClick = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), MinHeight = 44 };

        var tile = IconTile(icon, 36, 19);
        tile.Margin = new Thickness(0, 0, 14, 0);
        tile.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        if (!string.IsNullOrEmpty(subtitle)) text.Children.Add(Muted(subtitle, 12.5));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
        }

        if (onClick is not null)
        {
            var chevron = Icons.Create(Icons.ChevronRight, 16);
            chevron.Opacity = 0.6;
            chevron.Margin = new Thickness(14, 0, 0, 0);
            chevron.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chevron, 3);
            grid.Children.Add(chevron);
        }

        var card = new Border { Child = grid, Padding = new Thickness(16, 12) };
        card.Classes.Add("setting-card");
        if (onClick is not null)
        {
            card.Classes.Add("clickable");
            card.Cursor = new Cursor(StandardCursorType.Hand);
            // The row opens on click, unless the click was on its own control (toggle, buttons).
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (control is not null && e.Source is Visual v && (v == control || control.IsVisualAncestorOf(v))) return;
                onClick();
            };
        }
        return card;
    }

    private static Border IconTile(string icon, double size, double iconSize)
    {
        var tile = new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.26),
            Child = Icons.Create(icon, iconSize, accent: true),
        };
        tile.Classes.Add("icon-tile");
        return tile;
    }

    private static Control EmptyCard(string title, string subtitle)
    {
        var card = new Border
        {
            Padding = new Thickness(20, 22),
            Child = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    Muted(subtitle, 13),
                },
            },
        };
        card.Classes.Add("setting-card");
        card.Classes.Add("empty");
        return card;
    }

    private ToggleSwitch Toggle(Func<bool> get, Action<bool> set)
    {
        var toggle = new ToggleSwitch { IsChecked = get(), OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => { if (!_syncing) set(toggle.IsChecked == true); };
        _syncers.Add(() => toggle.IsChecked = get());
        return toggle;
    }

    /// <summary>A row of joined buttons, one of which is selected (like Windows' segmented controls).</summary>
    private Control Segmented((string Value, string Label)[] options, Func<string> get, Action<string> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<(string, Button)>();
        for (var i = 0; i < options.Length; i++)
        {
            var (value, label) = options[i];
            var b = new Button { Content = label };
            b.Classes.Add("segment");
            if (i == 0) b.Classes.Add("first");
            if (i == options.Length - 1) b.Classes.Add("last");
            b.Click += (_, _) => { if (get() != value) set(value); };
            buttons.Add((value, b));
            row.Children.Add(b);
        }
        void Sync() { foreach (var (v, b) in buttons) b.Classes.Set("selected", v == get()); }
        Sync();
        _syncers.Add(Sync);
        return row;
    }

    private Button AddWidgetButton(string? flyoutId, bool accent)
    {
        var button = new Button { Content = WithIcon(Icons.Plus, flyoutId is null ? "Add widget" : "Add") };
        button.Classes.Add(accent ? "primary" : "ghost");
        button.Click += (_, _) =>
        {
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (var d in WidgetRegistry.All)
            {
                var item = new MenuItem
                {
                    Header = new StackPanel
                    {
                        Children = { new TextBlock { Text = d.Name }, Muted(d.Description, 12) },
                    },
                    Icon = Icons.Create(Icons.ForWidget(d.Type), 18),
                };
                var type = d.Type;
                item.Click += (_, _) =>
                {
                    var added = _widgets.AddInstance(type, flyoutId);
                    // Widgets with options open straight to them (a new weather widget needs a city).
                    if (WidgetRegistry.Find(type)?.BuildOptions is not null) OpenWidget(added.Id, flyoutId);
                };
                menu.Items.Add(item);
            }
            menu.ShowAt(button);
        };
        return button;
    }

    private static Button IconButton(string icon, string tip, Action click)
    {
        var b = new Button { Content = Icons.Create(icon, 16), Padding = new Thickness(7) };
        b.Classes.Add("ghost");
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Control WithIcon(string icon, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { Icons.Create(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
    };

    private static TextBlock Muted(string text, double size = 13)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size };
        t.Classes.Add("muted");
        return t;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
