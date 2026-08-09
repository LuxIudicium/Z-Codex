using ZCodex.Core.Models;
using System.Windows;

namespace ZCodex.App;

// Bascule FR/EN à chaud (patron ThemeManager) : le dictionnaire de chaînes UI
// (Resources/Strings.fr|en.xaml) est repéré dans les MergedDictionaries par son Source et
// remplacé ; tout libellé XAML converti en DynamicResource suit immédiatement. Pose aussi
// AppLanguage.IsFr (Core) qui pilote les propriétés Display* des skills — les vues qui
// listent des skills doivent être rechargées par l'appelant (cf. SwitchLanguage).
public static class LanguageManager
{
    public static bool IsFr => AppLanguage.IsFr;

    public static void Apply(bool fr)
    {
        AppLanguage.IsFr = fr;
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{(fr ? "fr" : "en")}.xaml", UriKind.Relative)
        };
        var md = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < md.Count; i++)
        {
            if (md[i].Source?.OriginalString.Contains("Resources/Strings.") == true)
            {
                md[i] = dict;
                return;
            }
        }
        md.Add(dict);
    }

    /// <summary>Chaîne UI localisée depuis le dictionnaire courant (pour le code C# :
    /// MessageBox, textes construits). Clé absente → la clé elle-même (visible = bug).</summary>
    public static string T(string key)
        => Application.Current.TryFindResource(key) as string ?? key;
}
