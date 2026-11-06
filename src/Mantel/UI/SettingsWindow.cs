using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mantel.Core;
using Mantel.Updates;

namespace Mantel.UI;

/// <summary>Widget toggles and options, layout, startup and update settings.</summary>
public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly WidgetManager _widgets;
    private readonly UpdateService _updates;

    private readonly StackPanel _widgetList = new() { Spacing = 2 };
    private readonly TextBlock _updateStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _updateButton = new();
    private readonly ToggleSwitch _lockToggle = new();
    private readonly Dictionary<string, ToggleSwitch> _widgetToggles = new();
    private bool _syncing;

    public SettingsWindow(SettingsStore store, WidgetManager widgets, UpdateService updates)
    {
        _store = store;
        _widgets = widgets;
        _updates = updates;

        Title = "Mantel settings";
        Width = 500;
        Height = 760;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#141519"));
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Mantel/Assets/mantel.ico")));

        var snap = new ToggleSwitch { IsChecked = _widgets.SnapToGrid };
        snap.IsCheckedChanged += (_, _) => _widgets.SetSnapToGrid(snap.IsChecked == true);

        var fullscreen = new ToggleSwitch { IsChecked = _widgets.HideOnFullscreen };
        fullscreen.IsCheckedChanged += (_, _) => _widgets.SetHideOnFullscreen(fullscreen.IsChecked == true);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 8, 24, 24),
                Children =
                {
                    Section("WIDGETS"),
                    _widgetList,
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

        BuildWidgetList();

        _widgets.Changed += SyncFromModel;
        _widgets.StructureChanged += BuildWidgetList;
        _updates.StateChanged += SyncUpdateStatus;
        Closed += (_, _) =>
        {
            _widgets.Changed -= SyncFromModel;
            _widgets.StructureChanged -= BuildWidgetList;
            _updates.StateChanged -= SyncUpdateStatus;
        };

        SyncUpdateStatus();
    }

    // --- Widgets ---------------------------------------------------------------

    private void BuildWidgetList()
    {
        _widgetList.Children.Clear();
        _widgetToggles.Clear();

        foreach (var descriptor in WidgetRegistry.All)
        {
            var instances = _widgets.Instances.Where(i => i.Type == descriptor.Type).ToList();
            foreach (var instance in instances)
                _widgetList.Children.Add(WidgetEntry(descriptor, instance, numbered: instances.Count > 1));

            if (descriptor.AllowMultiple)
            {
                var add = new Button { Content = $"+ Add another {descriptor.Name.ToLowerInvariant()}" };
                add.Classes.Add("link");
                add.Margin = new Thickness(0, 0, 0, 6);
                add.Click += (_, _) => _widgets.AddInstance(descriptor.Type);
                _widgetList.Children.Add(add);
            }
        }
    }

    private Control WidgetEntry(WidgetDescriptor d, WidgetInstanceSettings instance, bool numbered)
    {
        var toggle = new ToggleSwitch { IsChecked = instance.Enabled, OnContent = null, OffContent = null };
        var id = instance.Id;
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing) _widgets.SetEnabled(id, toggle.IsChecked == true);
        };
        _widgetToggles[id] = toggle;

        var subtitleText = d.Subtitle?.Invoke(instance) ?? d.Description;
        var subtitle = new TextBlock { Text = subtitleText, TextWrapping = TextWrapping.Wrap };
        subtitle.Classes.Add("muted");

        if (d.BuildOptions is null && !d.AllowMultiple)
            return Row(d.Name, d.Description, toggle);

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

        var body = new StackPanel { Spacing = 6 };
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

        if (_widgets.CanRemove(instance))
        {
            var remove = new Button { Content = "Remove this " + d.Name.ToLowerInvariant() };
            remove.Classes.Add("link");
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            remove.Click += (_, _) => _widgets.RemoveInstance(id);
            body.Children.Add(remove);
        }

        return new Expander { Header = header, Content = body };
    }

    // --- Startup, updates, about -----------------------------------------------

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

    // --- Sync ----------------------------------------------------------------------

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

    // --- Layout helpers --------------------------------------------------------------

    private static TextBlock Section(string text)
    {
        var t = new TextBlock { Text = text };
        t.Classes.Add("section");
        return t;
    }

    private static Control Row(string title, string? subtitle, Control control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        if (subtitle is not null)
        {
            var sub = new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap };
            sub.Classes.Add("muted");
            text.Children.Add(sub);
        }

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
