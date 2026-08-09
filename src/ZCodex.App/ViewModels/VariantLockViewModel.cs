using System.Collections.ObjectModel;
using System.Windows.Media;

namespace ZCodex.App.ViewModels;

// Un "cadenas" (tuple de variantes) : relie des lignes (racines et/ou variantes) en une composition
// cohérente, repérée par un indice et une couleur. Sert à l'affichage (cadre + badge) et à la
// « vue complète » qui isole la composition d'un cadenas.
public class VariantLockViewModel : ViewModelBase
{
    public int Index { get; }
    public string ColorHex { get; }
    public Brush ColorBrush { get; }
    public ObservableCollection<CharacterSlotViewModel> Members { get; } = new();

    public VariantLockViewModel(int index, string colorHex)
    {
        Index = index;
        ColorHex = colorHex;
        ColorBrush = (Brush?)new BrushConverter().ConvertFromString(colorHex) ?? Brushes.Gray;
        if (ColorBrush.CanFreeze) ColorBrush.Freeze();
        Members.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MembersTooltip));
    }

    public string MembersTooltip =>
        T("S.Lock.MembersTooltip") + string.Join(", ", Members.Select(m => m.Name));

    // Libellés de menu (Header est typé object → StringFormat ne s'applique pas, d'où ces propriétés).
    // Calculés à chaque lecture : le menu contextuel est reconstruit à chaque ouverture, donc la
    // langue courante s'applique sans notification supplémentaire.
    public string EditLabel     => T("S.Lock.Edit");
    public string RemoveLabel   => T("S.Lock.Remove");
    public string ExportLabel   => T("S.Lock.Export");
    public string ExportAsLabel => T("S.Lock.ExportAs");

    private string T(string key) =>
        string.Format(ZCodex.App.LanguageManager.T(key), Index);
}

// Une cellule de la bande de cadenas d'UNE ligne : référence un cadenas du build et dit si la ligne
// en est membre. Les lignes réservent une cellule par cadenas → barres de même indice alignées
// verticalement (membre = barre colorée, non-membre = espace vide de même largeur).
public class LockCellViewModel
{
    public VariantLockViewModel Lock { get; }
    public bool IsMember { get; }
    public LockCellViewModel(VariantLockViewModel lk, bool isMember) { Lock = lk; IsMember = isMember; }
}
