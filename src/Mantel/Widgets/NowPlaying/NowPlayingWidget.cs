using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mantel.Core;
using Windows.Media.Control;
using Path = Avalonia.Controls.Shapes.Path;

namespace Mantel.Widgets.NowPlaying;

/// <summary>
/// What's playing anywhere on the PC (Spotify, browsers, most players), via Windows' own
/// media session API. Event-driven: it does nothing until the track or play state changes,
/// apart from a 1-second progress tick while something is actually playing.
/// Options: "hideWhenIdle" (default true).
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
    private readonly Border _art = new() { Width = ArtSize, Height = ArtSize, CornerRadius = new CornerRadius(10), ClipToBounds = true };
    private readonly Image _artImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _source = new();
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _progressFill = new() { Width = 0 };
    private readonly Path _playPauseIcon = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Control? _view;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _running;
    private bool _isPlaying;
    private int _mediaVersion; // ignore stale async results when tracks change quickly

    public NowPlayingWidget(WidgetInstanceSettings settings)
    {
        _settings = settings;
        _progressTimer.Tick += (_, _) => UpdateProgress();
    }

    private bool HideWhenIdle => !_settings.Options.TryGetValue("hideWhenIdle", out var v) || v == "true";

    public Control View => _view ??= BuildView();

    private Control BuildView()
    {
        _art.Background = Brush.Parse("#26FFFFFF");
        _art.Child = _artImage;

        _source.Classes.Add("label");
        _title.FontSize = 15;
        _title.FontWeight = FontWeight.SemiBold;
        _title.Foreground = Brush.Parse("#F0ECE4");
        _artist.Classes.Add("muted");

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(-6, 4, 0, 0),
            Children =
            {
                IconButton(PrevIcon, async () => { if (_session is { } s) await s.TrySkipPreviousAsync(); }),
                IconButton(PlayIcon, async () => { if (_session is { } s) await s.TryTogglePlayPauseAsync(); }, _playPauseIcon),
                IconButton(NextIcon, async () => { if (_session is { } s) await s.TrySkipNextAsync(); }),
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
        var track = new Border { Child = _progressFill, Margin = new Thickness(0, 12, 0, 0), Width = Width };
        track.Classes.Add("track");
        track.Height = 4;
        _progressFill.Height = 4;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { _art, text } };
        var root = new StackPanel { Width = Width, Children = { top, track } };
        ShowIdle();
        return root;
    }

    private static Button IconButton(Geometry icon, Func<Task> action, Path? path = null)
    {
        path ??= new Path();
        path.Data = icon;
        path.Fill = Brush.Parse("#F0ECE4");
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

    public void Start()
    {
        _ = View;
        _running = true;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (!_running) return;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            Dispatcher.UIThread.Post(() => Attach(_manager.GetCurrentSession()));
        }
        catch (Exception ex)
        {
            Log.Error("Media session API unavailable.", ex);
            Dispatcher.UIThread.Post(ShowIdle);
        }
    }

    public void Stop()
    {
        _running = false;
        _progressTimer.Stop();
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        Detach();
    }

    public void Dispose() => Stop();

    // WinRT raises these on background threads; everything UI hops to the UI thread.
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() => { if (_running) Attach(sender.GetCurrentSession()); });

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) =>
        Dispatcher.UIThread.Post(() => _ = LoadMediaAsync());

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) =>
        Dispatcher.UIThread.Post(UpdatePlayback);

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) =>
        Dispatcher.UIThread.Post(UpdateProgress);

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        Detach();
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
        _ = LoadMediaAsync();
        UpdatePlayback();
    }

    private void Detach()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
    }

    private async Task LoadMediaAsync()
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
                if (version != _mediaVersion) return;
                _title.Text = string.IsNullOrWhiteSpace(props.Title) ? "Unknown title" : props.Title;
                _artist.Text = string.Join(" · ", new[] { props.Artist, props.AlbumTitle }
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                (_artImage.Source as IDisposable)?.Dispose();
                _artImage.Source = art;
                View.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't read media properties.", ex);
        }
    }

    private void UpdatePlayback()
    {
        var status = _session?.GetPlaybackInfo()?.PlaybackStatus;
        _isPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        _playPauseIcon.Data = _isPlaying ? PauseIcon : PlayIcon;

        if (_isPlaying) _progressTimer.Start(); else _progressTimer.Stop();
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t is null) return;
            var length = t.EndTime - t.StartTime;
            if (length <= TimeSpan.Zero) { _progressFill.Width = 0; return; }

            // Players only report position now and then; extrapolate while playing.
            var position = t.Position - t.StartTime;
            if (_isPlaying) position += DateTimeOffset.Now - t.LastUpdatedTime;
            var fraction = Math.Clamp(position / length, 0, 1);
            _progressFill.Width = Width * fraction;
        }
        catch { /* timeline isn't supported by every player */ }
    }

    private void ShowIdle()
    {
        _mediaVersion++;
        _progressTimer.Stop();
        _source.Text = "NOW PLAYING";
        _title.Text = "Nothing playing";
        _artist.Text = "Play something in Spotify or your browser";
        (_artImage.Source as IDisposable)?.Dispose();
        _artImage.Source = null;
        _playPauseIcon.Data = PlayIcon;
        _progressFill.Width = 0;
        if (_view is not null) _view.IsVisible = !HideWhenIdle;
    }

    private static string SourceName(string appId)
    {
        var id = appId.ToLowerInvariant();
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
        OptionsUi.Panel(OptionsUi.Toggle(ctx, "hideWhenIdle", "Hide when nothing is playing", fallback: true));
}
