using Avalonia.Controls;
using Mantel.Flyouts;

namespace Mantel.Core;

/// <summary>
/// Turns widgets on and off and decides where each one lives: on the desktop or in a flyout.
/// "Off" means fully off: the widget object is disposed and its window closed, so it stops
/// all polling, sensor reads and network calls.
/// </summary>
public sealed class WidgetManager
{
    private readonly SettingsStore _store;
    private readonly FullscreenWatcher _fullscreen = new();
    private readonly Dictionary<string, (IWidget Widget, WidgetWindow Window)> _active = new();

    /// <summary>Raised when a widget is turned on/off or the lock state changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when instances or flyouts are added, removed or moved (lists need rebuilding).</summary>
    public event Action? StructureChanged;

    /// <summary>Lets a widget's right-click menu open the settings window.</summary>
    public Action? OpenSettings { get; set; }

    public FlyoutManager Flyouts { get; }

    public WidgetManager(SettingsStore store)
    {
        _store = store;
        _fullscreen.Changed += OnFullscreenChanged;
        Flyouts = new FlyoutManager(store.Settings, s => BuildMenu(s), () => _fullscreen.IsFullscreen);
    }

    private MantelSettings Settings => _store.Settings;

    public bool Locked => Settings.Locked;
    public bool SnapToGrid => Settings.SnapToGrid;
    public bool HideOnFullscreen => Settings.HideOnFullscreen;

    public IReadOnlyList<WidgetInstanceSettings> Instances => Settings.Widgets;
    public IReadOnlyList<FlyoutSettings> FlyoutList => Settings.Flyouts;

    public bool IsEnabled(string id) => Find(id)?.Enabled == true;

    private static bool OnDesktop(WidgetInstanceSettings s) => s.FlyoutId is null;

    /// <summary>Shows every enabled widget and sets up flyouts. Call once at startup.</summary>
    public void ShowEnabled()
    {
        var index = 0;
        foreach (var s in Settings.Widgets.Where(w => w.Enabled && OnDesktop(w)).ToList())
            Show(s, index++);
        _fullscreen.SetEnabled(Settings.HideOnFullscreen);
        Flyouts.Rebuild();
    }

    // --- Widgets ----------------------------------------------------------------------------

    public void SetEnabled(string id, bool enabled)
    {
        var s = Find(id);
        if (s is null || s.Enabled == enabled) return;

        s.Enabled = enabled;
        if (OnDesktop(s))
        {
            if (enabled) Show(s, _active.Count); else Hide(id);
        }
        else Flyouts.Rebuild();

        _store.Save();
        Changed?.Invoke();
    }

    /// <summary>Adds a widget, on the desktop or straight into a flyout.</summary>
    public WidgetInstanceSettings AddInstance(string type, string? flyoutId = null)
    {
        var s = new WidgetInstanceSettings { Type = type, Enabled = true, FlyoutId = flyoutId };
        Settings.Widgets.Add(s);
        if (flyoutId is null) Show(s, _active.Count); else Flyouts.Rebuild();
        _store.Save();
        StructureChanged?.Invoke();
        Changed?.Invoke();
        return s;
    }

