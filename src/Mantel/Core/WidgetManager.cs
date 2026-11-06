using Avalonia.Controls;

namespace Mantel.Core;

/// <summary>
/// Turns widgets on and off. "Off" means fully off: the widget object is disposed
/// and its window closed, so it stops all polling, sensor reads and network calls.
/// </summary>
public sealed class WidgetManager
{
    private readonly SettingsStore _store;
    private readonly Dictionary<string, (IWidget Widget, WidgetWindow Window)> _active = new();

    /// <summary>Raised when the set of enabled widgets or the lock state changes.</summary>
    public event Action? Changed;

    /// <summary>Lets a widget's right-click menu open the settings window.</summary>
    public Action? OpenSettings { get; set; }

    public WidgetManager(SettingsStore store) => _store = store;

    private MantelSettings Settings => _store.Settings;

    public bool Locked => Settings.Locked;

    public IEnumerable<WidgetInstanceSettings> Instances => Settings.Widgets;

    public bool IsEnabled(string id) => Settings.Widgets.FirstOrDefault(w => w.Id == id)?.Enabled == true;

    /// <summary>Shows every enabled widget. Call once at startup.</summary>
    public void ShowEnabled()
    {
        var index = 0;
        foreach (var s in Settings.Widgets.Where(w => w.Enabled))
            Show(s, index++);
    }

    public void SetEnabled(string id, bool enabled)
    {
        var s = Settings.Widgets.FirstOrDefault(w => w.Id == id);
        if (s is null || s.Enabled == enabled) return;

        s.Enabled = enabled;
        if (enabled) Show(s, _active.Count); else Hide(id);

        _store.Save();
        Changed?.Invoke();
    }

    public void SetLocked(bool locked)
    {
        if (Settings.Locked == locked) return;
        Settings.Locked = locked;
        foreach (var (_, window) in _active.Values) window.SetLocked(locked);
        _store.Save();
        Changed?.Invoke();
    }

    public void CloseAll()
    {
        foreach (var id in _active.Keys.ToList()) Hide(id);
    }

    private void Show(WidgetInstanceSettings s, int fallbackIndex)
    {
        if (_active.ContainsKey(s.Id)) return;
        var descriptor = WidgetRegistry.Find(s.Type);
        if (descriptor is null) return; // type removed in a later version; keep its settings, show nothing

        IWidget widget;
        try { widget = descriptor.Create(s); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Mantel: widget '{s.Type}' failed to start. {ex.Message}");
            return;
        }

        var window = new WidgetWindow(widget, s, Settings.Locked, BuildMenu(s, descriptor));
        window.Moved += _store.Save;
        window.PlaceOnScreen(fallbackIndex);
        window.Show();
        widget.Start();

        _active[s.Id] = (widget, window);
    }

    private void Hide(string id)
    {
        if (!_active.Remove(id, out var entry)) return;
        entry.Widget.Stop();
        entry.Widget.Dispose();
        entry.Window.Close();
    }

    private ContextMenu BuildMenu(WidgetInstanceSettings s, WidgetDescriptor d)
    {
        var lockItem = new MenuItem { Header = Settings.Locked ? "Unlock widgets" : "Lock widgets" };
        lockItem.Click += (_, _) => SetLocked(!Settings.Locked);

        var hideItem = new MenuItem { Header = $"Turn off {d.Name}" };
        hideItem.Click += (_, _) => SetEnabled(s.Id, false);

        var settingsItem = new MenuItem { Header = "Mantel settings…" };
        settingsItem.Click += (_, _) => OpenSettings?.Invoke();

        var menu = new ContextMenu();
        menu.Items.Add(lockItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);

        // The lock label goes stale after a toggle; refresh it each time the menu opens.
        menu.Opening += (_, _) => lockItem.Header = Settings.Locked ? "Unlock widgets" : "Lock widgets";
        return menu;
    }
}
