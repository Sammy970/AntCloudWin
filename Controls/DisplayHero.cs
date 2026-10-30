using System.Globalization;
using AntCloud.Core.Session;
using Avalonia;
using Avalonia.Media;

namespace AntCloud.Controls;

/// A monitor whose screen shows the session state; the app's one illustration. Dark glass when off, a
/// scanning shimmer while connecting or booting, a brand-blue screen with a breathing play button when
/// ready, a live dot while streaming. It flashes on like an old monitor when the PC starts.
sealed class DisplayHero : AnimatedControl
{
    public static readonly StyledProperty<Phase> PhaseProperty = AvaloniaProperty.Register<DisplayHero, Phase>(nameof(Phase));
    public static readonly StyledProperty<int?> QueuePositionProperty = AvaloniaProperty.Register<DisplayHero, int?>(nameof(QueuePosition));

    public Phase Phase { get => GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public int? QueuePosition { get => GetValue(QueuePositionProperty); set => SetValue(QueuePositionProperty, value); }

    static DisplayHero()
    {
        AffectsRender<DisplayHero>(PhaseProperty, QueuePositionProperty);
    }

    private static readonly Color AntBlue = Color.Parse("#4A86E8");
    private Phase previous = Phase.Idle;
    private double changedAt = -10;
    private double flashAt = -10;

    private const double CrossfadeSeconds = 0.5;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PhaseProperty) return;
        var old = (Phase)change.OldValue!;
        previous = old;
        changedAt = Motion.Enabled ? Now : -10;
        if (old == Phase.Idle && Phase != Phase.Idle && Motion.Enabled) flashAt = Now;
        Animate();
    }

    protected override bool NeedsFrames =>
        Now - changedAt < 1 || Now - flashAt < 1 || (Motion.Enabled && Phase != Phase.Idle);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Min(availableSize.Width, 340) : 340;
        return new Size(width, HeightFor(width));
    }

    private static double HeightFor(double width) => (width - 14) * 10 / 16 + 14 + 26 + 7;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var screen = new Rect(7, 7, w - 14, (w - 14) * 10 / 16);
        var bezel = new Rect(0, 0, w, screen.Height + 14);
        var p = Progress(changedAt, CrossfadeSeconds);
        var lit = Lit(Phase) ? p : Lit(previous) ? 1 - p : 0;

        // Glow under the monitor: brand blue when it's lit, a soft shadow otherwise.
        DrawGlow(ctx, bezel.Inflate(new Thickness(34, 30, 34, 40)).Translate(new Vector(0, 10)), Color.FromArgb(56, 0, 0, 0), 1 - lit);
        DrawGlow(ctx, bezel.Inflate(new Thickness(46, 40, 46, 46)).Translate(new Vector(0, 10)), Color.FromArgb(120, AntBlue.R, AntBlue.G, AntBlue.B), lit);

        ctx.DrawRectangle(Vertical(Color.Parse("#333333"), Color.Parse("#1C1C1C")),
                          new Pen(new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), 1),
                          new RoundedRect(bezel.Deflate(0.5), 16));

        using (ctx.PushClip(new RoundedRect(screen, 9)))
        {
            // Each state's colour is its own layer, so changes crossfade instead of snapping.
            if (ScreenState(previous) != ScreenState(Phase) && p < 1) ctx.DrawRectangle(Background(ScreenState(previous)), null, screen);
            using (ctx.PushOpacity(ScreenState(previous) != ScreenState(Phase) ? p : 1))
                ctx.DrawRectangle(Background(ScreenState(Phase)), null, screen);

            if (ContentKey(previous) != ContentKey(Phase) && p < 1)
                DrawContent(ctx, screen, previous, 1 - p, 1 + 0.1 * p);
            DrawContent(ctx, screen, Phase, ContentKey(previous) != ContentKey(Phase) ? p : 1, 0.85 + 0.15 * p);

            // Power-on: a bright line opens to fill the screen and fades.
            var opening = Progress(flashAt, 0.22);
            var fading = Progress(flashAt, 0.6, delay: 0.18);
            if (Now - flashAt < 0.8)
            {
                var h = screen.Height * Math.Max(0.02, opening);
                using (ctx.PushOpacity(0.9 * (1 - fading)))
                    ctx.DrawRectangle(Brushes.White, null, new Rect(screen.X, screen.Center.Y - h / 2, screen.Width, h));
            }

            // Glass reflection.
            ctx.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientStops = { new(Color.FromArgb(23, 255, 255, 255), 0), new(Colors.Transparent, 1) },
            }, null, screen);
        }

        // Stand: neck and foot.
        var neck = new Rect(w / 2 - 27, bezel.Bottom, 54, 26);
        ctx.DrawRectangle(Vertical(Color.Parse("#9E9E9E"), Color.Parse("#C7C7C7")), null, neck);
        ctx.DrawRectangle(Vertical(Color.Parse("#D1D1D1"), Color.Parse("#A8A8A8")), null, new RoundedRect(new Rect(w / 2 - 60, neck.Bottom, 120, 7), 3.5));
    }

    private static void DrawGlow(DrawingContext ctx, Rect rect, Color color, double opacity)
    {
        if (opacity <= 0.01) return;
        using (ctx.PushOpacity(opacity))
            ctx.DrawEllipse(new RadialGradientBrush { GradientStops = { new(color, 0), new(color, 0.35), new(Colors.Transparent, 1) } }, null, rect);
    }

    private void DrawContent(DrawingContext ctx, Rect screen, Phase phase, double opacity, double scale)
    {
        if (opacity <= 0.01) return;
        var c = screen.Center;
        using var _ = ctx.PushOpacity(opacity);
        using var __ = ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(c.X, c.Y));

        switch (phase)
        {
            case Phase.Idle:
                Icon(ctx, Icons.Power, c, 30, Color.FromArgb(56, 255, 255, 255), 1.5);
                break;

            case Phase.Connecting or Phase.Queued or Phase.Booting:
                ScanBand(ctx, screen);
                if (phase == Phase.Queued && QueuePosition is { } position)
                {
                    Text(ctx, $"#{position}", 44, FontWeight.SemiBold, Colors.White, new Point(c.X, c.Y - 10));
                    Text(ctx, "in queue", 12, FontWeight.Normal, Color.FromArgb(153, 255, 255, 255), new Point(c.X, c.Y + 26));
                }
                else
                {
                    Icon(ctx, phase == Phase.Booting ? Icons.Cpu : Icons.Antenna, c, 30, Color.FromArgb(217, 255, 255, 255), 1.5);
                }
                break;

            case Phase.Ready:
                // Breathes gently: the one thing on screen asking to be pressed.
                var b = Motion.Enabled ? (Math.Sin(Now * Math.PI * 2 / 3.2) + 1) / 2 : 0;
                var r = 27 * (1 + 0.06 * b);
                ctx.DrawEllipse(new RadialGradientBrush { GradientStops = { new(Color.FromArgb((byte)(40 + 75 * b), 255, 255, 255), 0.55), new(Colors.Transparent, 1) } },
                                null, c, r + 10 + 8 * b, r + 10 + 8 * b);
                ctx.DrawEllipse(Brushes.White, null, c, r, r);
                ctx.DrawEllipse(new SolidColorBrush(AntBlue), null, c, r - 2.5, r - 2.5);
                var tri = r * 0.42;
                var play = new StreamGeometry();
                using (var g = play.Open())
                {
                    g.BeginFigure(new Point(c.X - tri * 0.62, c.Y - tri), true);
                    g.LineTo(new Point(c.X + tri, c.Y));
                    g.LineTo(new Point(c.X - tri * 0.62, c.Y + tri));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(Brushes.White, new Pen(Brushes.White, 3, lineJoin: PenLineJoin.Round), play);
                break;

            case Phase.Pairing:
                ScanBand(ctx, screen);
                Icon(ctx, Icons.Link, c, 32, Colors.White, 2);
                break;

            case Phase.Streaming:
                var pulse = Motion.Enabled ? Now % 1.4 / 1.4 : 1;
                var dot = new Point(c.X - 22, c.Y - 9);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(102 * (1 - pulse)), 46, 184, 92)), null, dot, 3.5 * (1 + 1.4 * pulse), 3.5 * (1 + 1.4 * pulse));
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#2EB85C")), null, dot, 3.5, 3.5);
                Text(ctx, "L I V E", 11, FontWeight.Bold, Colors.White, new Point(c.X + 7, c.Y - 9));
                Text(ctx, "Playing in Moonlight", 12, FontWeight.Normal, Color.FromArgb(153, 255, 255, 255), new Point(c.X, c.Y + 13));
                break;
        }
    }

    /// A soft band of brand light sweeping down the screen while the PC is working.
    private static void ScanBand(DrawingContext ctx, Rect screen)
    {
        if (!Motion.Enabled) return;
        var t = Now % 2.4 / 2.4;
        var height = screen.Height * 0.45;
        var band = new Rect(screen.X, screen.Y + screen.Height * (t * 1.45 - 0.45), screen.Width, height);
        ctx.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = { new(Colors.Transparent, 0), new(Color.FromArgb(89, AntBlue.R, AntBlue.G, AntBlue.B), 0.5), new(Colors.Transparent, 1) },
        }, null, band);
    }

    private static void Icon(DrawingContext ctx, Geometry icon, Point center, double size, Color color, double stroke)
    {
        using (ctx.PushTransform(Matrix.CreateTranslation(center.X - size / 2, center.Y - size / 2)))
            StrokeIcon.Draw(ctx, icon, new Size(size, size), new SolidColorBrush(color), strokeWidth: stroke);
    }

    private static void Text(DrawingContext ctx, string text, double size, FontWeight weight, Color color, Point center)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                   new Typeface(FontFamily.Default, weight: weight), size, new SolidColorBrush(color));
        ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private static bool Lit(Phase p) => p is Phase.Ready or Phase.Pairing;

    private static string ScreenState(Phase p) => p switch
    {
        Phase.Idle => "off",
        Phase.Ready or Phase.Pairing => "lit",
        Phase.Streaming => "live",
        _ => "working",
    };

    /// What's drawn on the screen; the queue position changes inside "working" without a new screen.
    private static string ContentKey(Phase p) => p switch
    {
        Phase.Connecting or Phase.Queued or Phase.Booting => p == Phase.Booting ? "boot" : "connect",
        _ => p.ToString(),
    };

    private static IBrush Background(string state) => state switch
    {
        "lit" => Vertical(AntBlue, Color.FromRgb(46, 92, 191)),
        "live" => Vertical(Color.FromRgb(15, 23, 41), Color.FromRgb(5, 8, 15)),
        "off" => Vertical(Color.FromRgb(23, 23, 23), Color.FromRgb(10, 10, 10)),
        _ => Vertical(Color.FromRgb(18, 28, 51), Color.FromRgb(8, 13, 26)),
    };

    private static LinearGradientBrush Vertical(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new(top, 0), new(bottom, 1) },
    };
}
