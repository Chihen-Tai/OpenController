using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace XboxControllerCenter.App.Controls;

/// <summary>Attached properties used by styles (e.g. an icon on a button).</summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject d) => (Geometry?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, Geometry? value) => d.SetValue(IconProperty, value);

    /// <summary>
    /// When set on a page's root content, programmatic "bring into view" requests (raised while live data
    /// re-lays out the page) are ignored unless the requesting element has keyboard focus, so pages don't
    /// jump while you read them but keyboard navigation still scrolls focused controls into view.
    /// </summary>
    public static readonly DependencyProperty StableScrollProperty = DependencyProperty.RegisterAttached(
        "StableScroll", typeof(bool), typeof(Ui), new PropertyMetadata(false, OnStableScrollChanged));

    public static bool GetStableScroll(DependencyObject d) => (bool)d.GetValue(StableScrollProperty);
    public static void SetStableScroll(DependencyObject d, bool value) => d.SetValue(StableScrollProperty, value);

    private static void OnStableScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && e.NewValue is true)
            fe.RequestBringIntoView += (_, args) =>
            {
                if (args.OriginalSource is UIElement source && !source.IsKeyboardFocusWithin)
                    args.Handled = true;
            };
    }
}

/// <summary>Draws a 20 × 20 Fluent UI icon geometry in the inherited foreground color, scaled to its size.</summary>
public sealed class IconBlock : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(Geometry), typeof(IconBlock), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(IconBlock), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IconBlock()
    {
        Width = 16;
        Height = 16;
        Focusable = false;
    }

    public Geometry? Glyph { get => (Geometry?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        if (Glyph is null)
            return;
        double s = Math.Min(ActualWidth, ActualHeight) / 20.0;
        dc.PushTransform(new TranslateTransform((ActualWidth - (20 * s)) / 2, (ActualHeight - (20 * s)) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(Foreground, null, Glyph);
        dc.Pop();
        dc.Pop();
    }
}

/// <summary>Card container with a consistent header (icon, title, optional subtitle and actions).</summary>
public sealed class Card : HeaderedContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(Card), new PropertyMetadata(null));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(Card), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(Card), new PropertyMetadata(null));

    static Card()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(typeof(Card)));
        FocusableProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
}

public enum StatusKind
{
    Neutral,
    Ok,
    Info,
    Warning,
    Error,
}

