using ZCodex.Scraper;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App.Converters;

/// <summary>
/// Mappe une clé de stat ("energy", "cast", "recharge", "adrenaline") vers la
/// petite icône wiki correspondante. La valeur bindée EST la clé.
/// </summary>
public class StatIconConverter : IValueConverter
{
    // Cache : les 7 mêmes icônes de stats reviennent sur chaque tooltip → 1 décodage puis hits.
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = (value as string) ?? (parameter as string);
        if (string.IsNullOrEmpty(key)) return null;

        // On ne mémorise QUE les succès. Un GetOrAdd figeait le null d'une icône pas encore
        // téléchargée pour toute la session : elle arrivait sur le disque deux secondes plus
        // tard, et l'infobulle restait pourtant vide jusqu'au prochain lancement. Ces onze
        // icônes se comptent sur les doigts — retenter un décodage tant qu'il échoue ne coûte
        // qu'un File.Exists négatif.
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var img = Decode(key);
        if (img != null) _cache[key] = img;
        return img;
    }

    // Décodage EN MÉMOIRE (cf. ImageLoader) : une icône corrompue ne doit pas rester verrouillée
    // par l'affichage, sinon le service de téléchargement ne peut plus la remplacer.
    private static ImageSource? Decode(string key)
        => ImageLoader.FromFile(SkillStatIconService.GetLocalPath(key));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
