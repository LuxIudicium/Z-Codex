using ZCodex.Core.Models;
using ZCodex.Scraper;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App.Converters;

public class ProfessionToIconConverter : IValueConverter
{
    // Cache : au plus 10 professions → 1 décodage chacune puis hits sur tous les slots/tooltips.
    private static readonly ConcurrentDictionary<Profession, ImageSource?> _cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Profession profession || profession == Profession.None) return null;

        // Seuls les succès sont mémorisés : mettre un null en cache condamnerait l'icône pour
        // toute la session, alors que le téléchargement de démarrage la pose peut-être à
        // l'instant même (cf. StatIconConverter, même piège).
        if (_cache.TryGetValue(profession, out var cached)) return cached;

        var img = Decode(profession);
        if (img != null) _cache[profession] = img;
        return img;
    }

    // Décodage EN MÉMOIRE (cf. ImageLoader) : même raison que dans StatIconConverter — une icône
    // illisible verrouillée par l'affichage n'aurait plus jamais pu être réparée.
    private static ImageSource? Decode(Profession profession)
        => ImageLoader.FromFile(ProfessionIconService.GetLocalPath(profession));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
