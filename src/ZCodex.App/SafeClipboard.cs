using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ZCodex.App;

// Le presse-papiers Windows est une ressource GLOBALE, ouverte en exclusif par une seule
// application à la fois : un client RDP, un gestionnaire d'historique ou Teams qui le tient au
// mauvais moment fait échouer l'appel avec COMException CLIPBRD_E_CANT_OPEN. Le verrou est
// transitoire (quelques dizaines de ms) d'où les tentatives successives, mais il peut persister
// — un échec de copie ne doit jamais fermer l'application en pleine session de travail.
//
// Toutes les copies passent par ici : la moitié des appels directs à Clipboard étaient protégés,
// l'autre non, ce qui rendait la robustesse dépendante du bouton utilisé.
public static class SafeClipboard
{
    private const int Attempts = 3;
    private const int RetryDelayMs = 60;   // 3 × 60 ms = 120 ms au pire, imperceptible

    /// <summary>Copie du texte. Retourne false si le presse-papiers est resté inaccessible.</summary>
    public static bool SetText(string text) => Try(() => Clipboard.SetText(text));

    public static bool SetImage(BitmapSource image) => Try(() => Clipboard.SetImage(image));

    public static bool SetFileDropList(StringCollection files) => Try(() => Clipboard.SetFileDropList(files));

    /// <summary>Texte du presse-papiers, chaîne vide s'il est inaccessible ou non textuel.</summary>
    public static string GetText()
    {
        string result = string.Empty;
        Try(() => result = Clipboard.GetText());
        return result;
    }

    private static bool Try(Action action, [CallerMemberName] string operation = "")
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                if (attempt == Attempts)
                {
                    Debug.WriteLine($"SafeClipboard.{operation}: abandon après {Attempts} tentatives — {ex.Message}");
                    return false;
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    }
}
