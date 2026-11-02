using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DDLDesk;

internal static class WindowAppearance
{
    public static readonly DependencyProperty PanelOpacityProperty = DependencyProperty.RegisterAttached(
        "PanelOpacity", typeof(double), typeof(WindowAppearance), new FrameworkPropertyMetadata(.88));

    public static void SetPanelOpacity(Window window, double value) => window.SetValue(PanelOpacityProperty, value);

    public static void FollowOwner(Window window)
    {
        if (window.Owner != null)
            window.SetBinding(PanelOpacityProperty, new Binding { Source = window.Owner, Path = new PropertyPath(PanelOpacityProperty), Mode = BindingMode.OneWay });
    }

    public static Border Shell(Window window, UIElement content, Thickness? padding = null)
    {
        FollowOwner(window);
        var shell = Ui.Shell(content, padding);
        shell.SetBinding(Border.BackgroundProperty, new Binding { Source = window, Path = new PropertyPath(PanelOpacityProperty), Converter = new BackgroundConverter() });
        return shell;
    }

    private sealed class BackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double opacity = value is double number && double.IsFinite(number) ? Math.Clamp(number, .55, 1) : .88;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 10, 15, 21)); brush.Freeze(); return brush;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