    public void RemoveInstance(string id)
    {
        var s = Find(id);
        if (s is null) return;
        Hide(id);
        Settings.Widgets.Remove(s);
        if (!OnDesktop(s)) Flyouts.Rebuild();
        _store.Save();
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Moves a widget between the desktop (null) and a flyout.</summary>
    public void SetPlacement(string id, string? flyoutId)
    {
        var s = Find(id);
        if (s is null || s.FlyoutId == flyoutId) return;
        var wasOnDesktop = OnDesktop(s);
        s.FlyoutId = flyoutId;

        if (wasOnDesktop) Hide(id);
        if (OnDesktop(s) && s.Enabled) Show(s, _active.Count);
        Flyouts.Rebuild();

        _store.Save();
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Rebuilds a widget after its options change. Its position is kept.</summary>
    public void Reload(string id)
    {
        _store.Save();
        var s = Find(id);
        if (s is null || !s.Enabled) return;
        if (!OnDesktop(s)) { Flyouts.Rebuild(); return; }
        if (!_active.ContainsKey(id)) return;
        Hide(id);
        Show(s, 0);
    }

    /// <summary>Rebuilds everything, e.g. after a theme change (fonts and card shapes are set at build time).</summary>
    public void ReloadAll()
    {
        foreach (var id in _active.Keys.ToList()) Hide(id);
        var index = 0;
        foreach (var s in Settings.Widgets.Where(w => w.Enabled && OnDesktop(w)).ToList())
            Show(s, index++);
        Flyouts.Rebuild();
    }

    // --- Flyouts --------------------------------------------------------------------------------

    public FlyoutSettings AddFlyout()
    {
        var f = new FlyoutSettings
        {
            Name = $"Flyout {Settings.Flyouts.Count + 1}",
            Enabled = true,
            TrayIcon = true,
            Edge = "none",
        };
        Settings.Flyouts.Add(f);
        _store.Save();
        Flyouts.Rebuild();
        StructureChanged?.Invoke();
        return f;
    }

    public void RemoveFlyout(string id)
    {
        var f = Settings.Flyouts.FirstOrDefault(x => x.Id == id);
        if (f is null) return;
        Settings.Flyouts.Remove(f);
        // Its widgets go with it; a flyout is a container, not a place widgets should be stranded.
        Settings.Widgets.RemoveAll(w => w.FlyoutId == id);
        _store.Save();
        Flyouts.Rebuild();
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Call after changing a flyout's name, trigger or icon.</summary>
    public void FlyoutChanged(bool structural = false)
    {
        _store.Save();
        Flyouts.Rebuild();
        if (structural) StructureChanged?.Invoke();
    }

    // --- Layout settings ----------------------------------------------------------------------

    public void SetLocked(bool locked)
    {
        if (Settings.Locked == locked) return;
        Settings.Locked = locked;
        foreach (var (_, window) in _active.Values) window.SetLocked(locked);
        _store.Save();
        Changed?.Invoke();
    }

    public void SetSnapToGrid(bool snap)
    {
        Settings.SnapToGrid = snap;
        _store.Save();
    }

    public void SetHideOnFullscreen(bool hide)
    {
        Settings.HideOnFullscreen = hide;
        _fullscreen.SetEnabled(hide);
        _store.Save();
    }

    public void CloseAll()
    {
        _fullscreen.SetEnabled(false);
        Flyouts.Dispose();
        foreach (var id in _active.Keys.ToList()) Hide(id);
    }

    private WidgetInstanceSettings? Find(string id) => Settings.Widgets.FirstOrDefault(w => w.Id == id);

    private void Show(WidgetInstanceSettings s, int fallbackIndex)
    {
        if (_active.ContainsKey(s.Id) || !OnDesktop(s)) return;
        var descriptor = WidgetRegistry.Find(s.Type);
        if (descriptor is null) return; // type removed in a later version; keep its settings, show nothing

        IWidget? widget = null;
        try
        {
            widget = descriptor.Create(s);
            var window = new WidgetWindow(widget, s, Settings.Locked, () => Settings.SnapToGrid, BuildMenu(s));
            window.Moved += _store.Save;
            window.PlaceOnScreen(fallbackIndex);
            _active[s.Id] = (widget, window);

            if (_fullscreen.IsFullscreen) return; // shown (and started) when the full-screen app goes away
            window.Show();
            widget.Start();
        }
        catch (Exception ex)
        {
            // One broken widget must never stop the others, or the updater that could fix it.
            Log.Error($"Widget '{s.Type}' failed to start.", ex);
            if (_active.Remove(s.Id, out var entry)) entry.Window.Close();
            try { widget?.Dispose(); } catch { /* already failing */ }
        }
    }

    private void Hide(string id)
    {
        if (!_active.Remove(id, out var entry)) return;
        try
        {
            entry.Widget.Stop();
            entry.Widget.Dispose();
        }
        catch (Exception ex) { Log.Error($"Widget '{id}' failed to stop cleanly.", ex); }
        entry.Window.Close();
    }

    private void OnFullscreenChanged(bool fullscreen)
    {
        if (fullscreen) Flyouts.CloseAll();
        foreach (var (widget, window) in _active.Values)
        {
            try
            {
                if (fullscreen) { widget.Stop(); window.Hide(); }
                else { window.Show(); widget.Start(); }
            }
            catch (Exception ex) { Log.Error("Full-screen pause/resume failed for a widget.", ex); }
        }
    }

    private ContextMenu BuildMenu(WidgetInstanceSettings s)
    {
        var name = WidgetRegistry.Find(s.Type)?.Name ?? "widget";
        var menu = new ContextMenu();

        if (OnDesktop(s))
        {
            var lockItem = new MenuItem();
            lockItem.Click += (_, _) => SetLocked(!Settings.Locked);
            void SyncLabel() => lockItem.Header = Settings.Locked ? "Unlock widgets" : "Lock widgets";
            SyncLabel();
            menu.Opening += (_, _) => SyncLabel();
            menu.Items.Add(lockItem);
        }

        var offItem = new MenuItem { Header = $"Turn off {name}" };
        offItem.Click += (_, _) => SetEnabled(s.Id, false);
        menu.Items.Add(offItem);

        menu.Items.Add(new Separator());
        var settingsItem = new MenuItem { Header = "Mantel settings…" };
        settingsItem.Click += (_, _) => OpenSettings?.Invoke();
        menu.Items.Add(settingsItem);
        return menu;
    }
}
