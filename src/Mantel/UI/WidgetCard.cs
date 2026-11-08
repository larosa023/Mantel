using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mantel.Theme;

namespace Mantel.UI;

/// <summary>
/// Wraps a widget's view in the themed card, on the desktop and in flyouts alike.
/// Colors and shapes come from styles; the few things styles can't do live here.
/// </summary>
public static class WidgetCard
{
    /// <summary>Every widget's content is this wide, so cards line up on the desktop and in flyouts.</summary>
    public const double ContentWidth = 300;

    public static Border Create(Control view, ContextMenu? menu = null)
    {
        var card = new Border { Child = view, ContextMenu = menu, IsVisible = view.IsVisible };
        card.Classes.Add("card");

        // Fade in when it first appears (startup, turning it on, theme changes) instead of popping.
        card.Opacity = 0;
        card.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(280),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        };
        card.AttachedToVisualTree += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => card.Opacity = 1, Avalonia.Threading.DispatcherPriority.Render);

        // Bare has no card behind the text, so give the text a soft shadow to stay readable.
        if (ThemeManager.Current.Bare)
        {
            view.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                OffsetX = 0,
                OffsetY = 1,
                Color = Color.FromArgb(0xB0, 0, 0, 0),
            };
        }

        // A widget hides its whole card by hiding its view (e.g. Now Playing when idle).
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) card.IsVisible = view.IsVisible;
        };
        return card;
    }

    /// <summary>
    /// A widget's own background: "theme" (the theme's card), "glass" (half as solid) or "none"
    /// (no card at all, just the content with a soft shadow so it stays readable).
    /// </summary>
    public static void ApplyLook(Border card, Control view, string background)
    {
        switch (background)
        {
            case "none":
                card.Background = Brushes.Transparent;
                card.BoxShadow = default;
                card.Classes.Add("no-card");
                AddTextShadow(view);
                break;
            case "glass":
                if (Application.Current?.FindResource("Mantel.CardBackground") is ISolidColorBrush b)
                    card.Background = new SolidColorBrush(Color.FromArgb((byte)(b.Color.A / 2), b.Color.R, b.Color.G, b.Color.B));
                card.BoxShadow = default;
                break;
        }
    }

    private static void AddTextShadow(Control view)
    {
        if (view.Effect is not null) return;
        view.Effect = new DropShadowEffect { BlurRadius = 10, OffsetX = 0, OffsetY = 1, Color = Color.FromArgb(0xB0, 0, 0, 0) };
    }

    /// <summary>Applies the theme's font to a widget or flyout window.</summary>
    public static void ApplyFont(Window window)
    {
        window.Classes.Add("widget");
        if (ThemeManager.Current.FontFamily is { } font)
            window.FontFamily = new FontFamily(font);
    }
}
