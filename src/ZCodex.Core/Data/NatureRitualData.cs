using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Rituels de la nature (esprits Ranger de zone) qui modifient l'énergie, la recharge, le temps
/// d'incantation, l'entretien ou l'overcast des compétences. Environnement de simulation GLOBAL
/// (comme les flux) : un rituel actif s'applique aux infobulles de TOUS les persos.
///
/// Mécaniques confirmées par Philippe (source : wiki + verbatim) :
///  • Arrondi énergie ET recharge = HALF-TO-EVEN (au pair le plus proche), pas half-up.
///  • Energizing Wind (flat −15, plancher 10) est appliqué AVANT l'Expertise.
///  • Quickening Zephyr énergie = base ×1,30 (oracle wiki : 1→1, 5→6, 10→13, 15→20, 25→32).
///  • QZ et Expertise sont multiplicatifs et commutatifs → combinés puis arrondis UNE fois.
///  • EW + QZ simultanés : résultat dépendant de l'ordre de cast → on prend l'OPTIMISTE
///    (le moins cher des deux ordres), énergie et recharge.
///  • Les flats (Quicksand, Roaring Winds) s'ajoutent tout à la fin, après les %.
///  • Fast Casting reste hors périmètre.
/// Tranquility (durée d'enchantement) est déclarée ici pour le bandeau, mais son effet est traité
/// dans un lot séparé (Lot D) — ses fonctions énergie/recharge/cast sont des no-op.
/// </summary>
public static class NatureRitualData
{
    public enum Ritual
    {
        EnergizingWind,
        QuickeningZephyr,
        PrimalEchoes,
        RoaringWinds,
        Quicksand,
        NaturesRenewal,
        Equinox,
        Tranquility,
    }

    /// <summary>Métadonnées d'un rituel : identité, mappage vers la compétence de la base, libellé
    /// bilingue (<see cref="DisplayTooltip"/> choisit selon <see cref="AppLanguage.IsFr"/>).</summary>
    public sealed record Descriptor(Ritual Ritual, int SkillId, string Name, string TooltipFr, string TooltipEn)
    {
        /// <summary>Résumé du rituel dans la langue affichée.</summary>
        public string DisplayTooltip => AppLanguage.IsFr ? TooltipFr : TooltipEn;
    }

    // SkillId relevés dans la base réelle (probe scratchpad).
    public static readonly IReadOnlyList<Descriptor> All =
    [
        new(Ritual.EnergizingWind,   474,  "Energizing Wind",   "Compétences : −15 énergie (min. 10) et recharge +25 % (plus lente).",
            "Skills: −15 Energy (min. 10) and +25% recharge (slower)."),
        new(Ritual.QuickeningZephyr, 475,  "Quickening Zephyr", "Compétences : +30 % énergie et recharge ×2 plus rapide.",
            "Skills: +30% Energy and recharge twice as fast."),
        new(Ritual.PrimalEchoes,     469,  "Primal Echoes",     "Les sceaux coûtent 10 énergie.",
            "Signets cost 10 Energy."),
        new(Ritual.RoaringWinds,     1725, "Roaring Winds",     "Chants et cris coûtent +1…5 énergie (rang du lanceur).",
            "Chants and shouts cost +1…5 Energy (caster's rank)."),
        new(Ritual.Quicksand,        1473, "Quicksand",         "+1 énergie sur toute compétence, +1 de plus sur les attaques.",
            "+1 Energy on every skill, +1 more on attacks."),
        new(Ritual.NaturesRenewal,   476,  "Nature's Renewal",  "Enchantements/maléfices : incantation ×2 ; entretien des enchantements ×2 énergie.",
            "Enchantments/hexes: ×2 cast time; enchantment upkeep ×2 Energy."),
        new(Ritual.Equinox,          1212, "Equinox",           "Les sorts à overcast infligent +10 overcast.",
            "Overcast spells inflict +10 overcast."),
        new(Ritual.Tranquility,      1213, "Tranquility",       "Les enchantements expirent 20…50 % plus vite. (durée — lot séparé)",
            "Enchantments expire 20…50% faster. (duration — separate lot)"),
    ];

    public static Descriptor? BySkillId(int skillId) => All.FirstOrDefault(d => d.SkillId == skillId);

    public static int SkillIdOf(Ritual ritual) => All.First(d => d.Ritual == ritual).SkillId;

