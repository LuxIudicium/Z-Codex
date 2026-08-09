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
        return _cache.GetOrAdd(profession, Decode);
    }

    private static ImageSource? Decode(Profession profession)
    {
        var path = ProfessionIconService.GetLocalPath(profession);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = 24;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
