using System.Diagnostics;
using System.Text.Json;

namespace Mantel.Core;

/// <summary>
/// Loads and saves <see cref="MantelSettings"/>.
/// Lives in %AppData%\Mantel, not the install folder: Velopack replaces the
/// install folder on every update, so nothing user-owned may live there.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mantel");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public MantelSettings Settings { get; }

    /// <summary>True when no settings file existed (first launch on this PC).</summary>
    public bool IsFirstRun { get; }

    private SettingsStore(MantelSettings settings, bool isFirstRun)
    {
        Settings = settings;
        IsFirstRun = isFirstRun;
    }

    private bool _inMemory;

    /// <summary>A store that never touches disk (used to render screenshots in CI).</summary>
    public static SettingsStore InMemory(MantelSettings settings) => new(settings, isFirstRun: false) { _inMemory = true };

    /// <summary>The app's store, for widgets that save their own content (Notes).</summary>
    public static SettingsStore? Current { get; private set; }

    public static SettingsStore Load() => Current = LoadFromDisk();

    private static SettingsStore LoadFromDisk()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<MantelSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null) return new SettingsStore(loaded, isFirstRun: false);
            }
        }
        catch (Exception ex)
        {
            // A corrupt file shouldn't stop the app from starting. Keep a copy for debugging.
            Trace.WriteLine($"Mantel: could not read settings, starting fresh. {ex.Message}");
            TryBackupCorruptFile();
        }

        return new SettingsStore(new MantelSettings(), isFirstRun: true);
    }

    public void Save()
    {
        if (_inMemory) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true); // atomic replace: no half-written files
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Mantel: could not save settings. {ex.Message}");
        }
    }

    private static void TryBackupCorruptFile()
    {
        try { File.Copy(FilePath, FilePath + ".corrupt", overwrite: true); }
        catch { /* best effort */ }
    }
}
