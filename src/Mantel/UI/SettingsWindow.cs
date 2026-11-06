using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mantel.Core;
using Mantel.Updates;

namespace Mantel.UI;

/// <summary>Widget toggles, layout lock, startup and update settings.</summary>
public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly WidgetManager _widgets;
    private readonly UpdateService _updates;

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
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#141519"));

        Content = new ScrollViewer
        {
            MaxHeight = 760,
            Content = new StackPanel { Margin = new Thickness(24, 8, 24, 24), Children =
            {
                Section("WIDGETS"),
                BuildWidgetList(),
                Section("LAYOUT"),
                Row("Lock widget positions", "Turn off to drag widgets around", _lockToggle),
                Section("STARTUP"),
                Row("Start with Windows", null, BuildStartupToggle()),
                Section("UPDATES"),
                BuildUpdates(),
            }},
        };

        _lockToggle.IsChecked = _widgets.Locked;
        _lockToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing) _widgets.SetLocked(_lockToggle.IsChecked == true);
        };

        _widgets.Changed += SyncFromModel;
        _updates.StateChanged += SyncUpdateStatus;
        Closed += (_, _) =>
        {
            _widgets.Changed -= SyncFromModel;
            _updates.StateChanged -= SyncUpdateStatus;
        };

        SyncUpdateStatus();
    }

    private Control BuildWidgetList()
    {
        var panel = new StackPanel { Spacing = 2 };
        foreach (var instance in _widgets.Instances)
        {
            var d = WidgetRegistry.Find(instance.Type);
            if (d is null) continue;

            var toggle = new ToggleSwitch { IsChecked = instance.Enabled };
            var id = instance.Id;
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (!_syncing) _widgets.SetEnabled(id, toggle.IsChecked == true);
            };
            _widgetToggles[id] = toggle;
            panel.Children.Add(Row(d.Name, d.Description, toggle));
        }
        return panel;
    }

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

        var releaseNotes = new Button { Content = "Release notes" };
        releaseNotes.Click += (_, _) => AppInfo.OpenUrl($"{AppInfo.RepoUrl}/releases");

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 8) };
        var info = new StackPanel { Spacing = 2, Children = { version, _updateStatus } };
        header.Children.Add(info);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { releaseNotes, _updateButton },
        };
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);

        return new StackPanel { Children =
        {
            header,
            Row("Update automatically", "Downloads in the background and applies when you're away", auto),
            Row("Beta updates", "Get new features early. Can be rougher around the edges.", beta),
        }};
    }

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
