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
///
/// SPLIT PvE/PvP : 2 des 8 rituels ont une variante « (PvP) » aux chiffres DIFFÉRENTS
/// (Tranquility 1213/3460, Nature's Renewal 476/3445). Les deux versions ne coexistent jamais en
/// jeu → le rituel reste UNE entrée du bandeau, et c'est <see cref="PvpVariants"/> (le mode de jeu
/// du catalogue) qui décide de l'icône, du nom et des chiffres. Les 6 autres n'ont pas de jumelle.
/// </summary>
public static class NatureRitualData
{
    /// <summary>
    /// Mode de jeu courant du catalogue (PvP = vrai). Ambiant comme <see cref="AppLanguage.IsFr"/> :
    /// posé par MainViewModel à chaque bascule du filtre PvE/PvP, lu par tout ce qui dépend de la
    /// variante affichée (id d'icône, tables de rang de Tranquility et Nature's Renewal).
    /// N'affecte JAMAIS la persistance : <see cref="SkillIdOf"/> renvoie toujours l'id de base.
    /// </summary>
    public static bool PvpVariants { get; set; }

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
    /// bilingue (<see cref="DisplayTooltip"/> choisit selon <see cref="AppLanguage.IsFr"/>).
    /// <paramref name="PvpSkillId"/> = 0 quand le rituel n'a pas de variante « (PvP) » ; sinon les
    /// libellés PvP prennent le relais en mode PvP (chiffres différents).</summary>
    public sealed record Descriptor(
        Ritual Ritual, int SkillId, string Name, string TooltipFr, string TooltipEn,
        int PvpSkillId = 0, string? PvpTooltipFr = null, string? PvpTooltipEn = null)
    {
        /// <summary>Le rituel existe-t-il en deux versions aux chiffres différents ?</summary>
        public bool HasPvpVariant => PvpSkillId != 0;

        /// <summary>SkillId de la variante à AFFICHER (icône, infobulle, nom) dans le mode courant.
        /// Jamais utilisé pour persister : la sauvegarde passe par <see cref="SkillIdOf"/>.</summary>
        public int DisplaySkillId => PvpVariants && HasPvpVariant ? PvpSkillId : SkillId;

        /// <summary>Résumé du rituel dans la langue ET le mode de jeu affichés.</summary>
        public string DisplayTooltip => PvpVariants && HasPvpVariant
            ? (AppLanguage.IsFr ? PvpTooltipFr! : PvpTooltipEn!)
            : (AppLanguage.IsFr ? TooltipFr : TooltipEn);
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
            "Enchantments/hexes: ×2 cast time; enchantment upkeep ×2 Energy.",
            PvpSkillId: 3445,
            PvpTooltipFr: "Enchantements/maléfices : incantation +50…75 % (rang du lanceur) ; entretien des enchantements ×2 énergie.",
            PvpTooltipEn: "Enchantments/hexes: +50…75% cast time (caster's rank); enchantment upkeep ×2 Energy."),
        new(Ritual.Equinox,          1212, "Equinox",           "Les sorts à overcast infligent +10 overcast.",
            "Overcast spells inflict +10 overcast."),
        new(Ritual.Tranquility,      1213, "Tranquility",       "Les enchantements expirent 20…50 % plus vite.",
            "Enchantments expire 20…50% faster.",
            PvpSkillId: 3460,
            PvpTooltipFr: "Les enchantements expirent 10…30 % plus vite.",
            PvpTooltipEn: "Enchantments expire 10…30% faster."),
    ];

    /// <summary>Rituel portant ce SkillId, variante « (PvP) » COMPRISE : les deux ids mènent à la
    /// même entrée (une compétence équipée bascule d'id avec le mode, cf. ApplyGameModeTo).</summary>
    public static Descriptor? BySkillId(int skillId) =>
        All.FirstOrDefault(d => d.SkillId == skillId || d.PvpSkillId == skillId);

    /// <summary>Id de BASE (PvE) du rituel — celui qu'on persiste, stable quel que soit le mode.</summary>
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

    /// <summary>Temps d'incantation : Nature's Renewal allonge celui des enchantements et hex.
    /// <paramref name="naturesRenewalPct"/> = surcoût « plus long » en % — 100 en PvE (×2, fixe),
    /// 50…83 en PvP où l'effet dépend du rang de Survie du lanceur.
    /// (Le flux Jack of All Trades ×0,75 est appliqué séparément par l'infobulle.)</summary>
    public static float CastTime(float baseCast, Skill skill, IReadOnlySet<Ritual> active, int naturesRenewalPct = 100)
    {
        if (baseCast <= 0f) return baseCast;
        if (active.Contains(Ritual.NaturesRenewal) && (IsEnchantment(skill) || IsHex(skill)))
            return baseCast * (1f + Math.Max(naturesRenewalPct, 0) / 100f);
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
    // Rituels dont le modificateur scale avec un attribut : Roaring Winds, Tranquility, et —
    // en PvP seulement — Nature's Renewal. Tous les trois lisent Survie en pleine nature.

    /// <summary>Attribut des rituels à rang (pour lire le rang du/des lanceur(s) équipé(s)).</summary>
    public const string RitualAttribute = "Wilderness Survival";

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

    // Variante PvP (skill 3460) : ArenaNet a divisé l'effet par deux — « 10…26…30 % faster ».
    // Table = progression[1] de la skill 3460 (DB réelle) : 10 % (rang 0) → 26 % (12) → 30 % (15).
    private static readonly int[] TranquilityPvpByRank =
        { 10, 11, 13, 14, 15, 17, 18, 19, 21, 22, 23, 25, 26, 27, 29, 30, 31, 33, 34, 35, 37 };

    /// <summary>Pourcentage « expire plus vite » de Tranquility au rang de Survie (clampé 0..20),
    /// dans la version correspondant au mode de jeu courant (<see cref="PvpVariants"/>).</summary>
    public static int TranquilityPercentAtRank(int rank)
    {
        var table = PvpVariants ? TranquilityPvpByRank : TranquilityByRank;
        return table[Math.Clamp(rank, 0, table.Length - 1)];
    }

    /// <summary>% Tranquility résolu depuis la progression scrapée (progression[1]) — référence de
    /// test : croise la table codée en dur ci-dessus contre la vraie DB au harnais. Marche pour les
    /// deux versions (on lui passe la skill 1213 ou 3460).</summary>
    public static int TranquilityPercentResolved(Skill tranquility, int casterRank) =>
        SkillProgression.IntAt(tranquility.Progression is { Length: > 1 } p ? p[1] : null, casterRank) ?? 0;

    // ── Nature's Renewal : surcoût d'incantation, SPLITTÉ PvE/PvP ─────────────
    // PvE (skill 476) : « twice as long to cast » — fixe, aucun rang.
    // PvP (skill 3445) : « 50…70…75 % longer to cast » — dépend du rang de Survie du lanceur, comme
    // Tranquility. Table = progression[2] de la skill 3445 (DB réelle) : 50 % (rang 0) → 70 % (12)
    // → 75 % (15). Le doublement de l'ENTRETIEN des enchantements, lui, est identique dans les deux
    // versions (cf. Upkeep) — seule l'incantation a été nerfée.
    private static readonly int[] NaturesRenewalPvpByRank =
        { 50, 52, 53, 55, 57, 58, 60, 62, 63, 65, 67, 68, 70, 72, 73, 75, 77, 78, 80, 82, 83 };

    /// <summary>Surcoût d'incantation « plus long » (%) de Nature's Renewal : 100 en PvE (×2, sans
    /// rang), table PvP au rang de Survie (clampé 0..20) sinon.</summary>
    public static int NaturesRenewalPercentAtRank(int rank) =>
        PvpVariants ? NaturesRenewalPvpByRank[Math.Clamp(rank, 0, NaturesRenewalPvpByRank.Length - 1)] : 100;

    /// <summary>% Nature's Renewal (PvP) résolu depuis la progression scrapée (progression[2]) —
    /// référence de test, même rôle que <see cref="TranquilityPercentResolved"/>.</summary>
    public static int NaturesRenewalPercentResolved(Skill naturesRenewalPvp, int casterRank) =>
        SkillProgression.IntAt(naturesRenewalPvp.Progression is { Length: > 2 } p ? p[2] : null, casterRank) ?? 0;

    /// <summary>Le rituel a-t-il un rang réglable DANS LE MODE COURANT ? Nature's Renewal n'en a un
    /// qu'en PvP (en PvE son ×2 est fixe) ; Roaring Winds et Tranquility en ont un dans les deux.</summary>
    public static bool HasRank(Ritual ritual) => ritual switch
    {
        Ritual.RoaringWinds or Ritual.Tranquility => true,
        Ritual.NaturesRenewal                     => PvpVariants,
        _                                         => false,
    };
}
