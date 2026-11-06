using Avalonia.Controls;
using Avalonia.Threading;

namespace Mantel.Core;

/// <summary>
/// A widget's contract with the host. The host only creates a widget when it is
/// enabled, and disposes it when it is turned off, so a disabled widget costs nothing.
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
    Func<WidgetInstanceSettings, IWidget> Create);

/// <summary>
/// Convenience base for widgets that refresh on a timer. Override <see cref="BuildView"/>
/// and <see cref="Refresh"/>; the timer only runs between Start and Stop.
/// </summary>
public abstract class TimedWidget : IWidget
{
    private readonly DispatcherTimer _timer;
    private Control? _view;

    protected TimedWidget(WidgetInstanceSettings settings, TimeSpan interval)
    {
        Settings = settings;
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Refresh();
    }

    protected WidgetInstanceSettings Settings { get; }

    public Control View => _view ??= BuildView();

    protected abstract Control BuildView();

    /// <summary>Runs on the UI thread every interval. Keep it cheap.</summary>
    protected abstract void Refresh();

    public virtual void Start()
    {
        _ = View;
        Refresh();
        _timer.Start();
    }

    public virtual void Stop() => _timer.Stop();

    public virtual void Dispose() => Stop();
}
