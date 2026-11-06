using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Mantel.Core;

namespace Mantel.UI;

/// <summary>A small "Updated to vX" card in the bottom-right corner, shown once after an update.</summary>
public sealed class UpdatedToast : Window
{
    public UpdatedToast(string version)
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

        var title = new TextBlock { Text = $"Mantel updated to {version}", FontSize = 14 };
        var notes = new Button { Content = "What's new" };
        notes.Click += (_, _) => { AppInfo.OpenUrl(AppInfo.ReleaseNotesUrl(version)); Close(); };
        var dismiss = new Button { Content = "Dismiss" };
        dismiss.Click += (_, _) => Close();

        var card = new Border
        {
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    title,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { notes, dismiss } },
                },
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

        // Fade out of the way on its own.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}
