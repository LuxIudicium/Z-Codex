using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App.Converters;

public class UrlToImageConverter : IValueConverter
{
    // Cache d'icônes décodées (frozen → partageables). Sans lui, chaque binding rouvre
    // le fichier + reparse/redécode le JPEG : storm de décodages disque quand un teambuild
    // matérialise N×8 slots + leurs tooltips (icône skill + prof + 7 stats). Clé = chemin
    // (fragment #top/#bottom inclus) + taille d'affichage demandée.
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        double displaySize = ParseSize(parameter, 64);
        return _cache.GetOrAdd($"{path}|{displaySize}", _ => Decode(path, displaySize));
    }

    // Pré-chauffe le cache d'icônes EN ARRIÈRE-PLAN (parallélisé) → au moment de l'affichage,
    // le catalogue ne fait que des hits au lieu de décoder ~75-95 JPEG sur le thread UI (~17 ms
    // chacun = gel). À appeler DEPUIS LE THREAD UI (on y capture le facteur DPI avant de paralléliser).
    public static void Prewarm(IReadOnlyList<string> paths, double displaySize)
    {
        double dpiScale = GetDpiScale();   // lu ici, sur le thread UI (sinon retombe à 1.0)
        System.Threading.Tasks.Task.Run(() =>
            System.Threading.Tasks.Parallel.ForEach(
                paths,
                new System.Threading.Tasks.ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2)
                },
                p =>
                {
                    if (!string.IsNullOrEmpty(p))
                        _cache.GetOrAdd($"{p}|{displaySize}", _ => Decode(p, displaySize, dpiScale));
                }));
    }

    private static ImageSource? Decode(string path, double displaySize)
        => Decode(path, displaySize, GetDpiScale());

    private static ImageSource? Decode(string path, double displaySize, double dpiScale)
    {
        try
        {
            // Sépare le fragment "#top" / "#bottom" du chemin réel
            int hashIdx = path.LastIndexOf('#');
            string fragment   = hashIdx >= 0 ? path[hashIdx..] : string.Empty;
            string actualPath = hashIdx >= 0 ? path[..hashIdx] : path;

            var uri = Path.IsPathRooted(actualPath)
                ? new Uri(actualPath)
                : new Uri(actualPath, UriKind.RelativeOrAbsolute);

            // Taille de décodage = taille d'affichage EXACTE (passée via ConverterParameter) × échelle
            // DPI de l'écran. On laisse le DÉCODEUR JPEG faire le rééchantillonnage (net) pour viser
            // pile la taille écran → WPF n'a plus rien à rééchantillonner à l'exécution (1:1 = net).
            // On ne plafonne QUE si la source est plus grande → les icônes basse résolution (64px) ne
            // sont jamais agrandies (donc jamais floutées). Faute de paramètre, on retombe sur 64.
            int decodeTarget = (int)Math.Ceiling(displaySize * dpiScale);
            int nativeWidth;
            try
            {
                var meta = BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                nativeWidth = meta.PixelWidth;
            }
            catch { nativeWidth = 0; }

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = uri;
            if (nativeWidth > decodeTarget) img.DecodePixelWidth = decodeTarget;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();

            // Icônes 64×128 (Kurzick/Luxon) : on retourne seulement la moitié correspondante
            if (img.PixelHeight > img.PixelWidth)
            {
                int half = img.PixelHeight / 2;
                var rect = fragment == "#bottom"
                    ? new Int32Rect(0, half, img.PixelWidth, half)   // moitié basse → Luxon
                    : new Int32Rect(0, 0,    img.PixelWidth, half);  // moitié haute → Kurzick (défaut)
                var cropped = new CroppedBitmap(img, rect);
                cropped.Freeze();
                return cropped;
            }

            return img;
        }
        catch { return null; }
    }

    // Taille d'affichage cible (en DIP) passée par ConverterParameter, sinon la valeur par défaut.
    private static double ParseSize(object? parameter, double fallback)
        => parameter != null
           && double.TryParse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture),
                              NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
           && s > 0
            ? s
            : fallback;

    // Échelle DPI de l'écran (1.0 = 100%, 1.5 = 150%…) lue depuis la fenêtre principale.
    private static double GetDpiScale()
    {
        try
        {
            var win = System.Windows.Application.Current?.MainWindow;
            var src = win != null ? PresentationSource.FromVisual(win) : null;
            var m = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            return m > 0 ? m : 1.0;
        }
        catch { return 1.0; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
