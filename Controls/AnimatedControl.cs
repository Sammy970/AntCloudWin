using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace AntCloud.Controls;

/// A drawn control that redraws every frame while `NeedsFrames` is true, and not at all otherwise.
abstract class AnimatedControl : Control
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private bool scheduled;

    /// Seconds since the app started; the time base for every drawn animation.
    protected static double Now => Clock.Elapsed.TotalSeconds;

    protected abstract bool NeedsFrames { get; }

    protected void Animate()
    {
        if (scheduled || TopLevel.GetTopLevel(this) is not { } top) return;
        scheduled = true;
        top.RequestAnimationFrame(_ =>
        {
            scheduled = false;
            InvalidateVisual();
            if (NeedsFrames) Animate();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animate();
    }

    /// 0 → 1 progress of an animation that started at `start` and lasts `duration` seconds, eased out.
    protected static double Progress(double start, double duration, double delay = 0)
    {
        var t = Math.Clamp((Now - start - delay) / duration, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }
}
