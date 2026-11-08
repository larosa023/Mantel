using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mantel.UI;

/// <summary>A small card that pops up in the bottom-right corner with a chime ("Timer done").</summary>
public static class Notice
{
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    private const uint MB_ICONASTERISK = 0x40;

    public static void Show(string title, string message, bool sound = true)
    {
        try
        {
            if (sound && OperatingSystem.IsWindows()) MessageBeep(MB_ICONASTERISK);

            var window = new Window
            {
                SystemDecorations = SystemDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                CanResize = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            WidgetCard.ApplyFont(window);

            var heading = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold };
            var body = new TextBlock { Text = message, MaxWidth = 300, TextWrapping = TextWrapping.Wrap };
            body.Classes.Add("muted");
            var dismiss = new Button { Content = "Dismiss" };
            dismiss.Classes.Add("chip");
            dismiss.Click += (_, _) => window.Close();

            var card = new Border
            {
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                Icons.Create(Icons.Timer, 22, accent: true),
                                new StackPanel { Spacing = 2, Children = { heading, body } },
                            },
                        },
                        dismiss,
                    },
                },
            };
            card.Classes.Add("card");
            window.Content = card;

            window.Opened += (_, _) =>
            {
                var screen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
                if (screen is null) return;
                var area = screen.WorkingArea;
                var size = PixelSize.FromSize(window.Bounds.Size, screen.Scaling);
                window.Position = new PixelPoint(area.Right - size.Width - 8, area.Bottom - size.Height - 8);
            };
            window.Show();
            DispatcherTimer.RunOnce(() => window.Close(), TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Mantel.Core.Log.Error("Couldn't show a notice.", ex);
        }
    }
}
