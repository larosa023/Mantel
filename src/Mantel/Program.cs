using Avalonia;
using Avalonia.Controls;
using Mantel.Core;
using Velopack;

namespace Mantel;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: Velopack uses this to handle install/update/uninstall
        // hooks and to apply any update that was downloaded last session.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Disable())
            .Run();

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Mantel.SingleInstance", out var isFirst);
        if (!isFirst) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Used by the Avalonia designer as well.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
