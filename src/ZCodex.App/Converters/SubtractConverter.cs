using System;
using System.Globalization;
using System.Windows.Data;

namespace ZCodex.App.Converters;

// Retourne (value - ConverterParameter) en double. Utilisé pour caler le VerticalOffset de
// l'infobulle de slot à « hauteur de slot moins l'overlay d'attributs » (placement au-dessus de
// la bande d'attributs, pas au-dessus du slot entier).
public class SubtractConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        double p = parameter is null ? 0 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
        return v - p;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
