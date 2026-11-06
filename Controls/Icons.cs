using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AntCloud.Controls;

/// Line icons on a 24-unit grid (Lucide-style strokes), drawn by `StrokeIcon`.
static class Icons
{
    public static readonly Geometry Monitor = G("M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8 21h8 M12 17v4");
    public static readonly Geometry Sliders = G("M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4");
    public static readonly Geometry Gauge = G("M12 14l4-4 M3.34 19a10 10 0 1 1 17.32 0");
    public static readonly Geometry Info = G("M12 2a10 10 0 1 1 0 20a10 10 0 1 1 0-20z M12 16v-4 M12 8h.01");
    public static readonly Geometry Power = G("M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04");
    public static readonly Geometry Play = G("M7 4l13 8-13 8z");
    public static readonly Geometry Link = G("M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71 M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71");
    public static readonly Geometry Cpu = G("M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M9 9h6v6H9z M15 2v2 M15 20v2 M2 15h2 M2 9h2 M20 15h2 M20 9h2 M9 2v2 M9 20v2");
    public static readonly Geometry Antenna = G("M4.9 19.1C1 15.2 1 8.8 4.9 4.9 M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.5 M16.2 7.8c2.3 2.3 2.3 6.1 0 8.5 M19.1 4.9C23 8.8 23 15.1 19.1 19 M12 11a1 1 0 1 1 0 2a1 1 0 1 1 0-2z");
    public static readonly Geometry Check = G("M20 6L9 17l-5-5");
    public static readonly Geometry Moon = G("M12 3a6 6 0 0 0 9 9a9 9 0 1 1-9-9z");
    public static readonly Geometry Star = G("M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01z");
    public static readonly Geometry Warning = G("M21.73 18l-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3z M12 9v4 M12 17h.01");
    public static readonly Geometry AlertCircle = G("M12 2a10 10 0 1 1 0 20a10 10 0 1 1 0-20z M12 8v4 M12 16h.01");
    public static readonly Geometry CheckCircle = G("M12 2a10 10 0 1 1 0 20a10 10 0 1 1 0-20z M9 12l2 2 4-4");
    public static readonly Geometry More = G("M5 12h.01 M12 12h.01 M19 12h.01");
    public static readonly Geometry ArrowUpRight = G("M7 7h10v10 M7 17L17 7");
    public static readonly Geometry Code = G("M16 18l6-6-6-6 M8 6l-6 6 6 6");
    public static readonly Geometry Chat = G("M7.9 20A9 9 0 1 0 4 16.1L2 22z");
    public static readonly Geometry At = G("M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8");
    public static readonly Geometry Lock = G("M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M7 11V7a5 5 0 0 1 10 0v4");
    public static readonly Geometry Tv = G("M4 7h16a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z M17 2l-5 5-5-5");
    public static readonly Geometry Close = G("M18 6L6 18 M6 6l12 12");
    public static readonly Geometry Braces = G("M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2a2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1 M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1");
    public static readonly Geometry Gamepad = G("M6 11h4 M8 9v4 M15 12h.01 M18 10h.01 M17.32 5H6.68a4 4 0 0 0-3.978 3.59c-.006.052-.01.101-.017.152C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258-.007-.05-.011-.1-.017-.151A4 4 0 0 0 17.32 5z");
    public static readonly Geometry Keyboard = G("M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M6 9h.01 M10 9h.01 M14 9h.01 M18 9h.01 M8 13h.01 M12 13h.01 M16 13h.01 M7 16h10");

    private static Geometry G(string data) => Geometry.Parse(data);
}

/// Draws one of `Icons` at any size. Strokes scale with the icon, like a font glyph.
sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<StrokeIcon>();
    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<StrokeIcon, bool>(nameof(Filled));
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<StrokeIcon, double>(nameof(StrokeWidth), 2);

    static StrokeIcon()
    {
        AffectsRender<StrokeIcon>(DataProperty, ForegroundProperty, FilledProperty, StrokeWidthProperty);
    }

    public StrokeIcon()
    {
        Width = Height = 16;
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Data is { } data && Foreground is { } brush)
            Draw(context, data, Bounds.Size, brush, Filled, StrokeWidth);
    }

    /// Draws a 24-unit icon into a box of `size` at the current origin.
    public static void Draw(DrawingContext context, Geometry data, Size size, IBrush brush, bool filled = false, double strokeWidth = 2)
    {
        var scale = Math.Min(size.Width, size.Height) / 24;
        var offset = new Point((size.Width - 24 * scale) / 2, (size.Height - 24 * scale) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            var pen = new Pen(brush, strokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(filled ? brush : null, pen, data);
        }
    }
}
