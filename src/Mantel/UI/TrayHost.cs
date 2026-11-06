using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Mantel.UI;

/// <summary>
/// The one collection of tray icons Mantel owns: its main icon plus one per flyout that asks for
/// it. Removing an icon from <see cref="Icons"/> disposes it, which takes it off the taskbar.
/// </summary>
public static class TrayHost
{
    public static TrayIcons Icons { get; } = new();

    public static void Attach(Application app) => TrayIcon.SetIcons(app, Icons);

    public static WindowIcon LoadIcon(string name)
    {
        var file = name switch
        {
            "music" => "flyout-music.ico",
            "scores" => "flyout-scores.ico",
            "glance" => "flyout-glance.ico",
            _ => "mantel.ico",
        };
        return new WindowIcon(AssetLoader.Open(new Uri($"avares://Mantel/Assets/{file}")));
    }
}
