using Microsoft.Win32;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ZCodex.App.Views;

// Sélection d'un template d'équipement enregistré (fichiers .txt du dossier Equipment).
// Filtré par profession : seuls les templates neutres (armes seules, la profession est
// déduite de l'armure) ou de la profession demandée sont proposés. « Parcourir... » permet
// de sortir du dossier (sans filtre).
public partial class EquipmentTemplatePickerWindow : Window
{
    // Raccourci de localisation (fenêtre modale : la langue courante est figée à l'ouverture).
    private static string T(string key) => LanguageManager.T(key);

    private sealed record Entry(string Name, string Profession, string FilePath);

    public string? SelectedPath { get; private set; }

    public EquipmentTemplatePickerWindow(string templatesDir, Profession filter)
    {
        InitializeComponent();

        var entries = new List<Entry>();
        foreach (var file in SafeEnumerateTxt(templatesDir))
        {
            string text;
            try { text = File.ReadAllText(file); } catch { continue; }
            var builds = GwEquipmentCodec.DecodeLines(text);
            if (builds.Count == 0) continue; // pas un template d'équipement

            var eq   = builds.Count == 1 ? builds[0] : EquipmentBuild.Combine(builds);
            var prof = GwEquipmentInfo.GuessProfession(eq);
            if (filter != Profession.None && prof != Profession.None && prof != filter) continue;

            entries.Add(new Entry(Path.GetFileNameWithoutExtension(file),
                prof == Profession.None ? T("S.EquipPick.Neutral") : prof.DisplayName(), file));
        }
        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        HeaderText.Text = filter == Profession.None
            ? T("S.EquipPick.HeaderAll")
            : string.Format(T("S.EquipPick.HeaderProf"), filter.DisplayName());
        TemplateList.ItemsSource = entries;
        if (entries.Count > 0) TemplateList.SelectedIndex = 0;
        else OkBtn.IsEnabled = false;
    }

    private static IReadOnlyList<string> SafeEnumerateTxt(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.txt").ToList(); }
        catch { return []; }
    }

    private void TemplateList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (TemplateList.SelectedItem is not Entry entry) return;
        SelectedPath = entry.FilePath;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = T("S.Win.LoadEquipTitle"),
            Filter = T("S.Filter.EquipTpl"),
        };
        if (dlg.ShowDialog() != true) return;
        SelectedPath = dlg.FileName;
        DialogResult = true;
    }
}
