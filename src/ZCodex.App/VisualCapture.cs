using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App;

// Rendu d'un élément de l'arbre visuel vers un bitmap : screenshot du team build, export .png d'un
// cadenas, copie d'image de la fenêtre Spike. Les trois passaient par du code copié-collé, chacun
// avec ses défauts ; tout est centralisé ici.
//
// Le point à ne pas se refaire piéger : la taille de sortie doit être les BORNES DE CONTENU du
// visuel, pas ActualWidth × ActualHeight. Les deux diffèrent dès que le contenu n'occupe pas tout
// son emplacement (grille étroite dans une fenêtre large, par exemple), et un VisualBrush est en
// Stretch=Fill par défaut : il étire les bornes réelles jusqu'à remplir la destination, d'un facteur
// différent en X et en Y. C'est ce qui sortait les screenshots écrasés. Mesuré sur un cas de test :
// bornes 420 de large étirées sur 860,8 → tout l'écran comprimé d'un facteur 2 en horizontal.
// Destination au ratio des bornes = Fill devient l'identité, plus rien ne peut se déformer.
//
// Le VisualBrush lui-même n'est pas le problème, c'est même lui qui rend l'opération fiable : son
// Viewbox relatif par défaut suit les bornes réelles du contenu, donc il ignore à la fois la Margin
// de l'élément et le décalage de scroll (sous une ScrollViewer, le ScrollContentPresenter arrange
// son contenu à -HorizontalOffset/-VerticalOffset, et ça se retrouve dans le VisualOffset). Un
// bmp.Render(element) direct, lui, applique ce VisualOffset : il décale l'image de la marge et
// ampute purement et simplement le haut d'une grille scrollée. Vérifié au harnais : à 120 px de
// scroll, le rendu direct perd les 120 premiers px ; par le brush, l'image est identique à 0.
public static class VisualCapture
{
    /// <summary>
    /// Rend <paramref name="target"/> en entier, à la résolution physique de son écran, sur un fond
    /// opaque (les marges internes sont souvent transparentes et sortiraient en damier).
    /// Retourne null si l'élément n'a rien à rendre.
    /// </summary>
    public static RenderTargetBitmap? Render(FrameworkElement target, Brush background)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(target);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;

        var dpi = VisualTreeHelper.GetDpi(target);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(bounds.Width * dpi.DpiScaleX), (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        var destination = new Rect(0, 0, bounds.Width, bounds.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, destination);
            dc.DrawRectangle(new VisualBrush(target), null, destination);
        }
        bmp.Render(visual);
        return bmp;
    }

    /// <summary>Encode un bitmap en PNG sur disque.</summary>
    public static void SavePng(RenderTargetBitmap bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
