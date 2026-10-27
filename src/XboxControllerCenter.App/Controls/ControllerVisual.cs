using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.App.Controls;

/// <summary>
/// Interactive front view of an Xbox Series X|S style controller, drawn entirely from vector paths
/// (original artwork; proportions measured from product photos, no third-party images or SVGs used).
/// <para>
/// Layout follows the real hardware: left stick upper-left, hybrid dish D-pad lower-left, right stick
/// lower-right, ABXY upper-right (A bottom/green, B right/red, X left/blue, Y top/yellow), Xbox button
/// top-center, View/Menu left/right of center, Share below center, LB/RB on the shoulders, LT/RT behind them.
/// </para>
/// The drawing is defined in a fixed 640 × 450 design space and scaled uniformly (aspect ratio is never
/// distorted). Everything is vector geometry, so it stays sharp at any DPI.
/// </summary>
public sealed class ControllerVisual : ThemedElement
{
    public const double DesignWidth = 640;
    public const double DesignHeight = 450;
    private const double StickTravel = 12;

    // Positions in design space.
    private static readonly Point LeftStickCenter = new(170, 140);
    private static readonly Point RightStickCenter = new(396, 231);
    private static readonly Point DPadCenter = new(245, 240);
    private static readonly Point FaceCenter = new(468, 151);
    private static readonly Point GuideCenter = new(320, 93);
    private static readonly Point ViewCenter = new(278, 150);
    private static readonly Point MenuCenter = new(362, 150);
    private static readonly Point ShareCenter = new(320, 181);
    private const double FaceSpacing = 36;

    private static readonly Geometry s_body = BuildBody();
    // Bumper: a band that wraps over the shoulder; its lower edge sits slightly inside the body outline.
    private static readonly Geometry s_leftBumper = Geometry.Parse(
        "M 240,59 C 236,48 214,40 182,39 C 150,38 124,50 110,66 C 101,77 97,90 99,100 C 100,104 102,106 104,104 " +
        "C 112,90 124,80 136,74 C 162,64 204,60 240,61 Z");
    private static readonly Geometry s_rightBumper = Mirror(s_leftBumper);
    // Trigger: only its top sliver is visible behind the bumper in a front view.
    private static readonly Geometry s_leftTrigger = Geometry.Parse(
        "M 146,50 C 150,36 166,27 188,26 C 209,25 223,32 229,44 L 231,58 L 146,60 Z");
    private static readonly Geometry s_rightTrigger = Mirror(s_leftTrigger);
    private static readonly Geometry s_dpadCross = BuildCross(DPadCenter, armWidth: 26, halfLength: 38, radius: 5);

    private enum Part { LB, RB, A, B, X, Y, Up, Down, Left, Right, View, Menu, Share, Guide, L3, R3, Count }

