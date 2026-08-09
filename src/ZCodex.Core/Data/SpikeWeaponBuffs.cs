using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Les buffs d'arme du calcul de spike : les 5 du cadrage v1 + les 4 classiques
/// mono-allié du lot 2 (décision Philippe 14/07). Les buffs de GROUPE (Order of Pain/Vampire,
/// Strength of Honor, Winnowing, Ebon Battle Standard of Honor) restent comptés par l'autre
/// voie : cadre-vert sur le buff lui-même + compteur « Procs » (<see cref="SpikeProcSkills"/>)
/// — les doubler ici créerait un double compte.</summary>
public enum SpikeBuff
{
    SunderingWeapon, JudgesInsight, Vengeance, DestructiveWasGlaive, AnthemOfEnvy,
    // Lot 2 (14/07) — buffs mono-allié sans équivalent procs :
    SplinterWeapon, BrutalWeapon, GreatDwarfWeapon, FindTheirWeakness,
}

/// <summary>
/// Buffs d'arme du spike : effets lancés AVANT le spike qui modifient les dégâts des lignes.
/// État actif PAR PERSO (décision Philippe : icône cliquable sur la carte du membre, pas un
/// toggle global). Un buff n'est proposé que s'il est ÉQUIPÉ sur un membre du roster spike ;
/// Destructive Was Glaive (« YOUR Ritualist skills ») n'est proposé que sur son porteur.
///
/// Pénétration d'armure (wiki/Armor_penetration) — deux catégories :
/// - BASE (« have X% armor penetration ») : NON cumulable, seul le MAX s'applique — rejoint le
///   pool existant (AP innée du skill, rang de Force). Sundering Weapon 10 %, DWG 20 % (PvP 10 %).
/// - BONUS (« adds +X% ») : se CUMULE par-dessus le max de base. Judge's Insight +20 %.
///
/// Effets v1 (textes DB rang 12) :
/// - Sundering Weapon : 10 % AP de base sur les 3 PREMIÈRES attaques du perso (ordre de cast) ;
///   le Cracked Armor infligé reste couvert par le toggle « Cracked Armor » de la cible.
/// - Judge's Insight : attaques converties en SACRÉ (l'AL cible retombe sur l'armure de base,
///   contournant les bonus anti-physique) + 20 % AP en bonus.
/// - Vengeance : ×1,25 sur l'assiette DÉGÂTS de toutes les lignes du perso (sorts inclus —
///   décision Philippe) ; vol et perte de vie exclus.
/// - Destructive Was Glaive : 20 % AP de base (PvP : 10 %) sur les skills RITUALIST du porteur.
/// - Anthem of Envy : +X (résolu au rang de Leadership du CHANTEUR) sur le PREMIER attack skill
///   du perso dans l'ordre de cast ; la clause « cible > 50 % PV » est portée par le toggle
///   lui-même (icône éteinte = cible sous 50 %), pas par la case conditionnelle.
/// </summary>
public static class SpikeWeaponBuffs
{
    /// <summary>Un buff proposable : clé stable (persistance .pn3), noms core/PvP, tooltip bilingue
    /// (<see cref="DisplayTooltip"/> choisit selon <see cref="AppLanguage.IsFr"/>).</summary>
    public sealed record Descriptor(SpikeBuff Buff, string Key, string CoreName, string? PvpName,
                                    string TooltipFr, string TooltipEn, bool SelfOnly)
    {
        /// <summary>Tooltip du buff dans la langue affichée.</summary>
        public string DisplayTooltip => AppLanguage.IsFr ? TooltipFr : TooltipEn;
    }

