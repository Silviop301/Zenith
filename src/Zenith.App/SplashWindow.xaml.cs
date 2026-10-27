using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zenith.App.Controls;

namespace Zenith.App;

/// <summary>
/// The opening: the four dots of the Ascensão symbol appear one by one (0.22 s apart), each overshooting
/// a little, a halo opens around the top dot, and "Zenith" slides in. Timings from design/marca/ascensao-final.html.
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>The whole sequence, halo included. The app waits at least this long before showing the main window.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(2200);

    private static readonly TimeSpan DotStagger = TimeSpan.FromMilliseconds(220);
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public SplashWindow()
    {
        InitializeComponent();
        if (Motion.Enabled) Prepare();
        Loaded += (_, _) => { if (Motion.Enabled) Play(); };
    }

    /// <summary>Fades out and closes.</summary>
    public Task CloseAsync()
    {
        var done = new TaskCompletionSource();
        if (!Motion.Enabled)
        {
            Close();
            return Task.CompletedTask;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            Close();
            done.TrySetResult();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }

    private void Prepare()
    {
        foreach (var dot in new[] { Dot1, Dot2, Dot3, Dot4 })
        {
            dot.Opacity = 0;
            dot.RenderTransform = new ScaleTransform(0, 0);
        }
        Halo.RenderTransform = new ScaleTransform(1, 1);
        Word.Opacity = 0;
        WordShift.X = -8;
    }

    private void Play()
    {
        var dots = new[] { Dot1, Dot2, Dot3, Dot4 };
        for (var i = 0; i < dots.Length; i++)
        {
            var start = TimeSpan.FromTicks(DotStagger.Ticks * i);
            // 0 → 125% in 0.4 s, then settles to 100% by 0.64 s.
            var scale = new DoubleAnimationUsingKeyFrames { BeginTime = start };
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)), Ease));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640)), Ease));
            var transform = (ScaleTransform)dots[i].RenderTransform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(400)) { BeginTime = start, EasingFunction = Ease });
        }

        // Halo: shows at 1.36 s and opens to 2.8× while fading out by 2.2 s.
        var haloStart = TimeSpan.FromMilliseconds(1200);
        var haloOpacity = new DoubleAnimationUsingKeyFrames { BeginTime = haloStart };
        haloOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        haloOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000)), Ease));
        Halo.BeginAnimation(OpacityProperty, haloOpacity);
        var haloScale = new DoubleAnimation(1, 2.8, TimeSpan.FromMilliseconds(1000)) { BeginTime = haloStart, EasingFunction = Ease };
        var haloTransform = (ScaleTransform)Halo.RenderTransform;
        haloTransform.BeginAnimation(ScaleTransform.ScaleXProperty, haloScale);
        haloTransform.BeginAnimation(ScaleTransform.ScaleYProperty, haloScale);

        // The name: from 1.2 s to 1.68 s, sliding 8 px from the left.
        var wordStart = TimeSpan.FromMilliseconds(1200);
        var wordTime = TimeSpan.FromMilliseconds(480);
        Word.BeginAnimation(OpacityProperty, new DoubleAnimation(1, wordTime) { BeginTime = wordStart, EasingFunction = Ease });
        WordShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, wordTime) { BeginTime = wordStart, EasingFunction = Ease });
    }
}
