using ZCodex.Core.Data;
using ZCodex.Core.Models;

namespace ZCodex.App.ViewModels;

// Une icône du bandeau local de CE perso. Trois origines :
//  • une compétence qualifiante équipée sur CE perso : boost d'attribut (Lot A/B/C) ou accélérateur
//    d'adrénaline personnel (chantier infobulle, lot 1a) ;
//  • un effet diffusé par un COÉQUIPIER et reçu à la demande (Heroic Refrain, Weapon of Fury) — le
//    perso ne l'équipe pas forcément lui-même ;
//  • un mod d'arme du set actif (Skill null : ce n'est pas une compétence) — « Furieux » (lot 1a) ou
//    « de fractionnement » (lot 6a).
// Togglable dans tous les cas (clic = active/désactive le boost, ou reçoit/arrête de recevoir la
// diffusion). Cadre vert = actif, même idiome que le bandeau des rituels de la nature — mais local
// au perso, pas à l'équipe.
public class AttributeBoostIndicatorViewModel : ViewModelBase
{
    private readonly CharacterSlotViewModel _owner;

    public AttributeBoostIndicatorViewModel(CharacterSlotViewModel owner, Skill? skill, int toggleId, bool received)
    {
        _owner = owner;
        Skill = skill;
        ToggleId = toggleId;
        bool active = owner.IsAttributeBoostActive(toggleId);
        // Diffusion reçue (Heroic Refrain, Weapon of Fury) → « recevoir » ; boost propre → « activer ».
        string on  = T(received ? "S.Boost.Receive"       : "S.Boost.Activate");
        string off = T(received ? "S.Boost.StopReceiving" : "S.Boost.Deactivate");
        string click = string.Format(T("S.Boost.ClickTo"), active ? off : on);

        // Note au-dessus de l'instruction de clic : le mod « Furious », Jaundiced Gaze (effet de
        // retrait, lot 2), Glass Arrows (saignement sur coup bloqué, lot 4b) et l'Arme du Grand Nain
        // (28-40 % d'assommer, lot 4c) s'allument quand ils ont proc ;
        // Natural Temper ne fait rien tant qu'un effet actif
        // enchante le perso (Onslaught) ; le Sceau de l'Archer non plus tant que le set d'armes actif ne porte pas
        // d'arc, ni l'Application de poison tant qu'un effet convertit les dégâts d'attaque du perso (lot 4b) ; Selfless Spirit s'allume quand le sort vise un autre allié (lot 2) ; Ghostly Haste
        // quand un esprit est à portée, Signet of Mystic Speed quand l'enchantement vise ce perso (lot 3) ;
        // la Célérité symbolique (lot 5) prévient quand Incantation rapide vaut 0 — allumée, elle FAIT BAISSER
        // les sceaux du perso, ce qui est le vrai comportement du jeu mais surprend sans un mot d'explication.
        string? note = skill is null || skill.Id is 763 or 1199 or 3145
                       || skill.Id == KnockdownData.GreatDwarfWeaponSkillId ? T("S.Boost.ProcNote")
            // Conjuration (ou Aura de poussière d'ébène) allumée alors que l'arme n'inflige pas son type de
            // dégâts : sans ce mot, son absence totale d'effet passe pour un bug (lot 6a, § 6.1).
            : owner.BoostElementMismatch(skill) is { } required
                ? string.Format(T("S.Boost.NoEffectWrongElement"), SkillDamage.DisplayType(required))
            // Sceau de puissance spectrale en PvP : mécanique divergente de sa jumelle — un seul esprit en
            // profite en jeu, l'infobulle l'affiche sur tous (décision du 24/09/2026).
            : skill.Id == DamageBoostData.GhostlyMightPvpSkillId ? T("S.Boost.SingleSpiritNote")
            : AdrenalineBoostData.BySkillId(skill.Id) is { NeedsUnenchanted: true } && owner.IsEnchantedByAdrenalineEffect
                ? T("S.Boost.NoEffectEnchanted")
            : skill.Id == ConditionDurationData.ArcherSignetSkillId && owner.ArcherSignetWithoutBow
                ? T("S.Boost.NoEffectWithoutBow")
            : ConditionDurationData.BySkillId(skill.Id) is { RequiresPhysical: true } && owner.AttacksNoLongerPhysical
                ? T("S.Boost.NoEffectNonPhysical")
            : skill.Id is EnergyCostBoostData.SelflessSpiritKurzickSkillId or EnergyCostBoostData.SelflessSpiritLuxonSkillId
                ? T("S.Boost.OtherAllyNote")
            : skill.Id == AttributeSubstitutionData.SymbolicCeleritySkillId && owner.FastCastingRank == 0
                ? T("S.Boost.ZeroFastCasting")
            : skill.Id == SkillSpeedBoostData.GhostlyHasteSkillId ? T("S.Boost.SpiritNote")
            : skill.Id == SkillSpeedBoostData.SignetOfMysticSpeedSkillId ? T("S.Boost.SelfEnchantmentNote")
            : null;
        ClickNote = note is null ? click : $"{note}\n{click}";
    }

    private static string T(string key) => ZCodex.App.LanguageManager.T(key);

    // Null pour un mod d'ARME (« Furieux » du lot 1a, « de fractionnement » du lot 6a) : le bandeau montre
    // alors l'icône de la statistique concernée et ModText.
    public Skill? Skill { get; }
    public bool HasSkill => Skill is not null;
    // Infobulle du mod d'arme, à la place du tooltip de compétence.
    public string ModText => HasSkill ? string.Empty
        : T(ToggleId == DamageBoostData.SunderingModToggleId ? "S.Boost.SunderingTip" : "S.Boost.FuriousTip");

    private bool IsSunderingMod => !HasSkill && ToggleId == DamageBoostData.SunderingModToggleId;

    // Icône de statistique tenant lieu d'icône de compétence pour un mod d'arme. La pénétration d'armure
    // n'en a aucune dans le jeu de 11 icônes de stats : le bandeau affiche alors ModGlyph.
    public bool HasModIcon  => !HasSkill && !IsSunderingMod;
    public string ModIcon   => HasModIcon ? "adrenaline" : string.Empty;
    public bool HasModGlyph => IsSunderingMod;
    public string ModGlyph  => IsSunderingMod ? "%" : string.Empty;
    // Id sous lequel l'état est mémorisé chez le perso (cf. CharacterSlotViewModel.SetAttributeBoost).
    public int ToggleId { get; }
    // Note band-only, affichée sous le vrai tooltip de la compétence.
    public string ClickNote { get; }

    public bool IsActive => _owner.IsAttributeBoostActive(ToggleId);

    // Le bandeau est reconstruit à chaque bascule et à chaque changement de barre de compétences,
    // donc IsActive est relu à neuf — pas besoin de notifier ici.
    public void Toggle() => _owner.SetAttributeBoost(ToggleId, !IsActive);
}
