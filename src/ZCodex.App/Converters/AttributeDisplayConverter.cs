using System.Globalization;
using System.Windows.Data;
using ZCodex.Core.Models;

namespace ZCodex.App.Converters;

// Nom d'attribut / rang de titre / catégorie PvE ANGLAIS (clé de filtrage) → libellé affiché
// dans la langue courante. La collection reste en anglais (canon de filtrage) ; seul l'affichage
// est traduit. Re-convertit à chaque reconstruction de la liste (cf. RefreshAttributes / switch).
public class AttributeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => GwAttributeData.DisplayName(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
