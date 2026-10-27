using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenController.App.Controls;

/// <summary>true → Visible, false → Collapsed. Parameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter as string == "invert")
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Non-null / non-empty → Visible.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound int equals the parameter (tab switching).</summary>
public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && int.TryParse(parameter as string, out int p) && i == p ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true when the bound int equals the parameter (nav radio buttons, stick selector).</summary>
public sealed class IndexEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && int.TryParse(parameter as string, out int p) && i == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter as string, out int p) ? p : Binding.DoNothing;
}

/// <summary>Looks up an application resource by the key the view model provides (e.g. an icon geometry).</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public static ResourceKeyConverter Instance { get; } = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a 0..1 double with the given number of decimals (parameter).</summary>
public sealed class NumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int decimals = int.TryParse(parameter as string, out int d) ? d : 3;
        return value is double v ? v.ToString("F" + decimals, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : Binding.DoNothing;
}
