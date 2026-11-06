using Avalonia.Controls;
using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// A widget's contract with the host. The host only creates a widget when it is
/// enabled, and disposes it when it is turned off, so a disabled widget costs nothing.
/// A widget can hide itself (e.g. Now Playing with nothing playing) by setting
/// <c>View.IsVisible = false</c>; the host then hides the whole card.
/// </summary>
public interface IWidget : IDisposable
{
    /// <summary>The content drawn inside the widget card.</summary>
    Control View { get; }

    /// <summary>Begin polling or listening for data.</summary>
    void Start();

    /// <summary>Stop all work. May be followed by <see cref="Start"/> again.</summary>
    void Stop();
}

/// <summary>Describes a widget type in the registry.</summary>
public sealed record WidgetDescriptor(
    string Type,
    string Name,
    string Description,
    bool EnabledByDefault,
    Func<WidgetInstanceSettings, IWidget> Create)
{
    /// <summary>Whether the user can add more than one (clocks, countdowns).</summary>
    public bool AllowMultiple { get; init; }

    /// <summary>Builds this widget's settings panel. Null when it has nothing to configure.</summary>
    public Func<WidgetOptionsContext, Control>? BuildOptions { get; init; }

    /// <summary>A short line describing one instance, shown in lists (e.g. "Los Angeles").</summary>
    public Func<WidgetInstanceSettings, string?>? Subtitle { get; init; }
}

/// <summary>
/// Convenience base for widgets that refresh on a timer. Override <see cref="BuildView"/>
/// and <see cref="Refresh"/>; the timer only runs between Start and Stop. Exceptions in
/// Refresh are logged and swallowed so one bad tick never takes the app down.
/// </summary>
public abstract class TimedWidget : IWidget
{
    private readonly DispatcherTimer _timer;
    private Control? _view;
    private int _failures;

    protected TimedWidget(WidgetInstanceSettings settings, TimeSpan interval)
    {
        Settings = settings;
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => SafeRefresh();
    }

    protected WidgetInstanceSettings Settings { get; }

    public Control View => _view ??= BuildView();

    /// <summary>Lets a widget change its refresh rate (e.g. faster near a deadline).</summary>
    protected TimeSpan Interval
    {
        get => _timer.Interval;
        set { if (_timer.Interval != value) _timer.Interval = value; }
    }

    protected abstract Control BuildView();

    /// <summary>Runs on the UI thread every interval. Keep it cheap.</summary>
    protected abstract void Refresh();

    public virtual void Start()
    {
        _ = View;
        SafeRefresh();
        _timer.Start();
    }

    public virtual void Stop() => _timer.Stop();

    public virtual void Dispose() => Stop();

    private void SafeRefresh()
    {
        try
        {
            Refresh();
            _failures = 0;
        }
        catch (Exception ex)
        {
            // Log the first few, then stay quiet; a widget failing every second shouldn't flood the log.
            if (_failures++ < 3) Log.Error($"Widget '{Settings.Type}' refresh failed.", ex);
        }
    }
}
