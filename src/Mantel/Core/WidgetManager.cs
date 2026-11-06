using Avalonia.Controls;

namespace Mantel.Core;

/// <summary>
/// Turns widgets on and off. "Off" means fully off: the widget object is disposed
/// and its window closed, so it stops all polling, sensor reads and network calls.
/// </summary>
public sealed class WidgetManager
{
    private readonly SettingsStore _store;
    private readonly FullscreenWatcher _fullscreen = new();
    private readonly Dictionary<string, (IWidget Widget, WidgetWindow Window)> _active = new();

    /// <summary>Raised when a widget is turned on/off or the lock state changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when instances are added or removed (lists need rebuilding).</summary>
    public event Action? StructureChanged;

    /// <summary>Lets a widget's right-click menu open the settings window.</summary>
    public Action? OpenSettings { get; set; }

    public WidgetManager(SettingsStore store)
    {
        _store = store;
        _fullscreen.Changed += OnFullscreenChanged;
    }

    private MantelSettings Settings => _store.Settings;

    public bool Locked => Settings.Locked;

    public bool SnapToGrid => Settings.SnapToGrid;

    public bool HideOnFullscreen => Settings.HideOnFullscreen;

    public IReadOnlyList<WidgetInstanceSettings> Instances => Settings.Widgets;

    public bool IsEnabled(string id) => Find(id)?.Enabled == true;

    /// <summary>Shows every enabled widget. Call once at startup.</summary>
    public void ShowEnabled()
    {
        var index = 0;
        foreach (var s in Settings.Widgets.Where(w => w.Enabled).ToList())
            Show(s, index++);
        _fullscreen.SetEnabled(Settings.HideOnFullscreen);
    }

    public void SetEnabled(string id, bool enabled)
    {
        var s = Find(id);
        if (s is null || s.Enabled == enabled) return;

        s.Enabled = enabled;
        if (enabled) Show(s, _active.Count); else Hide(id);

        _store.Save();
        Changed?.Invoke();
    }

    /// <summary>Adds another instance of a multi-instance widget (a second clock, a new countdown).</summary>
    public WidgetInstanceSettings AddInstance(string type, Dictionary<string, string>? options = null)
    {
        var s = new WidgetInstanceSettings { Type = type, Enabled = true, Options = options ?? new() };
        Settings.Widgets.Add(s);
        Show(s, _active.Count);
        _store.Save();
        StructureChanged?.Invoke();
        Changed?.Invoke();
        return s;
    }

    /// <summary>Removes an instance entirely. Keeps at least one of each type so it stays in the list.</summary>
    public void RemoveInstance(string id)
    {
        var s = Find(id);
        if (s is null || !CanRemove(s)) return;
        Hide(id);
        Settings.Widgets.Remove(s);
        _store.Save();
        StructureChanged?.Invoke();
        Changed?.Invoke();
    }

    public bool CanRemove(WidgetInstanceSettings s) => Settings.Widgets.Count(w => w.Type == s.Type) > 1;

    /// <summary>Rebuilds a running widget after its options change. Its position is kept.</summary>
    public void Reload(string id)
    {
        _store.Save();
        var s = Find(id);
        if (s is null || !s.Enabled || !_active.ContainsKey(id)) return;
        Hide(id);
        Show(s, 0);
    }

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
        foreach (var id in _active.Keys.ToList()) Hide(id);
    }

    private WidgetInstanceSettings? Find(string id) => Settings.Widgets.FirstOrDefault(w => w.Id == id);

    private void Show(WidgetInstanceSettings s, int fallbackIndex)
    {
        if (_active.ContainsKey(s.Id)) return;
        var descriptor = WidgetRegistry.Find(s.Type);
        if (descriptor is null) return; // type removed in a later version; keep its settings, show nothing

        IWidget? widget = null;
        try
        {
            widget = descriptor.Create(s);
            var window = new WidgetWindow(widget, s, Settings.Locked, () => Settings.SnapToGrid, BuildMenu(s, descriptor));
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

    private ContextMenu BuildMenu(WidgetInstanceSettings s, WidgetDescriptor d)
    {
        var lockItem = new MenuItem();
        lockItem.Click += (_, _) => SetLocked(!Settings.Locked);

        var offItem = new MenuItem { Header = $"Turn off {d.Name}" };
        offItem.Click += (_, _) => SetEnabled(s.Id, false);

        var settingsItem = new MenuItem { Header = "Mantel settings…" };
        settingsItem.Click += (_, _) => OpenSettings?.Invoke();

        var menu = new ContextMenu();
        menu.Items.Add(lockItem);
        menu.Items.Add(offItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);

        void SyncLabel() => lockItem.Header = Settings.Locked ? "Unlock widgets" : "Lock widgets";
        SyncLabel();
        menu.Opening += (_, _) => SyncLabel();
        return menu;
    }
}
