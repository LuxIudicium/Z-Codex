using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.App.ViewModels;

/// <summary>
/// Aperçu d'un template d'ÉQUIPEMENT dans le navigateur, calqué sur l'écran « Charger un modèle
/// d'équipement » du jeu : profession en tête, puis le détail emplacement par emplacement.
///
/// Le jeu affiche des icônes d'objets ; on affiche le récapitulatif TEXTE, qui porte davantage
/// (nom composé avec insigne/rune, préfixe/suffixe, inscription) et qui ne dépend d'aucun asset
/// à télécharger. La profession n'est pas encodée dans le format : elle est déduite de l'armure
/// (cf. <see cref="GwEquipmentInfo.GuessProfession"/>), comme dans la liste de fichiers.
///
/// Objet immuable, reconstruit à chaque sélection ET à chaque bascule de langue — les libellés
/// sont figés à la construction dans la langue courante.
/// </summary>
public sealed class EquipmentPreviewViewModel
{
    public Profession Profession { get; }
    public IReadOnlyList<EquipmentSummaryLine> Lines { get; }

    public string ProfessionName =>
        Profession == Profession.None ? string.Empty : Profession.DisplayName();

    // Profession indéterminée = armure faite de pièces communes, ou aucune armure : on masque
    // l'en-tête plutôt que d'afficher un vide sous une icône absente.
    public bool HasProfession => Profession != Profession.None;

    public EquipmentPreviewViewModel(EquipmentBuild build)
    {
        Profession = GwEquipmentInfo.GuessProfession(build);
        // Effets des inscriptions inclus : leur nom (« Tout en muscles ») est un titre, pas une
        // statistique — l'aperçu a la place de les développer, l'infobulle ⚔ non.
        Lines      = GwEquipmentCodec.SummarizeLines(build, withInscriptionEffects: true);
    }
}
