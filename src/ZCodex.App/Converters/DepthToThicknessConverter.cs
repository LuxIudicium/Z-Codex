using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZCodex.App.Converters;

// Profondeur d'un nœud (0 = racine) → marge gauche, pour indenter les variantes sous leur parent.
// Pas d'indentation paramétrable via ConverterParameter (défaut 22px par niveau).
public class DepthToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int depth = value is int d ? d : 0;
        double step = 22;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
            step = p;
        return new Thickness(depth * step, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
