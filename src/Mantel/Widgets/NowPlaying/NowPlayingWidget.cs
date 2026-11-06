using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel.Core;
using Mantel.Integrations;
using Windows.Media.Control;
using Path = Avalonia.Controls.Shapes.Path;

namespace Mantel.Widgets.NowPlaying;

/// <summary>
/// What's playing. With Amplite running it talks to Amplite directly: EQ presets, volume on
/// scroll, click-to-seek and Amplite's own song info. Otherwise it reads Windows' media
/// session (Spotify, browsers, most players). Event-driven; the only timer is a 1-second
/// progress tick while something is actually playing.
/// Options: "hideWhenIdle" (default true), "showEq" (default true).
/// </summary>
public sealed class NowPlayingWidget : IWidget
{
    private const double ArtSize = 64;
    private const double Width = 300;

    private static readonly Geometry PrevIcon = Geometry.Parse("M6 6h2v12H6z M9.5 12l8.5 6V6z");
    private static readonly Geometry NextIcon = Geometry.Parse("M6 18l8.5-6L6 6v12z M16 6v12h2V6h-2z");
    private static readonly Geometry PlayIcon = Geometry.Parse("M8 5v14l11-7z");
    private static readonly Geometry PauseIcon = Geometry.Parse("M6 19h4V5H6v14z M14 5v14h4V5h-4z");

    private readonly WidgetInstanceSettings _settings;
    private readonly AmpliteClient _amplite = AmpliteClient.Shared;
    private readonly Border _art = new() { Width = ArtSize, Height = ArtSize, CornerRadius = new CornerRadius(10), ClipToBounds = true };
    private readonly Image _artImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _source = new();
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _volume = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _progressTrack = new();
    private readonly Border _progressFill = new() { Width = 0 };
    private readonly Path _playPauseIcon = new();
    private readonly Button _eqButton = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _volumeHide = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private Control? _view;

    // Windows media session (fallback when Amplite isn't running)
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private bool _running;
    private bool _isPlaying;
    private bool _usingAmplite;
    private string? _ampliteTrackUri;
    private int _mediaVersion; // ignore stale async results when tracks change quickly

    public NowPlayingWidget(WidgetInstanceSettings settings)
    {
        _settings = settings;
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _volumeHide.Tick += (_, _) => { _volumeHide.Stop(); _volume.IsVisible = false; };
    }

    private bool HideWhenIdle => !_settings.Options.TryGetValue("hideWhenIdle", out var v) || v == "true";
    private bool ShowEq => !_settings.Options.TryGetValue("showEq", out var v) || v == "true";

    public Control View => _view ??= BuildView();

    private Control BuildView()
    {
        _art.Classes.Add("placeholder");
        _art.Child = _artImage;

        _source.Classes.Add("label");
        _title.Classes.Add("title");
        _artist.Classes.Add("muted");
        _volume.Classes.Add("label");

        _eqButton.Classes.Add("chip");
        _eqButton.IsVisible = false;
        _eqButton.VerticalAlignment = VerticalAlignment.Center;
        _eqButton.Margin = new Thickness(6, 0, 0, 0);
        _eqButton.Click += (_, _) => ShowEqMenu();

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(-6, 4, 0, 0),
            Children =
            {
                IconButton(PrevIcon, () => Command("prev")),
                IconButton(PlayIcon, () => Command("toggle"), _playPauseIcon),
                IconButton(NextIcon, () => Command("next")),
                _eqButton,
                _volume,
            },
        };

        var text = new StackPanel
        {
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Width = Width - ArtSize - 14,
            Children = { _source, _title, _artist, controls },
        };

