using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Zenith.App.Controls;

/// <summary>Visible when the value is true, a non-empty string, a non-empty list or any other non-null value. Parameter "not" inverts.</summary>
public sealed class VisibleWhen : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var shown = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int n => n > 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "not") shown = !shown;
        return shown ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Icon name ("monitor", "power", ...) to its geometry in Theme/Icons.xaml.</summary>
public sealed class IconGeometry : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource($"Icon.{value}") as Geometry ?? Application.Current.TryFindResource("Icon.info");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Picks the first brush when the value is true, the second otherwise ("ResgateBrush|NeutralBrush").</summary>
public sealed class BrushWhen : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var keys = ((string)parameter!).Split('|');
        return Application.Current.TryFindResource(value is true ? keys[0] : keys[1]);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A 0..1 fraction to a width, given the full width as the parameter.</summary>
public sealed class FractionToWidth : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is double d ? d : 0) * double.Parse((string)parameter!, CultureInfo.InvariantCulture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class Not : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
