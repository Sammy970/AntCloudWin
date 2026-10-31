using System.Globalization;
using AntCloud.Core.Controllers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AntCloud.Controls;

/// <summary>
/// A drawn controller showing live input, the same design as the Mac, iPhone and Android apps: pressed buttons
/// light up in AntCloud blue, the sticks move with the real ones and the triggers fill as they're pulled. One
/// Xbox-style body for every controller; the face-button labels and colours follow its family.
/// Everything is laid out on a 480 × 340 grid and scaled to fit.
/// </summary>
sealed class GamepadDrawing : Control
{
    public static readonly StyledProperty<PadState> StateProperty = AvaloniaProperty.Register<GamepadDrawing, PadState>(nameof(State), PadState.Rest);
    public static readonly StyledProperty<PadStyle> PadStyleProperty = AvaloniaProperty.Register<GamepadDrawing, PadStyle>(nameof(PadStyle));
    public static readonly StyledProperty<bool> DimmedProperty = AvaloniaProperty.Register<GamepadDrawing, bool>(nameof(Dimmed));

    public PadState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public PadStyle PadStyle { get => GetValue(PadStyleProperty); set => SetValue(PadStyleProperty, value); }
    /// <summary>An unlit, unlabelled controller, for the empty state.</summary>
    public bool Dimmed { get => GetValue(DimmedProperty); set => SetValue(DimmedProperty, value); }

    static GamepadDrawing()
    {
        AffectsRender<GamepadDrawing>(StateProperty, PadStyleProperty, DimmedProperty);
    }

