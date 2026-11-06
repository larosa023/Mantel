using System.Diagnostics;
using Mantel.Core;

namespace Mantel.Integrations;

/// <summary>
/// Opens Fullcourt pages either in the browser or in the Fullcourt app, meaning the site
/// installed as an app from Edge or Chrome ("Install Fullcourt"). The installed app is found
/// through its Start menu shortcut, which carries the browser's --app-id for it.
/// </summary>
public static class FullcourtLauncher
{
    public sealed record InstalledApp(string Shortcut, string Target, string Arguments);

    private static InstalledApp? _cached;
    private static DateTime _cachedAt;

    /// <summary>Opens <paramref name="url"/>. mode: "web" (default browser) or "app".</summary>
    public static void Open(string url, string mode)
    {
        if (mode == "app" && TryOpenInApp(url)) return;
        AppInfo.OpenUrl(url);
    }

    /// <summary>The installed Fullcourt app, if there is one (looked up at most once a minute).</summary>
    public static InstalledApp? FindInstalledApp()
    {
        if (DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1)) return _cached;
        _cachedAt = DateTime.UtcNow;
        _cached = null;
        if (!OperatingSystem.IsWindows()) return null;

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var link in links.Where(l => Path.GetFileNameWithoutExtension(l).Contains("Fullcourt", StringComparison.OrdinalIgnoreCase)))
            {
                if (ReadShortcut(link) is { } app && app.Arguments.Contains("--app-id=", StringComparison.OrdinalIgnoreCase))
                    return _cached = app;
            }
        }
        return null;
    }

    private static bool TryOpenInApp(string url)
    {
        try
        {
            // 1. The installed app: same shortcut Windows uses, told to open this page.
            if (FindInstalledApp() is { } app)
            {
                Process.Start(new ProcessStartInfo(app.Target, $"{app.Arguments} --app-launch-url-for-shortcuts-menu-item=\"{url}\"")
                {
                    UseShellExecute = false,
                });
                return true;
            }

            // 2. Not installed: an app-style window (no tabs or address bar) in Edge, which every Windows PC has.
            if (FindEdge() is { } edge)
            {
                Process.Start(new ProcessStartInfo(edge, $"--app=\"{url}\"") { UseShellExecute = false });
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't open Fullcourt as an app; using the browser.", ex);
        }
        return false;
    }

    private static string? FindEdge()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        }.Select(root => Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Reads a .lnk's target and arguments through Windows' own shortcut object.</summary>
    private static InstalledApp? ReadShortcut(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(path);
            string target = shortcut.TargetPath;
            string arguments = shortcut.Arguments;
            return string.IsNullOrEmpty(target) ? null : new InstalledApp(path, target, arguments ?? "");
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't read shortcut {path}.", ex);
            return null;
        }
    }
}
