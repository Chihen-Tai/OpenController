using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using XboxControllerCenter.App.Localization;

namespace XboxControllerCenter.App.Controls;

/// <summary>
/// Input → output magnitude mapping on [0, 1] with labelled axes, the identity line for reference, the dead
/// region shaded, and a marker that follows the live input. Re-renders whenever the mapping changes.
/// </summary>
public sealed class CurvePlot : ThemedElement
{
    public static readonly DependencyProperty MappingProperty = DependencyProperty.Register(
        nameof(Mapping), typeof(Func<double, double>), typeof(CurvePlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkerProperty = DependencyProperty.Register(
        nameof(Marker), typeof(double), typeof(CurvePlot), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public CurvePlot()
    {
        AutomationProperties.SetName(this, "Response curve");
    }

    public Func<double, double>? Mapping { get => (Func<double, double>?)GetValue(MappingProperty); set => SetValue(MappingProperty, value); }

    /// <summary>Current input magnitude (0..1), or NaN for none.</summary>
    public double Marker { get => (double)GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 80)
            return;
        dc.DrawRoundedRectangle(B("Plot.Background"), P("Border.Card", 1), new Rect(0, 0, w, h), 8, 8);
        const double left = 30, right = 12, top = 12, bottom = 24;
        var plot = new Rect(left, top, w - left - right, h - top - bottom);
        Point P2(double x, double y) => new(plot.Left + (x * plot.Width), plot.Bottom - (Math.Clamp(y, 0, 1) * plot.Height));
        Brush label = B("Plot.Label");

        for (int i = 0; i <= 4; i++)
        {
            double t = i / 4.0;
            dc.DrawLine(P("Plot.Grid", 1), P2(t, 0), P2(t, 1));
            dc.DrawLine(P("Plot.Grid", 1), P2(0, t), P2(1, t));
        }
        foreach (double t in new[] { 0.0, 0.5, 1.0 })
        {
            string s = t.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            DrawText(dc, s, new Point(plot.Left + (t * plot.Width), plot.Bottom + 3), 9.5, label, HorizontalAlignment.Center);
            DrawText(dc, s, new Point(left - 5, plot.Bottom - (t * plot.Height)), 9.5, label, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
        DrawText(dc, Loc.T("Plot.In"), new Point(plot.Right, h - 2), 9.5, label, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        dc.DrawLine(P("Plot.Axis", 1, dashed: true), P2(0, 0), P2(1, 1));

        Func<double, double>? f = Mapping;
        if (f is null)
            return;

        // Shade the dead region (input where output stays 0).
        double dead = 0;
        for (int i = 1; i <= 400 && f(i / 400.0) <= 0; i++)
            dead = i / 400.0;
        if (dead > 0)
            dc.DrawRectangle(B("Plot.Deadzone"), null, new Rect(plot.Left, plot.Top, dead * plot.Width, plot.Height));

        var geo = new StreamGeometry();
        using (StreamGeometryContext g = geo.Open())
        {
            const int steps = 240;
            g.BeginFigure(P2(0, f(0)), false, false);
            for (int i = 1; i <= steps; i++)
            {
                double x = i / (double)steps;
                g.LineTo(P2(x, f(x)), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, P("Output", 2.2), geo);

        double m = Marker;
        if (!double.IsNaN(m))
        {
            m = Math.Clamp(m, 0, 1);
            Point pt = P2(m, f(m));
            dc.DrawLine(P("Accent", 1, dashed: true), new Point(pt.X, plot.Bottom), pt);
            dc.DrawEllipse(B("Accent"), P("Plot.Background", 1.5), pt, 5, 5);
        }
    }
}
