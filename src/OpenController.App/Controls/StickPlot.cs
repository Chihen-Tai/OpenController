using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using OpenController.App.Localization;
using OpenController.Models;

namespace OpenController.App.Controls;

/// <summary>Mutable data for a <see cref="StickPlot"/>, owned and filled by a view model on the UI thread.</summary>
public sealed class StickPlotData
{
    public StickPlotData(int trailCapacity = 600)
    {
        Trail = new StickVector[trailCapacity];
        SecondaryTrail = new StickVector[trailCapacity];
    }

    /// <summary>Primary point (raw input).</summary>
    public StickVector Current { get; set; }
    /// <summary>Optional overlay point (processed output).</summary>
    public StickVector? Secondary { get; set; }
    public StickVector[] Trail { get; }
    public int TrailCount { get; set; }
    public StickVector[] SecondaryTrail { get; }
    public int SecondaryTrailCount { get; set; }
    public double InnerDeadzone { get; set; }
    public double OuterDeadzone { get; set; }
    /// <summary>Circularity bins (max radius per angle) to outline, or null.</summary>
    public double[]? Bins { get; set; }
    public bool ShowSquareGate { get; set; } = true;
    /// <summary>Largest value shown at the plot edge (1 = full range; 0.25 zooms in on the center for drift).</summary>
    public double Range { get; set; } = 1.0;
}

