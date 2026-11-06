using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Mantel.Core;
using Mantel.Updates;

namespace Mantel.UI;

/// <summary>The tray icon and its menu: widget toggles, lock, settings, updates, quit.</summary>
public sealed class TrayController
{
    private readonly WidgetManager _widgets;
    private readonly UpdateService _updates;
    private readonly Action _openSettings;
    private readonly Action _quit;
    private readonly TrayIcon _tray;
    private readonly Dictionary<string, NativeMenuItem> _widgetItems = new();
    private NativeMenuItem _lockItem = null!;
    private NativeMenuItem _updateItem = null!;

    public TrayController(Application app, WidgetManager widgets, UpdateService updates, Action openSettings, Action quit)
    {
        _widgets = widgets;
        _updates = updates;
        _openSettings = openSettings;
        _quit = quit;

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Mantel/Assets/mantel.ico"))),
            ToolTipText = "Mantel",
            Menu = BuildMenu(),
        };
        _tray.Clicked += (_, _) => _openSettings();

        TrayHost.Icons.Add(_tray);

        _widgets.Changed += Rebuild;
        _widgets.StructureChanged += Rebuild;
        _updates.StateChanged += Sync;
        Sync();
    }

    /// <summary>Widgets were added or removed: rebuild the menu with fresh items.</summary>
    private void Rebuild()
    {
        _tray.Menu = BuildMenu();
        Sync();
    }

    private NativeMenu BuildMenu()
    {
        _widgetItems.Clear();
        var widgetsMenu = new NativeMenu();
        foreach (var d in WidgetRegistry.All)
        {
            var instances = _widgets.Instances.Where(i => i.Type == d.Type && i.FlyoutId is null).ToList();
            foreach (var instance in instances)
            {
                var id = instance.Id;
                var label = instances.Count > 1 && d.Subtitle?.Invoke(instance) is { } sub ? $"{d.Name} — {sub}" : d.Name;
                var item = new NativeMenuItem(label) { ToggleType = NativeMenuItemToggleType.CheckBox };
                item.Click += (_, _) => _widgets.SetEnabled(id, !_widgets.IsEnabled(id));
                _widgetItems[id] = item;
                widgetsMenu.Items.Add(item);
            }
        }

        _lockItem = new NativeMenuItem("Lock widgets") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _lockItem.Click += (_, _) => _widgets.SetLocked(!_widgets.Locked);

        _updateItem = new NativeMenuItem("Check for updates");
        _updateItem.Click += async (_, _) =>
        {
            if (_updates.State == UpdateState.ReadyToRestart) _updates.RestartAndApply();
            else await _updates.CheckAndDownloadAsync();
        };

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => _openSettings();

        var quit = new NativeMenuItem("Quit Mantel");
        quit.Click += (_, _) => _quit();

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Widgets") { Menu = widgetsMenu });

        var flyouts = _widgets.FlyoutList.Where(f => f.Enabled).ToList();
        if (flyouts.Count > 0)
        {
            var flyoutMenu = new NativeMenu();
            foreach (var f in flyouts)
            {
                var id = f.Id;
                var item = new NativeMenuItem(f.Name);
                item.Click += (_, _) => _widgets.Flyouts.Open(id);
                flyoutMenu.Items.Add(item);
            }
            menu.Items.Add(new NativeMenuItem("Open flyout") { Menu = flyoutMenu });
        }
        menu.Items.Add(_lockItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        return menu;
    }

    private void Sync()
    {
        foreach (var (id, item) in _widgetItems) item.IsChecked = _widgets.IsEnabled(id);
        _lockItem.IsChecked = _widgets.Locked;

        _updateItem.Header = _updates.State switch
        {
            UpdateState.NotInstalled => "Updates off (dev build)",
            UpdateState.Checking => "Checking for updates…",
            UpdateState.Downloading => "Downloading update…",
            UpdateState.ReadyToRestart => $"Restart to update ({_updates.PendingVersion})",
            _ => "Check for updates",
        };
        _updateItem.IsEnabled = _updates.IsInstalled &&
                                _updates.State is not (UpdateState.Checking or UpdateState.Downloading);

        _tray.ToolTipText = _updates.State == UpdateState.ReadyToRestart
            ? $"Mantel — update {_updates.PendingVersion} ready"
            : "Mantel";
    }

    public void Dispose() => _tray.IsVisible = false;
}
