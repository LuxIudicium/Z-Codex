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
/// Lot 2b : Energizing Chorus (famille « Chants ») retire de l'énergie aux cris et chants de l'équipe,
/// au rang de Motivation du lanceur — son effet passe par <see cref="EnergyCost"/>.
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
        // Réduction d'énergie des cris et chants (lot 2b).
        EnergizingChorus,
        // Sorts de protection qui accélèrent incantation et recharge (lot 3b) : affichés EN DERNIER dans le
        // bandeau, après Soothing (décision Philippe du 16/09/2026).
        TimeWard,
        EbonBattleStandard,
    }

    /// <summary>Famille d'un effet du bandeau : un petit séparateur s'intercale entre deux familles
    /// (bandeau et menu Sélection). <see cref="Enemy"/> = effet lancé par l'ennemi et subi par
    /// l'équipe (Soothing) : cadre rouge, et jamais « équipé » par un perso de l'équipe.</summary>
    public enum BandGroup { NatureRitual, Enchantment, Hex, Chant, Enemy, Ward }

    /// <summary>Métadonnées d'un rituel : identité, mappage vers la compétence de la base, libellé
    /// bilingue (<see cref="DisplayTooltip"/> choisit selon <see cref="AppLanguage.IsFr"/>).
    /// <paramref name="PvpSkillId"/> = 0 quand le rituel n'a pas de variante « (PvP) » ; sinon les
    /// libellés PvP prennent le relais en mode PvP (chiffres différents). <paramref name="EquippedOnly"/> :
    /// l'effet n'est proposé (bandeau, menu Sélection) que si un perso le porte, même en mode « tous », et son
    /// rang éventuel est toujours celui du porteur le plus fort, sans rang de simulation. Tout ce qui n'est pas un
    /// esprit : Dark Fury, Mark of Fury, Energizing Chorus (Philippe 15/09/2026) — les esprits (rituels de la nature,
    /// Soothing) restent proposés à tout moment en mode « tous ».</summary>
    public sealed record Descriptor(
        Ritual Ritual, int SkillId, string Name, string TooltipFr, string TooltipEn,
        int PvpSkillId = 0, string? PvpTooltipFr = null, string? PvpTooltipEn = null,
        BandGroup Group = BandGroup.NatureRitual, bool EquippedOnly = false)
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
            Group: BandGroup.Enchantment, EquippedOnly: true),
        new(Ritual.MarkOfFury,       1360, "Mark of Fury",      "Alliés qui frappent la cible : +0…2 coups d'adrénaline (rang de Magie du sang du lanceur).",
            "Allies hitting the target: +0…2 strikes of adrenaline (caster's Blood Magic rank).",
            Group: BandGroup.Hex, EquippedOnly: true),
        // Réduction d'énergie (lot 2b), SkillId relevé dans la base réelle le 15/09/2026 ; pas de variante « (PvP) ».
        new(Ritual.EnergizingChorus, 1569, "Energizing Chorus", "Alliés à portée de voix : prochain cri ou chant −3…7 énergie (rang de Motivation du lanceur).",
            "Allies within earshot: next shout or chant −3…7 Energy (caster's Motivation rank).",
            Group: BandGroup.Chant, EquippedOnly: true),
        new(Ritual.Soothing,         1266, "Soothing",          "Effet ennemi : l'équipe gagne l'adrénaline deux fois moins vite.",
            "Enemy effect: the team builds adrenaline half as fast.",
            PvpSkillId: 3009,
            PvpTooltipFr: "Effet ennemi : l'équipe gagne l'adrénaline deux fois moins vite.",
            PvpTooltipEn: "Enemy effect: the team builds adrenaline half as fast.",
            Group: BandGroup.Enemy),
        // Sorts de protection (lot 3b), ids relevés dans la base réelle le 16/09/2026 : pas de variante « (PvP) »,
        // PvE only, et « équipés seulement » comme tout effet du bandeau qui n'est pas un esprit. Time Ward porte un
        // badge au rang d'Incantation rapide de son porteur ; l'Étendard n'en a pas (son rang ne change que la durée
        // et la chance).
        new(Ritual.TimeWard,           3422, "Time Ward",
            "Alliés dans la zone : incantation des sorts et recharge de toutes les compétences −15…20 % (rang d'Incantation rapide du lanceur).",
            "Allies in the ward: spells cast and all skills recharge 15…20% faster (caster's Fast Casting rank).",
            Group: BandGroup.Ward, EquippedOnly: true),
        new(Ritual.EbonBattleStandard, 2232, "Ebon Battle Standard of Wisdom",
            "Alliés dans la zone : recharge des sorts −50 % (44…60 % de chance selon le rang).",
            "Allies in the ward: spells recharge 50% faster (44…60% chance by rank).",
            Group: BandGroup.Ward, EquippedOnly: true),
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
    /// <param name="energizingChorusReduction">Points retirés par Energizing Chorus, résolus au rang du lanceur (0 si N/A, lot 2b).</param>
    public static EnergyResult EnergyCost(
        int baseCost, Skill skill, IReadOnlySet<Ritual> active,
        int expertiseRank, int fluxPct, int roaringWindsBonus, EnergyReduction reduction = default,
        int energizingChorusReduction = 0)
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

        // Energizing Chorus (lot 2b) : points retirés aux cris et chants avec ceux des compétences du perso, donc
        // avant l'Expertise et sans minimum propre ; les surcoûts Roaring Winds / Quicksand s'ajoutent après
        // (décisions Philippe du 15/09/2026 : cri à 5 + Chorus 12 + Roaring Winds 12 = 4 ; Call of Haste 10,
        // Expertise 12 + Chorus 12 = 2). Effet du bandeau : il colore comme un rituel, jamais en violet.
        bool ec = active.Contains(Ritual.EnergizingChorus) && IsChantOrShout(skill) && energizingChorusReduction > 0;
        var team = ec ? new EnergyReduction(Flat: energizingChorusReduction) : default;
        var all = ec ? reduction with { Flat = reduction.Flat + energizingChorusReduction } : reduction;

        int final = Cascade(all, out int preFlux, out int afterFlux);
        int withoutSkills = reduction.LowersCost ? Cascade(team, out _, out _) : final;

        return new EnergyResult(
            baseCost, final,
            FluxLowered: fluxPct > 0 && afterFlux < preFlux,
            RitualChanged: final != baseCost && (ew || qz || pe || qs || rw || ec),
            SkillChanged: final != withoutSkills);

        // Ordre : base → % du coût de base → Energizing Wind → points retirés (compétences du perso + Energizing
        // Chorus) → Expertise × QZ → flux → flats.
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

    /// <summary>Recharge ou incantation affichée, et ce qui l'a changée (couleur de l'infobulle) : un effet du bandeau,
    /// une compétence du perso (lot 3), le flux.</summary>
    public readonly record struct SpeedResult(float Final, bool RitualChanged, bool SkillChanged, bool FluxChanged);

    /// <summary>Sorts de protection du BANDEAU d'équipe qui accélèrent l'équipe (lot 3b) :
    /// <paramref name="TimeWardPercent"/> résolu au rang d'Incantation rapide du porteur le plus fort (0 = éteint ou
    /// personne ne le porte) et <paramref name="EbonBattleStandard"/> (−50 % fixes sur les sorts). Ils rejoignent le
    /// produit des compétences du perso et son plafond de 50 %, mais colorent l'infobulle en AMBRE, comme tout effet
    /// du bandeau. Un sort de protection s'applique AUSSI À LUI-MÊME : l'icône allumée dit qu'il est déjà posé sur le
    /// terrain, et le jeu vérifie le bonus de recharge à la FIN de l'incantation, celui d'incantation à son DÉBUT —
    /// donc relancer Time Ward dans un Time Ward existant profite des deux (Philippe, 16/09/2026). Seul le tout
    /// premier lancer, sans rien au sol, n'en profite pas : la simulation, elle, suppose l'effet actif.</summary>
    public readonly record struct TeamSpeed(int TimeWardPercent = 0, bool EbonBattleStandard = false);

    // Plafond commun de la recharge et de l'incantation (chantier infobulle, lot 3, décisions Philippe du 15/09/2026) :
    // les effets se multiplient, mais le résultat ne descend jamais sous 50 % de la base, sauf si un effet SEUL fait
    // mieux — il s'applique alors à sa propre valeur (Meteor Shower, 60 s : QZ + Serpent's Quickness = 30 ; Over the
    // Limit rang 12 + Serpent's Quickness = 17).
    private static decimal Capped(decimal kept, int strongestPct) =>
        Math.Max(kept, Math.Min(0.5m, (100 - Math.Clamp(strongestPct, 0, 100)) / 100m));

    /// <summary>Fast Casting : l'incantation des sorts et sceaux est multipliée par 0,955^rang (wiki *Fast Casting* :
    /// « 0.955^Rank = ½^(Rank/15) »). La caractéristique passe outre les plafonds et ne colore rien dans l'infobulle,
    /// comme l'Expertise sur l'énergie.</summary>
    private static decimal FastCastingCastFactor(int rank)
    {
        decimal f = 1m;
        for (int i = 0; i < Math.Clamp(rank, 0, 20); i++) f *= 0.955m;
        return f;
    }

    /// <summary>Fast Casting touche l'incantation des sorts et des sceaux du perso ; une compétence qui n'est PAS
    /// d'Envoûteur n'en profite que si son incantation de base atteint 2 s (texte du jeu).</summary>
    public static bool FastCastingAffectsCast(Skill skill, float baseCast) =>
        (EnergyCostBoostData.IsSpell(skill) || IsSignet(skill))
        && (skill.Profession == Profession.Mesmer || baseCast >= 2f);

    /// <summary>Fast Casting réduit AUSSI la recharge des sorts d'Envoûteur, de 3 % par rang, en PvE seulement.</summary>
    public static bool FastCastingAffectsRecharge(Skill? skill) =>
        !PvpVariants && skill is { Profession: Profession.Mesmer } s && EnergyCostBoostData.IsSpell(s);

    /// <summary>Recharge modifiée : QZ ×0,5 (l'emporte sur EW : optimiste), EW ×1,25, réductions des compétences du perso
    /// (<paramref name="speed"/>, lot 3) multipliées avec eux dans le plafond de 50 % ; Fast Casting (sorts d'Envoûteur, PvE)
    /// s'applique par-dessus, hors plafond ; arrondi au pair ; puis les secondes ajoutées (Glyph of Sacrifice, Auspicious
    /// Incantation, Rage of the Ntouka), jamais réduites (wiki *Recharge time* : Meteor Shower + Glyph of Sacrifice +
    /// Serpent's Quickness = 70). Recharge instantanée = 0. Le blocage des attaques par Deadly Paradox
    /// (<see cref="SkillSpeed.RechargeBlock"/>) n'entre PAS dans la valeur : l'infobulle l'affiche « 10+recharge ».</summary>
    public static SpeedResult Recharge(float baseRecharge, Skill? skill, IReadOnlySet<Ritual> active,
        SkillSpeed speed = default, int fastCastingRank = 0, TeamSpeed team = default)
    {
        bool qz = active.Contains(Ritual.QuickeningZephyr);
        bool ew = active.Contains(Ritual.EnergizingWind);
        decimal fc = fastCastingRank > 0 && FastCastingAffectsRecharge(skill)
            ? (100 - 3 * Math.Clamp(fastCastingRank, 0, 20)) / 100m
            : 1m;
        // Sorts de protection du bandeau (lot 3b) : Time Ward accélère la recharge de TOUTES les compétences,
        // l'Étendard celle des seuls sorts — leur PROPRE recharge comprise, puisque l'icône allumée dit qu'un premier
        // exemplaire est déjà au sol (cf. TeamSpeed).
        int tw = active.Contains(Ritual.TimeWard) ? Math.Clamp(team.TimeWardPercent, 0, 100) : 0;
        int eb = active.Contains(Ritual.EbonBattleStandard) && team.EbonBattleStandard
                 && skill is { } spell && EnergyCostBoostData.IsSpell(spell)
            ? EbonBattleStandardPercent
            : 0;
        float final = Compute(speed, qz || ew, fc, tw, eb);
        return new(final,
            RitualChanged: (qz || ew || tw > 0 || eb > 0) && final != Compute(speed, false, fc, 0, 0),
            SkillChanged: speed.ChangesRecharge
                          && (speed.RechargeBlock > 0 || final != Compute(default, qz || ew, fc, tw, eb)),
            FluxChanged: false);

        float Compute(SkillSpeed s, bool rituals, decimal fcFactor, int twPct, int ebPct)
        {
            if (s.RechargeInstant) return 0f;
            decimal kept = (1m - s.RechargeCut) * ((100 - twPct) / 100m) * ((100 - ebPct) / 100m)
                         * (!rituals ? 1m : qz ? 0.5m : ew ? 1.25m : 1m);
            int strongest = Math.Max(s.RechargeStrongest, Math.Max(twPct, ebPct));
            if (rituals && qz) strongest = Math.Max(strongest, 50);
            decimal factor = Capped(kept, strongest) * fcFactor;
            // Arrondi au pair (convention GW1 énergie/recharge), SAUF quand Fast Casting intervient : sa table du wiki
            // arrondit au plus proche en montant (base 30 au rang 15 → 17, pas 16).
            var rounding = fcFactor != 1m ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven;
            decimal value = factor == 1m || baseRecharge <= 0f
                ? (decimal)baseRecharge
                : Math.Round((decimal)baseRecharge * factor, rounding);
            return (float)(value + s.RechargeAdded);
        }
    }

    /// <summary>Temps d'incantation modifié (lot 3) : Jaundiced Gaze retire ses secondes d'abord (« applies before
    /// Nature's Renewal », wiki), puis TOUS les effets se multiplient — compétences du perso, flux Jack of All Trades
    /// (<paramref name="fluxPct"/>), Nature's Renewal (<paramref name="naturesRenewalPct"/> « plus long » : 100 en PvE,
    /// 50…83 en PvP selon le rang de Survie du lanceur) — dans le plafond de 50 % du temps normal (tests en jeu de
    /// Philippe, 15/09/2026 ; seul Fast Casting le dépasse, hors périmètre) ; enfin Glyph of Sacrifice ramène à ¼ s et une
    /// incantation instantanée à 0. Les attaques ne reçoivent aucun effet de compétence (<see cref="SkillSpeedBoostData.SpeedFor"/>).</summary>
    public static SpeedResult CastTime(float baseCast, Skill skill, IReadOnlySet<Ritual> active,
        int naturesRenewalPct = 100, int fluxPct = 0, SkillSpeed speed = default, int fastCastingRank = 0,
        TeamSpeed team = default)
    {
        if (baseCast <= 0f) return new(baseCast, false, false, false);
        bool nr = active.Contains(Ritual.NaturesRenewal) && (IsEnchantment(skill) || IsHex(skill));
        decimal fc = fastCastingRank > 0 && FastCastingAffectsCast(skill, baseCast)
            ? FastCastingCastFactor(fastCastingRank)
            : 1m;
        // Time Ward (lot 3b) : l'incantation des SORTS seulement — ni attaque, ni sceau, ni rituel d'asservissement —
        // la sienne comprise (un Time Ward déjà posé accélère le suivant). L'Étendard ne touche pas l'incantation.
        int tw = active.Contains(Ritual.TimeWard) && EnergyCostBoostData.IsSpell(skill)
            ? Math.Clamp(team.TimeWardPercent, 0, 100)
            : 0;
        float final = Compute(speed, nr, fluxPct, fc, tw);
        return new(final,
            RitualChanged: (nr || tw > 0) && final != Compute(speed, false, fluxPct, fc, 0),
            SkillChanged: speed.ChangesCast && final != Compute(default, nr, fluxPct, fc, tw),
            FluxChanged: fluxPct > 0 && final != Compute(speed, nr, 0, fc, tw));

        float Compute(SkillSpeed s, bool withNr, int flux, decimal fcFactor, int twPct)
        {
            decimal v = Math.Max(0m, (decimal)baseCast - s.CastFlat);
            decimal kept = (1m - s.CastCut)
                         * ((100 - twPct) / 100m)
                         * (flux > 0 ? (100 - flux) / 100m : 1m)
                         * (withNr ? 1m + Math.Max(naturesRenewalPct, 0) / 100m : 1m);
            if (kept != 1m) v *= Math.Min(Capped(kept, Math.Max(s.CastStrongest, Math.Max(flux, twPct))), 2.5m);
            // Vitesse d'attaque (IAS, ajout du 16/09/2026) : « attaquer X % plus vite » retire X % de la DURÉE de l'attaque
            // (wiki *Attack speed*), donc de son temps d'activation — hors du plafond d'incantation, qui ne vise pas les
            // attaques ; le plafond propre à la vitesse d'attaque (33 % de durée en moins) est appliqué par SpeedFor.
            if (s.AttackSpeedCut > 0m) v *= 1m - s.AttackSpeedCut;
            // Fast Casting ne compte dans aucun plafond (wiki *Effect stacking*).
            v *= fcFactor;
            if (s.CastQuarter) v = Math.Min(v, 0.25m);
            if (s.CastInstant) v = 0m;
            // Le jeu calcule en pleine précision et arrondit à la milliseconde (wiki *Fast Casting*).
            return (float)Math.Round(v, 3, MidpointRounding.AwayFromZero);
        }
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
    // Energizing Chorus, et — en PvP seulement — Nature's Renewal et Infuriating Heat.

    /// <summary>Caractéristique qui fixe le rang d'un effet à rang (rang du/des lanceur(s) équipé(s)) :
    /// Survie en pleine nature pour les rituels d'origine, Expertise pour Infuriating Heat, Magie du
    /// sang pour Mark of Fury, Motivation pour Energizing Chorus.</summary>
    public static string AttributeOf(Ritual ritual) => ritual switch
    {
        Ritual.InfuriatingHeat  => "Expertise",
        Ritual.MarkOfFury       => "Blood Magic",
        Ritual.EnergizingChorus => "Motivation",
        Ritual.TimeWard         => "Fast Casting",
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

    // ── Energizing Chorus : énergie retirée aux cris et chants (chantier infobulle, lot 2b) ──
    // « The next shout or chant costs 3…6…7 less Energy for allies within earshot », progression[0] de la
    // skill 1569 au rang de Motivation du lanceur (DB réelle) : 3 (rang 0) → 6 (12) → 7 (15) → 8 (20).
    // Pas de variante « (PvP) ». Placement dans la cascade : cf. EnergyCost.
    private static readonly int[] EnergizingChorusByRank =
        { 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 8, 8, 8, 8 };

    /// <summary>Points d'énergie retirés par Energizing Chorus au rang de Motivation (clampé 0..20).</summary>
    public static int EnergizingChorusReductionAtRank(int rank) =>
        EnergizingChorusByRank[Math.Clamp(rank, 0, EnergizingChorusByRank.Length - 1)];

    // ── Sorts de protection du bandeau (lot 3b) ──────────────────────────────

    public const int TimeWardSkillId = 3422;
    public const int EbonBattleStandardSkillId = 2232;

    // Time Ward : « Allies in this ward cast spells 15…19…20% faster and recharge skills 15…19…20% faster »,
    // progression[1] (incantation) et progression[2] (recharge) de la skill 3422 — IDENTIQUES — au rang
    // d'Incantation rapide du lanceur. Ancres 0/12/15 = 15/19/20.
    private static readonly int[] TimeWardByRank =
        { 15, 15, 16, 16, 16, 17, 17, 17, 18, 18, 18, 19, 19, 19, 20, 20, 20, 21, 21, 21, 22 };

    /// <summary>Réduction Time Ward (%) au rang d'Incantation rapide (clampé 0..20).</summary>
    public static int TimeWardPercentAtRank(int rank) =>
        TimeWardByRank[Math.Clamp(rank, 0, TimeWardByRank.Length - 1)];

    /// <summary>Ebon Battle Standard of Wisdom : la recharge des sorts est TOUJOURS réduite de 50 %. Son rang
    /// (titre Avant-garde d'Ebon) ne change que la durée et la chance (44…60 %) — icône allumée = la chance a joué
    /// (décision du 14/09/2026), donc aucun badge de rang (Philippe, 16/09/2026).</summary>
    public const int EbonBattleStandardPercent = 50;

    /// <summary>L'effet a-t-il un rang réglable DANS LE MODE COURANT ? Nature's Renewal et Infuriating
    /// Heat n'en ont un qu'en PvP (en PvE leur effet est fixe) ; Roaring Winds, Tranquility, Mark of
    /// Fury et Energizing Chorus en ont un dans les deux.</summary>
    public static bool HasRank(Ritual ritual) => ritual switch
    {
        Ritual.RoaringWinds or Ritual.Tranquility or Ritual.MarkOfFury or Ritual.EnergizingChorus
            or Ritual.TimeWard                                         => true,
        Ritual.NaturesRenewal or Ritual.InfuriatingHeat                => PvpVariants,
        _                                                              => false,
    };
}