/// <summary>Compact status chip: colored dot or icon plus text.</summary>
public sealed class StatusPill : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(StatusKind), typeof(StatusPill), new PropertyMetadata(StatusKind.Neutral));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata("",
            (d, e) => AutomationProperties.SetName(d, e.NewValue as string ?? "")));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(StatusPill), new PropertyMetadata(null));

    static StatusPill()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusPill), new FrameworkPropertyMetadata(typeof(StatusPill)));
        FocusableProperty.OverrideMetadata(typeof(StatusPill), new FrameworkPropertyMetadata(false));
    }

    public StatusKind Kind { get => (StatusKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

/// <summary>
/// Responsive card layout: as many equal columns as fit <see cref="MinColumnWidth"/>, children flow left to right
/// and may span several columns (<see cref="ColumnSpanProperty"/>). Narrow windows get fewer columns instead of
/// clipped content.
/// </summary>
public sealed class AdaptivePanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(AdaptivePanel), new FrameworkPropertyMetadata(320.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptivePanel), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptivePanel), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(AdaptivePanel), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static int GetColumnSpan(DependencyObject d) => (int)d.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(DependencyObject d, int value) => d.SetValue(ColumnSpanProperty, value);

    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private readonly List<(UIElement Child, Rect Slot)> _slots = [];

    private (int Columns, double ColumnWidth) Columns(double width)
    {
        if (double.IsInfinity(width))
            width = MinColumnWidth * MaxColumns;
        int cols = (int)Math.Max(1, Math.Min(MaxColumns, Math.Floor((width + Spacing) / (MinColumnWidth + Spacing))));
        return (cols, (width - ((cols - 1) * Spacing)) / cols);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        (int cols, double colW) = Columns(availableSize.Width);
        _slots.Clear();
        double y = 0, rowHeight = 0;
        int col = 0;
        var row = new List<(UIElement, int Col, int Span)>();

        void FlushRow()
        {
            foreach ((UIElement child, int c, int span) in row)
                _slots.Add((child, new Rect(c * (colW + Spacing), y, (span * colW) + ((span - 1) * Spacing), rowHeight)));
            if (row.Count > 0)
                y += rowHeight + Spacing;
            row.Clear();
            rowHeight = 0;
            col = 0;
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;
            int span = Math.Clamp(GetColumnSpan(child), 1, cols);
            if (col + span > cols)
                FlushRow();
            double w = (span * colW) + ((span - 1) * Spacing);
            child.Measure(new Size(w, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            row.Add((child, col, span));
            col += span;
        }
        FlushRow();
        double width = double.IsInfinity(availableSize.Width) ? (cols * colW) + ((cols - 1) * Spacing) : availableSize.Width;
        return new Size(width, Math.Max(0, y - Spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(finalSize.Width - DesiredSize.Width) > 0.5)
            MeasureOverride(finalSize);
        foreach ((UIElement child, Rect slot) in _slots)
            child.Arrange(slot);
        return finalSize;
    }
}

/// <summary>Analog trigger depth bar with raw value, optional processed output and activation threshold.</summary>
public sealed class TriggerGauge : ThemedElement
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(TriggerGauge), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RawProperty = DependencyProperty.Register(
        nameof(Raw), typeof(double), typeof(TriggerGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutputProperty = DependencyProperty.Register(
        nameof(Output), typeof(double), typeof(TriggerGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(double), typeof(TriggerGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public TriggerGauge()
    {
        Height = 34;
        AutomationProperties.SetName(this, "Trigger depth");
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    /// <summary>Raw depth 0..1.</summary>
    public double Raw { get => (double)GetValue(RawProperty); set => SetValue(RawProperty, value); }
    /// <summary>Processed output 0..1, or NaN to hide.</summary>
    public double Output { get => (double)GetValue(OutputProperty); set => SetValue(OutputProperty, value); }
    public double Threshold { get => (double)GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 60)
            return;
        double raw = Math.Clamp(Raw, 0, 1);
        DrawText(dc, Label, new Point(0, 0), 12, B("Text.Secondary"), typeface: UiFontSemibold);
        string value = $"{Math.Round(raw * 255)}  ·  {raw * 100:0}%";
        DrawText(dc, value, new Point(w, 0), 12, B("Text.Primary"), HorizontalAlignment.Right, typeface: MonoFont);

        var track = new Rect(0, 20, w, 8);
        dc.DrawRoundedRectangle(B("Bg.Subtle"), P("Border.Card", 1), track, 4, 4);
        if (raw > 0)
            dc.DrawRoundedRectangle(B("Accent"), null, new Rect(0, 20, Math.Max(8, w * raw), 8), 4, 4);
        if (!double.IsNaN(Output))
            dc.DrawRectangle(B("Output"), null, new Rect(0, 30, w * Math.Clamp(Output, 0, 1), 2.5));
        if (!double.IsNaN(Threshold))
        {
            double x = w * Math.Clamp(Threshold, 0, 1);
            dc.DrawLine(P("Warning", 1.5), new Point(x, 17), new Point(x, 31));
        }
    }
}

/// <summary>
/// Battery glyph. A fill proportional to the value is drawn only for real percentages; coarse XInput levels
/// are drawn as 0–3 segments; unknown shows an empty outline. Nothing is ever invented.
/// </summary>
public sealed class BatteryGauge : ThemedElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(BatteryGauge), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CoarseSegmentsProperty = DependencyProperty.Register(
        nameof(CoarseSegments), typeof(int), typeof(BatteryGauge), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsChargingProperty = DependencyProperty.Register(
        nameof(IsCharging), typeof(bool), typeof(BatteryGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsLowProperty = DependencyProperty.Register(
        nameof(IsLow), typeof(bool), typeof(BatteryGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public BatteryGauge()
    {
        Width = 64;
        Height = 30;
        AutomationProperties.SetName(this, "Battery level");
    }

    /// <summary>0..100, or negative when no percentage is known.</summary>
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    /// <summary>0..3 lit segments for coarse levels, or -1.</summary>
    public int CoarseSegments { get => (int)GetValue(CoarseSegmentsProperty); set => SetValue(CoarseSegmentsProperty, value); }
    public bool IsCharging { get => (bool)GetValue(IsChargingProperty); set => SetValue(IsChargingProperty, value); }
    public bool IsLow { get => (bool)GetValue(IsLowProperty); set => SetValue(IsLowProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 10)
            return;
        var body = new Rect(1, 1, w - 7, h - 2);
        dc.DrawRoundedRectangle(null, P("Text.Secondary", 1.5), body, 5, 5);
        dc.DrawRoundedRectangle(B("Text.Secondary"), null, new Rect(w - 5, (h / 2) - 5, 4, 10), 1.5, 1.5);
        Rect inner = new(body.X + 3, body.Y + 3, body.Width - 6, body.Height - 6);
        Brush fill = IsLow ? B("Warning") : B("Accent");

        if (Percent >= 0)
        {
            double p = Math.Clamp(Percent, 0, 100) / 100;
            if (p > 0)
                dc.DrawRoundedRectangle(fill, null, new Rect(inner.X, inner.Y, Math.Max(3, inner.Width * p), inner.Height), 2.5, 2.5);
        }
        else if (CoarseSegments >= 0)
        {
            const int segments = 3;
            double gap = 2.5, segW = (inner.Width - ((segments - 1) * gap)) / segments;
            for (int i = 0; i < segments; i++)
            {
                var r = new Rect(inner.X + (i * (segW + gap)), inner.Y, segW, inner.Height);
                dc.DrawRoundedRectangle(i < CoarseSegments ? fill : B("Bg.Subtle"), null, r, 2, 2);
            }
        }
        else
        {
            dc.DrawLine(P("Text.Tertiary", 1.5), new Point(inner.X + (inner.Width * 0.35), inner.Y + (inner.Height / 2)),
                new Point(inner.X + (inner.Width * 0.65), inner.Y + (inner.Height / 2)));
        }

        if (IsCharging)
        {
            // Lightning bolt.
            double cx = body.X + (body.Width / 2), cy = h / 2;
            var bolt = Geometry.Parse($"M {cx + 2},{cy - 9} L {cx - 5},{cy + 1} L {cx},{cy + 1} L {cx - 2},{cy + 9} L {cx + 5},{cy - 1} L {cx},{cy - 1} Z");
            dc.DrawGeometry(B("Text.Primary"), P("Bg.Card", 1.2), bolt);
        }
    }
}
