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
        return _cache.GetOrAdd(key, Decode);
    }

    private static ImageSource? Decode(string key)
    {
        var path = SkillStatIconService.GetLocalPath(key);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = 16;
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
