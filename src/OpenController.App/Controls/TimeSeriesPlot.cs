using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;

namespace OpenController.App.Controls;

/// <summary>Data for a <see cref="TimeSeriesPlot"/>: equally spaced samples per channel, oldest first.</summary>
public sealed class TimeSeriesData
{
    public TimeSeriesData(params string[] channelNames)
    {
        Channels = channelNames.Select((n, i) => new Channel(n, i)).ToArray();
    }

    public Channel[] Channels { get; }
    public double YMin { get; set; } = -1;
    public double YMax { get; set; } = 1;
    /// <summary>Length of the visible window in seconds (for the time axis labels).</summary>
    public double WindowSeconds { get; set; } = 5;

    public sealed class Channel(string name, int colorIndex)
    {
        public string Name { get; set; } = name;
        /// <summary>Index into the Plot.Series1..6 theme colors.</summary>
        public int ColorIndex { get; } = colorIndex;
        public float[] Values { get; private set; } = new float[1024];
        public int Count { get; set; }
        public bool Visible { get; set; } = true;
        public bool Dashed { get; set; }

        public void EnsureCapacity(int n)
        {
            if (Values.Length < n)
                Values = new float[n];
        }
    }
}

/// <summary>Multi-channel line chart with value and time axes and a color legend.</summary>
public sealed class TimeSeriesPlot : ThemedElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(TimeSeriesData), typeof(TimeSeriesPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(long), typeof(TimeSeriesPlot), new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimeSeriesPlot()
    {
        AutomationProperties.SetName(this, "Input history chart");
    }

    public TimeSeriesData? Data { get => (TimeSeriesData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public long Revision { get => (long)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 60)
            return;
        dc.DrawRoundedRectangle(B("Plot.Background"), P("Border.Card", 1), new Rect(0, 0, w, h), 8, 8);
        TimeSeriesData? d = Data;
        if (d is null)
            return;

        const double left = 40, right = 10, top = 26, bottom = 22;
        var plot = new Rect(left, top, w - left - right, h - top - bottom);
        double ymin = d.YMin, ymax = d.YMax <= d.YMin ? d.YMin + 1 : d.YMax;
        double Y(double v) => plot.Bottom - ((Math.Clamp(v, ymin, ymax) - ymin) / (ymax - ymin) * plot.Height);
        Brush label = B("Plot.Label");

        // Value axis: 5 ticks.
        for (int i = 0; i <= 4; i++)
        {
            double v = ymin + ((ymax - ymin) * i / 4);
            double y = Y(v);
            dc.DrawLine(Math.Abs(v) < 1e-9 ? P("Plot.Axis", 1) : P("Plot.Grid", 1), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, v.ToString(Math.Abs(ymax - ymin) < 0.5 ? "0.00" : "0.0#", CultureInfo.InvariantCulture),
                new Point(left - 6, y), 9.5, label, HorizontalAlignment.Right, VerticalAlignment.Center, MonoFont);
        }

        // Time axis: -window … now.
        for (int i = 0; i <= 4; i++)
        {
            double x = plot.Left + (plot.Width * i / 4);
            dc.DrawLine(P("Plot.Grid", 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
            double t = -d.WindowSeconds * (4 - i) / 4;
            string text = i == 4 ? "0 s" : t.ToString("0.#", CultureInfo.InvariantCulture) + " s";
            DrawText(dc, text, new Point(x, plot.Bottom + 4), 9.5, label,
                i == 0 ? HorizontalAlignment.Left : i == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Center);
        }

        dc.PushClip(new RectangleGeometry(plot));
        foreach (TimeSeriesData.Channel ch in d.Channels)
        {
            if (!ch.Visible || ch.Count < 2)
                continue;
            var geo = new StreamGeometry();
            using (StreamGeometryContext g = geo.Open())
            {
                double dx = plot.Width / (ch.Count - 1);
                g.BeginFigure(new Point(plot.Left, Y(ch.Values[0])), false, false);
                for (int i = 1; i < ch.Count; i++)
                    g.LineTo(new Point(plot.Left + (i * dx), Y(ch.Values[i])), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, P(SeriesKey(ch.ColorIndex), 1.6, ch.Dashed), geo);
        }
        dc.Pop();

        // Legend (top-left, inside the header band).
        double lx = left;
        foreach (TimeSeriesData.Channel ch in d.Channels)
        {
            if (!ch.Visible)
                continue;
            dc.DrawRoundedRectangle(B(SeriesKey(ch.ColorIndex)), null, new Rect(lx, 9, 10, 4), 2, 2);
            FormattedText ft = Text(ch.Name, 11, B("Text.Secondary"));
            dc.DrawText(ft, new Point(lx + 14, 3));
            lx += 14 + ft.Width + 14;
        }
    }

    private static string SeriesKey(int i) => "Plot.Series" + ((i % 6) + 1).ToString(CultureInfo.InvariantCulture);
}