    // ── Prédicats de type (valeurs SkillType exactes de la base) ──────────────
    public static bool IsSignet(Skill s)      => s.SkillType.Contains("Signet", StringComparison.Ordinal);
    public static bool IsChantOrShout(Skill s) => s.SkillType is "Chant" or "Shout";
    public static bool IsAttack(Skill s)      => s.SkillType.EndsWith("Attack", StringComparison.Ordinal);
    public static bool IsEnchantment(Skill s) => s.SkillType.Contains("Enchant", StringComparison.Ordinal);
    // La base contient une coquille « Hex spell » (minuscule) → comparaison insensible à la casse.
    public static bool IsHex(Skill s)         => s.SkillType.Contains("Hex", StringComparison.OrdinalIgnoreCase);

    /// <summary>Arrondi au pair le plus proche (banker's rounding), convention GW1 énergie/recharge.</summary>
    public static int RoundEven(double x) => (int)Math.Round(x, MidpointRounding.ToEven);

    // Energizing Wind : réduit de 15 avec plancher 10 ; ne remonte JAMAIS un coût déjà ≤ 10.
    private static int ApplyEnergizingWind(int v) => v <= 10 ? v : Math.Max(v - 15, 10);

    /// <summary>Coût en énergie affiché, incluant Expertise + flux + rituels actifs.</summary>
    public readonly record struct EnergyResult(int Base, int Final, bool FluxLowered, bool RitualChanged);

    /// <param name="baseCost">Coût de base (Skill.EnergyCost).</param>
    /// <param name="expertiseRank">Rang d'Expertise effectif si la skill est concernée, sinon 0.</param>
    /// <param name="fluxPct">Réduction énergie de flux (0/20/25).</param>
    /// <param name="roaringWindsBonus">Bonus Roaring Winds résolu au rang du lanceur (0 si N/A).</param>
    public static EnergyResult EnergyCost(
        int baseCost, Skill skill, IReadOnlySet<Ritual> active,
        int expertiseRank, int fluxPct, int roaringWindsBonus)
    {
        bool ew = active.Contains(Ritual.EnergizingWind);
        bool qz = active.Contains(Ritual.QuickeningZephyr);
        bool pe = active.Contains(Ritual.PrimalEchoes) && IsSignet(skill);
        bool qs = active.Contains(Ritual.Quicksand);
        bool rw = active.Contains(Ritual.RoaringWinds) && IsChantOrShout(skill);

        // Primal Echoes fixe le coût d'entrée des signets à 10 (base 0 sinon).
        int start = pe ? 10 : baseCost;

        // Bloc multiplicatif : QZ × Expertise, combinés puis arrondis une seule fois (half-even).
        double qzF  = qz ? 1.30 : 1.0;
        double expF = expertiseRank > 0 ? (100 - 4 * expertiseRank) / 100.0 : 1.0;
        int Block(int v) => RoundEven(v * qzF * expF);

        // Placement d'Energizing Wind (flat, avant Expertise). EW+QZ simultanés : optimiste
        // (le moins cher des deux ordres EW↔QZ) ; sinon l'ordre confirmé « EW puis Expertise ».
        int preFlux;
        if (ew && qz)
            preFlux = Math.Min(Block(ApplyEnergizingWind(start)),   // EW puis %
                               ApplyEnergizingWind(Block(start)));   // % puis EW
        else if (ew)
            preFlux = Block(ApplyEnergizingWind(start));
        else
            preFlux = Block(start);

        // Flux : étape existante inchangée (half-up via +50), appliquée après le bloc %.
        int afterFlux = fluxPct > 0 ? (preFlux * (100 - fluxPct) + 50) / 100 : preFlux;

        // Flats tout à la fin. Quicksand (modèle Philippe) : −1 pour l'utilisation d'une compétence,
        // −1 de plus pour l'attaque elle-même → toute compétence d'ATTAQUE = +2 (qu'elle coûte de
        // l'énergie OU de l'adrénaline), toute autre compétence = +1.
        int flats = (qs ? (IsAttack(skill) ? 2 : 1) : 0)
                  + (rw ? roaringWindsBonus : 0);
        int final = afterFlux + flats;

        return new EnergyResult(
            baseCost, final,
            FluxLowered: fluxPct > 0 && afterFlux < preFlux,
            RitualChanged: final != baseCost && (ew || qz || pe || qs || rw));
    }

    /// <summary>Recharge modifiée. QZ ×0,5 (plus rapide), EW ×1,25 (plus lente), half-even.
    /// EW+QZ = optimiste : QZ ignore l'augmentation d'EW → ×0,5 seul.</summary>
    public static float Recharge(float baseRecharge, IReadOnlySet<Ritual> active)
    {
        if (baseRecharge <= 0f) return baseRecharge;
        bool ew = active.Contains(Ritual.EnergizingWind);
        bool qz = active.Contains(Ritual.QuickeningZephyr);
        if (qz) return RoundEven(baseRecharge * 0.5);   // QZ (l'emporte sur EW en optimiste)
        if (ew) return RoundEven(baseRecharge * 1.25);
        return baseRecharge;
    }

