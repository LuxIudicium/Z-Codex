using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App.Converters;

/// <summary>
/// Décode une icône du disque SANS jamais laisser WPF ouvrir le fichier lui-même.
///
/// <c>BitmapImage.UriSource</c> tient le fichier ouvert le temps du décodage, et le retient
/// encore un instant quand celui-ci ÉCHOUE. Sur une icône vide ou tronquée, l'affichage rate
/// son décodage et le verrou transitoire qui s'ensuit fait échouer le remplacement du fichier
/// par le service de téléchargement : l'icône corrompue n'était alors JAMAIS réparée, et un
/// fichier .tmp restait en travers. Constaté en reproduisant la panne — energy.png vidé à zéro
/// octet survivait à toutes les passes de réparation, quand recharge.png tronqué, lui, guérissait.
///
/// On lit donc les octets soi-même et on décode en mémoire : le fichier n'est ouvert que le
/// temps d'une lecture, en partage, et le service peut toujours le remplacer.
/// </summary>
internal static class ImageLoader
{
    /// <summary>
    /// Décode à la résolution NATIVE, sans <c>DecodePixelWidth</c>.
    ///
    /// Forcer une largeur de décodage déformait les icônes non carrées : WPF aligne la largeur
    /// demandée puis TRONQUE la hauteur au lieu de l'arrondir. Recharge.png (22×23 natif) sortait
    /// en 16×16 — écrasé verticalement et rétréci de 27 % — quand les icônes carrées du catalogue
    /// (20×20) traversaient l'opération intactes. D'où le symptôme « l'icône de recharge est mal
    /// rendue dans l'infobulle mais correcte dans le catalogue ».
    ///
    /// Ces icônes font une vingtaine de pixels pour un affichage à 14 : il n'y a rien à gagner à
    /// les redécoder plus petit, et c'est <c>Stretch="Uniform"</c> qui doit décider du cadrage,
    /// lui seul respectant le rapport d'origine. Bonus : le rendu suit alors le DPI de l'écran,
    /// au lieu d'être figé à une taille de décodage calculée pour du 100 %.
    /// </summary>
    public static ImageSource? FromFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes);
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = ms;
            img.CacheOption = BitmapCacheOption.OnLoad;   // décodage immédiat → le flux peut être libéré
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
}
