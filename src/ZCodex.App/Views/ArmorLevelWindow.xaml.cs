using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZCodex.App.Views;

// Paramètres « Dégâts selon l'armure » : niveaux d'armure personnalisés (colonnes ajoutées aux
// AL fixes 60/80/100/120, un champ par valeur, bouton « + » jusqu'à 8, champ vide = colonne
// retirée), niveau du personnage (lanceur, 1–20 — décision Philippe : max 20) et niveau de la
// cible (1–40, taux de critique).
public partial class ArmorLevelWindow : Window
{
    private const int MaxCustomLevels = 8;

    public List<int> ArmorLevels { get; private set; }
    public int CharacterLevel { get; private set; }
    public int TargetLevel { get; private set; }

    public ArmorLevelWindow(IReadOnlyList<int> currentArmorLevels,
                            int currentCharacterLevel, int currentTargetLevel)
    {
        InitializeComponent();
        ArmorLevels = new List<int>(currentArmorLevels);
        CharacterLevel = currentCharacterLevel;
        TargetLevel = currentTargetLevel;
        Loaded += (_, _) =>
        {
            foreach (int level in currentArmorLevels.Take(MaxCustomLevels))
                AddLevelBox(level.ToString());
            if (CustomLevelsPanel.Children.Count == 0)
                AddLevelBox(string.Empty);
            CharacterLevelBox.Text = currentCharacterLevel.ToString();
            TargetLevelBox.Text = currentTargetLevel.ToString();
            var first = (TextBox)CustomLevelsPanel.Children[0];
            first.SelectAll();
            first.Focus();
        };
    }

    private TextBox AddLevelBox(string text)
    {
        var box = new TextBox
        {
            Text = text, FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8),
        };
        box.KeyDown += LevelBox_KeyDown;
        CustomLevelsPanel.Children.Add(box);
        AddLevelButton.IsEnabled = CustomLevelsPanel.Children.Count < MaxCustomLevels;
        return box;
    }

    private void AddLevel_Click(object sender, RoutedEventArgs e)
    {
        if (CustomLevelsPanel.Children.Count >= MaxCustomLevels) return;
        AddLevelBox(string.Empty).Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Une saisie invalide laisse la fenêtre ouverte ; doublons ignorés silencieusement
        // (une seule colonne par AL, comme les égalités avec les fixes).
        var levels = new List<int>();
        foreach (TextBox box in CustomLevelsPanel.Children)
        {
            var text = box.Text.Trim();
            if (text.Length == 0) continue;
            if (!int.TryParse(text, out int al) || al < 0 || al > 200) return;
            if (!levels.Contains(al)) levels.Add(al);
        }
        if (!int.TryParse(CharacterLevelBox.Text.Trim(), out int character)
            || character < 1 || character > 20) return;
        if (!int.TryParse(TargetLevelBox.Text.Trim(), out int target)
            || target < 1 || target > 40) return;

        ArmorLevels = levels;
        CharacterLevel = character;
        TargetLevel = target;
        DialogResult = true;
    }

    private void LevelBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, new RoutedEventArgs());
    }
}
