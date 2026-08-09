using System.Globalization;
using System.Windows.Data;

namespace ZCodex.App.Converters;

// Nom de build affiché : placeholder localisé (« (Libellé build) »/« (Build Name) ») quand le nom
// stocké est la sentinelle « (unnamed) » ou vide ; sinon le nom tel quel. Utilisé pour les surfaces
// liées à un modèle Core (CharacterBuild) qui n'a pas de DisplayName. Même règle que
// CharacterSlotViewModel.DisplayName.
public class BuildNamePlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string;
        return string.IsNullOrWhiteSpace(name) || name == "(unnamed)"
            ? LanguageManager.T("S.Misc.BuildNamePlaceholder")
            : name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