    /// <summary>Temps d'incantation : Nature's Renewal double celui des enchantements et hex.
    /// (Le flux Jack of All Trades ×0,75 est appliqué séparément par l'infobulle.)</summary>
    public static float CastTime(float baseCast, Skill skill, IReadOnlySet<Ritual> active)
    {
        if (baseCast <= 0f) return baseCast;
        if (active.Contains(Ritual.NaturesRenewal) && (IsEnchantment(skill) || IsHex(skill)))
            return baseCast * 2f;
        return baseCast;
    }

    /// <summary>Énergie d'entretien (upkeep) : Nature's Renewal la double pour les enchantements.</summary>
    public static int Upkeep(int baseUpkeep, Skill skill, IReadOnlySet<Ritual> active)
    {
        if (baseUpkeep > 0 && active.Contains(Ritual.NaturesRenewal) && IsEnchantment(skill))
            return baseUpkeep * 2;
        return baseUpkeep;
    }

    /// <summary>Overcast : Equinox ajoute 10 aux sorts qui causent déjà de l'overcast.</summary>
    public static int Overcast(int baseOvercast, IReadOnlySet<Ritual> active)
        => baseOvercast > 0 && active.Contains(Ritual.Equinox) ? baseOvercast + 10 : baseOvercast;

    // ── Roaring Winds : bonus « +X more Energy » dépendant du rang ────────────
    // SEUL rituel dont le modificateur scale avec un attribut (Wilderness Survival).

    /// <summary>Attribut de Roaring Winds (pour lire le rang du/des lanceur(s) équipé(s)).</summary>
    public const string RoaringWindsAttribute = "Wilderness Survival";

    /// <summary>Rang max réglable pour la simulation (borne d'attribut).</summary>
    public const int MaxRitualRank = 20;

    // Surcoût « +X more Energy » par rang de Wilderness Survival (0..20), relevé de la base
    // (progression[1]) et vérifié au harnais contre la résolution wiki. Ancres 0/12/15 = 1/4/5.
    private static readonly int[] RoaringWindsByRank =
        { 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6, 6, 6, 6 };

    /// <summary>Surcoût Roaring Winds au rang donné (clampé 0..20).</summary>
    public static int RoaringWindsBonusAtRank(int rank) =>
        RoaringWindsByRank[Math.Clamp(rank, 0, RoaringWindsByRank.Length - 1)];

    // Variante par résolution wiki (garde le harnais honnête : croise la table codée en dur
    // ci-dessus contre la progression réelle scrapée). Non utilisée en production.
    private static readonly Regex RoaringWindsRegex = new(
        $@"[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+more Energy",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Surcoût Roaring Winds résolu depuis la description scrapée (référence de test).</summary>
    public static int RoaringWindsBonus(Skill roaringWinds, int casterRank)
    {
        var resolved = SkillProgression.Resolve(roaringWinds.Description, roaringWinds.Progression, casterRank);
        var m = RoaringWindsRegex.Match(resolved);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ── Tranquility : durée d'enchantement (Lot D) ────────────────────────────
    // SEUL effet de Tranquility (ses fonctions énergie/recharge/cast ci-dessus restent no-op) :
    // « Enchantments expire 20…44…50 % faster ». Le % scale avec le rang de Wilderness Survival du
    // lanceur (comme Roaring Winds). Table = progression[1] de la skill 1213, relevée de la vraie DB
    // et cross-checkée au harnais : 20 % (rang 0) → 44 % (12) → 50 % (15) → 60 % (20).
    // La composition avec les prolongateurs (+%) vit dans EnchantmentDuration.
    private static readonly int[] TranquilityByRank =
        { 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54, 56, 58, 60 };

    /// <summary>Pourcentage « expire plus vite » de Tranquility au rang de Survie (clampé 0..20).</summary>
    public static int TranquilityPercentAtRank(int rank) =>
        TranquilityByRank[Math.Clamp(rank, 0, TranquilityByRank.Length - 1)];

    /// <summary>% Tranquility résolu depuis la progression scrapée (progression[1]) — référence de
    /// test : croise la table codée en dur ci-dessus contre la vraie DB au harnais.</summary>
    public static int TranquilityPercentResolved(Skill tranquility, int casterRank) =>
        SkillProgression.IntAt(tranquility.Progression is { Length: > 1 } p ? p[1] : null, casterRank) ?? 0;
}
