using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mantel.Core;
using Mantel.UI;
using Mantel.Updates;

namespace Mantel;

public partial class App : Application
{
    private SettingsStore _store = null!;
    private WidgetManager _widgets = null!;
    private UpdateService _updates = null!;
    private TrayController _tray = null!;
    private SettingsWindow? _settingsWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            Startup();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Startup()
    {
        _store = SettingsStore.Load();
        var settings = _store.Settings;

        if (WidgetRegistry.EnsureInstances(settings, _store.IsFirstRun)) _store.Save();

        _widgets = new WidgetManager(_store) { OpenSettings = OpenSettings };
        _updates = new UpdateService(settings);
        _tray = new TrayController(this, _widgets, _updates, OpenSettings, Quit);

        // First launch of an installed build: start with Windows, like any widget app should.
        if (_store.IsFirstRun && _updates.IsInstalled) StartupRegistration.Enable();

        _widgets.ShowEnabled();
        _updates.Start();

        ShowUpdatedToastIfNeeded(settings);
    }

    private void ShowUpdatedToastIfNeeded(MantelSettings settings)
    {
        var previous = settings.LastRunVersion;
        if (previous == AppInfo.Version) return;

        settings.LastRunVersion = AppInfo.Version;
        _store.Save();

        if (previous is not null && _updates.IsInstalled)
            new UpdatedToast(AppInfo.Version).Show();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            open.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_store, _widgets, _updates);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void Quit()
    {
        _store.Save();
        _widgets.CloseAll();
        _updates.ApplyOnExitIfPending();
        _tray.Dispose();
        _desktop?.Shutdown();
    }
}
