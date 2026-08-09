using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZCodex.App.Converters;

/// <summary>Coût d'énergie &gt; 0 → Visible (groupe énergie de l'infobulle).</summary>
public class PositiveIntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is int i && i > 0) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Valeur float &gt; 0 → Visible (groupes cast / recharge de l'infobulle).</summary>
public class PositiveFloatToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is float f && f > 0f) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Entier nul → chaîne vide, sinon la valeur (colonnes Adrénaline/Sacrifice/Overcast/Upkeep
/// du catalogue : nulles pour la majorité des skills, on ne les affiche que quand elles existent).
/// ConverterParameter optionnel = suffixe ajouté à la valeur (ex : "%" pour Sacrifice).</summary>
public class ZeroIntToEmptyStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is int i && i > 0)
            ? i.ToString(CultureInfo.InvariantCulture) + (parameter as string ?? string.Empty)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