    private readonly double[] _intensity = new double[(int)Part.Count];
    private readonly bool[] _pressed = new bool[(int)Part.Count];
    private long _lastTick;
    private bool _animating;

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(GamepadState), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(default(GamepadState), FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((ControllerVisual)d).OnStateChanged()));

    public static readonly DependencyProperty OutputStateProperty = DependencyProperty.Register(
        nameof(OutputState), typeof(GamepadState), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(default(GamepadState), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowOutputProperty = DependencyProperty.Register(
        nameof(ShowOutput), typeof(bool), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsConnectedProperty = DependencyProperty.Register(
        nameof(IsConnected), typeof(bool), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((ControllerVisual)d).OnStateChanged()));

    public static readonly DependencyProperty ShowGuideProperty = DependencyProperty.Register(
        nameof(ShowGuide), typeof(bool), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(
        nameof(ShowLabels), typeof(bool), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
        nameof(Family), typeof(ControllerFamily), typeof(ControllerVisual),
        new FrameworkPropertyMetadata(ControllerFamily.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

    public ControllerVisual()
    {
        AutomationProperties.SetName(this, "Controller input visualization");
        Unloaded += (_, _) => StopAnimation();
    }

    /// <summary>Raw input to display.</summary>
    public GamepadState State { get => (GamepadState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>Processed output; its stick positions are drawn as rings when <see cref="ShowOutput"/> is set.</summary>
    public GamepadState OutputState { get => (GamepadState)GetValue(OutputStateProperty); set => SetValue(OutputStateProperty, value); }

    public bool ShowOutput { get => (bool)GetValue(ShowOutputProperty); set => SetValue(ShowOutputProperty, value); }

    public bool IsConnected { get => (bool)GetValue(IsConnectedProperty); set => SetValue(IsConnectedProperty, value); }

    /// <summary>Whether the input API reports the Xbox button (drawn dashed otherwise).</summary>
    public bool ShowGuide { get => (bool)GetValue(ShowGuideProperty); set => SetValue(ShowGuideProperty, value); }

    public bool ShowLabels { get => (bool)GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }

    /// <summary>
    /// Controller family whose button names (and face-button colors) are printed. The drawing keeps the Xbox
    /// silhouette; buttons are matched by position (PlayStation Cross / Nintendo B sit where Xbox A sits).
    /// </summary>
    public ControllerFamily Family { get => (ControllerFamily)GetValue(FamilyProperty); set => SetValue(FamilyProperty, value); }

    // ------------------------------------------------------------------ geometry

    /// <summary>Body outline: left half as cubic Béziers (top-center → bottom-center), mirrored for the right half.</summary>
    private static Geometry BuildBody()
    {
        Point[] left =
        [
            // start, then (c1, c2, end) triples
            new(320, 53),
            new(290, 52), new(258, 52), new(232, 54),
            new(196, 57), new(150, 62), new(124, 80),
            new(106, 92), new(96, 110), new(88, 132),
            new(74, 170), new(56, 228), new(46, 280),
            new(38, 318), new(34, 348), new(37, 372),
            new(40, 402), new(56, 428), new(80, 435),
            new(94, 439), new(108, 435), new(118, 426),
            new(138, 406), new(160, 368), new(186, 341),
            new(202, 327), new(218, 323), new(242, 322),
            new(272, 321), new(300, 321), new(320, 321),
        ];

        var figure = new PathFigure { StartPoint = left[0], IsClosed = true, IsFilled = true };
        for (int i = 1; i < left.Length; i += 3)
            figure.Segments.Add(new BezierSegment(left[i], left[i + 1], left[i + 2], true));

        // Right half: walk the left segments backwards, mirrored around x = 320.
        static Point M(Point p) => new(DesignWidth - p.X, p.Y);
        for (int i = left.Length - 1; i > 0; i -= 3)
            figure.Segments.Add(new BezierSegment(M(left[i - 1]), M(left[i - 2]), M(left[i - 3]), true));

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Mirror(Geometry g)
    {
        Geometry m = g.Clone();
        m.Transform = new MatrixTransform(-1, 0, 0, 1, DesignWidth, 0);
        PathGeometry flat = m.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
        flat.Freeze();
        return flat;
    }

    private static Geometry BuildCross(Point c, double armWidth, double halfLength, double radius)
    {
        double w = armWidth / 2;
        var vertical = new RectangleGeometry(new Rect(c.X - w, c.Y - halfLength, armWidth, halfLength * 2), radius, radius);
        var horizontal = new RectangleGeometry(new Rect(c.X - halfLength, c.Y - w, halfLength * 2, armWidth), radius, radius);
        PathGeometry cross = Geometry.Combine(vertical, horizontal, GeometryCombineMode.Union, null);
        cross.Freeze();
        return cross;
    }

    // ------------------------------------------------------------------ pressed-state animation

    private void OnStateChanged()
    {
        GamepadState s = IsConnected ? State : default;
        SetPressed(Part.LB, s, GamepadButtons.LeftShoulder);
        SetPressed(Part.RB, s, GamepadButtons.RightShoulder);
        SetPressed(Part.A, s, GamepadButtons.A);
        SetPressed(Part.B, s, GamepadButtons.B);
        SetPressed(Part.X, s, GamepadButtons.X);
        SetPressed(Part.Y, s, GamepadButtons.Y);
        SetPressed(Part.Up, s, GamepadButtons.DPadUp);
        SetPressed(Part.Down, s, GamepadButtons.DPadDown);
        SetPressed(Part.Left, s, GamepadButtons.DPadLeft);
        SetPressed(Part.Right, s, GamepadButtons.DPadRight);
        SetPressed(Part.View, s, GamepadButtons.View);
        SetPressed(Part.Menu, s, GamepadButtons.Menu);
        SetPressed(Part.Share, s, GamepadButtons.Share);
        SetPressed(Part.Guide, s, GamepadButtons.Guide);
        SetPressed(Part.L3, s, GamepadButtons.LeftThumb);
        SetPressed(Part.R3, s, GamepadButtons.RightThumb);
    }

    private void SetPressed(Part part, GamepadState s, GamepadButtons button)
    {
        bool down = (s.Buttons & button) != 0;
        _pressed[(int)part] = down;
        if (down)
            _intensity[(int)part] = 1; // press shows instantly
        else if (_intensity[(int)part] > 0)
            StartAnimation(); // release fades out
    }

    private void StartAnimation()
    {
        if (_animating)
            return;
        _animating = true;
        _lastTick = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnAnimationFrame;
    }

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnAnimationFrame;
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds;
        _lastTick = now;
        bool any = false;
        for (int i = 0; i < _intensity.Length; i++)
        {
            if (_pressed[i] || _intensity[i] <= 0)
                continue;
            _intensity[i] = Math.Max(0, _intensity[i] - (dt / 0.12));
            any |= _intensity[i] > 0;
        }
        InvalidateVisual();
        if (!any)
            StopAnimation();
    }

    private double I(Part p) => _intensity[(int)p];

    // ------------------------------------------------------------------ layout

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? DesignWidth : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? DesignHeight : availableSize.Height;
        double scale = Math.Min(w / DesignWidth, h / DesignHeight);
        return new Size(DesignWidth * scale, DesignHeight * scale);
    }

    // ------------------------------------------------------------------ rendering

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth / DesignWidth, ActualHeight / DesignHeight);
        if (scale <= 0 || double.IsNaN(scale))
            return;

        dc.PushTransform(new TranslateTransform((ActualWidth - (DesignWidth * scale)) / 2, (ActualHeight - (DesignHeight * scale)) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushOpacity(IsConnected ? 1.0 : 0.45);

        GamepadState s = IsConnected ? State : default;
        Brush accent = B("Accent");

        ControllerFamily family = Family;
        string L(GamepadControl c) => c.ShortLabel(family);

        DrawTrigger(dc, s_leftTrigger, s.LeftTrigger, new Point(188, 8), L(GamepadControl.LeftTrigger));
        DrawTrigger(dc, s_rightTrigger, s.RightTrigger, new Point(DesignWidth - 188, 8), L(GamepadControl.RightTrigger));
        DrawBody(dc);
        DrawBumper(dc, s_leftBumper, I(Part.LB), L(GamepadControl.LeftShoulder), new Point(94, 72), HorizontalAlignment.Right);
        DrawBumper(dc, s_rightBumper, I(Part.RB), L(GamepadControl.RightShoulder), new Point(DesignWidth - 94, 72), HorizontalAlignment.Left);

        DrawStick(dc, LeftStickCenter, s.LeftX, s.LeftY, I(Part.L3), OutputState.LeftX, OutputState.LeftY);
        DrawStick(dc, RightStickCenter, s.RightX, s.RightY, I(Part.R3), OutputState.RightX, OutputState.RightY);
        DrawDPad(dc, accent);

        DrawFaceButton(dc, new Point(FaceCenter.X, FaceCenter.Y + FaceSpacing), L(GamepadControl.A), FaceColorKey(family, GamepadControl.A), I(Part.A));
        DrawFaceButton(dc, new Point(FaceCenter.X + FaceSpacing, FaceCenter.Y), L(GamepadControl.B), FaceColorKey(family, GamepadControl.B), I(Part.B));
        DrawFaceButton(dc, new Point(FaceCenter.X - FaceSpacing, FaceCenter.Y), L(GamepadControl.X), FaceColorKey(family, GamepadControl.X), I(Part.X));
        DrawFaceButton(dc, new Point(FaceCenter.X, FaceCenter.Y - FaceSpacing), L(GamepadControl.Y), FaceColorKey(family, GamepadControl.Y), I(Part.Y));

        DrawRoundButton(dc, ViewCenter, I(Part.View), DrawViewGlyph);
        DrawRoundButton(dc, MenuCenter, I(Part.Menu), DrawMenuGlyph);
        DrawShareButton(dc, I(Part.Share));
        DrawGuide(dc, I(Part.Guide));

        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private void DrawBody(DrawingContext dc)
    {
        // Soft drop shadow built from a few offset layers (cheap, no bitmap effects).
        Color shadow = C("Pad.Shadow");
        for (int i = 4; i >= 1; i--)
        {
            dc.PushTransform(new TranslateTransform(0, 3 * i));
            dc.DrawGeometry(new SolidColorBrush(WithAlpha(shadow, (byte)(ThemeManagerIsDark ? 34 : 14))), null, s_body);
            dc.Pop();
        }

        // Lower rim (case thickness seen from slightly above).
        dc.PushTransform(new TranslateTransform(0, 5));
        dc.DrawGeometry(B("Pad.Rim"), null, s_body);
        dc.Pop();

        var bodyFill = new LinearGradientBrush(C("Pad.BodyTop"), C("Pad.BodyBottom"), new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawGeometry(bodyFill, P("Pad.Outline", 1.2), s_body);

        // Gentle top-center sheen and a thin highlight just inside the upper edge.
        dc.PushClip(s_body);
        var sheen = new RadialGradientBrush(WithAlpha(C("Pad.Highlight"), (byte)(C("Pad.Highlight").A / 3)), Colors.Transparent)
        {
            Center = new Point(0.5, 0.18),
            GradientOrigin = new Point(0.5, 0.1),
            RadiusX = 0.45,
            RadiusY = 0.45,
        };
        dc.DrawRectangle(sheen, null, new Rect(0, 0, DesignWidth, DesignHeight));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, DesignWidth, 150)));
        dc.PushTransform(new TranslateTransform(0, 1.6));
        dc.DrawGeometry(null, P("Pad.Highlight", 1.2), s_body);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private bool ThemeManagerIsDark => Services.ThemeManager.IsDark;

    private void DrawTrigger(DrawingContext dc, Geometry trigger, byte value, Point labelAt, string label)
    {
        dc.DrawGeometry(B("Pad.Trigger"), P("Pad.Outline", 1), trigger);
        double v = value / 255.0;
        if (v > 0)
        {
            // Analog depth: fill grows upward over the part that is visible above the bumper.
            Rect bounds = trigger.Bounds;
            const double visibleBottom = 52;
            double h = (visibleBottom - bounds.Top) * v;
            dc.PushClip(trigger);
            dc.DrawRectangle(B("Accent"), null, new Rect(bounds.X, visibleBottom - h, bounds.Width, bounds.Bottom - visibleBottom + h));
            dc.Pop();
        }
        if (ShowLabels)
            DrawText(dc, $"{label} {Math.Round(v * 100)}%", labelAt, 10.5, B("Pad.Label"), HorizontalAlignment.Center);
    }

    private void DrawBumper(DrawingContext dc, Geometry bumper, double intensity, string label, Point labelAt, HorizontalAlignment align)
    {
        var fill = new LinearGradientBrush(C("Pad.Bumper"), C("Pad.BodyBottom"), new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawGeometry(fill, P("Pad.Outline", 1), bumper);
        if (intensity > 0)
        {
            dc.PushOpacity(intensity);
            dc.DrawGeometry(B("Accent"), null, bumper);
            dc.Pop();
        }
        if (ShowLabels)
            DrawText(dc, label, labelAt, 10.5, B("Pad.Label"), align, VerticalAlignment.Center);
    }

    private void DrawStick(DrawingContext dc, Point c, short rawX, short rawY, double pressed, short outX, short outY)
    {
        // Raised collar around the opening.
        var collar = new RadialGradientBrush(C("Pad.WellRing"), C("Pad.BodyBottom"))
        {
            GradientOrigin = new Point(0.4, 0.25),
            Center = new Point(0.5, 0.45),
            RadiusX = 0.6,
            RadiusY = 0.6,
        };
        dc.DrawEllipse(collar, P("Pad.Outline", 1), c, 38, 38);
        dc.DrawEllipse(B("Pad.Well"), null, c, 31, 31);

        double nx = Normalize(rawX), ny = Normalize(rawY);
        var cap = new Point(c.X + (nx * StickTravel), c.Y - (ny * StickTravel));

        // Cap shadow, cap, concave dish, rim texture.
        dc.DrawEllipse(new SolidColorBrush(WithAlpha(C("Pad.Shadow"), 80)), null, new Point(cap.X + 1.5, cap.Y + 3), 26, 26);
        var capFill = new RadialGradientBrush(C("Pad.CapTop"), C("Pad.CapBottom"))
        {
            GradientOrigin = new Point(0.35, 0.25),
            Center = new Point(0.45, 0.4),
            RadiusX = 0.65,
            RadiusY = 0.65,
        };
        dc.DrawEllipse(capFill, P("Pad.Outline", 1), cap, 26, 26);
        Brush texture = new SolidColorBrush(WithAlpha(C("Pad.Glyph"), 70));
        for (int i = 0; i < 36; i++)
        {
            double a = i * Math.PI * 2 / 36;
            dc.DrawEllipse(texture, null, new Point(cap.X + (Math.Cos(a) * 22.3), cap.Y + (Math.Sin(a) * 22.3)), 0.75, 0.75);
        }
        var dish = new LinearGradientBrush(C("Pad.CapBottom"), C("Pad.CapDish"), new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawEllipse(dish, P("Pad.WellRing", 0.8), new Point(cap.X, cap.Y + 0.5), 17, 17);

        if (pressed > 0)
        {
            dc.PushOpacity(pressed);
            dc.DrawEllipse(null, P("Accent", 3), cap, 27.5, 27.5);
            dc.DrawEllipse(B("Accent.Soft"), null, cap, 26, 26);
            dc.Pop();
        }

        if (ShowOutput)
        {
            var o = new Point(c.X + (Normalize(outX) * StickTravel), c.Y - (Normalize(outY) * StickTravel));
            dc.DrawEllipse(null, P("Output", 2), o, 6, 6);
        }
    }

    private void DrawDPad(DrawingContext dc, Brush accent)
    {
        Point c = DPadCenter;
        // Hybrid dish: a shallow circular well around the cross.
        var dish = new RadialGradientBrush(C("Pad.Well"), C("Pad.BodyBottom"))
        {
            GradientOrigin = new Point(0.5, 0.35),
            RadiusX = 0.6,
            RadiusY = 0.6,
        };
        dc.DrawEllipse(dish, P("Pad.Outline", 1), c, 46, 46);
        dc.DrawEllipse(null, P("Pad.WellRing", 1), c, 42.5, 42.5);

        var face = new LinearGradientBrush(C("Pad.CapTop"), C("Pad.ButtonFace"), new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawGeometry(face, P("Pad.Outline", 1), s_dpadCross);

        // Facets: subtle lines from the center square to the arm edges.
        Pen facet = P("Pad.WellRing", 0.8);
        const double w = 13;
        dc.DrawRectangle(new SolidColorBrush(WithAlpha(C("Pad.Shadow"), 40)), null, new Rect(c.X - w, c.Y - w, 2 * w, 2 * w));
        dc.DrawLine(facet, new Point(c.X - w, c.Y - w), new Point(c.X + w, c.Y - w));
        dc.DrawLine(facet, new Point(c.X - w, c.Y + w), new Point(c.X + w, c.Y + w));
        dc.DrawLine(facet, new Point(c.X - w, c.Y - w), new Point(c.X - w, c.Y + w));
        dc.DrawLine(facet, new Point(c.X + w, c.Y - w), new Point(c.X + w, c.Y + w));

        HighlightArm(dc, accent, I(Part.Up), new Rect(c.X - w, c.Y - 38, 2 * w, 38 - w));
        HighlightArm(dc, accent, I(Part.Down), new Rect(c.X - w, c.Y + w, 2 * w, 38 - w));
        HighlightArm(dc, accent, I(Part.Left), new Rect(c.X - 38, c.Y - w, 38 - w, 2 * w));
        HighlightArm(dc, accent, I(Part.Right), new Rect(c.X + w, c.Y - w, 38 - w, 2 * w));
    }

    private static void HighlightArm(DrawingContext dc, Brush accent, double intensity, Rect arm)
    {
        if (intensity <= 0)
            return;
        dc.PushClip(s_dpadCross);
        dc.PushOpacity(intensity);
        dc.DrawRectangle(accent, null, arm);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Xbox: green/red/blue/yellow; PlayStation: blue Cross, red Circle, pink Square, green Triangle; Nintendo: plain.</summary>
    private static string FaceColorKey(ControllerFamily family, GamepadControl control) => family.LabelStyle() switch
    {
        ButtonLabelStyle.PlayStation => control switch
        {
            GamepadControl.A => "Pad.FaceX",
            GamepadControl.B => "Pad.FaceB",
            GamepadControl.X => "Pad.FaceSquare",
            _ => "Pad.FaceA",
        },
        ButtonLabelStyle.Nintendo => "Pad.FacePlain",
        _ => "Pad.Face" + control,
    };

    private void DrawFaceButton(DrawingContext dc, Point c, string letter, string colorKey, double pressed)
    {
        const double r = 16.5;
        dc.DrawEllipse(new SolidColorBrush(WithAlpha(C("Pad.Shadow"), 70)), null, new Point(c.X + 0.8, c.Y + 2), r, r);
        var face = new RadialGradientBrush(C("Pad.CapTop"), C("Pad.ButtonFace"))
        {
            GradientOrigin = new Point(0.35, 0.25),
            RadiusX = 0.7,
            RadiusY = 0.7,
        };
        dc.DrawEllipse(face, P("Pad.Outline", 1), c, r, r);
        dc.DrawEllipse(null, P("Pad.ButtonRing", 0.8), c, r - 1.5, r - 1.5);
        DrawFaceMark(dc, c, letter, B(colorKey));

        if (pressed > 0)
        {
            dc.PushOpacity(pressed);
            dc.DrawEllipse(B(colorKey), null, c, r, r);
            DrawFaceMark(dc, c, letter, Brushes.White);
            dc.Pop();
        }
    }

    /// <summary>A letter, or for PlayStation symbols an equally sized vector shape (font glyphs vary too much in size).</summary>
    private void DrawFaceMark(DrawingContext dc, Point c, string letter, Brush brush)
    {
        var pen = new Pen(brush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        const double h = 6.5;
        switch (letter)
        {
            case "✕":
                dc.DrawLine(pen, new Point(c.X - h, c.Y - h), new Point(c.X + h, c.Y + h));
                dc.DrawLine(pen, new Point(c.X - h, c.Y + h), new Point(c.X + h, c.Y - h));
                break;
            case "○":
                dc.DrawEllipse(null, pen, c, h + 0.5, h + 0.5);
                break;
            case "□":
                dc.DrawRectangle(null, pen, new Rect(c.X - h + 0.5, c.Y - h + 0.5, 2 * h - 1, 2 * h - 1));
                break;
            case "△":
                var tri = new StreamGeometry();
                using (StreamGeometryContext g = tri.Open())
                {
                    g.BeginFigure(new Point(c.X, c.Y - h - 0.5), false, true);
                    g.LineTo(new Point(c.X + h + 1, c.Y + h - 1), true, true);
                    g.LineTo(new Point(c.X - h - 1, c.Y + h - 1), true, true);
                }
                tri.Freeze();
                dc.DrawGeometry(null, pen, tri);
                break;
            default:
                DrawText(dc, letter, new Point(c.X, c.Y + 0.5), 17, brush, HorizontalAlignment.Center, VerticalAlignment.Center, UiFontSemibold);
                break;
        }
    }

    private void DrawRoundButton(DrawingContext dc, Point c, double pressed, Action<DrawingContext, Point, Pen> glyph)
    {
        const double r = 11.5;
        var face = new RadialGradientBrush(C("Pad.CapTop"), C("Pad.ButtonFace")) { GradientOrigin = new Point(0.35, 0.25) };
        dc.DrawEllipse(face, P("Pad.Outline", 1), c, r, r);
        glyph(dc, c, P("Pad.Glyph", 1.3));
        if (pressed > 0)
        {
            dc.PushOpacity(pressed);
            dc.DrawEllipse(B("Accent"), null, c, r, r);
            glyph(dc, c, P("Accent.OnAccent", 1.3));
            dc.Pop();
        }
    }

    private static void DrawViewGlyph(DrawingContext dc, Point c, Pen pen)
    {
        // Two overlapping windows.
        dc.DrawRoundedRectangle(null, pen, new Rect(c.X - 4.5, c.Y - 3.8, 6.5, 5), 1, 1);
        dc.DrawRoundedRectangle(null, pen, new Rect(c.X - 1.8, c.Y - 1.2, 6.5, 5), 1, 1);
    }

    private static void DrawMenuGlyph(DrawingContext dc, Point c, Pen pen)
    {
        for (int i = -1; i <= 1; i++)
            dc.DrawLine(pen, new Point(c.X - 4.5, c.Y + (i * 3)), new Point(c.X + 4.5, c.Y + (i * 3)));
    }

    private void DrawShareButton(DrawingContext dc, double pressed)
    {
        var rect = new Rect(ShareCenter.X - 16, ShareCenter.Y - 8.5, 32, 17);
        var face = new LinearGradientBrush(C("Pad.CapTop"), C("Pad.ButtonFace"), new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawRoundedRectangle(face, P("Pad.Outline", 1), rect, 8.5, 8.5);
        void Glyph(Pen pen)
        {
            // Tray with an upward arrow.
            Point c = ShareCenter;
            dc.DrawLine(pen, new Point(c.X - 5, c.Y), new Point(c.X - 5, c.Y + 3.5));
            dc.DrawLine(pen, new Point(c.X - 5, c.Y + 3.5), new Point(c.X + 5, c.Y + 3.5));
            dc.DrawLine(pen, new Point(c.X + 5, c.Y + 3.5), new Point(c.X + 5, c.Y));
            dc.DrawLine(pen, new Point(c.X, c.Y + 1.5), new Point(c.X, c.Y - 4.5));
            dc.DrawLine(pen, new Point(c.X - 2.5, c.Y - 2), new Point(c.X, c.Y - 4.5));
            dc.DrawLine(pen, new Point(c.X + 2.5, c.Y - 2), new Point(c.X, c.Y - 4.5));
        }
        Glyph(P("Pad.Glyph", 1.2));
        if (pressed > 0)
        {
            dc.PushOpacity(pressed);
            dc.DrawRoundedRectangle(B("Accent"), null, rect, 8.5, 8.5);
            Glyph(P("Accent.OnAccent", 1.2));
            dc.Pop();
        }
    }

    private void DrawGuide(DrawingContext dc, double pressed)
    {
        const double r = 21;
        Point c = GuideCenter;
        var face = new RadialGradientBrush(C("Pad.Guide"), C("Pad.Guide"))
        {
            GradientOrigin = new Point(0.35, 0.3),
        };
        face.GradientStops[1].Color = Blend(C("Pad.Guide"), C("Pad.Shadow"), 0.18);
        dc.DrawEllipse(face, ShowGuide ? P("Pad.Outline", 1) : P("Pad.Outline", 1, dashed: true), c, r, r);
        // Neutral "home" mark (deliberately not the Xbox logo).
        dc.DrawEllipse(null, P("Pad.GuideGlyph", 2), c, 9.5, 9.5);
        dc.DrawEllipse(B("Pad.GuideGlyph"), null, c, 2.5, 2.5);
        if (pressed > 0)
        {
            dc.PushOpacity(pressed);
            dc.DrawEllipse(B("Accent"), null, c, r, r);
            dc.DrawEllipse(null, P("Accent.OnAccent", 2), c, 9.5, 9.5);
            dc.Pop();
        }
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + ((b.A - a.A) * t)), (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    private static double Normalize(short v) => v >= 0 ? v / 32767.0 : v / 32768.0;
}
