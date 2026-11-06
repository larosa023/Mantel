using System.Diagnostics;
using System.Reflection;

namespace Mantel.Core;

public static class AppInfo
{
    public const string RepoUrl = "https://github.com/larosa023/Mantel";

    /// <summary>Version from the build, e.g. "1.2.0" or "1.3.0-beta.1".</summary>
    public static string Version { get; } = ReadVersion();

    public static string ReleaseNotesUrl(string version) => $"{RepoUrl}/releases/tag/v{version}";

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser configured; nothing useful to do */ }
    }

    private static string ReadVersion()
    {
        var info = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "0.0.0";
        var plus = info.IndexOf('+'); // drop the "+commitsha" suffix the SDK appends
        return plus >= 0 ? info[..plus] : info;
    }
}
