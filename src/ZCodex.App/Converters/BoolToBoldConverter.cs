using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZCodex.App.Converters;

// true → SemiBold, false → Normal. Utilisé par le calculateur d'armure (ligne Espérance,
// en-têtes du détail dépliable).
public class BoolToBoldConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeights.SemiBold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
