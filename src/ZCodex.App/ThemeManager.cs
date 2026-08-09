using System.Windows;

namespace ZCodex.App;

// Bascule clair/sombre à chaud : la palette (Theme.Light/Dark.xaml) est le
// MergedDictionaries[0] d'App.xaml, on la remplace ; Controls.xaml (index 1)
// référence tout en DynamicResource et n'est jamais échangé.
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/Theme.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        var md = Application.Current.Resources.MergedDictionaries;
        if (md.Count > 0 && md[0].Source?.OriginalString.Contains("Themes/Theme.") == true)
            md[0] = dict;
        else
            md.Insert(0, dict);
    }
}
