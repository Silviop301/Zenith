using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Zenith.App.Controls;

/// <summary>
/// The entrance from the design tokens: rise 10 px and fade in over 700 ms, 60 ms apart per item.
/// Off when Windows asks for less motion, and in screenshot mode.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; set; } = SystemParameters.ClientAreaAnimation;

    public static readonly TimeSpan Entrance = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(60);
    public static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static readonly DependencyProperty RiseProperty = DependencyProperty.RegisterAttached(
        "Rise", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnRiseChanged));

    /// <summary>Position in the stagger: the entrance starts Index × 60 ms later.</summary>
    public static readonly DependencyProperty IndexProperty = DependencyProperty.RegisterAttached(
        "Index", typeof(int), typeof(Motion), new PropertyMetadata(0));

    public static bool GetRise(DependencyObject o) => (bool)o.GetValue(RiseProperty);
    public static void SetRise(DependencyObject o, bool value) => o.SetValue(RiseProperty, value);
    public static int GetIndex(DependencyObject o) => (int)o.GetValue(IndexProperty);
    public static void SetIndex(DependencyObject o, int value) => o.SetValue(IndexProperty, value);

    private static void OnRiseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue) element.Loaded += (_, _) => Play(element);
    }

    private static void Play(FrameworkElement element)
    {
        if (!Enabled) return;
        var delay = TimeSpan.FromTicks(Stagger.Ticks * Math.Min(GetIndex(element), 12));
        var shift = new TranslateTransform(0, 10);
        element.RenderTransform = shift;
        element.Opacity = 0;

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Entrance) { BeginTime = delay, EasingFunction = Ease });
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, Entrance) { BeginTime = delay, EasingFunction = Ease });
    }
}
