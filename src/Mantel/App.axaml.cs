using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.Integrations;
using Mantel.Theme;
using Mantel.UI;
using Mantel.Updates;

namespace Mantel;

public partial class App : Application
{
    private SettingsStore _store = null!;
    private WidgetManager _widgets = null!;
    private UpdateService _updates = null!;
    private TrayController? _tray;
    private SettingsWindow? _settingsWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            InstallCrashGuard();
            Startup();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A bug in one widget should never take Mantel down, because a crashed Mantel can't
    /// download the update that fixes it. Log it and keep going.
    /// </summary>
    private static void InstallCrashGuard()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception (kept running).", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Fatal exception.", e.ExceptionObject as Exception);
    }

    private void Startup()
    {
        _store = SettingsStore.Load();
        var settings = _store.Settings;
        Log.Info($"Mantel {AppInfo.Version} starting.");

        if (WidgetRegistry.EnsureInstances(settings, _store.IsFirstRun)) _store.Save();

        TrayHost.Attach(this);
        try { ApplyTheme(); }
        catch (Exception ex) { Log.Error("Theme failed to apply; using the default look.", ex); }

        // The updater starts first and on its own, so even if something below fails,
        // Mantel can still fetch the release that fixes it.
        _updates = new UpdateService(settings);
        _updates.Start();

        _widgets = new WidgetManager(_store) { OpenSettings = OpenSettings };

        try { _tray = new TrayController(this, _widgets, _updates, OpenSettings, Quit); }
        catch (Exception ex) { Log.Error("Tray icon failed.", ex); }

        // First launch of an installed build: start with Windows, like any widget app should.
        if (_store.IsFirstRun && _updates.IsInstalled)
        {
            try { StartupRegistration.Enable(); }
            catch (Exception ex) { Log.Error("Couldn't register for startup.", ex); }
        }

        try { _widgets.ShowEnabled(); }
        catch (Exception ex) { Log.Error("Showing widgets failed.", ex); }

        ShowUpdatedToastIfNeeded(settings);

        // No tray means no way to reach Settings; open it so the user isn't stuck.
        if (_tray is null) OpenSettings();
    }

    private void ShowUpdatedToastIfNeeded(MantelSettings settings)
    {
        var previous = settings.LastRunVersion;
        if (previous == AppInfo.Version) return;

        settings.LastRunVersion = AppInfo.Version;
        _store.Save();

        if (previous is not null && _updates.IsInstalled)
        {
            try { new UpdatedToast(AppInfo.Version, OpenSettings).Show(); }
            catch (Exception ex) { Log.Error("Update toast failed.", ex); }
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            open.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_store, _widgets, _updates, OnAppearanceChanged);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // --- Theme and accent ------------------------------------------------------------------

    private bool _followingAmplite;

    private void ApplyTheme()
    {
        var settings = _store.Settings;
        ThemeManager.Apply(this, settings.Theme, settings.Accent);

        // "Match Amplite's accent": listen to Amplite and recolor live whenever its accent changes.
        var follow = settings.Accent == "amplite";
        if (follow && !_followingAmplite)
        {
            _followingAmplite = true;
            AmpliteClient.Shared.Changed += OnAmpliteAccent;
            AmpliteClient.Shared.Acquire();
            OnAmpliteAccent();
        }
        else if (!follow && _followingAmplite)
        {
            _followingAmplite = false;
            AmpliteClient.Shared.Changed -= OnAmpliteAccent;
            AmpliteClient.Shared.Release();
        }
    }

    private void OnAmpliteAccent()
    {
        if (ThemeManager.ParseAccent(AmpliteClient.Shared.State?.Accent) is { } color)
            ThemeManager.SetAccent(this, color);
    }

    private void OnAppearanceChanged()
    {
        var themeBefore = ThemeManager.Current.Id;
        ApplyTheme();
        // Colors update live; fonts, card shapes and the Bare shadow are set when widgets are built.
        if (ThemeManager.Current.Id != themeBefore) _widgets.ReloadAll();
    }

    private void Quit()
    {
        Log.Info("Quitting.");
        _store.Save();
        _widgets.CloseAll();
        _updates.ApplyOnExitIfPending();
        _tray?.Dispose();
        _desktop?.Shutdown();
    }
}
