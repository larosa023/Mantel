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
    public static Border Create(Control view, ContextMenu? menu = null)
    {
        var card = new Border { Child = view, ContextMenu = menu, IsVisible = view.IsVisible };
        card.Classes.Add("card");

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

    /// <summary>Applies the theme's font to a widget or flyout window.</summary>
    public static void ApplyFont(Window window)
    {
        window.Classes.Add("widget");
        if (ThemeManager.Current.FontFamily is { } font)
            window.FontFamily = new FontFamily(font);
    }
}
