using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace AntCloud.Controls;

/// Slowly drifting blurred light in AntCloud blues over a faint dot grid, behind the sign-in screen.
/// Still when animations are off.
sealed class Aurora : AnimatedControl
{
    private sealed record Blob(Color Color, double Size, double X, double Y, double Speed, double Phase);

    private static readonly Blob[] Blobs =
    [
        new(Color.Parse("#4A86E8"), 760, 0.18, 0.25, 0.11, 0),
        new(Color.FromRgb(92, 92, 230), 680, 0.85, 0.2, 0.08, 2),
        new(Color.FromRgb(64, 199, 250), 640, 0.7, 0.85, 0.13, 4),
        new(Color.Parse("#4A86E8"), 560, 0.3, 0.9, 0.09, 1),
    ];

    private RenderTargetBitmap? grid;
    private (Size Size, bool Dark) gridKey;

    protected override bool NeedsFrames => Motion.Enabled && IsEffectivelyVisible;

    private bool Dark => ActualThemeVariant == ThemeVariant.Dark;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        var dark = Dark;
        var bounds = new Rect(size);
        ctx.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = dark
                ? [new(Color.FromRgb(8, 15, 36), 0), new(Color.FromRgb(13, 23, 51), 1)]
                : [new(Color.FromRgb(237, 245, 255), 0), new(Color.FromRgb(219, 232, 255), 1)],
        }, null, bounds);

        var t = Motion.Enabled ? Now : 0;
        foreach (var b in Blobs)
        {
            var alpha = (byte)((dark ? 0.6 : 0.4) * 255);
            var x = size.Width * (b.X + 0.08 * Math.Cos(t * b.Speed + b.Phase));
            var y = size.Height * (b.Y + 0.08 * Math.Sin(t * b.Speed * 1.3 + b.Phase));
            ctx.DrawEllipse(new RadialGradientBrush
            {
                GradientStops = { new(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B), 0), new(Color.FromArgb(0, b.Color.R, b.Color.G, b.Color.B), 1) },
            }, null, new Point(x, y), b.Size / 2, b.Size / 2);
        }

        if (Grid(size, dark) is { } bitmap) ctx.DrawImage(bitmap, bounds);
    }

    /// The dot grid, fading toward the edges, drawn once per size and theme.
    private RenderTargetBitmap? Grid(Size size, bool dark)
    {
        if (size.Width < 1 || size.Height < 1) return null;
        if (grid != null && gridKey == (size, dark)) return grid;
        grid?.Dispose();
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        grid = new RenderTargetBitmap(new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        gridKey = (size, dark);
        using var ctx = grid.CreateDrawingContext();
        const double step = 22;
        var center = new Point(size.Width / 2, size.Height / 2);
        for (var x = step / 2; x < size.Width; x += step)
        {
            for (var y = step / 2; y < size.Height; y += step)
            {
                var d = Point.Distance(new Point(x, y), center);
                var fade = Math.Clamp(1 - (d - 80) / 440, 0, 1);
                if (fade <= 0) continue;
                var a = (byte)((dark ? 0.07 : 0.06) * fade * 255);
                ctx.DrawEllipse(new SolidColorBrush(dark ? Color.FromArgb(a, 255, 255, 255) : Color.FromArgb(a, 0, 0, 0)), null, new Point(x, y), 0.75, 0.75);
            }
        }
        return grid;
    }
}
