namespace ZCodex.Core.Data;

/// <summary>
/// Liste blanche des compétences à SEUIL du chantier spike : celles dont un paquet de dégâts est
/// « X <b>for each</b> [ressource statique dénombrable] (<b>maximum</b> Y) » (signets équipés,
/// conditions/hexes sur la cible, enchantements sur soi, énergie, compétences d'adrénaline…).
/// Une fois cadre-vert, elles reçoivent un compteur « ×N » sur la ligne : la valeur par-unité est
/// multipliée par le nombre choisi, plafonné au maximum annoncé (défaut = compte atteignant le
/// plafond, esprit « spike optimiste »).
///
/// À NE PAS confondre avec :
/// - les <b>ticks</b> (« X each second », temps) → <see cref="SkillDamage"/> tick segments ;
/// - les <b>procs</b> (« X whenever/each time [sujet] agit », par action, sans plafond) →
///   <see cref="SpikeProcSkills"/> ; Visions of Regret (« damage whenever they use a skill ») est
///   un PROC, pas un seuil — hors de cette liste (décision Philippe : traité plus tard).
///
/// Match par nom EXACT. Semée par audit sur la vraie DB (harnais), filtrée aux seuls paquets de
/// dégâts sur cible (les ~90 autres « for each » du catalogue = soin/énergie/durée/armure).
/// Périmètre (décision Philippe) : ressources STATIQUES, plafond lisible dans le texte (nombre
/// fixe ou valeur de progression). Les comptes DÉRIVÉS (Energy Surge/Burn = énergie perdue,
/// Destruction = secondes de vie de l'esprit, Rising Bile = secondes d'effet du hex) sont couverts
/// depuis le lot 3 (14/07) : le plafond du compte est dérivé d'un autre nombre du texte (énergie
/// perdue ou durée d'effet → max synthétique dans <see cref="SkillDamage"/>, ou « maximum N damage »
/// pour Destruction). La progression manquante de Rising Bile a été comblée côté données
/// (ManualProgression du scraper + DB). La valeur par-unité et le plafond sont PARSÉS de la
/// description résolue (dépendent du rang) — cette liste ne fait que le tri.
/// </summary>
public static class SpikeThresholdSkills
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Symbolic Strike",                                    // +12/signet équipé, max 70 (attaque)
        "Signet of Deadly Corruption", "Signet of Deadly Corruption (PvP)", // 29/condition, max 130
        "Signet of Mystic Wrath",                             // 25 holy/enchantement, max 102 (prog.)
        "Blades of Steel",                                    // +14/attaque dague en recharge, max 60
        "Aneurysm",                                           // 3/point d'énergie regagné, max 24
        "Signet of Rage",                                     // +9 holy/compétence d'adrénaline (part add., pas de max)
        // Lot 2 (validé Philippe 14/07) — sortis de l'audit, même forme exacte :
        "Doom",                                               // 50 foudre/rituel en recharge, max 135 (respecte l'armure)
        "Enraged Lunge (PvP)",                                // +19/skill Beast Mastery en recharge, max 80 (la core = +42 flat + Deep Wound, pas un seuil)
        "Energy Blast",                                       // 2/point d'énergie courante, max 130
        "Radiant Scythe",                                     // +1/point d'énergie courante, max 25 (attaque)
        // Lot 3 (validé Philippe 14/07) — COMPTES DÉRIVÉS : le plafond du compte vient d'un
        // nombre du texte autre qu'un « maximum N damage » (énergie perdue / secondes de vie) :
        "Energy Surge",                                       // 7/point d'énergie perdu, plafond = énergie perdue (max synthétique)
        "Energy Burn",                                        // 9/point d'énergie perdu, plafond = énergie perdue (max synthétique)
        "Destruction",                                        // 21/seconde de vie de l'esprit, max 150 damage (respecte pas l'armure : sans type)
        "Rising Bile",                                        // 5/seconde d'effet, plafond = durée du hex (20 s → max synthétique 100)
    };

    /// <summary>Compte de repli quand aucun « maximum » n'est annoncé (Signet of Rage) : une barre
    /// pleine = 8 compétences/conditions/enchantements au plus.</summary>
    public const int FallbackCap = 8;

    /// <summary>Vrai si la compétence porte un compteur de seuil « ×N » (liste blanche).</summary>
    public static bool IsThresholdCapable(string skillName) => Names.Contains(skillName);
}
