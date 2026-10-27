using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Zenith.App.Controls;

/// <summary>The Ascensão symbol while a scan runs: its dots light up from the smallest to the top one, slowly.</summary>
public sealed class AscensionPulse : Canvas
{
    // Same dots as design/marca/zenith-simbolo.svg (center x, center y, radius in a 64 px box).
    private static readonly (double X, double Y, double R)[] Dots = [(18.4, 42.0, 2.4), (24.0, 32.5, 3.2), (31.8, 26.6, 4.0), (42.0, 24.5, 5.4)];

    public AscensionPulse()
    {
        Width = 64;
        Height = 64;
        var noite = (Brush)Application.Current.FindResource("MarcaNoiteBrush");
        var ativo = (Brush)Application.Current.FindResource("StatusAtivoBrush");
        for (var i = 0; i < Dots.Length; i++)
        {
            var (x, y, r) = Dots[i];
            // The top dot shows the status color: purple while Zenith is working.
            var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = i == Dots.Length - 1 ? ativo : noite };
            SetLeft(dot, x - r);
            SetTop(dot, y - r);
            Children.Add(dot);
        }
        Loaded += (_, _) => Start();
    }

    private void Start()
    {
        if (!Motion.Enabled) return;
        for (var i = 0; i < Children.Count; i++)
        {
            var pulse = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromMilliseconds(2400) };
            var at = 250.0 * i;
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 500)), Motion.Ease));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000)), Motion.Ease));
            Children[i].BeginAnimation(OpacityProperty, pulse);
        }
    }
}
