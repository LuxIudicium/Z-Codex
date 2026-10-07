using System.Windows.Media;

namespace ZCodex.App.ViewModels;

/// <summary>Où en est un personnage dans une session partagée.</summary>
public enum CollabClaimState
{
    /// <summary>Pas de session sur ce teambuild : rien à afficher.</summary>
    None,
    /// <summary>Personne ne l'a pris : chacun peut le modifier, et le prendre.</summary>
    Free,
    /// <summary>Pris par moi : ma version fait foi.</summary>
    Mine,
    /// <summary>Pris par un autre participant : mes retouches y sont annulées.</summary>
    Other,
}

// Session partagée — état de « prise » d'un personnage, posé par CollabSessionViewModel.
//
// ⚠ Toutes les propriétés commencent par « Collab » : TeamBuildViewModel.OnChildChanged les
// exclut du suivi des modifications sur ce seul préfixe. Une prise n'est pas une édition du
// build — la compter comme telle salirait l'onglet et renverrait tout le build aux autres à
// chaque prise, chez chacun, en boucle.
public partial class CharacterSlotViewModel
{
    private CollabClaimState _collabClaim;
    private string _collabOwner = string.Empty;
    private Brush? _collabBrush;

    public CollabClaimState CollabClaim
    {
        get => _collabClaim;
        set
        {
            if (!SetField(ref _collabClaim, value)) return;
            OnPropertyChanged(nameof(CollabIsFree));
            OnPropertyChanged(nameof(CollabIsMine));
            OnPropertyChanged(nameof(CollabIsOther));
            OnPropertyChanged(nameof(CollabHasBadge));
            OnPropertyChanged(nameof(CollabShowBadge));
        }
    }

    private bool _collabIsRoot;

    /// <summary>Ligne racine : seule à porter le badge (la prise couvre toute la famille, les
    /// variantes suivent leur racine sans bouton à elles).</summary>
    public bool CollabIsRoot
    {
        get => _collabIsRoot;
        set { if (SetField(ref _collabIsRoot, value)) OnPropertyChanged(nameof(CollabShowBadge)); }
    }

    /// <summary>Pseudo du preneur (vide si libre).</summary>
    public string CollabOwner
    {
        get => _collabOwner;
        set => SetField(ref _collabOwner, value);
    }

    /// <summary>Couleur du preneur, la même dans la liste des participants du bandeau.</summary>
    public Brush? CollabBrush
    {
        get => _collabBrush;
        set => SetField(ref _collabBrush, value);
    }

    public bool CollabIsFree => _collabClaim == CollabClaimState.Free;
    public bool CollabIsMine => _collabClaim == CollabClaimState.Mine;
    public bool CollabIsOther => _collabClaim == CollabClaimState.Other;
    public bool CollabHasBadge => _collabClaim != CollabClaimState.None;
    public bool CollabShowBadge => CollabHasBadge && _collabIsRoot;
}
