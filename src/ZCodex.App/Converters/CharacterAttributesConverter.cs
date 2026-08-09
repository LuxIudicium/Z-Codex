using System.Globalization;
using System.Windows.Data;
using ZCodex.Core.Models;

namespace ZCodex.App.Converters;

// CharacterBuild → "12 Deadly Arts, 12 Channeling Magic, 3 Critical Strikes"
// Résout les clés "attr_N" en noms via GwAttributeData. Attributs investis uniquement,
// triés par points décroissants puis par nom (rendu preview façon paw·ned²).
public class CharacterAttributesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CharacterBuild c) return string.Empty;

        var parts = new List<(int Points, string Name)>();
        foreach (var kv in c.Attributes)
        {
            if (kv.Value <= 0) continue;
            if (!kv.Key.StartsWith("attr_", StringComparison.Ordinal)
                || !int.TryParse(kv.Key.AsSpan(5), out int id)) continue;
            // Libellé de la langue courante (le nom EN reste la clé de .pn3/matching) ; le tri
            // suit le texte affiché, comme le reste du browser (cf. BrowserViewModel.ApplySort).
            var name = GwAttributeData.ById(id)?.DisplayName;
            if (name == null) continue;
            parts.Add((kv.Value, name));
        }

        return string.Join(", ", parts
            .OrderByDescending(p => p.Points)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Points} {p.Name}"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
