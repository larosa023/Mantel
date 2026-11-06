using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Mantel.Core;

namespace Mantel.UI;

/// <summary>A small notice card in the bottom-right corner ("Updated to vX", "everything is off").</summary>
public sealed class UpdatedToast : Window
{
    public UpdatedToast(string title, string message, Action openSettings, string? notesUrl = null)
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WidgetCard.ApplyFont(this);

        var heading = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold };
        var hint = new TextBlock { Text = message, MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
        hint.Classes.Add("muted");

        var settings = new Button { Content = "Open settings" };
        settings.Classes.Add("chip");
        settings.Classes.Add("on");
        settings.Click += (_, _) => { openSettings(); Close(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { settings } };
        if (notesUrl is not null)
        {
            var notes = new Button { Content = "What's new" };
            notes.Classes.Add("chip");
            notes.Click += (_, _) => { AppInfo.OpenUrl(notesUrl); Close(); };
            buttons.Children.Add(notes);
        }
        var dismiss = new Button { Content = "Dismiss" };
        dismiss.Classes.Add("chip");
        dismiss.Click += (_, _) => Close();
        buttons.Children.Add(dismiss);

        var card = new Border
        {
            Child = new StackPanel
            {
                Spacing = 12,
                Children = { new StackPanel { Spacing = 3, Children = { heading, hint } }, buttons },
            },
        };
        card.Classes.Add("card");
        Content = card;

        Opened += (_, _) =>
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen is null) return;
            var area = screen.WorkingArea;
            var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
            Position = new PixelPoint(area.Right - size.Width - 8, area.Bottom - size.Height - 8);
        };

        // Gets out of the way on its own.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}
