namespace ZCodex.App.ViewModels;

// Session partagée liée à CET onglet — c'est ce que lit le bandeau au-dessus de la grille.
// ⚠ Préfixe « Collab » obligatoire : BeginTracking exclut du suivi des modifications toute
// propriété qui le porte (l'état de la connexion n'est pas une édition du build).
public partial class TeamBuildViewModel
{
    private CollabSessionViewModel? _collab;

    public CollabSessionViewModel? Collab
    {
        get => _collab;
        set
        {
            if (SetField(ref _collab, value)) OnPropertyChanged(nameof(CollabIsActive));
        }
    }

    public bool CollabIsActive => _collab is not null;
}