    public static readonly IReadOnlyList<Descriptor> All =
    [
        new(SpikeBuff.SunderingWeapon, "sundering-weapon", "Sundering Weapon", null,
            "Sundering Weapon — 10 % de pénétration sur les 3 premières attaques (ordre de cast)",
            "Sundering Weapon — 10% armor penetration on the first 3 attacks (cast order)",
            SelfOnly: false),
        new(SpikeBuff.JudgesInsight, "judges-insight", "Judge's Insight", null,
            "Judge's Insight — attaques converties en sacré (AL de base de la cible) + 20 % de pénétration",
            "Judge's Insight — attacks converted to holy (target's base AL) + 20% armor penetration",
            SelfOnly: false),
        new(SpikeBuff.Vengeance, "vengeance", "Vengeance", null,
            "Vengeance — ×1,25 sur tous les dégâts du perso (vol/perte de vie exclus)",
            "Vengeance — ×1.25 on all of the character's damage (life steal/loss excluded)",
            SelfOnly: false),
        new(SpikeBuff.DestructiveWasGlaive, "destructive-was-glaive", "Destructive Was Glaive",
            "Destructive Was Glaive (PvP)",
            "Destructive Was Glaive — 20 % de pénétration (PvP : 10 %) sur les skills Ritualist du porteur",
            "Destructive Was Glaive — 20% armor penetration (PvP: 10%) on the holder's Ritualist skills",
            SelfOnly: true),
        new(SpikeBuff.AnthemOfEnvy, "anthem-of-envy", "Anthem of Envy", "Anthem of Envy (PvP)",
            "Anthem of Envy — +X sur le premier attack skill (si la cible est à plus de 50 % de ses PV)",
            "Anthem of Envy — +X on the first attack skill (if the target is above 50% Health)",
            SelfOnly: false),
        // Lot 2 (validé Philippe 14/07) — buffs mono-allié ; valeurs résolues au rang du LANCEUR.
        new(SpikeBuff.SplinterWeapon, "splinter-weapon", "Splinter Weapon", "Splinter Weapon (PvP)",
            "Splinter Weapon — +X (rang du lanceur) sur les 4 premières attaques, ignore l'armure (cible unique : 1 éclat par coup)",
            "Splinter Weapon — +X (caster's rank) on the first 4 attacks, ignores armor (single target: 1 splinter per hit)",
            SelfOnly: false),
        new(SpikeBuff.BrutalWeapon, "brutal-weapon", "Brutal Weapon", null,
            "Brutal Weapon — +X sur chaque attaque (aucun effet si le perso est enchanté : laisser l'icône éteinte dans ce cas)",
            "Brutal Weapon — +X on each attack (no effect while the character is enchanted: leave the icon off in that case)",
            SelfOnly: false),
        new(SpikeBuff.GreatDwarfWeapon, "great-dwarf-weapon", "Great Dwarf Weapon", null,
            "Great Dwarf Weapon — +X de dégâts d'ARME (avant armure) sur chaque attaque ; le renversement (40 %) n'est pas compté",
            "Great Dwarf Weapon — +X WEAPON damage (before armor) on each attack; the knockdown (40%) is not counted",
            SelfOnly: false),
        new(SpikeBuff.FindTheirWeakness, "find-their-weakness", "\"Find Their Weakness!\"", null,
            "« Find Their Weakness! » — +X (rang du crieur) sur la première attaque ; la Deep Wound passe par le toggle de la cible",
            "\"Find Their Weakness!\" — +X (shouter's rank) on the first attack; the Deep Wound goes through the target's toggle",
            SelfOnly: false),
    ];

    /// <summary>AP de base de Sundering Weapon (« have 10% armor penetration »).</summary>
    public const int SunderingBasePen = 10;
    /// <summary>Nombre d'attaques couvertes par Sundering Weapon (« next 3 attacks »).</summary>
    public const int SunderingAttacks = 3;
    /// <summary>AP en BONUS de Judge's Insight (« adds +20% ») — cumulée par-dessus le max de base.</summary>
    public const int JudgesBonusPen = 20;
    /// <summary>Multiplicateur de Vengeance (« deals 25% more damage »).</summary>
    public const double VengeanceMultiplier = 1.25;
    /// <summary>ProfessionId Ritualist (périmètre de DWG : « Your Ritualist skills »).</summary>
    public const int RitualistProfessionId = 8;

    /// <summary>AP de base de DWG selon la copie équipée (core 20 %, PvP 10 %).</summary>
    public static int DwgBasePen(bool pvpCopy) => pvpCopy ? 10 : 20;

    /// <summary>Nombre d'attaques couvertes par Splinter Weapon (« Ends after 4 attacks »).</summary>
    public const int SplinterAttacks = 4;

    // Les valeurs résolues par SkillProgression sont entourées de marqueurs de surlignage
    // (MarkChars) — même convention que SkillDamage.Value, marqueurs optionnels (entier fixe).
    // « weapon » optionnel : Great Dwarf Weapon dit « +20 weapon damage ».
    private static readonly Regex BonusDamageRegex = new(
        $@"\+\s*[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+(?:weapon\s+)?damage\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Paquet SANS « + » de Splinter Weapon (« Attacks deal 41 damage to 3 adjacent foes »).
    private static readonly Regex PlainDamageRegex = new(
        $@"[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+damage\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Le « +X [weapon] damage » d'un buff (Anthem of Envy, Brutal Weapon, Great Dwarf
    /// Weapon, « Find Their Weakness! »), parsé de la description RÉSOLUE au rang du lanceur.
    /// 0 si introuvable.</summary>
    public static int BonusDamage(string resolvedDescription)
        => BonusDamageRegex.Match(resolvedDescription) is { Success: true } m
            ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>Le « X damage » (sans +) de Splinter Weapon, résolu au rang de Channeling du
    /// lanceur (41 core / 25 PvP à r12). 0 si introuvable.</summary>
    public static int SplinterDamage(string resolvedDescription)
        => PlainDamageRegex.Match(resolvedDescription) is { Success: true } m
            ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>Descripteur du buff dont une copie (core ou PvP) porte ce nom de skill, sinon null.</summary>
    public static Descriptor? FromSkillName(string skillName)
        => All.FirstOrDefault(d =>
            string.Equals(d.CoreName, skillName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.PvpName, skillName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Descripteur par clé de persistance, sinon null (clé inconnue d'un fichier futur).</summary>
    public static Descriptor? FromKey(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}
