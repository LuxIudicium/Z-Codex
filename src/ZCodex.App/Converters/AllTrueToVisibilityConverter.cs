using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZCodex.App.Converters;

// Plusieurs booléens → Visible si TOUS sont vrais, sinon Collapsed.
public class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
