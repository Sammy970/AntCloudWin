using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AntCloud.Controls;

/// Excellent → Good → Playable → High: the zones used by the network check and the stream overlay.
static class LatencyZones
{
    public sealed record Zone(string Name, double UpTo, Color Color, string Verdict, string Detail, Geometry Icon);

    public static readonly Zone[] All =
    [
        new("Excellent", 35, Color.Parse("#2EB85C"), "Excellent for streaming", "Feels close to a PC under your desk, even in fast shooters.", Icons.CheckCircle),
        new("Good", 60, Color.Parse("#8CCC33"), "Good for streaming", "Great for most games. Competitive shooters may feel a touch behind.", Icons.Check),
        new("Playable", 90, Color.Parse("#F7A325"), "Playable", "Fine for slower and single-player games; fast games will feel delayed.", Icons.AlertCircle),
        new("High", 150, Color.Parse("#E5484D"), "High latency", "Expect noticeable lag. Try another network if you can.", Icons.Warning),
    ];

    public static Zone For(double ms) => All.FirstOrDefault(z => ms <= z.UpTo) ?? All[^1];
}

/// One bar per latency sample, filling in as they arrive.
sealed class SampleBars : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty = AvaloniaProperty.Register<SampleBars, IReadOnlyList<double>?>(nameof(Samples));
    public static readonly StyledProperty<IBrush?> EmptyProperty = AvaloniaProperty.Register<SampleBars, IBrush?>(nameof(Empty));
    public IReadOnlyList<double>? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public IBrush? Empty { get => GetValue(EmptyProperty); set => SetValue(EmptyProperty, value); }
    public int Expected { get; set; } = 20;

    static SampleBars() => AffectsRender<SampleBars>(SamplesProperty, EmptyProperty);

    public override void Render(DrawingContext ctx)
    {
        var samples = Samples ?? [];
        var top = Math.Max(120, (samples.Count > 0 ? samples.Max() : 0) * 1.15);
        const double gap = 3;
        var width = (Bounds.Width - gap * (Expected - 1)) / Expected;
        for (var i = 0; i < Expected; i++)
        {
            var x = i * (width + gap);
            if (i < samples.Count)
            {
                var h = Math.Max(3, Bounds.Height * samples[i] / top);
                ctx.DrawRectangle(new SolidColorBrush(LatencyZones.For(samples[i]).Color), null, new RoundedRect(new Rect(x, Bounds.Height - h, width, h), 2));
            }
            else
            {
                ctx.DrawRectangle(Empty, null, new RoundedRect(new Rect(x, Bounds.Height - 3, width, 3), 1.5));
            }
        }
    }
}

/// The zones as a bar with a marker at the measured value.
sealed class LatencyScale : AnimatedControl
{
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<LatencyScale, double?>(nameof(Value));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<LatencyScale, IBrush?>(nameof(LabelBrush));
    public double? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    static LatencyScale() => AffectsRender<LatencyScale>(ValueProperty, LabelBrushProperty);

    private double shown;
    private double from;
    private double changedAt = -10;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty) return;
        from = change.OldValue is double old ? shown : Value ?? 0;
        changedAt = Motion.Enabled ? Now : -10;
        Animate();
    }

    protected override bool NeedsFrames => Now - changedAt < 0.6;

    protected override Size MeasureOverride(Size availableSize) => new(0, 34);

    public override void Render(DrawingContext ctx)
    {
        var max = LatencyZones.All[^1].UpTo;
        var w = Bounds.Width;
        var current = Value is { } v ? LatencyZones.For(v) : null;
        double lower = 0;
        foreach (var zone in LatencyZones.All)
        {
            var x0 = w * lower / max;
            var x1 = w * zone.UpTo / max - 2;
            var alpha = (byte)(current == zone ? 230 : 64);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, zone.Color.R, zone.Color.G, zone.Color.B)), null, new RoundedRect(new Rect(x0, 5, Math.Max(0, x1 - x0), 8), 4));
            var label = new FormattedText(zone.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, LabelBrush ?? Brushes.Gray);
            ctx.DrawText(label, new Point((x0 + x1) / 2 - label.Width / 2, 20));
            lower = zone.UpTo;
        }

        if (Value is { } value)
        {
            shown = from + (Math.Min(value, max) - from) * Progress(changedAt, 0.6);
            var x = w * shown / max;
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null, new Point(x, 10), 9, 9);
            ctx.DrawEllipse(Brushes.White, null, new Point(x, 9), 8, 8);
            ctx.DrawEllipse(new SolidColorBrush(LatencyZones.For(value).Color), null, new Point(x, 9), 4, 4);
        }
    }
}
