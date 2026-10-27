using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace OpenController.App.Controls;

/// <summary>Label + slider + exact numeric entry, bound to one double value.</summary>
public sealed class SliderRow : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderRow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SliderRow), new PropertyMetadata(1.0));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(SliderRow), new PropertyMetadata(0.005));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(SliderRow), new PropertyMetadata(3));

    public SliderRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(Label)) { Source = this });

        var slider = new Slider { VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });
        slider.SetBinding(RangeBase.MinimumProperty, new Binding(nameof(Minimum)) { Source = this });
        slider.SetBinding(RangeBase.MaximumProperty, new Binding(nameof(Maximum)) { Source = this });
        slider.SetBinding(RangeBase.SmallChangeProperty, new Binding(nameof(Step)) { Source = this });
        slider.SetBinding(RangeBase.LargeChangeProperty, new Binding(nameof(Step)) { Source = this, Converter = new ScaleConverter(10) });
        slider.SetBinding(Slider.TickFrequencyProperty, new Binding(nameof(Step)) { Source = this });
        slider.IsSnapToTickEnabled = true;
        Grid.SetColumn(slider, 1);

        var box = new TextBox { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(Value))
        {
            Source = this,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            Converter = new NumberConverter(),
            ConverterParameter = "3",
        });
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        };
        Grid.SetColumn(box, 2);

        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(box);
        Content = grid;
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    private sealed class ScaleConverter(double factor) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double d ? d * factor : value;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
