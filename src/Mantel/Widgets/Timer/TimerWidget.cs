using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mantel.Core;
using Mantel.UI;

namespace Mantel.Widgets.Timer;

/// <summary>
/// Stopwatch, countdown timer or Pomodoro, with a ring that empties as time runs down.
/// Options: "mode" ("timer", "stopwatch", "pomodoro"), "minutes" (timer length),
/// "work"/"break" (Pomodoro minutes). Running state lives in the options too, so it survives
/// theme changes and widget rebuilds.
/// </summary>
public sealed class TimerWidget : TimedWidget
{
    private readonly TextBlock _mode = new();
    private readonly TextBlock _time = new();
    private readonly TextBlock _detail = new();
    private readonly Button _startPause = new();
    private readonly Button _reset = new();
    private readonly StackPanel _presets = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Ring _ring = new();

    public TimerWidget(WidgetInstanceSettings settings) : base(settings, TimeSpan.FromSeconds(1)) { }

    private string Mode => Opt("mode", "timer");
    private int Minutes => int.TryParse(Opt("minutes", "10"), out var m) ? Math.Clamp(m, 1, 600) : 10;
    private int WorkMinutes => int.TryParse(Opt("work", "25"), out var m) ? Math.Clamp(m, 1, 180) : 25;
    private int BreakMinutes => int.TryParse(Opt("break", "5"), out var m) ? Math.Clamp(m, 1, 60) : 5;

    // Running state: when it started (UTC ticks, 0 = paused) and time banked before that.
    private long StartedAt { get => long.TryParse(Opt("_started", "0"), out var v) ? v : 0; set => Settings.Options["_started"] = value.ToString(CultureInfo.InvariantCulture); }
    private long BankedMs { get => long.TryParse(Opt("_banked", "0"), out var v) ? v : 0; set => Settings.Options["_banked"] = value.ToString(CultureInfo.InvariantCulture); }
    private bool OnBreak { get => Opt("_break", "false") == "true"; set => Settings.Options["_break"] = value ? "true" : "false"; }
    private int Rounds { get => int.TryParse(Opt("_rounds", "0"), out var v) ? v : 0; set => Settings.Options["_rounds"] = value.ToString(); }

    private bool Running => StartedAt > 0;

    private string Opt(string key, string fallback) =>
        Settings.Options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private TimeSpan Elapsed => TimeSpan.FromMilliseconds(BankedMs) + (Running ? DateTime.UtcNow - new DateTime(StartedAt, DateTimeKind.Utc) : TimeSpan.Zero);

    private TimeSpan Length => Mode switch
    {
        "pomodoro" => TimeSpan.FromMinutes(OnBreak ? BreakMinutes : WorkMinutes),
        "stopwatch" => TimeSpan.Zero,
        _ => TimeSpan.FromMinutes(Minutes),
    };

