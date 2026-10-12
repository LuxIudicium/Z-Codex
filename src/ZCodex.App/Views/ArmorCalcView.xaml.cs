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

    // ── Tailles réglées aux splitters, persistées (demande de Philippe du 12/10/2026) ──
    // Relues à chaque ouverture de l'onglet ; enregistrées à la fin de chaque glissement. Les lignes reviennent en
    // parts relatives (étoiles) : la disposition garde ses proportions si la fenêtre change de taille.
    private const double MinLeftWidth = 200, MaxLeftWidth = 1200;

    private void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (Vm?.SavedLayout is not { } l) return;
        if (l.LeftWidth > 0) LeftColumn.Width = new GridLength(Math.Clamp(l.LeftWidth, MinLeftWidth, MaxLeftWidth));
        if (l.TopHeight > 0 && l.AttacksHeight > 0)
        {
            TopRow.Height = new GridLength(l.TopHeight, GridUnitType.Star);
            AttacksRow.Height = new GridLength(l.AttacksHeight, GridUnitType.Star);
        }
        if (l.CatalogHeight > 0 && l.TableHeight > 0)
        {
            CatalogRow.Height = new GridLength(l.CatalogHeight, GridUnitType.Star);
            TableRow.Height = new GridLength(l.TableHeight, GridUnitType.Star);
        }
    }

    // L'en-tête de la table des attaques est hors de la zone qui défile : il réserve à droite la largeur de la barre de
    // défilement quand elle est là, sinon ses titres partent à droite de leurs valeurs (QA de Philippe du 12/10/2026).
    private void AttackScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        double bar = Math.Max(0, AttackScroll.ActualWidth - AttackScroll.ViewportWidth);
        if (AttackHeader.Margin.Right != bar) AttackHeader.Margin = new Thickness(0, 0, bar, 2);
    }

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => Vm?.SaveLayout(new(LeftColumn.ActualWidth, TopRow.ActualHeight, AttacksRow.ActualHeight,
                              CatalogRow.ActualHeight, TableRow.ActualHeight));

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

    private void AddFreeAttack_Click(object sender, RoutedEventArgs e) => Vm?.AddFreeAttack();

    private void RemoveFreeAttack_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null && (sender as FrameworkElement)?.DataContext is FreeAttackVM f)
            Vm.RemoveFreeAttack(f);
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
