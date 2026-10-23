using System.Globalization;
using System.Windows;
using System.Windows.Media;
using XboxControllerCenter.App.Services;

namespace XboxControllerCenter.App.Controls;

/// <summary>
/// Base for custom-drawn controls. Colors come from the active theme's resource tokens (never hard-coded),
/// pens/brushes are cached per theme version, and the control re-renders when the theme changes.
/// </summary>
public abstract class ThemedElement : FrameworkElement
{
    private readonly Dictionary<(string Key, double Thickness, bool Dashed), Pen> _pens = new();
    private readonly Dictionary<string, Brush> _brushes = new();
    private readonly Dictionary<(string Text, double Size, Brush Brush, Typeface Face), FormattedText> _texts = new();
    private int _version = -1;

    protected static readonly Typeface UiFont = new("Segoe UI");
    protected static readonly Typeface UiFontSemibold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    protected static readonly Typeface MonoFont = new("Cascadia Mono, Consolas");

    protected ThemedElement()
    {
        Loaded += (_, _) => ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    private void OnThemeChanged() => InvalidateVisual();

    private void EnsureVersion()
    {
        if (_version == ThemeManager.Version)
            return;
        _pens.Clear();
        _brushes.Clear();
        _texts.Clear();
        _version = ThemeManager.Version;
        OnThemeResourcesInvalidated();
    }

    /// <summary>Override to drop caches built from theme colors.</summary>
    protected virtual void OnThemeResourcesInvalidated()
    {
    }

    protected Brush B(string key)
    {
        EnsureVersion();
        if (_brushes.TryGetValue(key, out Brush? b))
            return b;
        b = TryFindResource(key) as Brush ?? Brushes.Magenta;
        if (b.CanFreeze && !b.IsFrozen)
            b = (Brush)b.GetCurrentValueAsFrozen();
        _brushes[key] = b;
        return b;
    }

    protected Color C(string key) => B(key) is SolidColorBrush s ? s.Color : Colors.Magenta;

    protected Pen P(string key, double thickness, bool dashed = false)
    {
        EnsureVersion();
        if (_pens.TryGetValue((key, thickness, dashed), out Pen? p))
            return p;
        p = new Pen(B(key), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (dashed)
            p.DashStyle = new DashStyle([3, 3], 0);
        p.Freeze();
        _pens[(key, thickness, dashed)] = p;
        return p;
    }

    protected static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Cached text layout (creating FormattedText every frame is comparatively expensive).</summary>
    protected FormattedText Text(string text, double size, Brush brush, Typeface? typeface = null)
    {
        EnsureVersion();
        var key = (text, size, brush, typeface ?? UiFont);
        if (_texts.TryGetValue(key, out FormattedText? ft))
            return ft;
        if (_texts.Count > 512)
            _texts.Clear();
        ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, key.Item4, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _texts[key] = ft;
        return ft;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _texts.Clear();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected void DrawText(DrawingContext dc, string text, Point p, double size, Brush brush,
        HorizontalAlignment h = HorizontalAlignment.Left, VerticalAlignment v = VerticalAlignment.Top, Typeface? typeface = null)
    {
        FormattedText ft = Text(text, size, brush, typeface);
        double x = h switch { HorizontalAlignment.Center => p.X - (ft.Width / 2), HorizontalAlignment.Right => p.X - ft.Width, _ => p.X };
        double y = v switch { VerticalAlignment.Center => p.Y - (ft.Height / 2), VerticalAlignment.Bottom => p.Y - ft.Height, _ => p.Y };
        dc.DrawText(ft, new Point(x, y));
    }
}
