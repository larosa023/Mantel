using System.Diagnostics;
using Avalonia.Threading;
using Mantel.Core;
using Velopack;
using Velopack.Sources;

namespace Mantel.Updates;

public enum UpdateState
{
    /// <summary>Running from a dev build (not installed); updates are off.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpToDate,
    Downloading,
    /// <summary>Downloaded and waiting to be applied.</summary>
    ReadyToRestart,
    Failed,
}

/// <summary>
/// Seamless updates via Velopack + GitHub Releases:
///  1. Check shortly after launch, then every few hours.
///  2. Download in the background (delta packages, usually tiny).
///  3. Apply the moment it's invisible to the user: when the PC has been idle for a while,
///     when Mantel quits, or on next launch. Widgets come back exactly where they were.
/// No admin rights are ever needed because Mantel installs per-user.
/// </summary>
public sealed class UpdateService
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan IdleBeforeApply = TimeSpan.FromMinutes(10);

    private readonly MantelSettings _settings;
    private readonly UpdateManager _stable;
    private readonly DispatcherTimer _checkTimer;
    private readonly DispatcherTimer _idleTimer;
    private UpdateManager? _pendingManager;
    private UpdateInfo? _pending;
    private bool _busy;

    public event Action? StateChanged;

    public UpdateState State { get; private set; }

    /// <summary>The version waiting to be applied, if any.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public string? LastError { get; private set; }

    public DateTime? LastChecked { get; private set; }

    public UpdateService(MantelSettings settings)
    {
        _settings = settings;
        _stable = CreateManager(beta: false);
        State = _stable.IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled;

        _checkTimer = new DispatcherTimer { Interval = FirstCheckDelay };
        _checkTimer.Tick += async (_, _) =>
        {
            _checkTimer.Interval = CheckInterval;
            if (_settings.AutoUpdate) await CheckAndDownloadAsync();
        };

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _idleTimer.Tick += (_, _) =>
        {
            if (State == UpdateState.ReadyToRestart && NativeMethods.GetIdleTime() >= IdleBeforeApply)
                RestartAndApply();
        };
    }

    public bool IsInstalled => _stable.IsInstalled;

    public void Start()
    {
        if (!IsInstalled) return;
        _checkTimer.Start();
        _idleTimer.Start();
    }

    /// <summary>Checks for an update and, if one exists, downloads it. Safe to call any time.</summary>
    public async Task CheckAndDownloadAsync()
    {
        if (!IsInstalled || _busy || State == UpdateState.ReadyToRestart) return;
        _busy = true;
        try
        {
            SetState(UpdateState.Checking);
            LastError = null;

            var (manager, info) = await FindBestUpdateAsync();
            LastChecked = DateTime.Now;

            if (manager is null || info is null)
            {
                SetState(UpdateState.UpToDate);
                return;
            }

            SetState(UpdateState.Downloading);
            await manager.DownloadUpdatesAsync(info);

            _pendingManager = manager;
            _pending = info;
            SetState(UpdateState.ReadyToRestart);
        }
        catch (Exception ex)
        {
            // Offline, GitHub down, rate-limited... try again next interval. Never bother the user.
            Log.Error("Update check failed.", ex);
            LastError = ex.Message;
            SetState(UpdateState.Failed);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Restarts Mantel into the downloaded version right now.</summary>
    public void RestartAndApply()
    {
        if (_pendingManager is null || _pending is null) return;
        Log.Info($"Restarting to apply update {PendingVersion}.");
        _pendingManager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    /// <summary>
    /// Called when the user quits Mantel. If an update is downloaded, it's applied silently
    /// after exit, without relaunching, so the next start is already on the new version.
    /// </summary>
    public void ApplyOnExitIfPending()
    {
        if (_pendingManager is null || _pending is null) return;
        try { _pendingManager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false); }
        catch (Exception ex) { Log.Error("Could not stage update on exit.", ex); }
    }

    /// <summary>
    /// Stable users check the stable channel. Beta users check both and take whichever is
    /// newer, so they still get stable releases that ship after a beta.
    /// </summary>
    private async Task<(UpdateManager? Manager, UpdateInfo? Info)> FindBestUpdateAsync()
    {
        var current = _stable.CurrentVersion;
        var candidates = new List<(UpdateManager, UpdateInfo)>();

        var stableInfo = await _stable.CheckForUpdatesAsync();
        if (stableInfo is not null) candidates.Add((_stable, stableInfo));

        if (_settings.BetaUpdates)
        {
            var beta = CreateManager(beta: true);
            var betaInfo = await beta.CheckForUpdatesAsync();
            if (betaInfo is not null) candidates.Add((beta, betaInfo));
        }

        var best = candidates
            .Where(c => current is null || c.Item2.TargetFullRelease.Version > current)
            .OrderByDescending(c => c.Item2.TargetFullRelease.Version)
            .FirstOrDefault();

        return best == default ? (null, null) : best;
    }

    private static UpdateManager CreateManager(bool beta)
    {
        var source = new GithubSource(AppInfo.RepoUrl, accessToken: null, prerelease: beta);
        // Stable uses Velopack's default Windows channel; betas are packed to "beta".
        var options = beta ? new UpdateOptions { ExplicitChannel = "beta" } : null;
        return new UpdateManager(source, options);
    }

    private void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
