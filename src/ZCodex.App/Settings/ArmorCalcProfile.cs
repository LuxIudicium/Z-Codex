using ZCodex.Core.Models;

namespace ZCodex.App.Settings;

// Profil nommé du calculateur d'armure (chantier 14, Lot C). Sérialise TOUT l'état des zones 1-2
// pour rejouer un scénario à l'identique. Persisté dans settings.json (AppSettings.ArmorProfiles),
// patron SpikeTargetProfile. Champs simples + valeurs par défaut saines (un profil ancien sans un
// champ neuf reste lisible).
public class ArmorCalcProfile
{
    public string Name { get; set; } = string.Empty;

    // Zone 1 — perso / équipement.
    public Profession Profession { get; set; } = Profession.Warrior;
    public int BaseArmor { get; set; } = 80;
    // Insigne choisi par pièce (Tête, Torse, Bras, Jambes, Pieds), modId ; 0 = aucun.
    public List<int> PieceInsigniaModIds { get; set; } = new() { 0, 0, 0, 0, 0 };
    public bool ShieldEquipped { get; set; }
    public int ShieldBaseArmor { get; set; } = 16;
    public bool ShieldRequirementMet { get; set; } = true;
    public bool ShieldIsStrength { get; set; }

    // Mods d'équipement réels ajoutés (runes, mods d'armes, inscriptions), par modId.
    public List<int> AddedModIds { get; set; } = new();

    // Ajouts manuels (cas non couverts par le catalogue).
    public List<CustomContribDto> Customs { get; set; } = new();

    // Zone 2 — effets externes cochés + rang. Clé = Name (lignes fusionnées, 17/07) ; les anciens
    // profils portaient « Name|Scope » par demi-effet — migrés par préfixe au chargement.
    public List<EffectStateDto> Effects { get; set; } = new();
    public int ArmorPenetrationPercent { get; set; }

    // Lot D — état du perso : active les inscriptions de réduction plate conditionnelles
    // (« Sheltered by Faith » si enchanté, « Nothing to Fear » si maudit, « Run For Your Life! »
    // en posture). Absent d'un profil ancien → false, aucune réduction conditionnelle appliquée.
    public bool IsEnchanted { get; set; }
    public bool IsHexed { get; set; }
    public bool IsInStance { get; set; }

    // Chantier 14 bis (17/07) — états étendus : situations (bools) et comptes/seuils (ints)
    // activant les clauses conditionnelles des insignes et les effets per-unit (IW ×comp.
    // Illusion, Mantra of Signets/Artificer's ×signets). Défauts = continuité Lot B pour les
    // comptes per-unit (1 signet, 5 comp. Illusion) ; PV à 100 % (Undertaker's inactif).
    public bool IsAttacking { get; set; }
    public bool IsHoldingItem { get; set; }
    public bool IsUsingPreparation { get; set; }
    public bool IsPetAlive { get; set; }
    public bool HasCondition { get; set; }
    public bool IsActivatingSkill { get; set; }
    public bool HasWeaponSpell { get; set; }
    public bool HasShoutChant { get; set; }
    public int HealthPercent { get; set; } = 100;
    public int EnchantmentCount { get; set; }
    public int RechargingSkillCount { get; set; }
    public int MinionCount { get; set; }
    public int SpiritCount { get; set; }
    public int SignetCount { get; set; } = 1;
    public int IllusionSkillCount { get; set; } = 5;

    // Lot D — attaques de référence (skillId + rang). Liste VIDE (profil antérieur au Lot D) →
    // la liste par défaut en place est conservée, pas écrasée.
    public List<ReferenceAttackDto> ReferenceAttacks { get; set; } = new();
}

// Une contribution saisie à la main. Category : 0=Core, 1=Bonus, 2=Special. Scope : chaîne de
// ArmorEffectsData.Scope (All/Physical/Elemental/Slashing/Projectile).
public class CustomContribDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public int Category { get; set; }
    public string Scope { get; set; } = "All";
}

// État d'un effet de la table du Lot B : coché + rang choisi (12 par défaut).
public class EffectStateDto
{
    public string Key { get; set; } = string.Empty;   // Name (anciens profils : Name|Scope)
    public bool Checked { get; set; }
    public int Rank { get; set; } = 12;
}

// Une attaque de la table de référence (Lot D) : skill du catalogue + rang affiché.
public class ReferenceAttackDto
{
    public int SkillId { get; set; }
    public int Rank { get; set; } = 15;   // rang par défaut (décision Philippe), éditable par ligne
}
