using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Overrides conditionnels NOMMÉS du chantier spike : paquets de dégâts bonus dont la condition
/// existe mais échappe au régex générique de <see cref="SkillDamage"/> — condition détachée du mot
/// « damage » (Melandru's Assault PvP : « …to target and foes adjacent to target if this foe is
/// enchanted »), état non-standard hors <c>ConditionStates</c> (Piercing Trap : Cracked Armor ;
/// EBSoH : « against Charr »), ou « if » placé EN TÊTE de phrase (Mind Burn : « If you have more
/// energy than target foe, deals 51 more… »).
///
/// Effet : le paquet visé (repéré par valeur + statut bonus, dans la description RÉSOLUE) est
/// forcé CONDITIONNEL, avec une clause FR pour le tooltip de la case « Condition remplie ». Comme
/// tout conditionnel, il reste COMPTÉ par défaut (case cochée) → le total par défaut ne change pas,
/// seule une case apparaît pour pouvoir l'exclure. Décision Philippe 14/07 : Piercing Trap piloté
/// par une case par-skill (pas par le toggle Cracked Armor global de la cible).
///
/// Match par nom EXACT. À NE PAS confondre avec l'extension « additional damage if » (Visions of
/// Regret), captée directement par MoreDamageRegex — pas besoin d'override.
/// </summary>
public static class SpikeConditionalOverrides
{
    /// <summary>Un paquet à forcer conditionnel : valeur résolue (AL 60), statut bonus (« +X more »
    /// = true, distingue le +51 bonus de Mind Burn de son 51 fire de base), clause bilingue du
    /// tooltip (<see cref="Display"/> choisit selon <see cref="AppLanguage.IsFr"/>).</summary>
    public sealed record Override(int Value, bool Bonus, string ConditionFr, string ConditionEn)
    {
        /// <summary>Clause conditionnelle dans la langue affichée.</summary>
        public string Display => AppLanguage.IsFr ? ConditionFr : ConditionEn;
    }

    private static readonly IReadOnlyList<Override> None = [];

    private static readonly Dictionary<string, Override[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // « +29 more damage … if this foe is enchanted » (condition détachée). La copie CORE n'a
        // que « +17 damage to nearby foes » (aucun conditionnel) → pas d'entrée.
        ["Melandru's Assault (PvP)"] = [new Override(29, true, "si la cible est enchantée", "if this foe is enchanted")],
        // « 51 more damage to any foes with Cracked Armor » (état hors liste). Case par-skill
        // (décision Philippe), indépendante du toggle Cracked Armor global.
        ["Piercing Trap"] = [new Override(51, true, "contre une cible avec Cracked Armor", "against a foe with Cracked Armor")],
        // « If you have more energy than target foe, deals 51 more fire damage » (« if » en tête).
        // Cible le +51 BONUS, pas le 51 fire de base.
        ["Mind Burn"] = [new Override(51, true, "si vous avez plus d'énergie que la cible", "if you have more Energy than the target")],
        // « +10 more damage against Charr » (espèce de foe). Le +15 de base reste inconditionnel.
        ["Ebon Battle Standard of Honor"] = [new Override(10, true, "contre les Charr", "against Charr")],
    };

    /// <summary>Les paquets à forcer conditionnels pour cette compétence (vide si aucune).</summary>
    public static IReadOnlyList<Override> For(string skillName)
        => Map.TryGetValue(skillName, out var o) ? o : None;
}
