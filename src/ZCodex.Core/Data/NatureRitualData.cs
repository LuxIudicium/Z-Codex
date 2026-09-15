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
///
/// BANDEAU D'ÉQUIPE (chantier infobulle, lot 1b) : le bandeau accueille aussi les effets d'équipe
/// sur l'adrénaline — Infuriating Heat (rituel de la nature), Dark Fury, Mark of Fury et Soothing
/// (lancé par l'ennemi) —, rangés par famille (<see cref="BandGroup"/>). Leur calcul vit dans
/// <see cref="AdrenalineTeamEffects"/> ; les fonctions énergie/recharge/cast les ignorent.
/// </summary>
public static class NatureRitualData
{
    /// <summary>
    /// Mode de jeu courant du catalogue (PvP = vrai). Ambiant comme <see cref="AppLanguage.IsFr"/> :
    /// posé par MainViewModel à chaque bascule du filtre PvE/PvP, lu par tout ce qui dépend de la
    /// variante affichée (id d'icône, tables de rang de Tranquility, Nature's Renewal et Infuriating Heat).
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
        // Effets d'adrénaline du bandeau d'équipe (lot 1b). Infuriating Heat est un rituel de la
        // nature ; les trois autres n'en sont pas, mais partagent le bandeau et sa persistance.
        InfuriatingHeat,
        DarkFury,
        MarkOfFury,
        Soothing,
    }

    /// <summary>Famille d'un effet du bandeau : un petit séparateur s'intercale entre deux familles
    /// (bandeau et menu Sélection). <see cref="Enemy"/> = effet lancé par l'ennemi et subi par
    /// l'équipe (Soothing) : cadre rouge, et jamais « équipé » par un perso de l'équipe.</summary>
    public enum BandGroup { NatureRitual, Enchantment, Hex, Enemy }

    /// <summary>Métadonnées d'un rituel : identité, mappage vers la compétence de la base, libellé
    /// bilingue (<see cref="DisplayTooltip"/> choisit selon <see cref="AppLanguage.IsFr"/>).
    /// <paramref name="PvpSkillId"/> = 0 quand le rituel n'a pas de variante « (PvP) » ; sinon les
    /// libellés PvP prennent le relais en mode PvP (chiffres différents).</summary>
    public sealed record Descriptor(
        Ritual Ritual, int SkillId, string Name, string TooltipFr, string TooltipEn,
        int PvpSkillId = 0, string? PvpTooltipFr = null, string? PvpTooltipEn = null,
        BandGroup Group = BandGroup.NatureRitual)
    {
        /// <summary>Le rituel existe-t-il en deux versions aux chiffres différents ?</summary>
        public bool HasPvpVariant => PvpSkillId != 0;

        /// <summary>Effet lancé par l'ennemi (Soothing) : le porter dans l'équipe le vise, lui.</summary>
        public bool IsEnemyEffect => Group == BandGroup.Enemy;

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
        // Effets d'adrénaline (lot 1b), SkillId relevés dans la base réelle le 14/09/2026.
        new(Ritual.InfuriatingHeat,  1730, "Infuriating Heat",  "Adrénaline gagnée ×2.",
            "Adrenaline gain ×2.",
            PvpSkillId: 3466,
            PvpTooltipFr: "Adrénaline gagnée +33…66 % (rang d'Expertise du lanceur).",
            PvpTooltipEn: "Adrenaline gain +33…66% (caster's Expertise rank)."),
        new(Ritual.DarkFury,         147,  "Dark Fury",         "Membres du groupe : +1 coup d'adrénaline par attaque réussie (enchantement).",
            "Party members: +1 strike of adrenaline per hit (enchantment).",
            Group: BandGroup.Enchantment),
        new(Ritual.MarkOfFury,       1360, "Mark of Fury",      "Alliés qui frappent la cible : +0…2 coups d'adrénaline (rang de Magie du sang du lanceur).",
            "Allies hitting the target: +0…2 strikes of adrenaline (caster's Blood Magic rank).",
            Group: BandGroup.Hex),
        new(Ritual.Soothing,         1266, "Soothing",          "Effet ennemi : l'équipe gagne l'adrénaline deux fois moins vite.",
            "Enemy effect: the team builds adrenaline half as fast.",
            PvpSkillId: 3009,
            PvpTooltipFr: "Effet ennemi : l'équipe gagne l'adrénaline deux fois moins vite.",
            PvpTooltipEn: "Enemy effect: the team builds adrenaline half as fast.",
            Group: BandGroup.Enemy),
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

    /// <summary>Coût en énergie affiché, incluant Expertise + flux + rituels actifs + compétences du perso.
    /// <paramref name="SkillChanged"/> : les réductions des compétences du perso (lot 2) changent le résultat.</summary>
    public readonly record struct EnergyResult(int Base, int Final, bool FluxLowered, bool RitualChanged, bool SkillChanged = false);

    /// <param name="baseCost">Coût de base (Skill.EnergyCost).</param>
    /// <param name="expertiseRank">Rang d'Expertise effectif si la skill est concernée, sinon 0.</param>
    /// <param name="fluxPct">Réduction énergie de flux (0/20/25).</param>
    /// <param name="roaringWindsBonus">Bonus Roaring Winds résolu au rang du lanceur (0 si N/A).</param>
    /// <param name="reduction">Réductions des compétences actives du perso qui touchent cette skill (lot 2).</param>
    public static EnergyResult EnergyCost(
        int baseCost, Skill skill, IReadOnlySet<Ritual> active,
        int expertiseRank, int fluxPct, int roaringWindsBonus, EnergyReduction reduction = default)
    {
        bool ew = active.Contains(Ritual.EnergizingWind);
        bool qz = active.Contains(Ritual.QuickeningZephyr);
        bool pe = active.Contains(Ritual.PrimalEchoes) && IsSignet(skill);
        bool qs = active.Contains(Ritual.Quicksand);
        bool rw = active.Contains(Ritual.RoaringWinds) && IsChantOrShout(skill);

        // Bloc multiplicatif : QZ × Expertise, combinés puis arrondis une seule fois (half-even).
        double qzF  = qz ? 1.30 : 1.0;
        double expF = expertiseRank > 0 ? (100 - 4 * expertiseRank) / 100.0 : 1.0;
        int Block(int v) => RoundEven(v * qzF * expF);

        // Flats tout à la fin. Quicksand (modèle Philippe) : −1 pour l'utilisation d'une compétence,
        // −1 de plus pour l'attaque elle-même → toute compétence d'ATTAQUE = +2 (qu'elle coûte de
        // l'énergie OU de l'adrénaline), toute autre compétence = +1.
        int flats = (qs ? (IsAttack(skill) ? 2 : 1) : 0)
                  + (rw ? roaringWindsBonus : 0);

        int final = Cascade(reduction, out int preFlux, out int afterFlux);
        int withoutSkills = reduction.LowersCost ? Cascade(default, out _, out _) : final;

        return new EnergyResult(
            baseCost, final,
            FluxLowered: fluxPct > 0 && afterFlux < preFlux,
            RitualChanged: final != baseCost && (ew || qz || pe || qs || rw),
            SkillChanged: final != withoutSkills);

        // Ordre : base → % du coût de base → Energizing Wind → points retirés → Expertise × QZ → flux → flats.
        int Cascade(EnergyReduction red, out int pre, out int after)
        {
            // Primal Echoes fixe le coût d'entrée des signets à 10 (base 0 sinon) ; Way of the Empty Palm
            // ramène à 0 les attaques qu'il touche.
            int start = red.Free ? 0 : pe ? 10 : baseCost;

            // Attuned Was Songkai + Renewing Memories : % ADDITIONNÉS, calculés sur le coût de base, ,5
            // arrondi en faveur du coût (tableau Songkai du wiki : 132/132 ; tests en jeu de Philippe du
            // 14/09/2026 : sort à 10 → 3, sort à 5 → 2 aux rangs 12).
            int v = start - (start * Math.Min(red.PercentOfBase, 100) + 49) / 100;

            // Points retirés par les compétences, AVANT l'Expertise (tableau Expert Focus du wiki : 83/84 ;
            // tests en jeu de Philippe : Expertise 15, attaque à l'arc 10 → 3, 5 → 1). Le plus haut « minimum »
            // tient, mais ne remonte jamais un coût déjà plus bas.
            int Points(int x) => red.Flat <= 0 || x <= 0 ? x : Math.Max(x - red.Flat, Math.Min(red.Minimum, x));

            // Placement d'Energizing Wind (flat, avant Expertise, et avant les points des compétences : le
            // moins cher, son plancher de 10 bloquerait sinon les réductions suivantes). EW+QZ simultanés :
            // optimiste (le moins cher des deux ordres EW↔QZ) ; sinon l'ordre confirmé « EW puis Expertise ».
            if (ew && qz)
                pre = Math.Min(Block(Points(ApplyEnergizingWind(v))),   // EW puis %
                               ApplyEnergizingWind(Block(Points(v))));   // % puis EW
            else if (ew)
                pre = Block(Points(ApplyEnergizingWind(v)));
            else
                pre = Block(Points(v));

            // Flux : étape existante inchangée (half-up via +50), appliquée après le bloc %.
            after = fluxPct > 0 ? (pre * (100 - fluxPct) + 50) / 100 : pre;
            return after + flats;
        }
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

    /// <summary>Overcast : Equinox ajoute 10 aux sorts qui causent déjà de l'overcast. Glyph of Energy
    /// (<paramref name="removed"/>, lot 2) l'annule, celui d'Equinox compris (note du wiki).</summary>
    public static int Overcast(int baseOvercast, IReadOnlySet<Ritual> active, bool removed = false)
        => baseOvercast <= 0 ? baseOvercast
         : removed ? 0
         : active.Contains(Ritual.Equinox) ? baseOvercast + 10 : baseOvercast;

    // ── Roaring Winds : bonus « +X more Energy » dépendant du rang ────────────
    // Effets dont le modificateur scale avec un attribut : Roaring Winds, Tranquility, Mark of Fury,
    // et — en PvP seulement — Nature's Renewal et Infuriating Heat.

    /// <summary>Caractéristique qui fixe le rang d'un effet à rang (rang du/des lanceur(s) équipé(s)) :
    /// Survie en pleine nature pour les rituels d'origine, Expertise pour Infuriating Heat, Magie du
    /// sang pour Mark of Fury.</summary>
    public static string AttributeOf(Ritual ritual) => ritual switch
    {
        Ritual.InfuriatingHeat => "Expertise",
        Ritual.MarkOfFury      => "Blood Magic",
        _                      => "Wilderness Survival",
    };

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

    // ── Effets d'adrénaline du bandeau d'équipe (chantier infobulle, lot 1b) ──
    // Règle de cumul et calcul des coups : AdrenalineGain (page wiki « Adrenaline »).

    // Infuriating Heat — PvE (1730) : « Doubles adrenaline gain », fixe (+100 %, dans le plafond des
    // multiplicateurs). PvP (3466) : « 33…59…66 % », progression[2] au rang d'Expertise du lanceur
    // (DB réelle) : 33 % (rang 0) → 59 % (12) → 66 % (15).
    private static readonly int[] InfuriatingHeatPvpByRank =
        { 33, 35, 37, 40, 42, 44, 46, 48, 51, 53, 55, 57, 59, 62, 64, 66, 68, 70, 73, 75, 77 };

    /// <summary>Multiplicateur (%) d'Infuriating Heat : 100 en PvE (sans rang), table PvP au rang
    /// d'Expertise (clampé 0..20) sinon.</summary>
    public static int InfuriatingHeatPercentAtRank(int rank) =>
        PvpVariants ? InfuriatingHeatPvpByRank[Math.Clamp(rank, 0, InfuriatingHeatPvpByRank.Length - 1)] : 100;

    // Mark of Fury (1360) : « Allies hitting target foe gain 0…2…2 strike[s] », progression[0] au rang
    // de Magie du sang du lanceur (DB réelle). Aucun coup sous le rang 4 : l'icône allumée n'y change rien.
    private static readonly int[] MarkOfFuryByRank =
        { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 3, 3 };

    /// <summary>Coups ajoutés par touche par Mark of Fury au rang de Magie du sang (clampé 0..20).</summary>
    public static int MarkOfFuryStrikesAtRank(int rank) =>
        MarkOfFuryByRank[Math.Clamp(rank, 0, MarkOfFuryByRank.Length - 1)];

    /// <summary>Effets d'adrénaline du bandeau actifs, communs à tous les persos de l'équipe. Dark Fury
    /// (+1 coup, sans rang) enchante : Natural Temper devient sans effet. Soothing, effet ennemi, divise
    /// le gain total. Rangs = lanceur le plus fort ou rang de simulation, résolus par l'appelant.</summary>
    public static AdrenalineGain.TeamEffects AdrenalineTeamEffects(
        IReadOnlySet<Ritual> active, int infuriatingHeatRank, int markOfFuryRank)
    {
        var effects = new List<AdrenalineGain.Effect>();
        if (active.Contains(Ritual.InfuriatingHeat))
            effects.Add(new(MultiplierPct: InfuriatingHeatPercentAtRank(infuriatingHeatRank)));
        if (active.Contains(Ritual.DarkFury))
            effects.Add(new(FixedStrikes: 1, Enchants: true));
        if (active.Contains(Ritual.MarkOfFury))
            effects.Add(new(FixedStrikes: MarkOfFuryStrikesAtRank(markOfFuryRank)));
        return new(effects, Slowed: active.Contains(Ritual.Soothing));
    }

    /// <summary>L'effet a-t-il un rang réglable DANS LE MODE COURANT ? Nature's Renewal et Infuriating
    /// Heat n'en ont un qu'en PvP (en PvE leur effet est fixe) ; Roaring Winds, Tranquility et Mark of
    /// Fury en ont un dans les deux.</summary>
    public static bool HasRank(Ritual ritual) => ritual switch
    {
        Ritual.RoaringWinds or Ritual.Tranquility or Ritual.MarkOfFury => true,
        Ritual.NaturesRenewal or Ritual.InfuriatingHeat                => PvpVariants,
        _                                                              => false,
    };
}