        _progressFill.Classes.Add("fill");
        _progressFill.Height = 4;
        _progressTrack.Classes.Add("track");
        _progressTrack.Child = _progressFill;
        _progressTrack.Height = 4;
        _progressTrack.Width = Width;
        // A taller invisible hit area makes the thin bar easy to click for seeking.
        var seekArea = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 8, 0, 4),
            Child = _progressTrack,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        seekArea.PointerPressed += OnSeek;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { _art, text } };
        var root = new StackPanel { Width = Width, Background = Brushes.Transparent, Children = { top, seekArea } };
        root.PointerWheelChanged += OnWheel;
        ShowIdle();
        return root;
    }

    private static Button IconButton(Geometry icon, Func<Task> action, Path? path = null)
    {
        path ??= new Path();
        path.Data = icon;
        path.Width = 16;
        path.Height = 16;
        path.Stretch = Stretch.Uniform;

        var button = new Button { Content = path, Padding = new Thickness(7) };
        button.Classes.Add("icon");
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { Log.Error("Media control failed.", ex); }
        };
        return button;
    }

    // --- Lifecycle -------------------------------------------------------------------

    public void Start()
    {
        _ = View;
        _running = true;
        _amplite.Changed += OnAmpliteChanged;
        _amplite.Acquire();
        _ = InitWindowsSessionAsync();
    }

    public void Stop()
    {
        _running = false;
        _progressTimer.Stop();
        _amplite.Changed -= OnAmpliteChanged;
        _amplite.Release();
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        DetachSession();
    }

    public void Dispose() => Stop();

    // --- Commands (go to whichever source is active) -----------------------------

    private async Task Command(string cmd)
    {
        if (_usingAmplite) { await _amplite.SendAsync(cmd); return; }
        if (_session is not { } s) return;
        switch (cmd)
        {
            case "toggle": await s.TryTogglePlayPauseAsync(); break;
            case "next": await s.TrySkipNextAsync(); break;
            case "prev": await s.TrySkipPreviousAsync(); break;
        }
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_usingAmplite) return; // Windows' media session has no volume control
        var step = e.Delta.Y > 0 ? 5 : -5;
        _ = _amplite.SendAsync($"vol:{(step > 0 ? "+" : "")}{step}");
        var now = Math.Clamp((_amplite.State?.Volume ?? 50) + step, 0, 100);
        _volume.Text = $"VOL {now}";
        _volume.IsVisible = true;
        _volumeHide.Stop();
        _volumeHide.Start();
        e.Handled = true;
    }

    private void OnSeek(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true; // don't start a window drag from the progress bar
        if (!_usingAmplite || _amplite.State is not { Track: { DurationMs: > 0 } track } state) return;
        var x = e.GetPosition(_progressTrack).X;
        var target = (long)(Math.Clamp(x / _progressTrack.Bounds.Width, 0, 1) * track.DurationMs);
        var delta = target - state.PositionNowMs;
        _ = _amplite.SendAsync($"seek:{(delta >= 0 ? "+" : "")}{delta}");
    }

    private void ShowEqMenu()
    {
        if (_amplite.State?.Eq is not { } eq) return;
        var menu = new MenuFlyout();

        var power = new MenuItem { Header = eq.On ? "Turn EQ off" : "Turn EQ on" };
        power.Click += (_, _) => _ = _amplite.SendAsync(eq.On ? "eq:off" : "eq:on");
        menu.Items.Add(power);
        menu.Items.Add(new Separator());

        foreach (var p in eq.Presets)
        {
            var item = new MenuItem
            {
                Header = p.Name,
                Icon = eq.On && p.Id == eq.Preset ? new TextBlock { Text = "✓" } : null,
            };
            var id = p.Id;
            item.Click += (_, _) => _ = _amplite.SendAsync($"eq:{id}");
            menu.Items.Add(item);
        }
        menu.ShowAt(_eqButton);
    }

    // --- Amplite ----------------------------------------------------------------------

    private void OnAmpliteChanged()
    {
        if (!_running) return;
        var state = _amplite.State;
        if (state?.Track is { } track)
        {
            _usingAmplite = true;
            _source.Text = "AMPLITE";
            _source.Classes.Set("accent", true);

            if (track.Uri != _ampliteTrackUri)
            {
                _ampliteTrackUri = track.Uri;
                var version = ++_mediaVersion;
                _title.Text = string.IsNullOrWhiteSpace(track.Title) ? "Unknown title" : track.Title;
                _artist.Text = string.Join(" · ", new[] { track.Artist, track.Album }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                _ = LoadAmpliteArtAsync(track.Image, version);
            }

            SetPlaying(state.Playing);
            UpdateEqChip(state.Eq);
            View.IsVisible = true;
            return;
        }

        // Amplite stopped or has nothing loaded: hand back to Windows' media session.
        if (_usingAmplite)
        {
            _usingAmplite = false;
            _ampliteTrackUri = null;
            _source.Classes.Set("accent", false);
            _eqButton.IsVisible = false;
            var current = _session;
            _session = null;
            AttachSession(current ?? _manager?.GetCurrentSession());
        }
    }

    private async Task LoadAmpliteArtAsync(string? url, int version)
    {
        var art = await _amplite.GetArtAsync(url, (int)(ArtSize * 2));
        if (version != _mediaVersion) return;
        _artImage.Source = art; // cached by the client; don't dispose
    }

    private void UpdateEqChip(AmpliteEq? eq)
    {
        var show = ShowEq && eq is { Available: true } && eq.Presets.Count > 0;
        _eqButton.IsVisible = show;
        if (!show) return;
        _eqButton.Content = eq!.On ? $"EQ · {eq.PresetName}" : "EQ off";
        _eqButton.Classes.Set("on", eq.On);
    }

    // --- Windows media session (fallback) -------------------------------------------

    private async Task InitWindowsSessionAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (!_running) return;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            Dispatcher.UIThread.Post(() => { if (!_usingAmplite) AttachSession(_manager.GetCurrentSession()); });
        }
        catch (Exception ex)
        {
            Log.Error("Media session API unavailable.", ex);
            Dispatcher.UIThread.Post(() => { if (!_usingAmplite) ShowIdle(); });
        }
    }

    // WinRT raises these on background threads; everything UI hops to the UI thread.
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() => { if (_running && !_usingAmplite) AttachSession(sender.GetCurrentSession()); });

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) =>
        Dispatcher.UIThread.Post(() => { if (!_usingAmplite) _ = LoadSessionMediaAsync(); });

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) =>
        Dispatcher.UIThread.Post(() => { if (!_usingAmplite) UpdateSessionPlayback(); });

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) =>
        Dispatcher.UIThread.Post(() => { if (!_usingAmplite) UpdateProgress(); });

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _session) && session is not null) return;
        DetachSession();
        _session = session;

        if (session is null)
        {
            ShowIdle();
            return;
        }

        session.MediaPropertiesChanged += OnMediaChanged;
        session.PlaybackInfoChanged += OnPlaybackChanged;
        session.TimelinePropertiesChanged += OnTimelineChanged;
        _source.Text = SourceName(session.SourceAppUserModelId);
        _ = LoadSessionMediaAsync();
        UpdateSessionPlayback();
    }

    private void DetachSession()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
    }

    private async Task LoadSessionMediaAsync()
    {
        var session = _session;
        if (session is null) return;
        var version = ++_mediaVersion;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _mediaVersion || props is null) return;

            Bitmap? art = null;
            if (props.Thumbnail is { } thumb)
            {
                using var ras = await thumb.OpenReadAsync();
                await using var stream = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                art = Bitmap.DecodeToWidth(ms, (int)(ArtSize * 2));
            }
            if (version != _mediaVersion) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (version != _mediaVersion || _usingAmplite) return;
                _title.Text = string.IsNullOrWhiteSpace(props.Title) ? "Unknown title" : props.Title;
                _artist.Text = string.Join(" · ", new[] { props.Artist, props.AlbumTitle }
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                _artImage.Source = art;
                View.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't read media properties.", ex);
        }
    }

    private void UpdateSessionPlayback()
    {
        var status = _session?.GetPlaybackInfo()?.PlaybackStatus;
        SetPlaying(status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }

    // --- Shared display --------------------------------------------------------------

    private void SetPlaying(bool playing)
    {
        _isPlaying = playing;
        _playPauseIcon.Data = playing ? PauseIcon : PlayIcon;
        if (playing) _progressTimer.Start(); else _progressTimer.Stop();
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        try
        {
            double fraction;
            if (_usingAmplite)
            {
                if (_amplite.State is not { Track: { DurationMs: > 0 } track } state) { _progressFill.Width = 0; return; }
                fraction = (double)state.PositionNowMs / track.DurationMs;
            }
            else
            {
                var t = _session?.GetTimelineProperties();
                if (t is null) return;
                var length = t.EndTime - t.StartTime;
                if (length <= TimeSpan.Zero) { _progressFill.Width = 0; return; }
                // Players only report position now and then; extrapolate while playing.
                var position = t.Position - t.StartTime;
                if (_isPlaying) position += DateTimeOffset.Now - t.LastUpdatedTime;
                fraction = position / length;
            }
            _progressFill.Width = Width * Math.Clamp(fraction, 0, 1);
        }
        catch { /* timeline isn't supported by every player */ }
    }

    private void ShowIdle()
    {
        _mediaVersion++;
        _progressTimer.Stop();
        _source.Text = "NOW PLAYING";
        _title.Text = "Nothing playing";
        _artist.Text = "Play something in Amplite, Spotify or your browser";
        _artImage.Source = null;
        _playPauseIcon.Data = PlayIcon;
        _progressFill.Width = 0;
        _eqButton.IsVisible = false;
        if (_view is not null) _view.IsVisible = !HideWhenIdle;
    }

    private static string SourceName(string appId)
    {
        var id = appId.ToLowerInvariant();
        if (id.Contains("amplite")) return "AMPLITE";
        if (id.Contains("spotify")) return "SPOTIFY";
        if (id.Contains("chrome")) return "CHROME";
        if (id.Contains("msedge")) return "EDGE";
        if (id.Contains("firefox")) return "FIREFOX";
        if (id.Contains("zunemusic") || id.Contains("media player")) return "MEDIA PLAYER";
        if (id.Contains("applemusic") || id.Contains("itunes")) return "APPLE MUSIC";
        if (id.Contains("vlc")) return "VLC";
        var name = appId.Split('!', '\\').Last();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return string.IsNullOrWhiteSpace(name) ? "NOW PLAYING" : name.ToUpperInvariant();
    }

    public static Control BuildOptions(WidgetOptionsContext ctx) =>
        OptionsUi.Panel(
            OptionsUi.Toggle(ctx, "hideWhenIdle", "Hide when nothing is playing", fallback: true),
            OptionsUi.Toggle(ctx, "showEq", "Show Amplite's EQ presets", fallback: true));
}
