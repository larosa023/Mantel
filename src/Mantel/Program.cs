using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Mantel.Core;
using Velopack;

namespace Mantel;

internal static class Program
{
    private const string ShowSettingsEventName = @"Local\Mantel.ShowSettings";

    private static Mutex? _singleInstance;
    private static EventWaitHandle? _showSettings;

    /// <summary>Set by the app once it's running; called when someone launches Mantel again.</summary>
    public static Action? ShowSettingsRequested { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: Velopack uses this to handle install/update/uninstall
        // hooks and to apply any update that was downloaded last session.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Disable())
            .Run();

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Mantel.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            // Mantel is already running (it lives in the tray). Opening it again from the Start
            // menu or a shortcut should do something visible: bring up its Settings window.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var existing))
                    using (existing) existing.Set();
            }
            catch { /* nothing useful to do */ }
            return;
        }

        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        var listener = new Thread(() =>
        {
            while (_showSettings.WaitOne())
                Dispatcher.UIThread.Post(() => ShowSettingsRequested?.Invoke());
        })
        { IsBackground = true, Name = "Mantel second-launch listener" };
        listener.Start();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Info($"Process exiting (exit code {Environment.ExitCode}).");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Error("Mantel crashed.", ex);
            throw;
        }
    }

    // Used by the Avalonia designer as well.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