    protected override Control BuildView()
    {
        _mode.Classes.Add("label");
        _time.Classes.Add("big");
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        _time.VerticalAlignment = VerticalAlignment.Center;
        _detail.Classes.Add("muted");
        _detail.HorizontalAlignment = HorizontalAlignment.Center;

        _ring.Width = 168;
        _ring.Height = 168;
        var dial = new Panel
        {
            Width = 168, Height = 168,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 6),
            Children =
            {
                _ring,
                new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _time, _detail } },
            },
        };

        _startPause.Classes.Add("chip");
        _startPause.Classes.Add("on");
        _startPause.Padding = new Thickness(18, 6);
        _startPause.Click += (_, _) => { if (Running) Pause(); else Start(); };
        _reset.Classes.Add("chip");
        _reset.Padding = new Thickness(14, 6);
        _reset.Content = "Reset";
        _reset.Click += (_, _) => Reset();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _startPause, _reset },
        };

        _presets.HorizontalAlignment = HorizontalAlignment.Center;
        _presets.Margin = new Thickness(0, 10, 0, 0);
        foreach (var m in new[] { 1, 5, 10, 15, 25, 45 })
        {
            var b = new Button { Content = $"{m}m", Padding = new Thickness(8, 3) };
            b.Classes.Add("chip");
            var minutes = m;
            b.Click += (_, _) =>
            {
                Settings.Options["minutes"] = minutes.ToString();
                Reset();
                Start();
            };
            _presets.Children.Add(b);
        }

        return new StackPanel { Width = WidgetCard.ContentWidth, Children = { _mode, dial, buttons, _presets } };
    }

    private void Start()
    {
        StartedAt = DateTime.UtcNow.Ticks;
        Refresh();
    }

    private void Pause()
    {
        BankedMs = (long)Elapsed.TotalMilliseconds;
        StartedAt = 0;
        Refresh();
    }

    private void Reset()
    {
        StartedAt = 0;
        BankedMs = 0;
        if (Mode == "pomodoro") { OnBreak = false; Rounds = 0; }
        Refresh();
    }

    protected override void Refresh()
    {
        var elapsed = Elapsed;
        var length = Length;
        _presets.IsVisible = Mode == "timer";

        if (Mode == "stopwatch")
        {
            _mode.Text = "STOPWATCH";
            _time.Text = Format(elapsed, showTenths: false);
            _detail.Text = Running ? "Running" : elapsed > TimeSpan.Zero ? "Paused" : "Ready";
            // The ring sweeps once a minute so a stopwatch still feels alive.
            _ring.Progress = elapsed.TotalSeconds % 60 / 60;
        }
        else
        {
            var left = length - elapsed;
            if (left <= TimeSpan.Zero && Running) { Finished(); return; }
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;

            _mode.Text = Mode == "pomodoro"
                ? (OnBreak ? "POMODORO · BREAK" : $"POMODORO · FOCUS {Rounds + 1}")
                : "TIMER";
            _time.Text = Format(left, showTenths: false);
            _detail.Text = Running
                ? $"Ends {DateTime.Now + left:h:mm tt}"
                : elapsed > TimeSpan.Zero ? "Paused" : $"{(int)length.TotalMinutes} min";
            _ring.Progress = length.TotalMilliseconds <= 0 ? 0 : left.TotalMilliseconds / length.TotalMilliseconds;
        }

        // Tick every second only while it's running; idle, it barely wakes.
        Interval = Running ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30);

        _startPause.Content = Running ? "Pause" : elapsed > TimeSpan.Zero ? "Resume" : "Start";
        _reset.IsVisible = elapsed > TimeSpan.Zero || Running;
    }

    private void Finished()
    {
        StartedAt = 0;
        BankedMs = 0;
        if (Mode == "pomodoro")
        {
            var wasBreak = OnBreak;
            if (!wasBreak) Rounds++;
            OnBreak = !wasBreak;
            Notice.Show(wasBreak ? "Break's over" : "Time for a break",
                wasBreak ? $"Focus round {Rounds + 1} is starting." : $"Nice work. {BreakMinutes} minutes to stretch.");
            Start(); // Pomodoro rolls straight into the next phase
            SettingsStore.Current?.Save();
        }
        else
        {
            Notice.Show("Timer done", $"{Minutes} minute{(Minutes == 1 ? "" : "s")} are up.");
            SettingsStore.Current?.Save();
            Refresh();
        }
    }

    private static string Format(TimeSpan t, bool showTenths) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    public override void Stop()
    {
        base.Stop();
        SettingsStore.Current?.Save(); // keep the running state across restarts
    }

    public static Control BuildOptions(WidgetOptionsContext ctx) =>
        OptionsUi.Panel(
            OptionsUi.Choice(ctx, "mode", "Mode", new[] { ("timer", "Timer"), ("stopwatch", "Stopwatch"), ("pomodoro", "Pomodoro") }, "timer"),
            OptionsUi.Text(ctx, "minutes", "Timer length (minutes)", "10"),
            OptionsUi.Text(ctx, "work", "Pomodoro focus (minutes)", "25"),
            OptionsUi.Text(ctx, "break", "Pomodoro break (minutes)", "5"));

    public static string? Subtitle(WidgetInstanceSettings s) =>
        (s.Options.TryGetValue("mode", out var m) ? m : "timer") switch
        {
            "stopwatch" => "Stopwatch",
            "pomodoro" => "Pomodoro",
            _ => $"{(s.Options.TryGetValue("minutes", out var min) ? min : "10")} minute timer",
        };

    /// <summary>A circular track with an accent arc for the time left.</summary>
    private sealed class Ring : Control
    {
        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = Math.Clamp(value, 0, 1); InvalidateVisual(); }
        }

        public override void Render(DrawingContext context)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            const double thickness = 7;
            var r = size / 2 - thickness;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

            var track = (this.FindResource("Mantel.Track") as IBrush) ?? Brushes.Gray;
            var accent = (this.FindResource("Mantel.Accent") as IBrush) ?? Brushes.Orange;
            context.DrawEllipse(null, new Pen(track, thickness), center, r, r);

            if (_progress <= 0) return;
            if (_progress >= 0.999)
            {
                context.DrawEllipse(null, new Pen(accent, thickness, lineCap: PenLineCap.Round), center, r, r);
                return;
            }
            // Arc clockwise from 12 o'clock.
            var end = -Math.PI / 2 + _progress * Math.PI * 2;
            var start = new Point(center.X, center.Y - r);
            var stop = new Point(center.X + r * Math.Cos(end), center.Y + r * Math.Sin(end));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(start, false);
                g.ArcTo(stop, new Size(r, r), 0, _progress > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(accent, thickness, lineCap: PenLineCap.Round), geometry);
        }
    }
}
