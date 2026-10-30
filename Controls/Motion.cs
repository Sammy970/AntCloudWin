using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AntCloud.Controls;

/// Fades content up into place the first time it appears, after `Delay` seconds:
/// `c:Reveal.Delay="0.08"`. With Windows' "Animation effects" off it appears at once.
static class Reveal
{
    public static readonly AttachedProperty<double> DelayProperty =
        AvaloniaProperty.RegisterAttached<Visual, double>("Delay", typeof(Reveal), -1);

    public static double GetDelay(Visual v) => v.GetValue(DelayProperty);
    public static void SetDelay(Visual v, double value) => v.SetValue(DelayProperty, value);

    static Reveal()
    {
        DelayProperty.Changed.AddClassHandler<Visual>((v, e) =>
        {
            if (e.NewValue is double delay && delay >= 0) v.AttachedToVisualTree += (_, _) => Play(v, delay);
        });
    }

    private static async void Play(Visual v, double delay)
    {
        if (!Motion.Enabled) return;
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(0.55),
            Delay = TimeSpan.FromSeconds(delay),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 16d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        if (v.RenderTransform is not TranslateTransform) v.RenderTransform = new TranslateTransform();
        if (v is Animatable a) await animation.RunAsync(a);
    }
}

static class Motion
{
    /// True while rendering a screenshot (`ANTCLOUD_SNAPSHOT`). Off-screen rendering drops everything inside a
    /// translucent border that has a box shadow, so snapshot mode leaves those shadows out.
    public static bool Snapshot { get; } = Environment.GetEnvironmentVariable("ANTCLOUD_SNAPSHOT") != null;

    /// A soft shadow for a floating card, except in snapshot mode.
    public static BoxShadows CardShadow(string shadow) => Snapshot ? default : BoxShadows.Parse(shadow);

    /// False when the user turned off animations (Windows: Settings → Accessibility → Visual effects), or for
    /// screenshots, which need everything in its final place.
    public static bool Enabled { get; set; } = SystemAnimationsEnabled();

    private static bool SystemAnimationsEnabled()
    {
        if (Environment.GetEnvironmentVariable("ANTCLOUD_SNAPSHOT") != null) return false;
        if (!OperatingSystem.IsWindows()) return true;
        // SPI_GETCLIENTAREAANIMATION
        return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);

    /// A short horizontal shake, for a failed sign-in.
    public static async void Shake(Visual v)
    {
        if (!Motion.Enabled) return;
        if (v.RenderTransform is not TranslateTransform) v.RenderTransform = new TranslateTransform();
        var animation = new Animation { Duration = TimeSpan.FromSeconds(0.45) };
        double[] offsets = [0, -9, 9, -7, 7, -4, 4, 0];
        for (var i = 0; i < offsets.Length; i++)
            animation.Children.Add(new KeyFrame { Cue = new Cue((double)i / (offsets.Length - 1)), Setters = { new Setter(TranslateTransform.XProperty, offsets[i]) } });
        if (v is Animatable a) await animation.RunAsync(a);
    }
}
