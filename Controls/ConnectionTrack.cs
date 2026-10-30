using System.Globalization;
using AntCloud.Core.Session;
using Avalonia;
using Avalonia.Media;

namespace AntCloud.Controls;

/// Connect → Boot → Ready → Stream, with the current step pulsing and the line filling segment by segment.
sealed class ConnectionTrack : AnimatedControl
{
    public static readonly StyledProperty<Phase> PhaseProperty = AvaloniaProperty.Register<ConnectionTrack, Phase>(nameof(Phase));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = Avalonia.Controls.TextBlock.ForegroundProperty.AddOwner<ConnectionTrack>();
    public static readonly StyledProperty<IBrush?> MutedProperty = AvaloniaProperty.Register<ConnectionTrack, IBrush?>(nameof(Muted));

    public Phase Phase { get => GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? Muted { get => GetValue(MutedProperty); set => SetValue(MutedProperty, value); }

    static ConnectionTrack()
    {
        AffectsRender<ConnectionTrack>(PhaseProperty, ForegroundProperty, MutedProperty);
    }

    private static readonly string[] Steps = ["Connect", "Boot", "Ready", "Stream"];
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#4A86E8"));

    /// The filled length, in steps, animated toward `Completed(Phase)`.
    private double shown;
    private double from;
    private double changedAt = -10;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PhaseProperty) return;
        from = shown;
        changedAt = Motion.Enabled ? Now : -10;
        Animate();
    }

    protected override bool NeedsFrames => Math.Abs(shown - Completed(Phase)) > 0.001 || (Motion.Enabled && Current(Phase) != null);

    private static int Completed(Phase p) => p switch
    {
        Phase.Booting => 1,
        Phase.Ready or Phase.Pairing => 3,
        Phase.Streaming => 4,
        _ => 0,
    };

    private static int? Current(Phase p) => p switch
    {
        Phase.Connecting or Phase.Queued => 0,
        Phase.Booting => 1,
        Phase.Pairing => 3,
        _ => null,
    };

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? Math.Min(availableSize.Width, 400) : 400, 44);

    public override void Render(DrawingContext ctx)
    {
        var target = Completed(Phase);
        // Each step of travel takes 0.3 s, so a jump from Connect to Ready fills one segment after another.
        var duration = Math.Max(0.3, Math.Abs(target - from) * 0.3);
        shown = from + (target - from) * Progress(changedAt, duration);

        var muted = Muted ?? Brushes.Gray;
        var fg = Foreground ?? Brushes.White;
        var labels = Steps.Select(s => new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                                         new Typeface(FontFamily.Default), 12, fg)).ToArray();
        var margin = labels.Max(l => l.Width) / 2;
        var span = Bounds.Width - 2 * margin;
        const double cy = 9, r = 9;

        for (var i = 1; i < Steps.Length; i++)
        {
            var x0 = margin + span * (i - 1) / 3 + r + 6;
            var x1 = margin + span * i / 3 - r - 6;
            ctx.DrawRectangle(muted, null, new RoundedRect(new Rect(x0, cy - 1, x1 - x0, 2), 1));
            var fill = Math.Clamp(shown - (i - 1), 0, 1);
            if (fill > 0) ctx.DrawRectangle(Blue, null, new RoundedRect(new Rect(x0, cy - 1, (x1 - x0) * fill, 2), 1));
        }

        var current = Current(Phase);
        for (var i = 0; i < Steps.Length; i++)
        {
            var center = new Point(margin + span * i / 3, cy);
            var done = i < shown - 0.5;
            var isCurrent = i == current;
            var scale = isCurrent ? 1.12 : 1;
            if (done)
            {
                ctx.DrawEllipse(Blue, null, center, r, r);
                using (ctx.PushTransform(Matrix.CreateTranslation(center.X - 5, center.Y - 5)))
                    StrokeIcon.Draw(ctx, Icons.Check, new Size(10, 10), Brushes.White, strokeWidth: 3.6);
            }
            else
            {
                ctx.DrawEllipse(null, new Pen(isCurrent ? Blue : muted, 1.5), center, r * scale - 0.75, r * scale - 0.75);
                if (isCurrent)
                {
                    var pulse = Motion.Enabled ? Now % 1.4 / 1.4 : 1;
                    ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(102 * (1 - pulse)), 74, 134, 232)), null, center, 3.5 * (1 + 1.4 * pulse), 3.5 * (1 + 1.4 * pulse));
                    ctx.DrawEllipse(Blue, null, center, 3.5, 3.5);
                }
            }
            var label = labels[i];
            if (!done && !isCurrent) label.SetForegroundBrush(muted);
            ctx.DrawText(label, new Point(center.X - label.Width / 2, cy + r + 8));
        }
    }
}
