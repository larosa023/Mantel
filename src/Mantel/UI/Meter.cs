using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Layoutable = Avalonia.Layout.Layoutable;

namespace Mantel.UI;

/// <summary>A label, a value on the right, and a thin bar underneath. Shared by the stats widgets.</summary>
public sealed class Meter
{
    private readonly double _width;
    private readonly TextBlock _value = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Border _fill = new() { Width = 0 };

    public Meter(string label, double width, double hotAt = 0.9)
    {
        _width = width;
        HotAt = hotAt;

        var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        title.Classes.Add("label");
        _value.Classes.Add("value");
        _fill.Classes.Add("fill");
        // Readings glide to their new value instead of jumping.
        _fill.Transitions = new Transitions
        {
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(450), Easing = new CubicEaseOut() },
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(title);
        Grid.SetColumn(_value, 1);
        header.Children.Add(_value);

        var track = new Border { Child = _fill, Width = width };
        track.Classes.Add("track");

        View = new StackPanel { Spacing = 6, Width = width, Children = { header, track } };
    }

    public double HotAt { get; }

    public Control View { get; }

    public void Set(double fraction, string text)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        _value.Text = text;
        _fill.Width = _width * fraction;
        _fill.Classes.Set("hot", fraction >= HotAt);
    }
}