/// <summary>
/// 2-D stick position plot: unit circle and square gate, ±0.5 grid with axis labels, deadzone (red) and outer
/// deadzone (amber) rings, raw trail/point (neutral) and processed trail/point (blue), optional circularity outline.
/// </summary>
public sealed class StickPlot : ThemedElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(StickPlotData), typeof(StickPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(long), typeof(StickPlot), new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLegendProperty = DependencyProperty.Register(
        nameof(ShowLegend), typeof(bool), typeof(StickPlot), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public StickPlot()
    {
        AutomationProperties.SetName(this, "Stick position plot");
    }

    public StickPlotData? Data { get => (StickPlotData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public long Revision { get => (long)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public bool ShowLegend { get => (bool)GetValue(ShowLegendProperty); set => SetValue(ShowLegendProperty, value); }

    private double LegendHeight => ShowLegend ? 20 : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? w + LegendHeight : availableSize.Height;
        double side = Math.Min(w, h - LegendHeight);
        return new Size(side, side + LegendHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight - LegendHeight);
        if (side < 60)
            return;
        double ox = (ActualWidth - side) / 2;
        var area = new Rect(ox, 0, side, side);
        dc.DrawRoundedRectangle(B("Plot.Background"), P("Border.Card", 1), area, 8, 8);

        const double pad = 22; // room for tick labels
        double r = (side / 2) - pad;
        var c = new Point(ox + (side / 2), side / 2);
        StickPlotData? d = Data;
        double range = d?.Range is > 0 and var rg ? rg : 1.0;
        double k = r / range; // pixels per unit
        Point Map(StickVector v) => new(c.X + (Math.Clamp(v.X / range, -1.45, 1.45) * r), c.Y - (Math.Clamp(v.Y / range, -1.45, 1.45) * r));

        // Grid ±0.5, axes, unit circle, square gate.
        Pen grid = P("Plot.Grid", 1), axis = P("Plot.Axis", 1);
        foreach (double g in new[] { -0.5, 0.5 })
        {
            dc.DrawLine(grid, new Point(c.X + (g * r), c.Y - r), new Point(c.X + (g * r), c.Y + r));
            dc.DrawLine(grid, new Point(c.X - r, c.Y - (g * r)), new Point(c.X + r, c.Y - (g * r)));
        }
        if (d?.ShowSquareGate != false && range >= 1)
            dc.DrawRectangle(null, P("Plot.Grid", 1, dashed: true), new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
        dc.DrawLine(axis, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(axis, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
        dc.DrawEllipse(null, axis, c, r, r);

        Brush label = B("Plot.Label");
        string max = range.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        DrawText(dc, max, new Point(c.X + r + 3, c.Y), 9.5, label, v: VerticalAlignment.Center);
        DrawText(dc, "-" + max, new Point(c.X - r - 3, c.Y), 9.5, label, HorizontalAlignment.Right, VerticalAlignment.Center);
        DrawText(dc, max, new Point(c.X - 4, c.Y - r - 1), 9.5, label, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        DrawText(dc, "-" + max, new Point(c.X - 4, c.Y + r + 1), 9.5, label, HorizontalAlignment.Right);
        // Axis names sit on the opposite side of the axis from the tick labels so they never overlap.
        DrawText(dc, "X", new Point(c.X + r, c.Y + 2), 9.5, label, HorizontalAlignment.Right);
        DrawText(dc, "Y", new Point(c.X + 4, c.Y - r - 1), 9.5, label, HorizontalAlignment.Left, VerticalAlignment.Bottom);

        if (d is not null)
        {
            if (d.InnerDeadzone > 0)
                dc.DrawEllipse(B("Plot.Deadzone"), P("Danger", 1, dashed: true), c, Math.Min(d.InnerDeadzone * k, r * 1.45), Math.Min(d.InnerDeadzone * k, r * 1.45));
            if (d.OuterDeadzone > 0 && range >= 1)
                dc.DrawEllipse(null, P("Warning", 1.2, dashed: true), c, (1 - d.OuterDeadzone) * k, (1 - d.OuterDeadzone) * k);
            if (d.Bins is { Length: > 2 } bins)
                DrawBins(dc, bins, c, r);

            Brush rawTrail = B("Plot.RawTrail"), outTrail = B("Plot.OutputTrail");
            for (int i = 0; i < d.TrailCount; i++)
                dc.DrawEllipse(rawTrail, null, Map(d.Trail[i]), 1.2, 1.2);
            for (int i = 0; i < d.SecondaryTrailCount; i++)
                dc.DrawEllipse(outTrail, null, Map(d.SecondaryTrail[i]), 1.2, 1.2);

            Point p = Map(d.Current);
            dc.DrawLine(P("Plot.Raw", 1.2), c, p);
            dc.DrawEllipse(B("Plot.Raw"), P("Plot.Background", 1.5), p, 5, 5);
            if (d.Secondary is { } s)
                dc.DrawEllipse(null, P("Output", 2), Map(s), 7, 7);
        }

        if (ShowLegend)
            DrawLegend(dc, ox, side + 4);
    }

    private void DrawLegend(DrawingContext dc, double x, double y)
    {
        Brush text = B("Text.Secondary");
        double cx = x + 6;
        void Item(Action<Point> glyph, string label)
        {
            glyph(new Point(cx + 4, y + 7));
            FormattedText ft = Text(label, 11, text);
            dc.DrawText(ft, new Point(cx + 12, y));
            cx += 12 + ft.Width + 12;
        }
        Item(p => dc.DrawEllipse(B("Plot.Raw"), null, p, 4, 4), Loc.T("Plot.Raw"));
        Item(p => dc.DrawEllipse(null, P("Output", 2), p, 4, 4), Loc.T("Plot.Output"));
        Item(p => dc.DrawEllipse(B("Plot.Deadzone"), P("Danger", 1), p, 4, 4), Loc.T("Plot.Deadzone"));
    }

    private void DrawBins(DrawingContext dc, double[] bins, Point c, double r)
    {
        var geo = new StreamGeometry();
        using (StreamGeometryContext g = geo.Open())
        {
            bool started = false;
            for (int i = 0; i <= bins.Length; i++)
            {
                double radius = bins[i % bins.Length];
                if (double.IsNaN(radius))
                {
                    started = false;
                    continue;
                }
                double a = ((i % bins.Length) + 0.5) * 2 * Math.PI / bins.Length;
                var pt = new Point(c.X + (Math.Cos(a) * Math.Min(radius, 1.45) * r), c.Y - (Math.Sin(a) * Math.Min(radius, 1.45) * r));
                if (!started)
                {
                    g.BeginFigure(pt, false, false);
                    started = true;
                }
                else
                {
                    g.LineTo(pt, true, false);
                }
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, P("Warning", 1.6), geo);
    }
}