    private static readonly Color Blue = Color.Parse("#4A86E8");
    private static readonly Point LeftStick = new(152, 142), RightStick = new(290, 200), Dpad = new(190, 200), Face = new(328, 142);

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsFinite(available.Width) ? Math.Min(available.Width, 480) : 480;
        return new Size(w, w * 340 / 480);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var k = Bounds.Width / 480;
        using var _ = ctx.PushTransform(Matrix.CreateScale(k, k));
        using var __ = ctx.PushOpacity(Dimmed ? 0.45 : 1);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var s = State;
        var control = dark ? Color.FromRgb(0x17, 0x17, 0x17) : Color.FromRgb(0x33, 0x33, 0x33);
        var track = dark ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Color.FromRgb(0xD1, 0xD1, 0xD1);
        var secondary = dark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x6B, 0x6B, 0x6B);
        bool On(PadButton b) => !Dimmed && s.Pressed.Contains(b);
        IBrush Fill(bool on) => new SolidColorBrush(on ? Blue : control);
        IBrush Glow(double alpha) => new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), Blue.R, Blue.G, Blue.B));

        // Triggers, as fill bars above the shoulders.
        var (lt, rt) = PadStyle switch { PadStyle.PlayStation => ("L2", "R2"), PadStyle.Nintendo => ("ZL", "ZR"), _ => ("LT", "RT") };
        foreach (var (c, v, name) in new[] { (new Point(138, 14), s.LT, lt), (new Point(342, 14), s.RT, rt) })
        {
            ctx.DrawRectangle(new SolidColorBrush(track), null, new RoundedRect(new Rect(c.X - 48, c.Y - 5, 96, 10), 5));
            if (!Dimmed && v > 0) ctx.DrawRectangle(new SolidColorBrush(Blue), null, new RoundedRect(new Rect(c.X - 48, c.Y - 5, 96 * Math.Clamp(v, 0, 1), 10), 5));
            Text(ctx, Dimmed ? name : $"{name} {(int)Math.Round(v * 100)}%", new Point(c.X, c.Y + 18), 10, secondary, FontWeight.SemiBold);
        }

        // Bumpers, tucked behind the shoulders.
        foreach (var (c, angle, b) in new[] { (new Point(140, 84), -7.0, PadButton.LeftShoulder), (new Point(340, 84), 7.0, PadButton.RightShoulder) })
        {
            var on = On(b);
            using var t = ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y));
            if (on) ctx.DrawRectangle(Glow(0.35), null, new RoundedRect(new Rect(c.X - 60, c.Y - 21, 120, 42), 20));
            ctx.DrawRectangle(Fill(on), null, new RoundedRect(new Rect(c.X - 52, c.Y - 15, 104, 30), 14));
            var label = PadStyle switch
            {
                PadStyle.PlayStation => b == PadButton.LeftShoulder ? "L1" : "R1",
                PadStyle.Nintendo => b == PadButton.LeftShoulder ? "L" : "R",
                _ => b == PadButton.LeftShoulder ? "LB" : "RB",
            };
            if (!Dimmed) Text(ctx, label, new Point(c.X, c.Y - 7), 10, Color.FromArgb((byte)(on ? 255 : 153), 255, 255, 255), FontWeight.Bold);
        }

        // Body: a soft layered shadow, then one moulded outline shaded top to bottom, with a highlight on the top edge.
        var body = BodyGeometry();
        using (ctx.PushTransform(Matrix.CreateTranslation(0, 9)))
        {
            for (var i = 6; i >= 1; i--)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(dark ? 16 : 7), 0, 0, 0)), i * 5, lineJoin: PenLineJoin.Round), body);
        }
        ctx.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = dark
                ? [new(Color.FromRgb(0x45, 0x45, 0x45), 0), new(Color.FromRgb(0x26, 0x26, 0x26), 1)]
                : [new(Color.FromRgb(0xFC, 0xFC, 0xFC), 0), new(Color.FromRgb(0xDB, 0xDB, 0xDB), 1)],
        }, new Pen(new SolidColorBrush(Color.FromArgb((byte)(dark ? 128 : 26), 0, 0, 0)), 0.5), body);
        ctx.DrawGeometry(null, new Pen(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 0.4, RelativeUnit.Relative),
            GradientStops = [new(Color.FromArgb((byte)(dark ? 46 : 230), 255, 255, 255), 0), new(Colors.Transparent, 1)],
        }, 1.5), body);

        // Shallow wells around the sticks and D-pad.
        foreach (var (c, r) in new[] { (LeftStick, 38.0), (RightStick, 38.0), (Dpad, 36.0) })
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(dark ? 70 : 18), 0, 0, 0)), null, c, r, r);

        // Sticks: a base the stick pivots in, and a cap that follows it.
        void Stick(Point c, double x, double y, bool pressed)
        {
            if (pressed) ctx.DrawEllipse(Glow(0.35), null, c, 40, 40);
            ctx.DrawEllipse(new SolidColorBrush(control), null, c, 31, 31);
            var cap = new Point(c.X + x * 12, c.Y - y * 12);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), null, cap + new Point(0, 2), 23, 23);
            ctx.DrawEllipse(new RadialGradientBrush
            {
                GradientStops = pressed
                    ? [new(Color.FromArgb(191, Blue.R, Blue.G, Blue.B), 0), new(Blue, 1)]
                    : [new(Color.FromRgb(0x2B, 0x2B, 0x2B), 0), new(Color.FromRgb(0x45, 0x45, 0x45), 1)],
            }, new Pen(new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), 1), cap, 23, 23);
        }
        Stick(LeftStick, s.LX, s.LY, On(PadButton.LeftStick));
        Stick(RightStick, s.RX, s.RY, On(PadButton.RightStick));

        // D-pad: a cross with each arm lighting up on its own.
        ctx.DrawRectangle(new SolidColorBrush(control), null, new RoundedRect(new Rect(Dpad.X - 11, Dpad.Y - 29, 22, 58), 6));
        ctx.DrawRectangle(new SolidColorBrush(control), null, new RoundedRect(new Rect(Dpad.X - 29, Dpad.Y - 11, 58, 22), 6));
        foreach (var (b, d, angle) in new[] { (PadButton.DpadUp, new Point(0, -16), 0.0), (PadButton.DpadDown, new Point(0, 16), 180.0), (PadButton.DpadLeft, new Point(-16, 0), 270.0), (PadButton.DpadRight, new Point(16, 0), 90.0) })
        {
            var c = Dpad + d;
            var on = On(b);
            if (on)
            {
                ctx.DrawEllipse(Glow(0.35), null, c, 16, 16);
                ctx.DrawRectangle(new SolidColorBrush(Blue), null, new RoundedRect(new Rect(c.X - 10, c.Y - 10, 20, 20), 5));
            }
            var arrow = new StreamGeometry();
            using (var g = arrow.Open())
            {
                g.BeginFigure(new Point(0, -3.5), true);
                g.LineTo(new Point(3.5, 2.5));
                g.LineTo(new Point(-3.5, 2.5));
                g.EndFigure(true);
            }
            using var t = ctx.PushTransform(Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y));
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(on ? 255 : 90), 255, 255, 255)), null, arrow);
        }

        // Face buttons, with the letters coloured as printed on the real controllers.
        foreach (var (b, d) in new[] { (PadButton.Y, new Point(0, -28)), (PadButton.A, new Point(0, 28)), (PadButton.X, new Point(-28, 0)), (PadButton.B, new Point(28, 0)) })
        {
            var c = Face + d;
            var on = On(b);
            if (on) ctx.DrawEllipse(Glow(0.4), null, c, 22, 22);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, c + new Point(0, 1), 14, 14);
            ctx.DrawEllipse(Fill(on), new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), 1), c, 14, 14);
            if (!Dimmed) Text(ctx, FaceLabel(b), c, PadStyle == PadStyle.PlayStation ? 14 : 13, on ? Colors.White : FaceColor(b), FontWeight.ExtraBold);
        }

        // View, Menu and the home button.
        foreach (var (b, c, r) in new[] { (PadButton.View, new Point(214, 150), 9.0), (PadButton.Menu, new Point(266, 150), 9.0), (PadButton.Home, new Point(240, 102), 16.0) })
        {
            var on = On(b);
            if (on) ctx.DrawEllipse(Glow(0.4), null, c, r + 7, r + 7);
            ctx.DrawEllipse(Fill(on), new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), 1), c, r, r);
        }
    }

    private string FaceLabel(PadButton b) => (PadStyle, b) switch
    {
        (PadStyle.PlayStation, PadButton.A) => "✕", (PadStyle.PlayStation, PadButton.B) => "○",
        (PadStyle.PlayStation, PadButton.X) => "□", (PadStyle.PlayStation, PadButton.Y) => "△",
        (PadStyle.Nintendo, PadButton.A) => "B", (PadStyle.Nintendo, PadButton.B) => "A",
        (PadStyle.Nintendo, PadButton.X) => "Y", (PadStyle.Nintendo, PadButton.Y) => "X",
        _ => b.ToString(),
    };

    private Color FaceColor(PadButton b) => (PadStyle, b) switch
    {
        (PadStyle.Xbox, PadButton.A) => Color.Parse("#6BCC45"), (PadStyle.Xbox, PadButton.B) => Color.Parse("#ED4D45"),
        (PadStyle.Xbox, PadButton.X) => Color.Parse("#408CF2"), (PadStyle.Xbox, PadButton.Y) => Color.Parse("#FFC733"),
        (PadStyle.PlayStation, PadButton.A) => Color.Parse("#80A6FF"), (PadStyle.PlayStation, PadButton.B) => Color.Parse("#FF7373"),
        (PadStyle.PlayStation, PadButton.X) => Color.Parse("#F28CD9"), (PadStyle.PlayStation, PadButton.Y) => Color.Parse("#59D9B3"),
        _ => Color.FromArgb(217, 255, 255, 255),
    };

    private static void Text(DrawingContext ctx, string text, Point center, double size, Color color, FontWeight weight)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                   new Typeface(FontFamily.Default, weight: weight), size, new SolidColorBrush(color));
        ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    /// <summary>The outline: the left half as curves from the top centre around the left grip, mirrored for the right.</summary>
    private static StreamGeometry BodyGeometry()
    {
        (Point End, Point C1, Point C2)[] left =
        [
            (new(118, 92), new(196, 80), new(150, 80)),
            (new(52, 166), new(82, 102), new(60, 126)),
            (new(50, 288), new(42, 212), new(34, 256)),
            (new(124, 304), new(66, 322), new(106, 326)),
            (new(194, 252), new(142, 282), new(162, 254)),
            (new(240, 250), new(214, 250), new(228, 250)),
        ];
        static Point M(Point p) => new(480 - p.X, p.Y);
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(240, 80), true);
        foreach (var (end, c1, c2) in left) g.CubicBezierTo(c1, c2, end);
        // Mirror back up the right side: reversed order, with each curve's control points swapped.
        var starts = new[] { new Point(240, 80) }.Concat(left.Select(s => s.End)).ToArray();
        for (var i = left.Length - 1; i >= 0; i--) g.CubicBezierTo(M(left[i].C2), M(left[i].C1), M(starts[i]));
        g.EndFigure(true);
        return geometry;
    }
}
