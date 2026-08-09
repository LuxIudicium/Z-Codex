using System.Globalization;
using System.Windows.Data;
using ZCodex.Core.Models;

namespace ZCodex.App.Converters;

// CharacterBuild → "A / Rt" (abréviations primaire / secondaire) dans la langue d'affichage
// (FR : "En / E", "G / Mo"). Masque la secondaire si None (ex. "Mo").
// DisplayAbbr et non Abbr : l'abréviation EN reste réservée aux codes de template exportés.
public class CharacterProfessionsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CharacterBuild c) return string.Empty;
        var pri = c.PrimaryProfession.DisplayAbbr();
        return c.SecondaryProfession == Profession.None ? pri : $"{pri} / {c.SecondaryProfession.DisplayAbbr()}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
