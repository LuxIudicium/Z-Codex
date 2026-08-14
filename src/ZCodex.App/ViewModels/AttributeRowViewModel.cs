using ZCodex.Core.Models;

namespace ZCodex.App.ViewModels;

public class AttributeRowViewModel : ViewModelBase
{
    private int _points;
    private int _bonusPoints;
    private bool _atMost;

    public AttributeRowViewModel(int attributeId, string name, bool isPrimary)
    {
        AttributeId = attributeId;
        Name        = name;
        IsPrimary   = isPrimary;
    }

    public int    AttributeId { get; }
    public string Name        { get; }   // clé EN (logique/matching) — NE PAS traduire
    public bool   IsPrimary   { get; }

    // Nom affiché (langue courante) ; couvre attributs + rangs de titre. Name reste la clé logique.
    public string DisplayName => GwAttributeData.DisplayName(Name);
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(DisplayName));

    // Ligne d'une caractéristique de la profession SECONDAIRE (renseigné par CharacterSlotViewModel).
    // Sert au blocage des niveaux bonus quand le filtre PvP du catalogue est actif.
    public bool IsSecondary { get; init; }

    // Bornes : caractéristique standard = 0–12 base + 0–20 bonus. Rang de titre = 0–10, sans bonus.
    public int MaxPoints { get; init; } = 12;
    public int MaxBonus  { get; init; } = 20;

    // Filtre PvP du catalogue (SkillPanelViewModel.SelectedGameMode) : en PvP, aucun niveau bonus
    // JOUEUR (rune/casque/conso — cf. BonusPoints) n'est autorisé sur une caractéristique de la
    // profession SECONDAIRE (décision Philippe).
    //
    // /!\ NE CONCERNE PAS LES FLUX (précision Philippe) : les flux ne sont effectifs qu'EN PvP et
    // donnent eux aussi des bonus de caractéristique (Hidden Talent +2 aux SEC, Meek Shall Inherit
    // +2 PR et SEC) — simplement le joueur n'a pas la main dessus. Ils transitent par FluxBonus et
    // sont ajoutés en aval dans CharacterSlotViewModel.AttributeLevel, donc HORS de ce plafond :
    // les borner ici serait un bug. Seul le bonus que le JOUEUR pose est bloqué.
    //
    // État volontairement STATIQUE : le filtre est global à l'app, et les persos sont recréés en
    // permanence (chargement .pn3, undo, variantes) — un flag par perso obligerait à resynchroniser
    // chaque site de création. Posé par MainViewModel ; la purge des bonus déjà en place y est faite
    // aussi (décision Philippe : purge + blocage, pas de restauration au retour en PvE).
    public static bool PvpBlocksSecondaryBonus { get; set; }

    // Plafond du bonus JOUEUR : 0 si bloqué (rang de titre via MaxBonus 0, ou carac SEC en PvP).
    // Point de passage UNIQUE : le setter de BonusPoints et AdjustBonus s'y bornent, donc tous les
    // chemins de mutation (molette, débordement, SetEffectiveLevel) sont couverts sans garde locale.
    // N'affecte NI FluxBonus NI EffectiveLevel+flux (cf. la note ci-dessus).
    public int EffectiveMaxBonus => IsSecondary && PvpBlocksSecondaryBonus ? 0 : MaxBonus;

    // Niveau de caractéristique investi via les attribute points (0–MaxPoints).
    public int Points
    {
        get => _points;
        set
        {
            if (SetField(ref _points, Math.Clamp(value, 0, MaxPoints)))
            {
                OnPropertyChanged(nameof(PointsDisplay));
                OnPropertyChanged(nameof(EffectiveLevel));
                OnPropertyChanged(nameof(EffectiveDisplay));
            }
        }
    }

    // Niveaux bonus hors budget (rune +1/+2/+3, casque +1, consommables +1 cumulables).
    // Hard cap du niveau effectif à 20 (ex: base 12 + bonus 8 → effectif 20).
    // Borné à EffectiveMaxBonus → retombe à 0 sur une carac SEC quand le filtre PvP est actif.
    public int BonusPoints
    {
        get => _bonusPoints;
        set
        {
            if (SetField(ref _bonusPoints, Math.Clamp(value, 0, EffectiveMaxBonus)))
            {
                OnPropertyChanged(nameof(PointsDisplay));
                OnPropertyChanged(nameof(BonusDisplay));
                OnPropertyChanged(nameof(EffectiveLevel));
                OnPropertyChanged(nameof(EffectiveDisplay));
            }
        }
    }

    // Bonus de flux « attributs » (+2 : Hidden Talent / Meek Shall Inherit), poussé par
    // CharacterSlotViewModel. Affiché À PART dans la couleur du flux (ex. « 11+2+2 »), donc
    // HORS EffectiveLevel — le +2 est ajouté en aval par CharacterSlotViewModel.AttributeLevel
    // pour ne pas le compter deux fois. 0 = aucun flux d'attribut actif sur cette ligne.
    private int _fluxBonus;
    public int FluxBonus
    {
        get => _fluxBonus;
        set
        {
            if (SetField(ref _fluxBonus, value))
            {
                OnPropertyChanged(nameof(HasFluxBonus));
                OnPropertyChanged(nameof(FluxBonusDisplay));
            }
        }
    }

    public bool   HasFluxBonus    => _fluxBonus > 0;
    public string FluxBonusDisplay => _fluxBonus > 0 ? $"+{_fluxBonus}" : string.Empty;

    // Bonus d'un boost de compétence équipée ACTIF (Aura of the Lich, Awaken the Blood...), poussé
    // par CharacterSlotViewModel. Même mécanique que FluxBonus (affiché À PART, HORS EffectiveLevel,
    // ajouté en aval par CharacterSlotViewModel.AttributeLevel) — canal séparé pour ne pas mélanger
    // les deux sources (couleur dédiée, violette). 0 = aucun boost actif sur cette ligne.
    private int _skillBoost;
    public int SkillBoost
    {
        get => _skillBoost;
        set
        {
            if (SetField(ref _skillBoost, value))
            {
                OnPropertyChanged(nameof(HasSkillBoost));
                OnPropertyChanged(nameof(SkillBoostDisplay));
            }
        }
    }

    public bool   HasSkillBoost    => _skillBoost > 0;
    public string SkillBoostDisplay => _skillBoost > 0 ? $"+{_skillBoost}" : string.Empty;

    // Sens du seuil pour la recherche (ignoré par l'éditeur) : false = ≥ (au moins), true = ≤ (au plus).
    public bool AtMost
    {
        get => _atMost;
        set { if (SetField(ref _atMost, value)) OnPropertyChanged(nameof(ComparisonGlyph)); }
    }

    // Glyphe affiché sur le bouton bascule de la grille de seuils.
    public string ComparisonGlyph => _atMost ? "≤" : "≥";

    public void ToggleComparison() => AtMost = !_atMost;

    // Niveau effectif utilisé pour les calculs de variables de skills au survol.
    public int EffectiveLevel => Math.Min(Points + BonusPoints, 20);

    // Affiche "8+3" si bonus, "8" sinon.
    public string PointsDisplay => BonusPoints > 0 ? $"{Points}+{BonusPoints}" : Points.ToString();

    // ── Cadre d'attributs de l'ÉDITEUR DE BUILD uniquement ────────────────────────────────
    // Là-bas, base et bonus ont chacun leur spinner : il faut les deux valeurs SÉPARÉMENT plus
    // le total. Ailleurs (carte du teambuild, grille de recherche) la place manque et l'affichage
    // combiné PointsDisplay reste seul en piste.
    public string BonusDisplay     => BonusPoints > 0 ? $"+{BonusPoints}" : "0";
    public string EffectiveDisplay => $"= {EffectiveLevel}";

    // Ligne qui accepte un niveau bonus (faux pour un rang de titre : pas de rune sur un titre)
    // → le spinner de bonus et le total sont masqués. Fixé à la construction via MaxBonus, donc
    // sans notification : on ne se sert PAS d'EffectiveMaxBonus ici, qui dépend du filtre PvP
    // global et changerait sans prévenir la vue. Sous ce filtre les boutons restent affichés mais
    // le clamp du setter les rend sans effet — comme le Shift+molette des slots aujourd'hui.
    public bool SupportsBonus => MaxBonus > 0;

    public void Adjust(int delta)      => Points      = Math.Clamp(Points      + delta, 0, MaxPoints);
    public void AdjustBonus(int delta) => BonusPoints = Math.Clamp(BonusPoints + delta, 0, EffectiveMaxBonus);
    public void Increment()            => Adjust(1);
    public void Decrement()            => Adjust(-1);
}
