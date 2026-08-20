using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZCodex.App.ViewModels;

namespace ZCodex.App.Views;

public partial class ArmorCalcView : UserControl
{
    public ArmorCalcView()
    {
        InitializeComponent();
    }

    private ArmorCalcViewModel? Vm => DataContext as ArmorCalcViewModel;

    // Attaque à ajouter : la sélection du catalogue, sinon le texte saisi (nom exact, ou résultat
    // unique du filtre) — le bouton marche même sans avoir cliqué dans la liste.
    private Core.Models.Skill? ResolveAttackToAdd()
    {
        if (Vm is null) return null;
        var cat = Vm.AttackCatalog;
        if (cat.SelectedSkill is { } sel) return sel;
        var text = AttackSearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var exact = cat.FilteredSkills.FirstOrDefault(s =>
                string.Equals(s.Name, text, StringComparison.CurrentCultureIgnoreCase));
            if (exact is not null) return exact;
        }
        return cat.FilteredSkills.Count == 1 ? cat.FilteredSkills[0] : null;
    }

    private void AttackSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Vm?.AddAttack(ResolveAttackToAdd());
    }

    private void AttackResult_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.AttackCatalog.SelectedSkill is { } skill) Vm.AddAttack(skill);
    }

    // Sélection d'une cellule de la grille → détail dépliable.
    private void Cell_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ResultCellVM cell)
            cell.Select();
    }

    private void AddMod_Click(object sender, RoutedEventArgs e) => Vm?.AddSelectedMod();

    private void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null && (sender as FrameworkElement)?.DataContext is EquipmentModOption mod)
            Vm.RemoveMod(mod);
    }

    private void AddAttack_Click(object sender, RoutedEventArgs e) => Vm?.AddAttack(ResolveAttackToAdd());

    // Ligne de retour sous la recherche du catalogue : lève les filtres qu'elle vient de nommer.
    private void SearchNotice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SkillPanelViewModel panel })
            panel.LiftBlockingFilters();
    }

    private void RemoveAttack_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null && (sender as FrameworkElement)?.DataContext is ReferenceAttackVM a)
            Vm.RemoveAttack(a);
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e) => Vm?.AddCustom();

    private void RemoveCustom_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null && (sender as FrameworkElement)?.DataContext is CustomContribVM c)
            Vm.RemoveCustom(c);
    }

    private void ImportPCode_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        ProfileStatus.Text = Vm.ImportPCode(PCodeBox.Text);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var name = string.IsNullOrWhiteSpace(ProfileNameBox.Text)
            ? Vm.SelectedProfileName ?? ""
            : ProfileNameBox.Text;
        ProfileStatus.Text = Vm.SaveProfile(name);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        ProfileStatus.Text = Vm.DeleteProfile(Vm.SelectedProfileName);
    }
}
