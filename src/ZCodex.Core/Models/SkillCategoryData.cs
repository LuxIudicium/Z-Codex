using System.Globalization;
using System.Text;
using ZCodex.Core.Data;

namespace ZCodex.Core.Models;

/// <summary>
/// Les deux colonnes de catégories du catalogue — « Types » (ce que dit l'infobulle du jeu) et
/// « Mechanics » (ce que la compétence fait, ou ce à quoi elle appartient). Cf. la frontière
/// tranchée en cadrage : docs/skill_meta_filters_plan.md, décision 11.
///
/// Trois responsabilités, volontairement au même endroit parce qu'elles partagent les mêmes
/// libellés canoniques :
///   1. les DÉFINITIONS des deux colonnes (libellé EN + indentation + prédicat) ;
///   2. le CALCUL des mécaniques dérivables de la base (colonne Skills.Mechanics) ;
///   3. la RECONNAISSANCE d'une catégorie dans une saisie de recherche (« flash ench »).
/// </summary>
public static class SkillCategoryData
{
    // ── Modèle ────────────────────────────────────────────────────────────────
    //
    // Même forme que GwAttributeData.PveCategoryDef : libellé EN canonique (clé de logique et de
    // restauration de sélection), indentation d'affichage, prédicat d'appartenance. Toute entrée
    // est cliquable — les regroupements « Attacks », « Spells »… ont quitté la colonne des types,
    // où ils n'étaient que décoratifs, pour devenir de vraies mécaniques.
    public record SkillCategoryDef(string Label, int Indent, Func<Skill, bool> Matches)
    {
        public bool Test(Skill s) => Matches(s);
    }

    // Entrées méta : aucune restriction (équivalent de « All attributes » du panneau 2).
    public const string AllTypesLabel = "All types";
    public const string AllMechanicsLabel = "All mechanics";

    // ── 1. Types ──────────────────────────────────────────────────────────────

    // Préfixes présents dans SkillType qui ne sont PAS des types mais des mécaniques (décision 10 :
    // l'Expertise agit sur le coût des compétences de contact, Deadly Haste sur celles à moitié de
    // portée). « Touch Hex Spell » est un maléfice qu'on lance au contact : sans ce retrait, les
    // 36 « Touch … » et les 12 « Half Range … » disparaîtraient de leur type de base.
    private static readonly string[] TypeModifiers = ["Half Range ", "Touch "];

    // Le catalogue ne contient qu'une quarantaine de valeurs brutes de SkillType, mais elles sont
    // canonicalisées des dizaines de milliers de fois par rafraîchissement (chaque entrée des deux
    // colonnes est évaluée sur chaque compétence pour savoir si elle doit être grisée) → mémo.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _canonCache = new(StringComparer.Ordinal);

    /// <summary>Type canonique d'une compétence : son SkillType débarrassé des préfixes de
    /// mécanique, en minuscules. « Touch Hex Spell » → « hex spell » ; « Hex spell » (coquille de
    /// la base, 2 compétences) → « hex spell » aussi.</summary>
    public static string CanonicalType(string? skillType) =>
        _canonCache.GetOrAdd(skillType ?? string.Empty, Canonicalize);

    private static string Canonicalize(string skillType)
    {
        var t = skillType.Trim();
        for (bool stripped = true; stripped;)
        {
            stripped = false;
            foreach (var m in TypeModifiers)
                if (t.StartsWith(m, StringComparison.OrdinalIgnoreCase))
                {
                    t = t[m.Length..];
                    stripped = true;
                }
        }
        return t.ToLowerInvariant();
    }

    // Prédicat d'un type : égalité sur le type CANONIQUE, jamais sur la chaîne brute (décision 9 —
    // la base contient « Hex spell », « Touch Signet », « Half Range Bow Attack »…).
    private static Func<Skill, bool> Type(params string[] canonical)
    {
        var set = new HashSet<string>(canonical.Select(c => c.ToLowerInvariant()), StringComparer.Ordinal);
        return s => set.Contains(CanonicalType(s.SkillType));
    }

    public static IReadOnlyList<SkillCategoryDef> TypeDefs { get; } = BuildTypeDefs();

    // Liste PLATE des types réels — les regroupements « Attacks »/« Spells »/« Rituals »/« Other
    // types » ne sont plus des intertitres ici : ce sont des MÉCANIQUES cliquables (arbitrage
    // Philippe 19/08/2026 — un tir à l'arc est mécaniquement une attaque à distance, une frappe de
    // hache une attaque de corps à corps). ⚠ L'ordre ci-dessous n'est PAS celui qu'on affiche : il
    // regroupe par famille pour la lecture du code. L'affichage passe par SortForDisplay, qui trie
    // dans la LANGUE COURANTE (20/08/2026) — un tri figé en EN se disloque en FR.
    private static IReadOnlyList<SkillCategoryDef> BuildTypeDefs() =>
    [
        new(AllTypesLabel, 0, _ => true),

        new("Axe Attack",      1, Type("axe attack")),
        new("Bow Attack",      1, Type("bow attack")),
        new("Dual Attack",     1, Type("dual attack")),
        new("Hammer Attack",   1, Type("hammer attack")),
        new("Lead Attack",     1, Type("lead attack")),
        // « Melee Attack » n'est PAS un parapluie des attaques de mêlée : c'est un type à part
        // entière, à côté d'épée/hache/marteau/faux (arbitrage Philippe). Spear Swipe, typée
        // « Spear Melee Attack », compte dans les deux — l'appartenance multiple est assumée.
        new("Melee Attack",    1, Type("melee attack", "spear melee attack")),
        new("Off-Hand Attack", 1, Type("off-hand attack")),
        new("Pet Attack",      1, Type("pet attack")),
        new("Ranged Attack",   1, Type("ranged attack")),
        new("Scythe Attack",   1, Type("scythe attack")),
        new("Spear Attack",    1, Type("spear attack", "spear melee attack")),
        new("Sword Attack",    1, Type("sword attack")),

        // « Spell » = le type Spell SEUL. Le parapluie « tous les sorts » est une MÉCANIQUE, pas un
        // type — la colonne Skill Types reste la liste de ce qu'affiche le jeu (décision 11).
        new("Spell",                   1, Type("spell")),
        new("Enchantment Spell",       1, Type("enchantment spell")),
        new("Flash Enchantment Spell", 1, Type("flash enchantment spell")),
        new("Hex Spell",               1, Type("hex spell")),
        new("Item Spell",              1, Type("item spell")),
        new("Ward Spell",              1, Type("ward spell")),
        new("Weapon Spell",            1, Type("weapon spell")),
        new("Well Spell",              1, Type("well spell")),

        new("Binding Ritual",       1, Type("binding ritual")),
        new("Nature Ritual",        1, Type("nature ritual")),
        new("Ebon Vanguard Ritual", 1, Type("ebon vanguard ritual")),

        new("Chant",       1, Type("chant")),
        new("Echo",        1, Type("echo")),
        new("Form",        1, Type("form")),
        new("Glyph",       1, Type("glyph")),
        new("Preparation", 1, Type("preparation")),
        new("Shout",       1, Type("shout")),
        new("Signet",      1, Type("signet")),
        new("Skill",       1, Type("skill")),
        new("Stance",      1, Type("stance")),
        new("Trap",        1, Type("trap")),
    ];

    // ── 2. Mécaniques : jetons stockés en base ────────────────────────────────
    //
    // Ce sont des CLÉS techniques, pas des libellés : renommer un libellé (ou le traduire) ne doit
    // pas invalider une colonne Skills.Mechanics déjà calculée. Format CSV, comme Skills.Conditions.
    // Les 3 grandes familles de types + les familles d'attaques (arbitrage Philippe 19/08/2026).
    // ⚠ Elles ne partitionnent PAS le catalogue : le reste (chants, cris, sceaux, poses de combat,
    // pièges…) n'a plus d'entrée depuis que « Autres types » a été supprimée (Philippe, 21/08/2026)
    // — la colonne « Skill Types », elle, les liste toujours un par un.
    public const string MechAttack      = "attack";
    public const string MechMelee       = "melee";
    public const string MechDagger      = "dagger";
    public const string MechRanged      = "ranged";
    public const string MechSpell       = "spell";
    public const string MechRitual      = "ritual";
    // Aire d'effet. Le parent est l'union des portées + les zones persistantes sans portée nommée.
    public const string MechAoe         = "aoe";
    public const string MechAoeAdjacent = "aoe:adjacent";
    public const string MechAoeNear     = "aoe:near";
    public const string MechAoeArea     = "aoe:area";
    public const string MechAoeEarshot  = "aoe:earshot";
    public const string MechAoeParty    = "aoe:party";
    public const string MechAoeUnlimited = "aoe:unlimited";
    public const string MechEnchRemoval     = "enchremoval";
    public const string MechEnchRemovalFoe  = "enchremoval:foe";
    public const string MechEnchRemovalSelf = "enchremoval:self";
    public const string MechCondRemoval     = "condremoval";
    public const string MechCondTransfer    = "condremoval:transfer";
    public const string MechHexRemoval      = "hexremoval";
    public const string MechHexRemovalAlly  = "hexremoval:ally";
    public const string MechHexRemovalFoe   = "hexremoval:foe";
    public const string MechInterrupt       = "interrupt";
    public const string MechInterruptSpell  = "interrupt:spell";
    public const string MechInterruptSpellChant = "interrupt:spellchant";
    public const string MechInterruptAction = "interrupt:action";
    public const string MechInterruptSkill  = "interrupt:skill";
    public const string MechInterruptAttack = "interrupt:attack";
    // Mise à terre. Listes nommées du wiki (https://wiki.guildwars.com/wiki/Knock_down).
    // La distinction inconditionnel / conditionnel porte sur la CIBLE (« si l'ennemi se déplace »,
    // « s'il attaque », « s'il est Affaibli »…), pas sur ce que le lanceur doit faire avant :
    // Entangling Asp exige une attaque d'ouverture et reste INCONDITIONNELLE.
    // Décision Philippe (19/08/2026) : on ne modélise NI la prévention (Balanced Stance, Ward of
    // Stability…), NI les compétences qui profitent d'un ennemi déjà à terre — le wiki range dans
    // cette dernière liste des compétences dont le texte ne parle jamais de chute (Cruel Spear,
    // Sloth Hunter's Shot… qui disent « ne se déplace pas »), c'est de l'interprétation.
    public const string MechKnockdown       = "kd";
    public const string MechKnockdownUncond = "kd:uncond";
    public const string MechKnockdownCond   = "kd:cond";
    public const string MechKnockdownSelf   = "kd:self";
    // Ralentissement du déplacement. Source : catégorie wiki « Skills that cause Decreased Movement
    // Speed » (https://wiki.guildwars.com/wiki/Snare_(tactic)).
    // Arbitrage Philippe (19/08/2026) : la mécanique ne couvre QUE la vitesse de déplacement. Le
    // wiki range aussi parmi les snares l'Infirmité et la mise à terre, mais elles ont déjà leur
    // filtre (condition « Crippled », mécanique « Knock-down ») — les réunir ici ferait doublon.
    // Écartés pour la raison qui a écarté « profite d'un ennemi à terre » en §9.16 : le body block,
    // la levée d'accélérations et les « incitations » (Enduring Toxin, Weaken Knees), dont le texte
    // punit le déplacement sans jamais le ralentir.
    public const string MechSnare     = "snare";
    public const string MechSnareFoe  = "snare:foe";
    public const string MechSnareSelf = "snare:self";
    public const string MechSnareAny  = "snare:anyone";
    // Autour de la mise a terre — ce ne sont PAS des competences qui renversent, mais des
    // competences qui l'empechent, en profitent, ou l'exigent. Trois tables nommees de
    // https://wiki.guildwars.com/wiki/Knock_down (demande Philippe, 21/08/2026).
    public const string MechKdRelated = "kdrel";
    public const string MechKdPrevent = "kdrel:prevent";
    public const string MechKdBenefit = "kdrel:benefit";
    public const string MechKdRequire = "kdrel:require";
    // Hausse de caracteristique : categorie wiki « Skills that cause Increased Attribute ».
    public const string MechAttributeBoost = "attrboost";
    // Accélérations du déplacement. Source : catégorie wiki « Skills that cause Increased Movement
    // Speed » (67 entrées) recoupée avec les 4 tables nommées de https://wiki.guildwars.com/wiki/
    // Speed_boost (64) — 68 noms distincts, 52 au catalogue. La clé reprend « IMS », l'abréviation
    // que la page wiki donne elle-même (« often referred to as an IMS »), et laisse « ias » libre
    // pour la vitesse d'attaque. Muddy Terrain, seule compétence du jeu qui ANNULE une
    // accélération, n'entre pas ici : empêcher n'est pas faire (même règle qu'en §9.16).
    public const string MechSpeedBoost     = "ims";
    public const string MechSpeedBoostSelf = "ims:self";
    public const string MechSpeedBoostAlly = "ims:ally";
    public const string MechSpeedBoostFoe  = "ims:foe";
    public const string MechSpeedBoostPet  = "ims:pet";
    // Vitesse d'attaque, les deux sens. Sources : les catégories wiki « Skills that cause
    // Increased / Decreased Attack Speed » recoupées avec https://wiki.guildwars.com/wiki/
    // Attack_speed — dont les tables rattrapent ce que les catégories oublient (Tryptophan Signet
    // ralentit l'attaque sans être dans la catégorie, Reckless Haste l'accélère chez l'ENNEMI).
    // Clés « ias » / « das », les sigles que la page wiki emploie elle-même.
    public const string MechAtkSpeedUp       = "ias";
    public const string MechAtkSpeedUpSelf   = "ias:self";
    public const string MechAtkSpeedUpPet    = "ias:pet";
    public const string MechAtkSpeedUpSpirit = "ias:spirit";
    public const string MechAtkSpeedUpFoe    = "ias:foe";
    public const string MechAtkSpeedDown     = "das";
    // Soin et gain de vie. ⚠ Ce sont DEUX mécaniques du jeu, pas deux mots pour la même chose
    // (https://wiki.guildwars.com/wiki/Heal#Healing_vs._health_gain) : seul le SOIN est modifié par
    // Life Attunement, Aura of Faith, Blessure profonde, Lingering Curse… et déclenche les
    // maléfices punitifs Scourge Healing et Soul Bind. Le gain de vie direct, lui, échappe à tout
    // ça — et peut soigner un esprit, ce qu'un soin ne peut jamais faire.
    // ⚠⚠ Les DESCRIPTIONS DU JEU sont fausses sur ce point : 22 compétences portent une balise
    // « contrairement à la description, cette compétence cause du soin et non du gain de vie »
    // (ou l'inverse). Toute heuristique sur le texte se tromperait 22 fois — d'où les listes.
    public const string MechHealing    = "healing";
    public const string MechHealthGain = "healthgain";
    // Vol de vie : catégorie wiki « Skills that cause Life Stealing ». C'est la TROISIÈME façon de
    // rendre de la vie, distincte des deux autres — « le vol de vie ne compte ni comme des dégâts
    // ni comme un soin, il ignore donc l'armure et contourne la plupart des protections »
    // (https://wiki.guildwars.com/wiki/Life_stealing). « Life Draining », lui, n'est pas une
    // compétence : c'est l'effet d'une arme vampirique, hors périmètre d'un filtre de compétences.
    public const string MechLifeSteal = "lifesteal";
    // Régénération / dégénérescence de vie, comptées en « pips » (1 pip = 2 points par seconde,
    // plafonnées à 10 de chaque côté). ⚠ Encore deux mécaniques à part : « la régénération ne
    // compte pas comme un soin et la dégénérescence ne compte pas comme des dégâts, elles ne
    // déclenchent donc pas les effets qui dépendent d'un gain ou d'une perte directe de vie »
    // (https://wiki.guildwars.com/wiki/Health_regeneration).
    // ⚠ La catégorie « Health Degeneration » ne couvre que la dégénérescence DIRECTE. Les quatre
    // conditions qui dégénèrent (Saignement 3 pips, Brûlure 7, Maladie 4, Poison 4) restent dans
    // le bandeau des conditions : 97 compétences du catalogue les infligent sans figurer ici
    // (arbitrage Philippe, 19/08/2026 — ne pas faire doublon avec les entrées de conditions).
    public const string MechRegen = "regen";
    public const string MechDegen = "degen";
    // Régénération / dégénérescence d'ÉNERGIE. Autre ressource, autre échelle : 1 pip d'énergie
    // rend 1 point toutes les 3 secondes (contre 2 points par seconde pour la vie), et tout
    // personnage part avec 2 pips de base (https://wiki.guildwars.com/wiki/Energy).
    // ⚠ Comme pour la vie, la catégorie ne couvre que la dégénérescence DIRECTE : les
    // enchantements maintenus coûtent eux aussi 1 pip, mais ils ont déjà leur entrée
    // « Maintained Enchantment » (Upkeep > 0) — le wiki les range d'ailleurs dans une section
    // séparée de sa page taxonomique, « Skills with upkeep resource costs ».
    public const string MechEnergyRegen = "eregen";
    public const string MechEnergyDegen = "edegen";
    // Les six autres mécaniques d'énergie, toutes reprises de la page taxonomique du wiki
    // https://wiki.guildwars.com/wiki/List_of_energy-related_skills — chacune de ses sections
    // pointe une catégorie, et les découpages sur soi / allié / ennemi sont donnés par la PAGE en
    // listes nommées, pas déduits d'un texte.
    public const string MechEnergyGain      = "egain";
    public const string MechEnergyGainSelf  = "egain:self";
    public const string MechEnergyGainAlly  = "egain:ally";
    public const string MechEnergyGainFoe   = "egain:foe";
    public const string MechEnergyLoss      = "eloss";
    public const string MechEnergyLossSelf  = "eloss:self";
    public const string MechEnergyLossFoe   = "eloss:foe";
    public const string MechEnergyLossAny   = "eloss:any";
    public const string MechEnergySteal     = "esteal";
    public const string MechEnergyCostUp    = "ecostup";
    public const string MechEnergyCostDown  = "ecostdown";
    public const string MechMaxEnergy       = "emax";
    // Compétences qui INTERAGISSENT avec les sceaux, sans en être. Liste nommée de
    // https://wiki.guildwars.com/wiki/Signet — section « Skills which interact with or affect
    // signets ». Deux d'entre elles sont elles-mêmes des sceaux (Keystone Signet, Signet of
    // Distraction) : le wiki les y met, elles agissent sur les autres sceaux.
    public const string MechSignetRelated = "signetrel";
    // Serviteurs. Source : https://wiki.guildwars.com/wiki/List_of_minion_skills, qui sépare
    // nettement les deux notions — celles qui ANIMENT un serviteur (catégorie « Skills that cause
    // Minion ») et celles qui AGISSENT sur ceux qu'on a déjà (section « Related skills », rangée
    // par le wiki en soin / dégâts / divers — seaux non repris, arbitrage Philippe : un seau à une
    // entrée et un fourre-tout n'apprennent rien). Aucun recouvrement entre les deux listes.
    // Vocabulaire : le client français dit « serviteurs » et « animer ».
    public const string MechMinionAnimation = "minionanim";
    public const string MechMinionRelated   = "minionrel";
    // Invocations. Cinq listes NOMMÉES du wiki, sur trois pages qui se renvoient l'une à l'autre :
    //   « Invoque une créature »            = catégorie « Skills that cause Spirit » + les serviteurs
    //                                         + les 4 invocations asura + l'assassin d'Ebon ;
    //   « Liées aux esprits »               = Spirit#Skills that specifically interact with spirits ;
    //   « Liées aux rituels d'asservissement » = Binding ritual#Skills that interact with… ;
    //   « Liées aux invocations »           = Summoned creature#… (déclenchées + autres) ;
    //   « Anti-invocation »                 = catégorie « Anti-summon skills ».
    // Les recouvrements sont voulus : un esprit EST une créature invoquée, et l'anti-invocation
    // vise aussi bien les serviteurs que les esprits.
    public const string MechSummonCreature   = "summon";
    public const string MechSpiritRelated    = "spiritrel";
    public const string MechBindingRitualRel = "brrel";
    public const string MechSummonRelated    = "summonrel";
    public const string MechAntiSummon       = "antisummon";
    public const string MechEnchantment = "enchantment";
    public const string MechMaintained  = "maintained";
    public const string MechSacrifice   = "sacrifice";
    public const string MechCondition   = "condition";   // inflige au moins une condition à la cible
    public const string MechTouch       = "touch";
    public const string MechHalfRange   = "halfrange";
    // Une clé par condition : « cond:Bleeding », « cond:Deep Wound »… (nom canonique GwConditionData).
    public const string ConditionPrefix = "cond:";

    // Types canoniques de chaque famille d'attaque. Les préfixes « Touch »/« Half Range » ayant
    // déjà été retirés par CanonicalType, « Half Range Bow Attack » tombe bien dans les attaques
    // à distance.
    private static readonly HashSet<string> DaggerTypes = new(StringComparer.Ordinal)
        { "lead attack", "off-hand attack", "dual attack" };

    private static readonly HashSet<string> MeleeTypes = new(StringComparer.Ordinal)
    {
        "axe attack", "sword attack", "hammer attack", "scythe attack",
        "melee attack", "spear melee attack", "pet attack",
        "lead attack", "off-hand attack", "dual attack",
    };

    private static readonly HashSet<string> RangedTypes = new(StringComparer.Ordinal)
        { "bow attack", "spear attack", "ranged attack" };

    // ── Aire d'effet : les portées telles que le jeu les écrit dans les descriptions ──
    //
    // Aucune colonne ne porte cette information : le texte de la description EST la source. Les
    // trois rayons concentriques du jeu s'y nomment mot pour mot, et « party » désigne la portée
    // du groupe. On lit la description ANGLAISE (canonique), jamais la française.
    private static readonly System.Text.RegularExpressions.Regex AoeAdjacentRe =
        new(@"\badjacent\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Compiled);
    // Le jeu nomme ce rayon de DEUX façons : « nearby foes » et « foes near your target » /
    // « foes near target's initial location » / « foes near you ». Chercher le seul mot « nearby »
    // ratait 43 compétences, dont Searing Flames et Searing Heat (constaté 20/08/2026).
    // Deux exclusions :
    //   · « Shadow Step to a nearby LOCATION » est une destination de déplacement, pas une zone
    //     d'effet — 2 compétences (Viper's Defense, Heart of Shadow) seraient sinon comptées ;
    //   · « if target foe IS near a corpse / one of its allies » est une CONDITION de proximité,
    //     pas une aire d'effet — 3 compétences (Signet of Sorrow, Crossfire, « You're All
    //     Alone! »). Le « is/are » les sépare proprement, sans liste en dur à tenir ;
    //   · « for each nearby ally » COMPTE des cibles au lieu de les toucher — même piège que
    //     pour la zone de groupe (§9.10) et pour earshot. Philippe a pointé Leader's Zeal
    //     (20/08/2026). ⚠ L'exclusion se juge PAR OCCURRENCE, pas sur toute la description :
    //     Feast of Souls compte les esprits (« for each nearby allied spirit ») PUIS les détruit
    //     (« All nearby allied spirits are destroyed ») — sa 2e occurrence le garde dans Near,
    //     et le point qui sépare les deux phrases empêche le « for each » de l'atteindre.
    // Arbitrage Philippe (20/08/2026) : les 9 compétences à nombre de cibles PLAFONNÉ (Chain
    // Lightning « two foes near your target », Incendiary Arrows, Depravity…) comptent bien —
    // la sélection s'y fait par proximité, même si le compte est borné.
    private static readonly System.Text.RegularExpressions.Regex AoeNearRe =
        new(@"(?<!\bfor each\b[^.]{0,40})(?<!\b(?:is|are)\s)\bnear(by)?\b(?!\s+(location|spot|place))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex AoeAreaRe =
        new(@"\bin the area\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Compiled);
    // 5e portée (arbitrage Philippe, 20/08/2026) : le rayon des cris et des chants. Le client
    // FRANÇAIS dit « à portée de voix » — 98 des 101 descriptions FR concernées, et Philippe l'a
    // confirmé. Plus large que « in the area », plus étroit que la zone de groupe : d'où sa place
    // entre les deux dans la liste.
    // Mêmes deux pièges que pour les autres portées, exclus de la même façon :
    //   · « for each foe/ally/party member in earshot » COMPTE des cibles au lieu de les toucher
    //     (Eremite's Zeal, Leader's Comfort, « Lead the Way! », « Make Your Time! », Signet of
    //     Return ×2) — c'est le décompte que Philippe avait déjà écarté pour Party Range (§9.10) ;
    //   · « if you ARE WITHIN earshot of a spirit », « WHILE WITHIN earshot », « if target foe IS
    //     WITHIN earshot » est une condition de proximité (Lamentation, Spirit Light Weapon,
    //     Screaming Shot). Lamentation reste dans AoE par sa portée « near », c'est bien le cas.
    private static readonly System.Text.RegularExpressions.Regex AoeEarshotRe =
        new(@"(?<!\bfor each\b[^.]{0,40})(?<!\b(?:is|are|while)\s(?:in|within)\s)\bearshot\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Compiled);
    // Zone de GROUPE : liste nommée du wiki (https://wiki.guildwars.com/wiki/Party_area), et non
    // une recherche du mot « party » dans la description. Cette recherche ramenait 81 compétences,
    // dont la plupart n'ont rien d'une aire d'effet : « resurrects target party member » ne vise
    // qu'un seul membre, « for each party member » ne fait que les compter. La zone de groupe est
    // le plus grand rayon du jeu (un peu plus large que la boussole) et c'est aussi la distance
    // au-delà de laquelle un enchantement maintenu se rompt.
    // Clé = nom de BASE : la variante « (PvP) » suit automatiquement. Les entrées absentes du
    // catalogue (compétences célestes, compétences de monstre) sont sans effet.
    // Retrait d'enchantements : listes nommées du wiki
    // (https://wiki.guildwars.com/wiki/Enchantment_removal). Volontairement PAS dérivées du texte :
    // un balayage large des descriptions ramène 74 candidats, dont 28 compétences de Derviche qui
    // consomment UN DE LEURS PROPRES enchantements comme coût (Pious…, Twin Moon Sweep, les
    // avatars) — le wiki ne les compte pas comme des retraits, et elles ne dépouillent personne.
    // « Shadow Shroud » est également hors liste : elle EMPÊCHE d'enchanter, elle ne retire rien.
    // Clé = nom de BASE, la variante « (PvP) » suit.

    // Sections « can remove enchantments on foes » + « … that prevent spell targeting »
    // + « on foe and self ».
    private static readonly HashSet<string> EnchRemovalOnFoe = new(StringComparer.OrdinalIgnoreCase)
    {
        "Air of Disenchantment", "Assault Enchantments", "Chilblains", "Corrupt Enchantment",
        "Dark Apostasy", "Discharge Enchantment", "Disenchantment", "Drain Enchantment",
        "Envenom Enchantments", "Expunge Enchantments", "Feedback", "Gaze of Contempt",
        "Hex Eater Vortex", "Inspired Enchantment", "Jaundiced Gaze", "Lift Enchantment",
        "Lyssa's Balance", "Mirror of Disenchantment", "Order of Apostasy", "Pain of Disenchantment",
        "Rend Enchantments", "Rending Aura", "Revealed Enchantment", "Rip Enchantment",
        "Shatter Enchantment", "Shatter Storm", "Shattering Assault", "Signet of Disenchantment",
        "Signet of Twilight", "Strip Enchantment", "Test of Faith", "Well of the Profane",
        // « on foe and self » : ces trois-là sont AUSSI dans la liste « on self » ci-dessous.
        "Rending Sweep", "Rending Touch", "Winds of Disenchantment",
    };

    // Interruptions. La page wiki « Interrupt » ne donne qu'une table partielle (32 entrées, celles
    // « with a secondary effect ») : les listes ci-dessous viennent d'un dépouillement des 93
    // descriptions qui mentionnent l'interruption, classées à la main sur le texte COMPLET.
    //
    // Trois familles sont volontairement EXCLUES :
    //   · « Easily interrupted » (pièges, Healing Spring, Precision Shot…) → propriété de la
    //     compétence pendant son incantation, pas une interruption qu'elle provoque ;
    //   · la défense (Mantra of Resolve, Glyph of Concentration, Tranquil Was Tanasen,
    //     Mantra of Concentration, Pious Concentration, Trapper's Focus, Persistence of Memory) ;
    //   · les déclencheurs (Frustration : elle inflige des dégâts QUAND la cible est interrompue).
    // Clé = nom de BASE, la variante « (PvP) » suit.

    // Quatre seaux, par ce que la compétence peut atteindre (arbitrage Philippe 19/08/2026).
    // Rappel de sa définition : une ATTAQUE, c'est une arme qui frappe (hache, épée, marteau, faux,
    // dague, arc, lance — attaques de mêlée comprises, elles marchent avec n'importe quelle arme de
    // mêlée). Une compétence, un sort par exemple, n'est PAS nécessairement une attaque.
    //
    // ⚠ Piège de lecture : « Interrupts an action. Interruption effect: +dégâts si vous interrompez
    // une compétence » (Disrupting Shot) ou « … si l'action était un sort » (Savage Shot) sont des
    // interruptions d'ACTION avec un bonus conditionnel, pas des interruptions ciblées.

    // N'interrompt QUE des SORTS. Séparé des « sorts ET chants » le 21/08/2026 (demande Philippe) :
    // la distinction est celle qui compte en jeu — arrêter un chant de Parangon demande l'un des 8
    // de l'autre seau. Le partage se lit mot pour mot dans les descriptions (« Interrupts a
    // spell. » ici, « Interrupts a spell or chant. » là) ; les deux listes sont DISJOINTES.
    private static readonly HashSet<string> InterruptSpells = new(StringComparer.OrdinalIgnoreCase)
    {
        "Broad Head Arrow", "Choking Gas", "Concussion Shot", "Maelstrom",
        "Signet of Distraction", "Temple Strike",
        // « Interrupts a spell. Can interrupt any skill if target foe is hexed. » → sorts ET compétences.
        "Signet of Disruption",
    };

    // Interrompt les sorts ET les chants : les 8 « Power … » du Mesmer, à un nom près.
    private static readonly HashSet<string> InterruptSpellsChants = new(StringComparer.OrdinalIgnoreCase)
    {
        "Power Block", "Power Drain", "Power Flux", "Power Leak", "Power Leech", "Power Lock",
        "Power Return", "Power Spike",
    };

    // Interrompt l'ACTIVATION D'UNE COMPÉTENCE. Une compétence d'attaque EN EST UNE (Philippe,
    // 19/08/2026) : ce seau les couvre donc, contrairement à l'auto-attaque, qui n'est pas une
    // compétence. D'où le recouvrement avec InterruptAttacks sur les compétences d'attaque, et
    // « Actions = Skills ∪ Attacks ».
    private static readonly HashSet<string> InterruptSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Complicate", "Cry of Frustration", "Cry of Pain", "Disrupting Dagger", "Disrupting Lunge",
        "Psychic Distraction", "Rust", "Shivers of Dread", "Signet of Disruption", "Spinal Shivers",
        "Tease", "Web of Disruption",
        // Cas particulier signalé par Philippe : elle ne peut interrompre que les compétences qui
        // ne sont PAS des attaques — plus étroit que le reste du seau, mais c'est là qu'elle va.
        "Warmonger's Weapon",
    };

    // N'interrompt que des attaques — auto-attaques et compétences d'attaque.
    private static readonly HashSet<string> InterruptAttacks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Clumsiness", "Lightning Javelin", "Signet of Clumsiness", "Wailing Weapon", "Wandering Eye",
        // Ajout Philippe (21/08/2026). ⚠ Le wiki ne la marque PAS comme une interruption : ni sa
        // page, ni sa discussion, ni son historique n'emploient le mot, là où sa jumelle
        // Clumsiness porte « causes1 = Interrupt » et dit « Interrupts next attack ». C'est donc
        // un trou de la source, comblé sur la connaissance du jeu de Philippe.
        "Ineptitude",
    };

    // Interrompt n'importe quelle action, compétences et attaques confondues.
    private static readonly HashSet<string> InterruptActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Agonizing Chop", "Anthem of Disruption", "Critical Chop", "Disarm", "Disrupting Accuracy",
        "Disrupting Chop", "Disrupting Shot", "Disrupting Stab", "Disrupting Throw", "Dissonance",
        "Distracting Blow", "Distracting Shot", "Distracting Strike", "Dwarven Battle Stance",
        "Exhausting Assault", "Keystone Signet", "Leech Signet", "Lightbringer's Gaze",
        "Lyssa's Assault", "Lyssa's Haste", "Magebane Shot", "Panic", "Psychic Instability",
        "Punishing Shot", "Savage Shot", "Savage Slash", "Simple Thievery", "Skull Crack",
        "Teinai's Wind", "Thunderclap",
    };

    // Met à terre quoi qu'il arrive (aucune condition sur la cible).
    // Absentes du catalogue (PNJ / missions) : Devourer Siege, Hidden Rock, Junundu Siege,
    // Junundu Tunnel, Mega Snowball, Siege Devourer Swipe, Ursan Rage, Sugar Shock.
    private static readonly HashSet<string> KnockdownUnconditional = new(StringComparer.OrdinalIgnoreCase)
    {
        "Backbreaker", "Brawling Headbutt", "Devastating Hammer", "Dragon's Stomp", "Earth Shaker",
        "Earthquake", "Entangling Asp", "Gale", "Grapple", "Grasping Was Kuurong", "Hammer Bash",
        "Lightning Surge", "Magehunter's Smash", "Meteor", "Meteor Shower", "Shock", "Shove",
        "Signet of Judgment", "Spike Trap", "\"You Move Like a Dwarf!\"",
        "Devourer Siege", "Hidden Rock", "Junundu Siege", "Junundu Tunnel", "Mega Snowball",
        "Siege Devourer Swipe", "Ursan Rage", "Sugar Shock",
    };

    // Met à terre SI la cible remplit une condition (se déplace, attaque, incante, est Affaiblie,
    // bloque…) ou si un tirage réussit (Great Dwarf Weapon). Absentes du catalogue : Choking
    // Breath, Ice Breaker. Weakness Trap ne figure sur aucune liste du wiki mais met à terre les
    // Charr : elle est bien conditionnelle.
    private static readonly HashSet<string> KnockdownConditional = new(StringComparer.OrdinalIgnoreCase)
    {
        "\"Coward!\"", "\"None Shall Pass!\"", "Balthazar's Pendulum", "Bane Signet", "Bestial Pounce",
        "Bull's Charge", "Bull's Strike", "Churning Earth", "Club of a Thousand Bears",
        "Counter Blow", "Enraged Smash", "Great Dwarf Weapon", "Griffon's Sweep", "Gust",
        "Heavy Blow", "Horns of the Ox", "Iron Palm", "Irresistible Blow", "Judgment Strike",
        "Leviathan's Sweep", "Mark of Instability", "Mind Shock", "Pounce", "Psychic Instability",
        "Reaper's Sweep", "Savage Pounce", "Scorpion Wire", "Shield Bash", "Shield of Force",
        "Shield of Judgment", "Signet of Clumsiness", "Slippery Ground", "Stoning", "Trampling Ox",
        "Tripwire", "Unsteady Ground", "Wanderlust", "Wastrel's Collapse", "Water Trident",
        "Weakness Trap", "Whirlwind", "Yeti Smash",
        "Choking Breath", "Ice Breaker",
    };

    // Met LE LANCEUR à terre. Grapple met les deux à terre : elle est aussi inconditionnelle.
    private static readonly HashSet<string> KnockdownSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "Desperation Blow", "Drunken Blow", "Grapple",
    };

    // Ralentit L'ENNEMI. Absente du catalogue : Icicles (compétence d'événement Wintersday, règle
    // §9.14). Muddy Terrain figure AUSSI dans SnareOnSelf : son esprit ralentit tout ce qui passe à
    // portée, alliés et lanceur compris — même double appartenance que Grapple pour la mise à terre.
    private static readonly HashSet<string> SnareOnFoes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Binding Chains", "Crippling Anguish", "Dark Prison", "Deep Freeze", "Earthen Shackles",
        "Ethereal Burden", "Freezing Gust", "Frozen Burst", "Grasping Earth", "Hidden Caltrops",
        "Ice Prison", "Ice Spikes", "Icy Shackles", "Imagined Burden", "Kitah's Burden",
        "Mind Freeze", "Mirror of Ice", "Seeping Wound", "Shadow Prison",
        "Shadowy Burden", "Shard Storm", "Shared Burden", "Siphon Speed", "Sum of All Fears",
        "Teinai's Prison", "Tryptophan Signet", "Ward Against Foes", "Winter's Embrace",
        "Icicles",
    };

    // Ralentit LE LANCEUR : contrepartie assumée d'une armure (Armor of Earth, Dolyak Signet) ou
    // d'une cadence d'attaque (Flail). Seau à part, comme « On Yourself » pour la mise à terre.
    private static readonly HashSet<string> SnareOnSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armor of Earth", "Dolyak Signet", "Flail",
    };

    // ── Autour de la mise a terre : 3 tables nommees de la page Knock down ──────────────────
    // Absentes du catalogue (PNJ / formes PvE, regle §9.14) : Ice Fort, Raven Flight, Junundu Bite.
    // Cle = nom de BASE, les variantes « (PvP) » suivent.
    private static readonly HashSet<string> KdPreventSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aura of Stability", "Balanced Stance", "Balthazar's Pendulum", "\"Brace Yourself!\"",
        "Dolyak Signet", "\"Don't Trip!\"", "Dwarven Stability", "Fleeting Stability",
        "\"I Am Unstoppable!\"", "Ice Fort", "Raven Flight", "Steady Stance",
        "Ward of Stability",
    };

    // Les deux sous-tables du wiki reunies : celles qui profitent SPECIFIQUEMENT d'une cible a
    // terre, et celles qui en profitent au passage (« si la cible ne bouge pas », « si elle
    // n'utilise pas de competence » — un personnage a terre ne fait ni l'un ni l'autre).
    private static readonly HashSet<string> KdBenefitSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "A Touch of Guile", "Aftershock", "Bed of Coals", "Bestial Mauling", "Cruel Spear",
        "Crushing Blow", "\"Fear Me!\"", "Fetid Ground", "Holy Strike", "Junundu Bite",
        "Lacerating Chop", "Low Blow", "Melandru's Shot", "Overbearing Smash",
        "Protector's Defense", "Rending Aura", "Renewing Smash", "Sloth Hunter's Shot",
        "\"Stand Your Ground!\"", "Steelfang Slash", "Stonesoul Strike", "Waste Not, Want Not",
    };

    // « No effect unless… » : sans cible a terre, elles ne font rien du tout.
    private static readonly HashSet<string> KdRequireSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Awe", "Belly Smash", "Brambles", "Earthbind", "Falling Lotus Strike", "Falling Spider",
        "\"I Meant to Do That!\"", "Lift Enchantment", "\"On Your Knees!\"",
        "Pulverizing Smash", "Supportive Spirit",
    };

    // ── Hausse de caracteristique ───────────────────────────────────────────────────────────
    // Categorie wiki « Skills that cause Increased Attribute », recoupee avec le catalogue : les
    // 16 noms y sont tous. Rien ne lui echappe — les seules autres competences dont la description
    // parle de caracteristiques les SUBSTITUENT (Signet of Illusions, Symbolic Celerity, Symbols
    // of Inspiration) ou les mettent a 0 (Wail of Doom, Atrophy, les 3 benedictions Norn).
    // Cle = nom de BASE : « Expert's Dexterity (PvP) » et les deux « Elemental Lord (Kurzick /
    // Luxon) » — les seules formes sous lesquelles cette derniere existe — suivent toutes seules.
    private static readonly HashSet<string> AttributeBoostSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armor of Frost", "Aura of the Lich", "Awaken the Blood", "Elemental Attunement",
        "Elemental Lord", "Expert's Dexterity", "Glyph of Elemental Power", "Glyph of Energy",
        "Heroic Refrain", "Masochism", "Master of Magic", "Ritual Lord", "Seven Weapons Stance",
        "Shadow Theft", "Trapper's Focus",
    };

    // ⚠ Audit PvE/PvP de la liste (cf. la lecon des 156 paires) : « Masochism (PvP) » NE partage
    // PAS la mecanique. La version PvE donne « +2 Death Magic and Soul Reaping » ; la PvP donne de
    // l'energie a chaque sacrifice et AUCUNE caracteristique. Le wiki le savait — sa categorie
    // nomme « Expert's Dexterity (PvP) » explicitement, mais jamais « Masochism (PvP) ».
    // Sortie par son nom COMPLET, la cle de base ne pouvant pas l'attraper.
    private static readonly HashSet<string> AttributeBoostExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Masochism (PvP)",
    };

    // Ralentit TOUT LE MONDE sans distinction de camp : l'esprit de Muddy Terrain freine ce qui
    // passe à sa portée, ennemis, alliés et lanceur compris. Elle était dans les DEUX seaux
    // précédents ; Philippe l'a fait déménager ici (21/08/2026), sur le modèle du 3e seau de la
    // perte d'énergie. Elle est donc la seule de son entrée.
    private static readonly HashSet<string> SnareOnAnyone = new(StringComparer.OrdinalIgnoreCase)
    {
        "Muddy Terrain",
    };

    // Accélère LE LANCEUR. Gust est aussi dans SpeedBoostOnAlly (« vous et la cible alliée »),
    // Run as One et Rampage as One aussi dans SpeedBoostOnPet (« vous et votre animal »).
    // Absentes du catalogue (PNJ / formes / effets d'environnement, règle §9.14) : les 8 dernières.
    private static readonly HashSet<string> SpeedBoostOnSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armor of Mist", "Battle Rage", "Bull's Charge", "Burning Speed", "Charging Strike",
        "Dark Escape", "Dash", "Dodge", "Drunken Master", "Enchanted Haste", "Enraging Charge",
        "Escape", "Featherfoot Grace", "Flame Djinn's Haste", "Fleeting Stability", "Gust",
        "Harrier's Haste", "Illusion of Haste", "Mindbender", "Natural Stride", "Onslaught",
        "Pious Haste", "Primal Rage", "Rampage as One", "Run as One", "Rush", "Shadow of Haste",
        "Siphon Speed", "Soldier's Speed", "Sprint", "Storm Chaser", "Storm Djinn's Haste",
        "Storm's Embrace", "Whirling Charge", "Zojun's Haste",
        "Ursan Force", "Volfen Pounce", "Junundu Tunnel", "HYAHHHHH!", "\"Kilroy Stonekin\"",
        "Chimera of Intensity", "Charging Spirit", "Falken Quick",
    };

    // Accélère UN ALLIÉ (cri de groupe, enchantement lancé sur un autre, écho).
    private static readonly HashSet<string> SpeedBoostOnAlly = new(StringComparer.OrdinalIgnoreCase)
    {
        "\"Charge!\"", "\"Fall Back!\"", "\"Incoming!\"", "\"It's Just a Flesh Wound.\"",
        "\"Lead the Way!\"", "\"Make Haste!\"", "\"Retreat!\"", "Godspeed", "Gust", "Hasty Refrain",
        "Windborne Speed",
        "\"There's not enough time!!\"", "\"Let's Get 'Em!\"", "\"It's Good to Be King!\"",
        "Cry of Madness", "Motivating Insults", "Shadowy Soul Explosion",
    };

    // Accélère L'ENNEMI — Shameful Fear l'accélère pour mieux le punir de se déplacer.
    private static readonly HashSet<string> SpeedBoostOnFoe = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shameful Fear",
        "Last Rites of Torment", "Last Rites of Torment (Skill)",
    };

    // Accélère L'ANIMAL DE COMPAGNIE. Arbitrage Philippe (19/08/2026) : le wiki isole Call of Haste
    // dans un fourre-tout « Miscellaneous » et laisse Run as One / Rampage as One dans « on self »,
    // alors qu'elles accélèrent aussi l'animal — le seau les prend toutes les quatre, pour que le
    // filtre réponde vraiment à « qu'est-ce qui accélère mon animal ».
    private static readonly HashSet<string> SpeedBoostOnPet = new(StringComparer.OrdinalIgnoreCase)
    {
        "Call of Haste", "Rampage as One", "Run as One",
    };

    // Accélère TES propres attaques. Weapon of Aggression y figure malgré son type : c'est le seul
    // sort d'arme donné « target = self » par le wiki. Never Rampage Alone et Rampage as One sont
    // aussi dans IasOnPet (« vous et votre animal »). Bestial Fury reste ici : malgré sa ligne de
    // Maîtrise des bêtes, son texte dit « YOU attack 25% faster ».
    // Absentes du catalogue (règle §9.14) : les 2 dernières.
    private static readonly HashSet<string> IasOnSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "\"I Will Avenge You!\"", "Aggressive Refrain", "Berserker Stance", "Bestial Fury", "Burst of Aggression",
        "Critical Agility", "Drunken Master", "Dwarven Battle Stance", "Expert's Dexterity",
        "Flail", "Flurry", "Frenzy", "Heart of Fury", "Heket's Rampage", "Lightning Reflexes",
        "Never Rampage Alone", "Onslaught", "Pious Fury", "Primal Rage", "Rampage as One",
        "Rapid Fire", "Seven Weapons Stance", "Soldier's Fury", "Soldier's Stance", "Tiger Stance",
        "Tiger's Fury", "Way of the Assassin", "Weapon of Aggression",
        "\"Tango Down!\"", "Volfen Bloodlust",
    };

    // Accélère L'ANIMAL DE COMPAGNIE.
    private static readonly HashSet<string> IasOnPet = new(StringComparer.OrdinalIgnoreCase)
    {
        "Call of Haste", "Feral Aggression", "Never Rampage Alone", "Predatory Bond",
        "Rampage as One",
    };

    // Accélère LES ESPRITS que tu contrôles. Seau à part demandé par Philippe (19/08/2026) : un
    // esprit n'est pas un allié comme un autre — il ne se déplace pas et ne reçoit pas la plupart
    // des sorts. Une seule entrée aujourd'hui, la seule du catalogue dans ce cas.
    private static readonly HashSet<string> IasOnSpirit = new(StringComparer.OrdinalIgnoreCase)
    {
        "Signet of Ghostly Might",
    };

    // Accélère L'ENNEMI — Reckless Haste le fait attaquer plus vite mais rater une fois sur deux.
    // ⚠ Il n'y a AUCUN seau « sur un allié » : au catalogue, la seule compétence qui accélérerait
    // l'attaque d'un autre joueur est Weapon of Aggression, et le wiki la donne self. Les trois
    // qui viseraient vraiment les alliés sont absentes (PNJ) — « There's not enough time!! »,
    // Motivating Insults, Shadowy Soul Explosion — donc pas de seau vide en permanence.
    private static readonly HashSet<string> IasOnFoe = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reckless Haste",
    };

    // Ralentit l'attaque de l'ennemi. Tryptophan Signet manque à la catégorie wiki mais figure
    // dans la table de la page, texte à l'appui (« move and attack 23...40% slower »).
    // ⚠ Crippling Anguish (PvP) a perdu « and attacks » : cf. MechanicExceptions.
    // Absente du catalogue : Spectral Agony (compétence de Mursaat).
    private static readonly HashSet<string> DasSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Crippling Anguish", "Faintheartedness", "Meekness", "Shadow of Fear", "Shared Burden",
        "Sum of All Fears", "Teinai's Heat", "Tryptophan Signet",
        "Spectral Agony",
    };

    // SOIN, au sens mécanique du terme : catégorie wiki « Skills that cause Healing ».
    // Reclassées en gain de vie après lecture des notes de page ET des discussions (19/08/2026) :
    //   • Mark of Protection — balise « cause du gain de vie, pas du soin », deux tests
    //     indépendants en discussion (mars 2026) : ni Scourge Healing ni Unyielding Aura ne réagit ;
    //   • Seed of Life — balise « cause en fait du gain de vie », test de 2011 avec capture ;
    //   • Vampirism — le soin passe par le VOL DE VIE de l'esprit (arbitrage Philippe).
    // ⚠ Illusion of Pain RESTE ici malgré sa note « ne déclenche pas Scourge Healing » : sa
    // discussion explique que c'est le MALÉFICE qui soigne, donc aucune cible à punir, et que la
    // réduction par Blessure profonde et Lingering Curse prouve que c'est bien du soin.
    // ⚠ Mending Grip est un OUBLI de la catégorie wiki, pas une omission volontaire : sa
    // description dit « Target ally is [[heal]]ed for 20…80 Health », avec le lien vers la page
    // Heal, et toute sa discussion la traite comme un soin. Trouvée par le balayage du catalogue.
    // Absentes du catalogue (PNJ / événement, règle §9.14) : Death's Embrace, Renewing Corruption,
    // Star Shine, Sugar Infusion.
    private static readonly HashSet<string> HealingSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Air of Superiority", "Angelic Bond", "Angelic Protection", "Aura of Restoration",
        "Avatar of Dwayna", "Blessed Light", "Blood Bond", "Blood of the Master",
        "Boon of Creation", "Boon Signet", "Breath of the Great Dwarf", "Chorus of Restoration",
        "Comfort Animal", "Companionship", "Cure Hex", "Death's Charge", "Death's Embrace",
        "Dismiss Condition", "Divine Boon", "Divine Healing", "Divine Intervention",
        "Dwayna's Kiss", "Dwayna's Sorrow", "Dwayna's Touch", "Ebon Escape", "Elemental Lord",
        "Empathic Removal", "Ether Feast", "Ether Renewal", "Ethereal Light", "Extinguish",
        "Faithful Intervention", "Feast for the Dead", "Feast of Souls", "Finale of Restoration",
        "Foul Feast", "Ghostmirror Light", "Gift of Health", "Glimmer of Light",
        "Glyph of Restoration", "Heal Area", "Heal as One", "Heal Other", "Heal Party",
        "Healing Burst", "Healing Light", "Healing Ribbon", "Healing Ring", "Healing Signet",
        "Healing Spring", "Healing Touch", "Healing Whisper", "Heart of Shadow",
        "Heaven's Delight", "Illusion of Pain", "Illusion of Weakness", "Imbue Health",
        "Infuse Health", "Jamei's Gaze", "Karei's Healing Circle", "Leader's Comfort", "Life",
        "Life Sheath", "Light of Deliverance", "Lion's Comfort", "Mantra of Signets",
        "Mend Ailment", "Mend Body and Soul", "Mend Condition", "Mending Grip", "Mending Touch",
        "Mist Form",
        "Mystic Healing", "Natural Healing", "Orison of Healing", "Parasitic Bond",
        "Patient Spirit", "Phoenix", "Predatory Bond", "Preservation", "Protective Was Kaolai",
        "Rejuvenation", "Release Enchantments", "Renew Life", "Renewing Corruption",
        "Restore Condition", "Reversal of Fortune", "Shadow Refuge", "Shield Guardian",
        "Shielding Hands", "Signet of Devotion", "Signet of Pious Light", "Signet of Rejuvenation",
        "Signet of Synergy", "Soothing Memories", "Spirit Bond", "Spirit Light", "Spirit to Flesh",
        "Spirit Transfer", "Star Shine", "Sugar Infusion", "Supportive Spirit",
        "\"There's Nothing to Fear!\"", "Verata's Gaze", "Vigorous Spirit",
        "Watchful Intervention", "Watchful Spirit", "Wielder's Boon", "Word of Healing",
        "Words of Comfort", "Zealous Benediction",
    };

    // GAIN DE VIE DIRECT : catégorie wiki « Skills that cause Health Gain ». Healing Hands y reste
    // malgré la page Heal qui la range parmi les soins — sa catégorie ET la balise de sa propre
    // page disent gain de vie (arbitrage Philippe, 19/08/2026).
    // Blood Bond est dans les DEUX listes, comme sur le wiki : elle fait les deux.
    // ⚠ Contemplation of Purity est le second oubli de catégorie : « you gain 0…80 Health »,
    // la formulation canonique du gain direct selon la page Health gain, et aucune balise ne la
    // contredit. Le vol de vie, lui, reste dehors — les 34 « Steals X Health » du catalogue sont
    // une mécanique à part entière (Taste of Death le dit dans ses notes : « comme tout vol de
    // vie… »), qui aura son propre filtre.
    // Absentes du catalogue : Adoration, Side Step, Star Servant, Stout-Hearted.
    // VOL DE VIE. ⚠ Le bénéficiaire n'est pas toujours le lanceur : les 4 sorts d'arme volent pour
    // la cible alliée, Order of the Vampire pour tout le groupe, Heal as One pour l'animal, et les
    // esprits GARDENT la vie volée — la note de Bloodsong le dit noir sur blanc (« for its own
    // benefit, not for the benefit of the player who spawned the spirit »). Philippe a choisi la
    // liste à plat (19/08/2026), mais le découpage est écrit ici si le besoin vient.
    // ⚠ Heal as One (PvP) a perdu le vol de vie : cf. MechanicExceptions.
    // Absentes du catalogue (PNJ, règle §9.14) : « It's Good to Be King! », Feast of Vengeance,
    // Star Servant, Taste of Undeath, Touch of Dhuum, Twisting Jaws.
    // Hors liste, les 6 compétences qui INTERAGISSENT avec le vol de vie sans en causer
    // (Life Sheath, Reversal of Fortune, Shielding Hands, Weapon of Remedy, Vengeful Weapon,
    // Union) — ce sont des « related skills », chantier distinct (§8 du plan).
    // RÉGÉNÉRATION de vie. Absentes du catalogue (PNJ / compétences d'événement, règle §9.14) :
    // « Tango Down! », Agnar's Rage, Celestial Stance (Fête du dragon), Cry of Madness,
    // Motivating Insults, Power of the Staff of the Mists, Purify Soul, Verata's Promise.
    // RÉGÉNÉRATION d'énergie. Absentes du catalogue (règle §9.14) : Aura of the Juggernaut et
    // Cry of Madness (compétences de monstre), Scepter of Orr's Power (objet transporté),
    // « Tango Down! » (quête spéciale).
    // GAIN d'énergie. Découpage de la page wiki : « on allies » et « on foes » sont deux listes
    // nommées, « on self » est le reste de la catégorie. ⚠ Energy Boon est dans les DEUX premiers
    // seaux, la page l'excluant explicitement de son « notitlematch » (elle donne de l'énergie au
    // lanceur ET à la cible). Absentes du catalogue : Rebel Yell, Star Servant.
    // Liste nommée du wiki, 14 noms, tous au catalogue. Filet passé sur les 1487 descriptions :
    // les 74 autres compétences qui écrivent « signet » sont toutes des sceaux elles-mêmes.
    // ANIME des serviteurs. Absentes du catalogue (règle §9.14) : Animate Candy Golem (Wintersday)
    // et Star Servant (compétence de monstre).
    private static readonly HashSet<string> MinionAnimationSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Animate Bone Fiend", "Animate Bone Horror", "Animate Bone Minions", "Animate Flesh Golem",
        "Animate Shambling Horror", "Animate Vampiric Horror", "Aura of the Lich", "Jagged Bones",
        "Malign Intervention",
        "Animate Candy Golem", "Star Servant",
    };

    // AGIT sur les serviteurs qu'on contrôle déjà. Absente du catalogue : Redemption of Purity.
    private static readonly HashSet<string> MinionRelatedSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Blood of the Master", "Dark Bond", "Feast for the Dead", "Infuse Condition",
        "Order of Undeath", "Putrid Flesh", "Taste of Death", "Verata's Aura", "Verata's Gaze",
        "Verata's Sacrifice",
        "Redemption of Purity",
    };

    // INVOQUE une créature, en dehors des rituels et des serviteurs, tous deux déduits (cf. Compute).
    // La catégorie « Skills that cause Spirit » recoupe EXACTEMENT les 71 compétences de type Rituel
    // du catalogue — vérifié dans les deux sens le 20/08/2026 : 0 rituel hors catégorie, et rien dans
    // la catégorie qui ne soit un rituel sauf Signet of Spirits. On déduit donc du type plutôt que de
    // recopier 71 noms : la liste se tient toute seule si le catalogue bouge.
    // Absentes du catalogue (règle §9.14) : Celestial Summoning, Star Strike, Star Servant (célestes).
    private static readonly HashSet<string> SummonCreatureExtraSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Signet of Spirits",
        "Summon Ice Imp", "Summon Mursaat", "Summon Naga Shaman", "Summon Ruby Djinn",
        "Ebon Vanguard Assassin Support",
        "Celestial Summoning", "Star Strike", "Star Servant",
    };

    // INTERAGIT avec les esprits déjà en place. Absentes du catalogue : Purify Soul et
    // Spirit Siphon (Master Riyo), toutes deux compétences de monstre.
    // ⚠ Clamor of Souls est un AJOUT : le wiki l'oublie alors que sa dernière phrase est mot pour mot
    // celle d'Essence Strike et de Ghostly Haste, toutes deux listées (« if you are within earshot of
    // a spirit »). Troisième trou de catégorie trouvé au filet, après Mending Grip et Contemplation
    // of Purity.
    private static readonly HashSet<string> SpiritRelatedSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armor of Unfeeling", "Clamor of Souls", "Draw Spirit", "Essence Strike", "Feast of Souls",
        "Gaze from Beyond", "Gaze of Fury", "Ghostly Haste", "Ghostmirror Light", "Lamentation",
        "Mend Body and Soul", "Offering of Spirit", "Painful Bond", "Reclaim Essence", "Rupture Soul",
        "Signet of Binding", "Signet of Ghostly Might", "Signet of Spirits", "Spirit Boon Strike",
        "Spirit Burn", "Spirit Channeling", "Spirit Light", "Spirit Light Weapon", "Spirit Siphon",
        "Spirit to Flesh", "Spirit Transfer", "Spirit Walk", "Spiritleech Aura", "Summon Spirits",
        "Purify Soul",
    };

    // INTERAGIT avec le TYPE « rituel d'asservissement » (coût, recharge, attributs), pas avec les
    // esprits qui en sortent — d'où une liste séparée. Filet à zéro : aucune autre compétence du
    // catalogue n'écrit « binding ritual ».
    private static readonly HashSet<string> BindingRitualRelatedSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armor of Unfeeling", "Attuned Was Songkai", "Doom", "Reclaim Essence", "Ritual Lord",
        "Soul Twisting", "Weapon of Quickening",
    };

    // INTERAGIT avec n'importe quelle créature invoquée — serviteurs ET esprits. Le wiki les range en
    // deux sous-listes (déclenchées par l'invocation / autres) ; six lignes en tout, laissées à plat.
    private static readonly HashSet<string> SummonRelatedSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boon of Creation", "Explosive Growth", "Spirit's Gift",
        "Signet of Creation", "Signet of Ghostly Might", "Swap",
    };

    // CONTRE les créatures invoquées. Terme non officiel du wiki, mais la catégorie est nommée et
    // tenue. Absentes du catalogue : Ethereal Soul Explosion, Purify Soul, Redemption of Purity,
    // Signet of the Unseen.
    private static readonly HashSet<string> AntiSummonSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Banish", "Banishing Strike", "Consume Soul", "Gaze of Fury", "Holy Spear",
        "Signet of Binding", "Spiritual Pain", "Verata's Aura", "Verata's Gaze",
        "Ethereal Soul Explosion", "Purify Soul", "Redemption of Purity", "Signet of the Unseen",
    };

    private static readonly HashSet<string> SignetRelatedSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ignorance", "Keystone Signet", "Lion's Comfort", "Lyric of Purification", "Lyric of Zeal",
        "Mantra of Inscriptions", "Mantra of Signets", "Primal Echoes", "Rust", "Scribe's Insight",
        "Signet of Distraction", "Symbolic Celerity", "Symbolic Posture", "Symbolic Strike",
    };

    private static readonly HashSet<string> EnergyGainSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "Air Attunement", "Air of Superiority", "Angorodon's Gaze", "Arcane Conundrum",
        "Arcane Zeal", "Aria of Zeal", "Assassin's Promise", "Aura of Restoration",
        "Auspicious Blow", "Auspicious Incantation", "Black Lotus Strike", "Blessed Signet",
        "Body Shot", "Bonetti's Defense", "Boon of Creation", "Caretaker's Charge",
        "Castigation Signet", "Channeling", "Clamor of Souls", "Consume Corpse", "Counterattack",
        "Critical Eye", "Critical Strike", "Defender's Zeal", "Drain Delusions",
        "Drain Enchantment", "Earth Attunement", "Elemental Attunement", "Elemental Lord",
        "Energetic Was Lee Sa", "Energy Boon", "Energy Drain", "Energy Tap", "Eremite's Zeal",
        "Essence Bond", "Essence Strike", "Ether Prism", "Ether Renewal", "Ether Signet",
        "Ethereal Burden", "Falling Lotus Strike", "Ferocious Strike", "Fire Attunement",
        "Flourish", "Foul Feast", "Glowing Gaze", "Glowing Ice", "Glowing Signet", "Glowstone",
        "Golden Lotus Strike", "Healing Light", "Hex Eater Signet", "Inspired Enchantment",
        "Inspired Hex", "Kitah's Burden", "Knee Cutter", "Leader's Zeal", "Leech Signet",
        "Lightbringer Signet", "Lotus Strike", "Lyric of Zeal", "Mantra of Earth",
        "Mantra of Flame", "Mantra of Frost", "Mantra of Lightning", "Mantra of Recall",
        "Marksman's Wager", "Masochism", "Master of Magic", "Meditation", "Mind Blast",
        "Offering of Blood", "Offering of Spirit", "Pious Renewal", "Power Drain", "Prepared Shot",
        "Radiant Scythe", "Reaper's Mark", "Rebel Yell", "Reclaim Essence", "Renewing Smash",
        "Renewing Surge", "Revealed Enchantment", "Revealed Hex", "Scavenger Strike",
        "Scavenger's Focus", "Scribe's Insight", "Second Wind", "Shock Arrow",
        "Signet of Corruption", "Signet of Creation", "Signet of Lost Souls", "Signet of Recall",
        "Signet of Spirits", "Smooth Criminal", "Soothing Memories", "Spirit Channeling",
        "Spirit of Failure", "Spirit Siphon", "Star Burst", "Star Servant", "Steady Stance",
        "Storm Chaser", "\"Victory Is Mine!\"", "Warrior's Endurance", "Wary Stance",
        "Waste Not, Want Not", "Water Attunement", "Way of the Lotus", "Weapon of Fury",
        "Wielder's Zeal", "Zealous Anthem", "Zealous Benediction", "Zealous Renewal",
        "Zealous Sweep", "Zealous Vow",
    };

    private static readonly HashSet<string> EnergyGainAlly = new(StringComparer.OrdinalIgnoreCase)
    {
        "Balthazar's Spirit", "Energizing Finale", "Energy Boon", "\"Never Give Up!\"",
        "Weapon of Renewal",
    };

    private static readonly HashSet<string> EnergyGainFoe = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aneurysm", "Power Return",
    };

    // PERTE d'énergie. Même découpage donné par la page. Quicksand est la seule « sur n'importe
    // qui » : son sable vide l'énergie de tout ce qui s'y arrête, alliés compris.
    // Absentes du catalogue : Chaotic Soul Explosion, Cry of Madness.
    private static readonly HashSet<string> EnergyLossSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dark Apostasy", "Decapitate", "Distortion", "Divine Boon", "Ether Lord",
        "Glyph of Essence", "Mantra of Resolve", "Marksman's Wager", "Protective Bond",
        "Purge Signet", "Rebirth", "Shivers of Dread", "Signet of Disenchantment",
        "Spinal Shivers", "Storm Djinn's Haste", "Succor", "Zealot's Fire",
    };

    private static readonly HashSet<string> EnergyLossFoe = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ancestor's Visage", "Aneurysm", "Chaos Storm", "Chaotic Soul Explosion", "Cry of Madness",
        "Debilitating Shot", "Depravity", "Drain Delusions", "Energy Burn", "Energy Drain",
        "Energy Surge", "Energy Tap", "Ether Feast", "Ether Nightmare", "Ether Phantom",
        "\"Fear Me!\"", "Feedback", "Mind Wrack", "Power Leak", "Price of Pride",
        "Signet of Weariness", "Spirit Shackles", "Spirit Siphon", "Sympathetic Visage",
    };

    private static readonly HashSet<string> EnergyLossAny = new(StringComparer.OrdinalIgnoreCase)
    {
        "Quicksand",
    };

    // VOL d'énergie : la prendre à l'ennemi pour se la donner. Distinct du simple couple
    // gain + perte (Energy Drain ou Energy Tap font les deux sans être un vol).
    private static readonly HashSet<string> EnergyStealSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Avatar of Lyssa", "Guilt", "Power Leech", "Shame", "Tease",
        // Ajout Philippe (21/08/2026) : ce n'est PAS un vol 1:1 — la cible perd N énergie et le
        // lanceur en gagne un multiple (Energy Drain ×3, Energy Tap ×2) ou l'échange se fait en
        // régénération/dégénérescence (Ether Lord). Fonctionnellement, c'est la même chose : ce
        // que l'un perd, l'autre le gagne dans le même geste. Le wiki, lui, ne les compte pas.
        "Energy Drain", "Energy Tap", "Ether Lord",
    };

    // COÛT en énergie : les 4 rituels de la nature qui l'augmentent, et les 18 qui le réduisent.
    // Absentes du catalogue : « Kilroy Stonekin », Chimera of Intensity.
    private static readonly HashSet<string> EnergyCostUpSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Nature's Renewal", "Primal Echoes", "Quickening Zephyr", "Roaring Winds",
    };

    private static readonly HashSet<string> EnergyCostDownSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Air of Enchantment", "Anguished Was Lingwah", "Attuned Was Songkai",
        "Chimera of Intensity", "Cultist's Fervor", "Divine Spirit", "Energizing Chorus",
        "Energizing Wind", "Expert Focus", "Glyph of Energy", "Glyph of Lesser Energy",
        "Healer's Covenant", "Jaundiced Gaze", "\"Kilroy Stonekin\"", "Renewing Memories",
        "Selfless Spirit", "Soul Twisting", "Way of the Empty Palm",
    };

    // ÉNERGIE MAXIMALE : liste nommée de la page (3 entrées, dont Scepter of Orr's Aura absente).
    private static readonly HashSet<string> MaxEnergySkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Empowerment", "Mighty Was Vorizun", "Scepter of Orr's Aura",
    };

    private static readonly HashSet<string> EnergyRegenSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aura of the Juggernaut", "Blood is Power", "Blood Ritual", "Cry of Madness",
        "Energetic Was Lee Sa", "Ether Lord", "Ether Prodigy", "Lyssa's Aura",
        "Melandru's Resilience", "Scepter of Orr's Power", "Song of Power", "Spirit Channeling",
        "Succor", "\"Tango Down!\"", "\"The Power Is Yours!\"", "Vow of Revolution",
        "Well of Power",
    };

    // DÉGÉNÉRESCENCE d'énergie. Absente du catalogue : Crystal Haze (compétence de monstre).
    private static readonly HashSet<string> EnergyDegenSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Crystal Haze", "Ether Lord", "Ether Phantom", "Malaise", "Obsidian Flesh", "Power Flux",
        "Signet of Recall", "Well of Weariness", "Wither", "Zealous Renewal", "Zealous Vow",
    };

    private static readonly HashSet<string> RegenSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Agnar's Rage", "Blood Renewal", "Celestial Stance", "Conviction", "Cry of Madness",
        "Feel No Pain", "Feigned Neutrality", "Healing Breeze", "Hexer's Vigor",
        "\"I Will Avenge You!\"", "\"I Will Survive!\"", "Ice Spear", "Life Siphon",
        "Life Transfer", "Melandru's Resilience", "Mending", "Mending Refrain",
        "Motivating Insults", "Mystic Regeneration", "Never Rampage Alone", "\"Never Surrender!\"",
        "Power of the Staff of the Mists", "Purify Soul", "Recuperation", "Resilient Was Xiko",
        "Resilient Weapon", "Restful Breeze", "Shadow Refuge", "Shadow Sanctuary",
        "Shield of Regeneration", "Shroud of Distress", "Succor", "Swirling Aura",
        "Symbiotic Bond", "\"Tango Down!\"", "\"Together as One!\"", "Troll Unguent",
        "Vampiric Spirit", "Verata's Promise", "Verata's Sacrifice", "Volfen Blessing",
        "Vow of Piety", "Ward Against Harm", "Watchful Healing", "Watchful Spirit",
        "Weapon of Warding", "Well of Blood", "Well of Power",
    };

    // DÉGÉNÉRESCENCE de vie, directe seulement. Malaise est la seule qui frappe LE LANCEUR
    // (« You have -1 Health degeneration »). Lacerate, Radiation Field et Ulcerous Lungs
    // dégénèrent ET infligent une condition : elles sont dans les deux mécaniques, à juste titre.
    // Absentes du catalogue : Holiday Blues (Wintersday), Spectral Agony (Saul D'Alessio),
    // The Chalice of Corruption, Tongue Whip.
    private static readonly HashSet<string> DegenSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Conjure Nightmare", "Conjure Phantasm", "Corrupt Enchantment", "Crippling Anguish",
        "Cry of Pain", "Enduring Toxin", "Ether Nightmare", "Faintheartedness", "Holiday Blues",
        "Illusion of Pain", "Images of Remorse", "Lacerate", "Lamentation", "Life Siphon",
        "Life Transfer", "Lingering Curse", "Malaise", "Mark of Insecurity", "Migraine",
        "Overload", "Parasitic Bond", "Phantom Pain", "Putrid Bile", "Radiation Field",
        "Reaper's Mark", "Recurring Insecurity", "Shrinking Armor",
        "Spectral Agony (Saul D'Alessio)", "Suffering", "Teinai's Heat", "Teinai's Prison",
        "The Chalice of Corruption", "Tongue Whip", "Toxicity", "Ulcerous Lungs", "Vile Miasma",
        "Weaken Knees", "Well of Silence", "Well of Suffering", "Wither",
    };

    private static readonly HashSet<string> LifeStealSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Angorodon's Gaze", "Avatar of Grenth", "Blood Drinker", "Blood of the Aggressor",
        "Bloodsong", "Consume Soul", "Defiant Was Xinrae", "Feast of Corruption",
        "Feast of Vengeance", "Grenth's Aura", "Heal as One", "Insidious Parasite",
        "\"It's Good to Be King!\"", "Lifebane Strike", "Mark of Subversion", "Nightmare Weapon",
        "Order of the Vampire", "Ravenous Gaze", "Shadow Strike", "Soul Leech", "Spiritleech Aura",
        "Star Servant", "Strip Enchantment", "Taste of Death", "Taste of Undeath",
        "Touch of Dhuum", "Twisting Jaws", "Unholy Feast", "Vampiric Assault", "Vampiric Bite",
        "Vampiric Gaze", "Vampiric Spirit", "Vampiric Swarm", "Vampiric Touch", "Vampirism",
        "Vengeful Was Khanhei", "Vengeful Weapon", "Weapon of Remedy", "Xinrae's Weapon",
    };

    private static readonly HashSet<string> HealthGainSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Adoration", "Animate Vampiric Horror", "Aria of Restoration", "Ballad of Restoration",
        "Blood Bond", "Caretaker's Charge", "Consume Corpse", "Contemplation of Purity",
        "Death's Retreat", "Divert Hexes",
        "Drain Enchantment", "Draw Conditions", "\"Fall Back!\"", "Generous Was Tsungrai",
        "Grenth's Balance", "Healing Hands", "Healing Seed", "\"Help!\"", "\"Incoming!\"",
        "Live Vicariously", "Mark of Protection", "Mystic Vigor", "Pious Renewal",
        "Pious Restoration", "Predator's Pounce", "Predatory Season", "Sadist's Signet",
        "Second Wind", "Seed of Life", "Side Step", "Signet of Lost Souls", "Song of Restoration",
        "Soul Feast", "Spirit Boon Strike", "Spirit Light Weapon", "Spirit's Gift", "Star Servant",
        "Stout-Hearted", "Summon Spirits", "Taste of Pain", "Twin Moon Sweep", "Vampirism",
        "Victorious Sweep", "\"Victory Is Mine!\"", "Vital Boon", "Watchful Healing",
        "Way of Perfection",
    };

    // Retrait de maléfices : listes nommées du wiki
    // (https://wiki.guildwars.com/wiki/Hex_removal), sections « On ally » et « On foe ».
    // Clé = nom de BASE, la variante « (PvP) » suit.
    private static readonly HashSet<string> HexRemovalOnAlly = new(StringComparer.OrdinalIgnoreCase)
    {
        "\"Tango Down!\"", "Avatar of Dwayna", "Blessed Light", "Contemplation of Purity",
        "Convert Hexes", "Cure Hex", "Deny Hexes", "Divert Hexes", "Empathic Removal",
        "Expel Hexes", "Hex Eater Signet", "Hex Eater Vortex", "Hexbreaker Aria", "Holy Veil",
        "Inspired Hex", "Nature's Blessing", "Peace and Harmony", "Pious Restoration",
        "Purge Signet", "Remove Hex", "Revealed Hex", "Reverse Hex", "Shatter Hex",
        "Signet of Removal", "Smite Hex", "Spotless Mind", "Star Shine", "Withdraw Hexes",
    };

    private static readonly HashSet<string> HexRemovalOnFoe = new(StringComparer.OrdinalIgnoreCase)
    {
        "Drain Delusions", "Shatter Delusions",
    };

    // Retrait de conditions : listes nommées du wiki
    // (https://wiki.guildwars.com/wiki/Condition_removal), sections « remove any condition » et
    // « remove a specific condition ». Contrôle croisé fait : un balayage large des descriptions ne
    // ramène que 6 candidats hors liste, tous faux positifs (retrait de STANCE pour Forceful Blow,
    // retrait d'ENCHANTEMENT pour Wounding Strike et Signet of Pious Restraint, simple déclencheur
    // « gagne ou perd une condition » pour Fragility).
    // Clé = nom de BASE, la variante « (PvP) » suit.
    private static readonly HashSet<string> ConditionRemovalSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "\"It's Just a Flesh Wound.\"", "\"Tango Down!\"", "Air of Superiority", "Antidote Signet",
        "Assassin's Remedy", "Avatar of Melandru", "Blessed Light", "Blessing of the Kirin",
        "Cautery Signet", "Contemplation of Purity", "Conviction", "Crystal Wave", "Dismiss Condition",
        "Divert Hexes", "Draw Conditions", "Empathic Removal", "Enchanted Haste", "Extinguish",
        "Foul Feast", "Grenth's Fingers", "Grenth's Grasp", "Harrier's Grasp", "Hypochondria",
        "Impossible Odds", "Infuse Condition", "Life Sheath", "Lyric of Purification", "Martyr",
        "Mend Ailment", "Mend Body and Soul", "Mend Condition", "Mending Grip", "Mending Touch",
        "Peace and Harmony", "Plague Sending", "Plague Signet", "Plague Touch", "Pure Was Li Ming",
        "Purge Conditions", "Purge Signet", "Purifying Finale", "Purifying Veil", "Reap Impurities",
        "Rejuvenating Soul Explosion", "Relentless Assault", "Remedy Signet", "Resilient Was Xiko",
        "Restore Condition", "Signet of Malice", "Signet of Removal", "Smite Condition",
        "Song of Purification", "Spear of Redemption", "Spirit's Gift", "Spotless Soul",
        "Star Shine", "Verata's Sacrifice", "Weapon of Remedy", "Wielder's Remedy", "Yellow Snow",
        // retrait d'une condition PRÉCISE
        "\"Charge!\"", "\"I Am Unstoppable!\"", "Avatar of Grenth", "Breath of the Great Dwarf",
        "Ebon Dust Aura", "Guiding Hands", "Illusion of Haste", "Mystic Corruption", "Raven Shriek",
        "Sight Beyond Sight", "Tainted Flesh",
    };

    // Section « skills that transfer conditions » : la condition ne disparaît pas, elle change de
    // porteur. Sous-ensemble du retrait.
    private static readonly HashSet<string> ConditionTransferSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draw Conditions", "Foul Feast", "Grenth's Fingers", "Grenth's Grasp", "Hypochondria",
        "Infuse Condition", "Martyr", "Pestilence", "Plague Sending", "Plague Signet",
        "Plague Touch", "Verata's Sacrifice",
    };

    // Sections « on self » + « on foe and self », PLUS les compétences de Derviche qui consomment
    // un de leurs propres enchantements comme coût (ajout Philippe 19/08/2026 — le wiki ne les
    // classe pas comme des retraits, lui).
    //
    // ⚠ Ne PAS confondre avec les compétences qui RÉAGISSENT à la perte d'un enchantement
    // (« whenever you lose a Dervish enchantment, … ») : Aura of Holy Might, Avatar of Balthazar,
    // de Dwayna, de Grenth, de Melandru. Elles ne retirent rien, elles se déclenchent. Idem
    // Avatar of Lyssa, qui ne fait qu'accélérer la recharge. Elles relèveraient d'un futur
    // « Enchantment removal related skills », pas d'ici.
    private static readonly HashSet<string> EnchRemovalOnSelf = new(StringComparer.OrdinalIgnoreCase)
    {
        "Contemplation of Purity", "Ether Prodigy", "Release Enchantments", "Second Wind",
        "Rending Sweep", "Rending Touch", "Winds of Disenchantment",

        // Derviche : consommation d'un de ses propres enchantements.
        "Eremite's Attack", "Irresistible Sweep", "Pious Assault", "Pious Concentration",
        "Pious Fury", "Pious Haste", "Pious Restoration", "Reaper's Sweep",
        "Signet of Pious Light", "Signet of Pious Restraint", "Twin Moon Sweep",
        "Wearying Strike", "Wounding Strike",
    };

    // Portée ILLIMITÉE : les deux seules compétences du jeu dont l'effet ne connaît aucun rayon.
    // Vérifié sur trois pages du wiki + les deux discussions (20/08/2026, demande Philippe) :
    //   · Range — « Cautery Signet and Mystic Healing are exceptions - they have unlimited range » ;
    //   · Area of effect, encart d'anomalie sous « party area » — « have an unlimited range,
    //     affecting even party members outside of party area » ;
    //   · Mystic Healing, Notes — « infinite range and affects all party members regardless of
    //     where they are in the instance » ; Cautery Signet, Notes — « infinite range ».
    // La discussion de Cautery Signet (§Range 2007, §Contradiction in notes 2011) a tranché une
    // contradiction ancienne des notes : la portée n'est PAS bornée à la zone de groupe, c'est
    // seulement l'IA qui l'ignore et n'utilise la compétence que si un allié à portée de groupe
    // porte une condition. Elles restent AUSSI dans « Groupe » : le wiki les liste bien dans la
    // table de la zone de groupe, avec cette exception en encart.
    // Clé = nom de BASE, donc « Mystic Healing (PvP) » suit.
    private static readonly HashSet<string> UnlimitedRangeSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cautery Signet", "Mystic Healing",
    };

    private static readonly HashSet<string> PartyAreaSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Breath of the Great Dwarf", "Cautery Signet", "Celestial Haste", "Celestial Stance",
        "Dark Fury", "Dwayna's Sorrow", "Extinguish", "Feast of Souls", "Heal Party",
        "Light of Deliverance", "Martyr", "Meditation of the Reaper", "Mirror of Disenchantment",
        "Mystic Healing", "Order of Apostasy", "Order of Pain", "Order of the Vampire",
        "Protective Was Kaolai", "Release Enchantments", "Seed of Life",
        "Star Servant", "Star Shine", "Star Strike",
    };
    // Rituel d'asservissement à effet de ZONE : son esprit agit sur tout ce qui est à sa portée
    // (« within range », « in earshot »…), par opposition aux esprits d'attaque (« Its attacks
    // deal… ») qui ne frappent qu'une cible. Arbitrage Philippe : seuls les premiers sont des AoE ;
    // AUCUN rituel de la nature n'en est un.
    private static readonly System.Text.RegularExpressions.Regex ZoneRitualRe =
        new(@"\b(with)?in range\b|\bin earshot\b|\bin the area\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Mécaniques DÉRIVABLES de la base, sans réseau : type, upkeep, sacrifice, conditions déjà
    /// scrapées. Les mécaniques qui demandent une page wiki (retraits, interruptions, KD…) viendront
    /// s'ajouter dans la même colonne, écrites par le scraper — la lecture reste identique (A2).
    /// </summary>
    /// <summary>Nom sous lequel une compétence est cherchée dans les listes en dur : son nom
    /// débarrassé de TOUS les suffixes de variante. « (PvP) » d'abord — c'est le cas des 156 paires
    /// — puis « (Kurzick) »/« (Luxon) » et « (Codex) ». ⚠ Sans les suffixes d'allégeance,
    /// « Elemental Lord (Kurzick) » et « Summon Spirits (Luxon) » échapperaient aux listes du wiki,
    /// qui ne connaît que le nom nu (constaté sur le soin, 19/08/2026).</summary>
    private static string ListKey(string? name)
    {
        var n = Search.SkillVariants.BaseName(name ?? string.Empty);
        foreach (var suffix in VariantSuffixes)
            if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return n[..^suffix.Length];
        return n;
    }

    private static readonly string[] VariantSuffixes = [" (Kurzick)", " (Luxon)", " (Codex)"];

    public static string[] Compute(Skill s)
    {
        var keys = new List<string>();
        var raw = s.SkillType ?? string.Empty;
        var canon = CanonicalType(raw);

        // Les 3 grandes familles. Ce qui ne tombe dans aucune n'a plus de clé de type depuis la
        // suppression d'« Autres types » (21/08/2026) : c'est voulu.
        if (canon.Length > 0)
        {
            if (canon.EndsWith("attack", StringComparison.Ordinal)) keys.Add(MechAttack);
            else if (canon == "spell" || canon.EndsWith(" spell", StringComparison.Ordinal)) keys.Add(MechSpell);
            else if (canon.EndsWith("ritual", StringComparison.Ordinal)) keys.Add(MechRitual);
        }

        // Familles d'attaques (arbitrage Philippe 19/08/2026) : un tir à l'arc ou au javelot EST une
        // attaque à distance ; hache/épée/marteau/faux, la chaîne de l'Assassin et les attaques
        // d'animal sont des attaques de corps à corps. La chaîne préparatoire/secondaire/double est
        // en plus une attaque de dague. SEULE exception : Spear Swipe (« Spear Melee Attack ») est
        // une attaque de lance qui frappe au corps à corps, donc pas à distance.
        if (MeleeTypes.Contains(canon))  keys.Add(MechMelee);
        if (DaggerTypes.Contains(canon)) keys.Add(MechDagger);
        if (RangedTypes.Contains(canon)) keys.Add(MechRanged);

        // « Enchantment » n'existe pas comme SkillType : c'est l'union des deux types d'enchantement
        // (arbitrage Philippe). Les Formes (avatars du Derviche) n'en sont PAS.
        if (canon is "enchantment spell" or "flash enchantment spell")
            keys.Add(MechEnchantment);

        // L'upkeep EST le coût du maintien, et seuls des enchantements peuvent être maintenus :
        // relation bijective (Philippe, 19/08/2026). Aucune liste à tenir à jour.
        if (s.Upkeep > 0) keys.Add(MechMaintained);

        if (s.Sacrifice > 0) keys.Add(MechSacrifice);

        if (raw.Contains("Touch", StringComparison.OrdinalIgnoreCase)) keys.Add(MechTouch);
        if (raw.Contains("Half Range", StringComparison.OrdinalIgnoreCase)) keys.Add(MechHalfRange);

        // ── Aire d'effet ──
        // Wards et puits : leur texte dit « in this ward / in this well », jamais un des trois
        // rayons, mais leur zone a la portée « Area » (arbitrage Philippe 19/08/2026).
        // AUCUN rituel de la nature n'est une AoE (arbitrage Philippe). Sans cette exclusion,
        // « Pestilence » y entrerait par sa description (« conditions … to all creatures in the
        // area ») — c'est la seule des 25 dans ce cas.
        var desc = CanonicalType(raw) == "nature ritual" ? string.Empty : s.Description ?? string.Empty;
        bool adjacent = AoeAdjacentRe.IsMatch(desc);
        bool near     = AoeNearRe.IsMatch(desc);
        bool area     = AoeAreaRe.IsMatch(desc) || canon is "ward spell" or "well spell";
        // Arbitrage Philippe 20/08/2026 : TOUT cri porte à la voix, quelle que soit la profession
        // — c'est la nature du type, pas une affaire de description. 41 des 67 cris ne le disaient
        // pas dans leur texte. (Les 26 chants, eux, le disent tous les 26 : rien à ajouter.)
        bool earshot  = AoeEarshotRe.IsMatch(desc) || canon == "shout";
        bool party    = PartyAreaSkills.Contains(ListKey(s.Name));
        bool infinite = UnlimitedRangeSkills.Contains(ListKey(s.Name));
        // Zone persistante sans portée nommée : elle entre dans AoE, mais dans aucun des 4 rayons.
        bool zoneRitual = canon == "binding ritual" && ZoneRitualRe.IsMatch(desc);

        if (adjacent) keys.Add(MechAoeAdjacent);
        if (near)     keys.Add(MechAoeNear);
        if (area)     keys.Add(MechAoeArea);
        if (earshot)  keys.Add(MechAoeEarshot);
        if (party)    keys.Add(MechAoeParty);
        if (infinite) keys.Add(MechAoeUnlimited);
        if (adjacent || near || area || earshot || party || infinite || zoneRitual) keys.Add(MechAoe);

        // Retrait d'enchantements. Trois compétences retirent des DEUX côtés (Rending Sweep,
        // Rending Touch, Winds of Disenchantment) : elles comptent dans les deux sous-catégories.
        var baseName = ListKey(s.Name);
        bool remFoe  = EnchRemovalOnFoe.Contains(baseName);
        bool remSelf = EnchRemovalOnSelf.Contains(baseName);
        if (remFoe)  keys.Add(MechEnchRemovalFoe);
        if (remSelf) keys.Add(MechEnchRemovalSelf);
        if (remFoe || remSelf) keys.Add(MechEnchRemoval);

        // Retrait de conditions. Le transfert en est un cas particulier : la condition change de
        // porteur au lieu de disparaître — il compte donc dans les deux.
        bool condTransfer = ConditionTransferSkills.Contains(baseName);
        if (condTransfer) keys.Add(MechCondTransfer);
        if (condTransfer || ConditionRemovalSkills.Contains(baseName)) keys.Add(MechCondRemoval);

        // Retrait de maléfices. Le wiki distingue « on ally » (l'immense majorité) et « on foe »
        // — retirer un maléfice à un ennemi pour en tirer un bénéfice (Drain Delusions).
        bool hexAlly = HexRemovalOnAlly.Contains(baseName);
        bool hexFoe  = HexRemovalOnFoe.Contains(baseName);
        if (hexAlly) keys.Add(MechHexRemovalAlly);
        if (hexFoe)  keys.Add(MechHexRemovalFoe);
        if (hexAlly || hexFoe) keys.Add(MechHexRemoval);

        // Interruptions.
        bool intSpell  = InterruptSpells.Contains(baseName);
        bool intChant  = InterruptSpellsChants.Contains(baseName);
        bool intSkill  = InterruptSkills.Contains(baseName);
        bool intAttack = InterruptAttacks.Contains(baseName);
        bool intAction = InterruptActions.Contains(baseName);
        if (intSpell)  keys.Add(MechInterruptSpell);
        if (intChant)  keys.Add(MechInterruptSpellChant);
        if (intSkill)  keys.Add(MechInterruptSkill);
        if (intAttack) keys.Add(MechInterruptAttack);
        if (intAction) keys.Add(MechInterruptAction);
        if (intSpell || intChant || intSkill || intAttack || intAction) keys.Add(MechInterrupt);

        // Mise à terre.
        bool kdUncond = KnockdownUnconditional.Contains(baseName);
        bool kdCond   = KnockdownConditional.Contains(baseName);
        bool kdSelf   = KnockdownSelf.Contains(baseName);
        if (kdUncond) keys.Add(MechKnockdownUncond);
        if (kdCond)   keys.Add(MechKnockdownCond);
        if (kdSelf)   keys.Add(MechKnockdownSelf);
        if (kdUncond || kdCond || kdSelf) keys.Add(MechKnockdown);

        // Ralentissement du déplacement.
        bool snareFoe  = SnareOnFoes.Contains(baseName);
        bool snareSelf = SnareOnSelf.Contains(baseName);
        bool snareAny  = SnareOnAnyone.Contains(baseName);
        if (snareFoe)  keys.Add(MechSnareFoe);
        if (snareSelf) keys.Add(MechSnareSelf);
        if (snareAny)  keys.Add(MechSnareAny);
        if (snareFoe || snareSelf || snareAny) keys.Add(MechSnare);

        // Autour de la mise a terre. Les 3 seaux sont disjoints et le parent EST leur union :
        // il n'a pas de liste a lui.
        bool kdPrevent = KdPreventSkills.Contains(baseName);
        bool kdBenefit = KdBenefitSkills.Contains(baseName);
        bool kdRequire = KdRequireSkills.Contains(baseName);
        if (kdPrevent) keys.Add(MechKdPrevent);
        if (kdBenefit) keys.Add(MechKdBenefit);
        if (kdRequire) keys.Add(MechKdRequire);
        if (kdPrevent || kdBenefit || kdRequire) keys.Add(MechKdRelated);

        if (AttributeBoostSkills.Contains(baseName) && !AttributeBoostExcluded.Contains(s.Name ?? string.Empty))
            keys.Add(MechAttributeBoost);

        // Accélération du déplacement.
        bool imsSelf = SpeedBoostOnSelf.Contains(baseName);
        bool imsAlly = SpeedBoostOnAlly.Contains(baseName);
        bool imsFoe  = SpeedBoostOnFoe.Contains(baseName);
        bool imsPet  = SpeedBoostOnPet.Contains(baseName);
        if (imsSelf) keys.Add(MechSpeedBoostSelf);
        if (imsAlly) keys.Add(MechSpeedBoostAlly);
        if (imsFoe)  keys.Add(MechSpeedBoostFoe);
        if (imsPet)  keys.Add(MechSpeedBoostPet);
        if (imsSelf || imsAlly || imsFoe || imsPet) keys.Add(MechSpeedBoost);

        // Vitesse d'attaque.
        bool iasSelf   = IasOnSelf.Contains(baseName);
        bool iasPet    = IasOnPet.Contains(baseName);
        bool iasSpirit = IasOnSpirit.Contains(baseName);
        bool iasFoe    = IasOnFoe.Contains(baseName);
        if (iasSelf)   keys.Add(MechAtkSpeedUpSelf);
        if (iasPet)    keys.Add(MechAtkSpeedUpPet);
        if (iasSpirit) keys.Add(MechAtkSpeedUpSpirit);
        if (iasFoe)    keys.Add(MechAtkSpeedUpFoe);
        if (iasSelf || iasPet || iasSpirit || iasFoe) keys.Add(MechAtkSpeedUp);
        if (DasSkills.Contains(baseName)) keys.Add(MechAtkSpeedDown);

        // Soin / gain de vie — deux mécaniques distinctes, une compétence peut faire les deux.
        if (HealingSkills.Contains(baseName))    keys.Add(MechHealing);
        if (HealthGainSkills.Contains(baseName)) keys.Add(MechHealthGain);
        if (LifeStealSkills.Contains(baseName))  keys.Add(MechLifeSteal);
        if (RegenSkills.Contains(baseName))      keys.Add(MechRegen);
        if (DegenSkills.Contains(baseName))      keys.Add(MechDegen);
        if (EnergyRegenSkills.Contains(baseName)) keys.Add(MechEnergyRegen);
        if (EnergyDegenSkills.Contains(baseName)) keys.Add(MechEnergyDegen);

        bool egSelf = EnergyGainSelf.Contains(baseName);
        bool egAlly = EnergyGainAlly.Contains(baseName);
        bool egFoe  = EnergyGainFoe.Contains(baseName);
        if (egSelf) keys.Add(MechEnergyGainSelf);
        if (egAlly) keys.Add(MechEnergyGainAlly);
        if (egFoe)  keys.Add(MechEnergyGainFoe);
        if (egSelf || egAlly || egFoe) keys.Add(MechEnergyGain);

        bool elSelf = EnergyLossSelf.Contains(baseName);
        bool elFoe  = EnergyLossFoe.Contains(baseName);
        bool elAny  = EnergyLossAny.Contains(baseName);
        if (elSelf) keys.Add(MechEnergyLossSelf);
        if (elFoe)  keys.Add(MechEnergyLossFoe);
        if (elAny)  keys.Add(MechEnergyLossAny);
        if (elSelf || elFoe || elAny) keys.Add(MechEnergyLoss);

        if (EnergyStealSkills.Contains(baseName))    keys.Add(MechEnergySteal);
        if (EnergyCostUpSkills.Contains(baseName))   keys.Add(MechEnergyCostUp);
        if (EnergyCostDownSkills.Contains(baseName)) keys.Add(MechEnergyCostDown);
        if (MaxEnergySkills.Contains(baseName))      keys.Add(MechMaxEnergy);
        if (SignetRelatedSkills.Contains(baseName))  keys.Add(MechSignetRelated);
        if (MinionAnimationSkills.Contains(baseName)) keys.Add(MechMinionAnimation);
        if (MinionRelatedSkills.Contains(baseName))   keys.Add(MechMinionRelated);
        if (SpiritRelatedSkills.Contains(baseName))        keys.Add(MechSpiritRelated);
        if (BindingRitualRelatedSkills.Contains(baseName)) keys.Add(MechBindingRitualRel);
        if (SummonRelatedSkills.Contains(baseName))        keys.Add(MechSummonRelated);
        if (AntiSummonSkills.Contains(baseName))           keys.Add(MechAntiSummon);
        // Tout rituel invoque un esprit — c'est la définition même du type, d'asservissement comme
        // de la nature (et le rituel de l'Avant-garde d'Ebon).
        if (keys.Contains(MechRitual)
            || MinionAnimationSkills.Contains(baseName)
            || SummonCreatureExtraSkills.Contains(baseName)) keys.Add(MechSummonCreature);

        // Conditions infligées à la CIBLE : les entrées « :self » frappent le lanceur — même
        // convention que le bandeau de conditions et le calculateur d'armure (ConditionInfliction).
        var conditions = s.Conditions
            .Where(c => !GwConditionData.IsSelf(c))
            .Select(GwConditionData.NameOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (conditions.Count > 0)
        {
            keys.Add(MechCondition);
            // Ordre canonique du wiki (celui de GwConditionData.All), pas l'ordre de scraping.
            foreach (var c in GwConditionData.All)
                if (conditions.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                    keys.Add(ConditionPrefix + c.Name);
        }

        // Retrait ciblé des clés qu'une variante ne partage pas avec sa jumelle : on retire la clé
        // parente ET ses clés filles (cf. MechanicExceptions).
        foreach (var (name, key) in MechanicExceptions)
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                keys.RemoveAll(k => k == key || k.StartsWith(key + ":", StringComparison.Ordinal));

        return [.. keys];
    }

    // ── Quand une variante ne partage PAS la mécanique de sa jumelle ──────────
    //
    // Les listes ci-dessus sont tenues par nom de BASE (ListKey), ce qui fait suivre la variante
    // « (PvP) » gratuitement — vrai pour la quasi-totalité des 156 paires du catalogue. Les
    // exceptions vont dans les DEUX SENS, et la table les traite pareil : elle nomme la ligne
    // EXACTE à qui la clé ne doit pas être posée.
    //
    //   a) la variante PvP a PERDU la mécanique (le cas courant) — on nomme la ligne « (PvP) » ;
    //   b) la mécanique n'existe QUE en PvP — la catégorie wiki liste alors « X (PvP) » SANS sa
    //      page de base, et c'est la ligne PvE qu'il faut nommer. Un balayage de toutes les
    //      catégories importées (19/08/2026) n'a trouvé ce cas b que deux fois, tous deux dans
    //      « Energy Gain ».
    // Vérifié le 19/08/2026 par un diff phrase à phrase des 156 paires : il n'y en a pas d'autres.
    // Les 13 autres paires divergentes ne changent que la portée ou la formulation, jamais
    // l'appartenance (Avatar de Melandru soigne le lanceur au lieu du groupe mais SOIGNE toujours,
    // Signet of Judgment devient à moitié de portée mais met toujours à terre…).
    private static readonly (string Name, string Key)[] MechanicExceptions =
    [
        // « You attack, move and gain adrenaline 25% faster » → « You attack and gain adrenaline
        // 25% faster » : la version PvP n'accélère plus le déplacement.
        ("Onslaught (PvP)", MechSpeedBoost),
        // La version PvP a perdu « Initial effect: removes Crippled condition » : elle ne retire
        // plus rien, elle ne fait plus que s'infliger l'Infirmité en fin d'enchantement.
        ("Illusion of Haste (PvP)", MechCondRemoval),
        // « Target foe moves and attacks 50% slower » → « Target foe moves 50% slower » : la
        // version PvP ne ralentit plus que le déplacement.
        ("Crippling Anguish (PvP)", MechAtkSpeedDown),
        // « Your pet attacks 25% faster and you gain … Health » → la version PvP ne garde que le
        // gain de vie.
        ("Predatory Bond (PvP)", MechAtkSpeedUp),
        // Refonte complète : la version PvP détruit une créature invoquée après l'avoir renforcée,
        // elle n'accélère plus rien.
        ("Signet of Ghostly Might (PvP)", MechAtkSpeedUp),
        // « Your pet's attacks steal 1…20 Health » a disparu : la version PvP ne fait plus que
        // soigner et ressusciter l'animal.
        ("Heal as One (PvP)", MechLifeSteal),
        // « You have +N Health regeneration and a X% chance to block » → il ne reste que le blocage.
        ("Shroud of Distress (PvP)", MechRegen),

        // ── cas b : mécanique PvP seulement, c'est la ligne PvE qu'on écarte ──
        // La version PvE ne donne que « +2 Death Magic and Soul Reaping » ; seule la PvP dit
        // « You gain 1…3 Energy whenever you sacrifice Health ».
        ("Masochism", MechEnergyGain),
        // La version PvE crée trois esprits ; seule la PvP dit « You gain 3…12 Energy ».
        ("Signet of Spirits", MechEnergyGain),

        // ── cas c : les DEUX variantes comptent, mais dans des listes DIFFÉRENTES ──
        // Ces trois paires sont le cas le plus retors du chantier : le wiki nomme explicitement
        // « X » dans une liste et « X (PvP) » dans une autre. Tenir les listes par nom de base les
        // ferait entrer toutes les deux partout ; il faut retirer chaque ligne de la liste qui n'est
        // pas la sienne.
        // PvE : « Your spirits take 50% less damage » (esprits) ; PvP : « damage reduction while
        // casting binding rituals » (rituels).
        ("Armor of Unfeeling", MechBindingRitualRel),
        ("Armor of Unfeeling (PvP)", MechSpiritRelated),
        // PvE : « All spirits you control … attack 33% faster » (esprits) ; PvP : « Target allied
        // summoned creature deals +5…35 damage » (invocations en général).
        ("Signet of Ghostly Might", MechSummonRelated),
        ("Signet of Ghostly Might (PvP)", MechSpiritRelated),
        // PvE : CRÉE trois esprits (elle est donc « invoque une créature », pas « liée aux
        // esprits ») ; seule la PvP interagit avec un esprit déjà là — et n'invoque plus rien.
        ("Signet of Spirits", MechSpiritRelated),
        ("Signet of Spirits (PvP)", MechSummonCreature),
    ];

    /// <summary>Cette clé est-elle produite par <see cref="Compute"/> ? Les autres viennent du wiki :
    /// un recalcul ne doit pas les effacer (c'est tout l'intérêt d'une colonne unique — A2).</summary>
    public static bool IsComputedKey(string key) =>
        key is MechAttack or MechMelee or MechDagger or MechRanged or MechSpell or MechRitual
            or MechEnchantment or MechMaintained or MechSacrifice or MechCondition
            or MechTouch or MechHalfRange or MechAoe or MechEnchRemoval or MechCondRemoval
            or MechHexRemoval or MechInterrupt or MechKnockdown or MechSnare or MechSpeedBoost
            or MechAtkSpeedUp or MechAtkSpeedDown or MechHealing or MechHealthGain or MechLifeSteal
            or MechRegen or MechDegen or MechEnergyRegen or MechEnergyDegen
            or MechEnergyGain or MechEnergyLoss or MechEnergySteal
            or MechEnergyCostUp or MechEnergyCostDown or MechMaxEnergy or MechSignetRelated
            or MechMinionAnimation or MechMinionRelated or MechSummonCreature
            or MechSpiritRelated or MechBindingRitualRel or MechSummonRelated or MechAntiSummon
            or MechKdRelated or MechAttributeBoost
        || key.StartsWith(ConditionPrefix, StringComparison.Ordinal)
        || key.StartsWith(MechAoe + ":", StringComparison.Ordinal)
        || key.StartsWith(MechEnchRemoval + ":", StringComparison.Ordinal)
        || key.StartsWith(MechCondRemoval + ":", StringComparison.Ordinal)
        || key.StartsWith(MechHexRemoval + ":", StringComparison.Ordinal)
        || key.StartsWith(MechInterrupt + ":", StringComparison.Ordinal)
        || key.StartsWith(MechKnockdown + ":", StringComparison.Ordinal)
        || key.StartsWith(MechSnare + ":", StringComparison.Ordinal)
        || key.StartsWith(MechKdRelated + ":", StringComparison.Ordinal)
        || key.StartsWith(MechEnergyGain + ":", StringComparison.Ordinal)
        || key.StartsWith(MechEnergyLoss + ":", StringComparison.Ordinal)
        || key.StartsWith(MechSpeedBoost + ":", StringComparison.Ordinal)
        || key.StartsWith(MechAtkSpeedUp + ":", StringComparison.Ordinal);

    /// <summary>Recalcule les mécaniques dérivables en CONSERVANT celles qui viennent d'ailleurs.</summary>
    public static string[] Merge(IEnumerable<string> existing, Skill s) =>
    [
        .. existing.Where(k => !IsComputedKey(k)),
        .. Compute(s),
    ];

    /// <summary>Découpe la colonne CSV Skills.Mechanics. "" → aucune.</summary>
    public static string[] ParseCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── 2 bis. Mécaniques : définitions de la colonne ─────────────────────────

    // Les deux faces de la famille « Conditions » — en poser, en retirer. Libellés courts depuis le
    // 20/08/2026 : sous un parent qui dit déjà « Conditions », « Skills Inflicting Conditions » et
    // « Condition Removal » répétaient leur parent, et le premier était le plus long libellé de
    // toute la colonne. ⚠ Ces chaînes sont les CLÉS de logique (dictionnaire FR, alias, prédicats) —
    // rien ne les persiste sur disque, la colonne Skills.Mechanics stocke les clés « condition » et
    // « condremoval », pas les libellés.
    public const string ConditionsHeaderLabel = "Inflicted";

    // Familles parentes (demande Philippe 20/08/2026). Elles n'ont PAS de clé calculée à elles :
    // leur prédicat est l'union de celui de leurs enfants, ce qui évite une clé de plus dans la
    // colonne Skills.Mechanics pour une entrée qui n'apporte aucune information nouvelle.
    public const string ConditionsFamilyLabel = "Conditions";
    public const string EnergyFamilyLabel = "Energy";
    public const string HealthFamilyLabel = "Health & Healing";

    // Les 4 familles de types, désormais cliquables dans la colonne Mechanics.
    public const string AttacksLabel = "Attacks";
    public const string SpellsLabel = "Spells";
    public const string RitualsLabel = "Rituals";
    public const string AoeLabel = "AoE";
    public const string EnchRemovalLabel = "Enchantment Removal";
    public const string CondRemovalLabel = "Removed";
    public const string HexRemovalLabel = "Hex Removal";
    public const string InterruptLabel = "Interrupts";
    public const string KnockdownLabel = "Knock-down";
    public const string KdRelatedLabel = "Knock-down Related";
    public const string AttributeBoostLabel = "Attribute Boosting";
    // Anglicisme retenu par Philippe (19/08/2026) : c'est le mot que les joueurs francophones
    // emploient, et il n'existe aucune page FR correspondante sur le wiki officiel.
    public const string SnareLabel = "Snares";
    // Le titre de la page wiki plutôt que celui de la catégorie (« Increased Movement Speed ») :
    // c'est le mot des joueurs, et le pendant exact de « Snares » — la page dit elle-même que l'un
    // est l'opposé de l'autre. Choix de Philippe (19/08/2026).
    public const string SpeedBoostLabel = "Speed Boosts";
    // Les noms exacts des deux catégories wiki (choix de Philippe, 19/08/2026) : « Speed Boosts »
    // étant déjà pris par le déplacement, il fallait lever l'ambiguïté sans sigle.
    public const string AtkSpeedUpLabel   = "Increased Attack Speed";
    public const string AtkSpeedDownLabel = "Decreased Attack Speed";
    // Deux entrées sœurs, comme les deux catégories du wiki (arbitrage Philippe, 19/08/2026) :
    // « Health Gain » ne chapeaute PAS « Healing », même si la page Health gain range le soin
    // parmi les six façons de gagner de la vie — sa catégorie, elle, ne liste que le gain direct.
    public const string HealingLabel    = "Healing";
    public const string HealthGainLabel = "Health Gain";
    public const string LifeStealLabel  = "Life Stealing";
    public const string RegenLabel      = "Health Regeneration";
    public const string DegenLabel      = "Health Degeneration";
    public const string EnergyRegenLabel = "Energy Regeneration";
    public const string EnergyDegenLabel = "Energy Degeneration";
    public const string EnergyGainLabel    = "Energy Gain";
    public const string EnergyLossLabel    = "Energy Loss";
    public const string EnergyStealLabel   = "Energy Stealing";
    public const string EnergyCostUpLabel  = "Increased Energy Cost";
    public const string EnergyCostDownLabel = "Decreased Energy Cost";
    public const string MaxEnergyLabel     = "Maximum Energy";
    // Motif de nommage des familles « related » (arbitrage Philippe 19/08/2026), à reprendre pour
    // les invocations et les rituels d'asservissement.
    public const string SignetRelatedLabel = "Signet Related";
    public const string MinionAnimationLabel = "Minion Animation";
    public const string MinionRelatedLabel   = "Minion Related";
    // ⚠ Singulier VOULU (Philippe, 21/08/2026) : au tri alphabetique, « Summon » passe avant
    // « Summon Related », « Summons » passait apres (l'espace trie avant le « s »).
    public const string SummonCreatureLabel     = "Summon";
    public const string SpiritRelatedLabel      = "Spirit Related";
    public const string BindingRitualRelLabel   = "Binding Ritual Related";
    public const string SummonRelatedLabel      = "Summon Related";
    public const string AntiSummonLabel         = "Anti-Summon";

    public static IReadOnlyList<SkillCategoryDef> MechanicDefs { get; } = BuildMechanicDefs();

    private static IReadOnlyList<SkillCategoryDef> BuildMechanicDefs()
    {
        static Func<Skill, bool> Key(string k) => s => s.HasMechanic(k);
        // Prédicat d'une famille parente : l'union de ses enfants.
        static Func<Skill, bool> Any(params string[] ks) => s => Array.Exists(ks, s.HasMechanic);

        var defs = new List<SkillCategoryDef>
        {
            new(AllMechanicsLabel, 0, _ => true),

            // Les 4 familles, arrivées ici depuis la colonne des types (elles y étaient de simples
            // intertitres) : ce sont bien des mécaniques, et elles s'emboîtent.
            new(AttacksLabel, 0, Key(MechAttack)),
            new("Melee Attack",  1, Key(MechMelee)),
            new("Dagger Attack", 2, Key(MechDagger)),
            new("Ranged Attack", 1, Key(MechRanged)),

            new(SpellsLabel, 0, Key(MechSpell)),
            new("Enchantment", 1, Key(MechEnchantment)),
            new("Maintained Enchantment", 2, Key(MechMaintained)),

            // ⚠ Les deux enfants ne sont PAS un sous-ensemble du parent : « Rituels » sélectionne
            // les compétences QUI SONT des rituels, ses enfants celles qui INTERAGISSENT avec eux
            // (souvent des sorts). Regroupement thématique voulu par Philippe (20/08/2026) — même
            // liberté que les portées d'AoE ou les 3 enfants de Renversement, dont les sommes ne
            // font déjà pas celle de leur parent.
            new(RitualsLabel, 0, Key(MechRitual)),
            new(BindingRitualRelLabel, 1, Key(MechBindingRitualRel)),
            new(SpiritRelatedLabel,    1, Key(MechSpiritRelated)),

            // Aire d'effet. ⚠ La somme des 4 portées ne fait PAS le total du parent : les rituels
            // d'asservissement à effet de zone y entrent sans rayon nommé, et une compétence peut
            // citer deux rayons (Shockwave en touche trois).
            new(AoeLabel, 0, Key(MechAoe)),
            new("Adjacent",    1, Key(MechAoeAdjacent)),
            new("Near",        1, Key(MechAoeNear)),
            new("Area",        1, Key(MechAoeArea)),
            new("Earshot",     1, Key(MechAoeEarshot)),
            new("Party Range",     1, Key(MechAoeParty)),
            new("Unlimited Range", 1, Key(MechAoeUnlimited)),

            new(EnchRemovalLabel, 0, Key(MechEnchRemoval)),
            new("On Foe",  1, Key(MechEnchRemovalFoe)),
            new("On Self", 1, Key(MechEnchRemovalSelf)),

            new(HexRemovalLabel, 0, Key(MechHexRemoval)),
            new("On Ally", 1, Key(MechHexRemovalAlly)),
            new("On Foe",  1, Key(MechHexRemovalFoe)),

            new(InterruptLabel, 0, Key(MechInterrupt)),
            new("Actions",         1, Key(MechInterruptAction)),
            new("Skills",          1, Key(MechInterruptSkill)),
            new("Spells",          1, Key(MechInterruptSpell)),
            new("Spells & Chants", 1, Key(MechInterruptSpellChant)),
            new("Attacks",         1, Key(MechInterruptAttack)),

            // ⚠ La somme des 3 enfants dépasse le parent : Grapple met à terre l'ennemi ET soi.
            new(KnockdownLabel, 0, Key(MechKnockdown)),
            new("Unconditional", 1, Key(MechKnockdownUncond)),
            new("Conditional",   1, Key(MechKnockdownCond)),
            new("On Yourself",   1, Key(MechKnockdownSelf)),

            // Ce que la mise a terre provoque chez LES AUTRES competences. Comme pour « Rituels »,
            // les enfants ne sont pas un sous-ensemble d'un parent voisin : ces competences ne
            // renversent pas, elles reagissent au renversement.
            new(KdRelatedLabel, 0, Key(MechKdRelated)),
            new("Prevent",      1, Key(MechKdPrevent)),
            new("Benefit From", 1, Key(MechKdBenefit)),
            new("Require",      1, Key(MechKdRequire)),

            // Les 3 enfants sont DISJOINTS depuis que Muddy Terrain a son propre seau (21/08/2026).
            new(SnareLabel, 0, Key(MechSnare)),
            new("On Foe",    1, Key(MechSnareFoe)),
            new("On Self",   1, Key(MechSnareSelf)),
            new("On Anyone", 1, Key(MechSnareAny)),

            // ⚠ La somme des 4 enfants dépasse le parent : Gust accélère le lanceur ET une cible
            // alliée, Run as One et Rampage as One le lanceur ET son animal.
            new(SpeedBoostLabel, 0, Key(MechSpeedBoost)),
            new("On Self", 1, Key(MechSpeedBoostSelf)),
            new("On Ally", 1, Key(MechSpeedBoostAlly)),
            new("On Foe",  1, Key(MechSpeedBoostFoe)),
            new("On Pet",  1, Key(MechSpeedBoostPet)),

            // ⚠ La somme des 4 enfants dépasse le parent : Never Rampage Alone et Rampage as One
            // accélèrent le lanceur ET son animal.
            new(AtkSpeedUpLabel, 0, Key(MechAtkSpeedUp)),
            new("On Self",    1, Key(MechAtkSpeedUpSelf)),
            new("On Pet",     1, Key(MechAtkSpeedUpPet)),
            new("On Spirits", 1, Key(MechAtkSpeedUpSpirit)),
            new("On Foe",     1, Key(MechAtkSpeedUpFoe)),

            // Toutes visent l'ennemi : aucun sous-seau n'apporterait rien.
            new(AtkSpeedDownLabel, 0, Key(MechAtkSpeedDown)),

            // Chaque mécanique reste à plat SOUS la famille, comme la table du wiki : aucun
            // découpage n'y existe, et le déduire du texte demanderait de trancher à la main 43 des
            // 124 descriptions (arbitrage Philippe).
            new(HealthFamilyLabel, 0, Any(MechHealing, MechHealthGain, MechLifeSteal,
                                          MechRegen, MechDegen, MechSacrifice)),
            new(HealingLabel,       1, Key(MechHealing)),
            new(HealthGainLabel,    1, Key(MechHealthGain)),
            new(LifeStealLabel,     1, Key(MechLifeSteal)),
            new(RegenLabel,         1, Key(MechRegen)),
            new(DegenLabel,         1, Key(MechDegen)),
            new("Health Sacrifice", 1, Key(MechSacrifice)),

            new(EnergyFamilyLabel, 0, Any(MechEnergyRegen, MechEnergyDegen, MechEnergyGain,
                                          MechEnergyLoss, MechEnergySteal, MechEnergyCostDown,
                                          MechEnergyCostUp, MechMaxEnergy)),
            new(EnergyRegenLabel, 1, Key(MechEnergyRegen)),
            new(EnergyDegenLabel, 1, Key(MechEnergyDegen)),

            // ⚠ La somme des 3 enfants dépasse le parent : Energy Boon donne de l'énergie au
            // lanceur ET à sa cible alliée (le wiki l'exclut de son « notitlematch » exprès).
            new(EnergyGainLabel, 1, Key(MechEnergyGain)),
            new("On Self",  2, Key(MechEnergyGainSelf)),
            new("On Ally",  2, Key(MechEnergyGainAlly)),
            new("On Foe",   2, Key(MechEnergyGainFoe)),

            new(EnergyLossLabel, 1, Key(MechEnergyLoss)),
            new("On Self",   2, Key(MechEnergyLossSelf)),
            new("On Foe",    2, Key(MechEnergyLossFoe)),
            new("On Anyone", 2, Key(MechEnergyLossAny)),

            new(EnergyStealLabel,    1, Key(MechEnergySteal)),
            new(EnergyCostDownLabel, 1, Key(MechEnergyCostDown)),
            new(EnergyCostUpLabel,   1, Key(MechEnergyCostUp)),
            new(MaxEnergyLabel,      1, Key(MechMaxEnergy)),

            new(AttributeBoostLabel, 0, Key(MechAttributeBoost)),

            new(SignetRelatedLabel, 0, Key(MechSignetRelated)),
            new(MinionAnimationLabel, 0, Key(MechMinionAnimation)),
            new(MinionRelatedLabel,   0, Key(MechMinionRelated)),
            new(SummonCreatureLabel,     0, Key(MechSummonCreature)),
            new(SummonRelatedLabel,      0, Key(MechSummonRelated)),
            new(AntiSummonLabel,         0, Key(MechAntiSummon)),

            // « Conditions-Inflicting Skills » aurait été le renommage naturel, mais l'anglais
            // veut le singulier dans un modificateur composé (« condition-inflicting »). Plutôt que
            // de trancher sur un libellé, Philippe a préféré le regroupement : les deux faces de la
            // mécanique — en poser, en retirer — sous un même parent.
            new(ConditionsFamilyLabel, 0, Any(MechCondRemoval, MechCondition)),
            new(CondRemovalLabel, 1, Key(MechCondRemoval)),
            new("Condition Transfer", 2, Key(MechCondTransfer)),
            new(ConditionsHeaderLabel, 1, Key(MechCondition)),
        };

        foreach (var c in GwConditionData.All)
            defs.Add(new(c.Name, 2, Key(ConditionPrefix + c.Name)));

        defs.Add(new("Touch", 0, Key(MechTouch)));
        defs.Add(new("Half Range", 0, Key(MechHalfRange)));

        return defs;
    }

    // ── 3. Libellés français ──────────────────────────────────────────────────
    //
    // Dictionnaire curé (décision 16) : TypeFr de la base est une donnée PAR COMPÉTENCE, fausse par
    // endroits (« Enchantement » y désigne aussi des Stances) — inutilisable comme dictionnaire de
    // types, même défaut que AttributeFr.
    //
    // ⚠⚠ LA VÉRITÉ EST LE CLIENT FRANÇAIS DU JEU, PAS LE WIKI. Ce dictionnaire avait d'abord été
    // bâti sur le wiki FR : 8 libellés de type étaient faux, corrigés le 19/08/2026 sur une capture
    // de la fenêtre « Compétences et caractéristiques » fournie par Philippe — Sceau (et non
    // Emblème), Enchantement, Maléfice, Sort d'altération d'objet, Sort de protection, Sort
    // d'altération d'arme, Transformation, Attaque AU corps à corps.
    // Seconde capture (19/08/2026, Olias N/P + liste des compétences de Derviche) : 5 de plus —
    // Attaque au javelot (et non « de lance »), Attaque à la faux, Enchantement instantané (et non
    // « flash »), Echo SANS accent, et l'attribut Maîtrise du javelot dans GwAttributeData.
    // Confirmés justes par cette capture : Sort de puits, Chant, Cri, Compétence, Pose de combat,
    // Sceau, Sort, Enchantement, Maléfice, Transformation, Attaque au corps à corps.
    // Restent non vus en capture (mais corroborés par le TypeFr de la base) : Glyphe, Préparation,
    // Rituel de la nature. ⚠ « Contact » et « Moitié de portée » sont NOS libellés de mécanique,
    // pas des types du jeu — le jeu écrit « Sort de toucher » dans les descriptions.
    private static readonly Dictionary<string, string> _frLabels = BuildFrLabels();

    private static Dictionary<string, string> BuildFrLabels()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AllTypesLabel]     = "Tous les types",
            [AllMechanicsLabel] = "Toutes les mécaniques",

            [AttacksLabel]    = "Attaques",
            [SpellsLabel]     = "Sorts",
            [RitualsLabel]    = "Rituels",

            // « AoE » se dit tel quel entre joueurs francophones ; les portées, elles, se traduisent.
            [AoeLabel]      = "AoE",
            ["Adjacent"]    = "Adjacent",
            ["Near"]        = "À proximité",
            ["Area"]        = "Dans la zone",
            ["Earshot"]     = "À portée de voix",
            ["Party Range"]     = "Groupe",
            ["Unlimited Range"] = "Portée illimitée",
            [EnchRemovalLabel] = "Retrait d'enchantements",
            ["On Foe"]  = "Sur un ennemi",
            ["On Self"] = "Sur soi",
            [CondRemovalLabel] = "Retirées",
            ["Condition Transfer"] = "Transfert de conditions",
            [HexRemovalLabel] = "Retrait de maléfices",
            ["On Ally"] = "Sur un allié",
            [InterruptLabel] = "Interruptions",
            ["Actions"] = "Actions",
            ["Skills"] = "Compétences",
            ["Spells & Chants"] = "Sorts et chants",
            ["Attacks"] = "Attaques",
            [KnockdownLabel] = "Renversement",
            ["Unconditional"] = "Inconditionnel",
            ["Conditional"] = "Conditionnel",
            ["On Yourself"] = "Sur soi-même",
            [KdRelatedLabel] = "Liées au renversement",
            ["Prevent"]      = "L'empêchent",
            ["Benefit From"] = "En profitent",
            ["Require"]      = "L'exigent",
            [AttributeBoostLabel] = "Hausse de caractéristique",
            [SnareLabel] = "Snares",
            [SpeedBoostLabel] = "Accélérations",
            ["On Pet"] = "Sur l'animal",
            [AtkSpeedUpLabel]   = "Vitesse d'attaque accrue",
            [AtkSpeedDownLabel] = "Vitesse d'attaque réduite",
            ["On Spirits"] = "Sur les esprits",
            [HealingLabel]    = "Soins",
            [HealthGainLabel] = "Gain de vie",
            [LifeStealLabel]  = "Vol de vie",
            [RegenLabel]      = "Régénération de vie",
            [DegenLabel]      = "Dégénérescence de vie",
            [EnergyRegenLabel] = "Régénération d'énergie",
            [EnergyDegenLabel] = "Dégénérescence d'énergie",
            [EnergyGainLabel]     = "Gain d'énergie",
            [EnergyLossLabel]     = "Perte d'énergie",
            [EnergyStealLabel]    = "Vol d'énergie",
            [EnergyCostUpLabel]   = "Coût en énergie augmenté",
            [EnergyCostDownLabel] = "Coût en énergie réduit",
            [MaxEnergyLabel]      = "Énergie maximale",
            [SignetRelatedLabel]  = "Liées aux sceaux",
            [MinionAnimationLabel] = "Anime des serviteurs",
            [MinionRelatedLabel]   = "Liées aux serviteurs",
            [SummonCreatureLabel]     = "Invocations",
            [SpiritRelatedLabel]      = "Liées aux esprits",
            [BindingRitualRelLabel]   = "Liées aux rituels d'asservissement",
            [SummonRelatedLabel]      = "Liées aux invocations",
            [AntiSummonLabel]         = "Anti-invocation",
            ["On Anyone"]         = "Sur tout le monde",

            ["Axe Attack"]      = "Attaque de hache",
            ["Bow Attack"]      = "Attaque d'arc",
            ["Dual Attack"]     = "Attaque double",
            ["Hammer Attack"]   = "Attaque de marteau",
            ["Lead Attack"]     = "Attaque préparatoire",
            ["Melee Attack"]    = "Attaque au corps à corps",
            ["Off-Hand Attack"] = "Attaque secondaire",
            ["Pet Attack"]      = "Attaque de familier",
            ["Ranged Attack"]   = "Attaque à distance",
            ["Scythe Attack"]   = "Attaque à la faux",
            ["Spear Attack"]    = "Attaque au javelot",
            ["Sword Attack"]    = "Attaque d'épée",
            ["Dagger Attack"]   = "Attaque de dague",

            ["Spell"]                   = "Sort",
            ["Enchantment Spell"]       = "Enchantement",
            ["Flash Enchantment Spell"] = "Enchantement instantané",
            ["Hex Spell"]               = "Maléfice",
            ["Item Spell"]              = "Sort d'altération d'objet",
            ["Ward Spell"]              = "Sort de protection",
            ["Weapon Spell"]            = "Sort d'altération d'arme",
            ["Well Spell"]              = "Sort de puits",

            ["Binding Ritual"]       = "Rituel d'asservissement",
            ["Nature Ritual"]        = "Rituel de la nature",
            ["Ebon Vanguard Ritual"] = "Rituel de l'Avant-garde d'Ebon",

            ["Chant"]       = "Chant",
            ["Echo"]        = "Echo",
            ["Form"]        = "Transformation",
            ["Glyph"]       = "Glyphe",
            ["Preparation"] = "Préparation",
            ["Shout"]       = "Cri",
            ["Signet"]      = "Sceau",
            ["Skill"]       = "Compétence",
            ["Stance"]      = "Pose de combat",
            ["Trap"]        = "Piège",

            ["Enchantment"]            = "Enchantements",
            ["Maintained Enchantment"] = "Enchantements maintenus",
            ["Health Sacrifice"]       = "Sacrifice de vie",
            [ConditionsHeaderLabel]    = "Infligées",
            [ConditionsFamilyLabel]    = "Conditions",
            [EnergyFamilyLabel]        = "Énergie",
            [HealthFamilyLabel]        = "Vie et soins",
            ["Touch"]                  = "Compétences de toucher",
            ["Half Range"]             = "Moitié de portée",
        };

        // Conditions : mêmes noms FR que le bandeau de conditions et le calculateur d'armure.
        foreach (var c in GwConditionData.All)
            if (GwConditionData.Fr.TryGetValue(c.Name, out var fr))
                d[c.Name] = fr;

        return d;
    }

    /// <summary>Libellé affiché (langue courante) d'une catégorie. L'EN reste la clé de filtrage.</summary>
    public static string DisplayName(string? english) =>
        AppLanguage.IsFr && english != null && _frLabels.TryGetValue(english, out var fr)
            ? fr
            : english ?? string.Empty;

    // ── 3 bis. Ordre d'AFFICHAGE des deux colonnes ────────────────────────────

    /// <summary>Enfants dont l'ordre a un sens et que le tri alphabétique détruirait. Les quatre
    /// portées d'AoE vont du plus petit au plus grand rayon : c'est ainsi que le jeu les présente
    /// et que le joueur les compare.</summary>
    private static readonly HashSet<string> KeepChildOrder = new(StringComparer.Ordinal)
    {
        AoeLabel,
    };

    private sealed record Node(SkillCategoryDef Def, List<Node> Children);

    /// <summary>Range les filtres par ordre alphabétique DANS LA LANGUE COURANTE (demande Philippe
    /// 20/08/2026 : avec une trentaine de mécaniques, l'ordre thématique ne se retrouvait plus).
    /// La hiérarchie est préservée — un enfant reste sous son parent, et seuls les frères sont
    /// triés entre eux. L'entrée méta (« Tous les types », « Toutes les mécaniques ») reste
    /// épinglée en tête : c'est une remise à zéro, pas un filtre.
    /// ⚠ L'ordre DÉPEND DE LA LANGUE : à rejouer à chaque bascule FR/EN.</summary>
    public static IReadOnlyList<SkillCategoryDef> SortForDisplay(IReadOnlyList<SkillCategoryDef> defs)
    {
        if (defs.Count == 0) return defs;

        var result = new List<SkillCategoryDef>(defs.Count);
        int i = 0;
        if (defs[0].Label is AllTypesLabel or AllMechanicsLabel) result.Add(defs[i++]);

        var roots = BuildTree(defs, ref i, defs.Count > i ? defs[i].Indent : 0);
        // Comparaison CULTURELLE et non ordinale : sans elle « Échos » se retrouve après « Sorts »
        // parce que « É » est au-delà de « z » en Unicode brut.
        var cmp = StringComparer.Create(
            CultureInfo.GetCultureInfo(AppLanguage.IsFr ? "fr-FR" : "en-US"), ignoreCase: true);
        Flatten(roots, result, cmp, sort: true);
        return result;
    }

    private static List<Node> BuildTree(IReadOnlyList<SkillCategoryDef> defs, ref int i, int level)
    {
        var list = new List<Node>();
        while (i < defs.Count && defs[i].Indent >= level)
        {
            var def = defs[i++];
            list.Add(new Node(def, BuildTree(defs, ref i, def.Indent + 1)));
        }
        return list;
    }

    private static void Flatten(List<Node> nodes, List<SkillCategoryDef> outp,
                                StringComparer cmp, bool sort)
    {
        foreach (var n in sort ? nodes.OrderBy(x => DisplayName(x.Def.Label), cmp) : nodes.AsEnumerable())
        {
            outp.Add(n.Def);
            Flatten(n.Children, outp, cmp, sort: !KeepChildOrder.Contains(n.Def.Label));
        }
    }

    // ── 4. Reconnaissance d'une catégorie dans la recherche ───────────────────

    /// <summary>Longueur minimale de la saisie pour que les CATÉGORIES entrent en jeu. La recherche
    /// par nom, elle, démarre dès le 1er caractère : « ec » doit rester « ec » dans les noms, pas
    /// tout « Echo » (décision 13).</summary>
    public const int MinCategoryQueryLength = 3;

    // Synonymes de ce que la règle « préfixe d'un mot du libellé » ne peut pas deviner : les formes
    // fléchies inverses (« blindness » n'est pas un préfixe de « Blind ») et le vocabulaire de
    // joueur. Recherche = LANGUE COURANTE uniquement (la règle bilingue a été refusée).
    private static readonly Dictionary<string, string[]> _aliasesEn = new(StringComparer.Ordinal)
    {
        ["Blind"]                  = ["blindness", "blinded"],
        ["Disease"]                = ["diseased"],
        ["Weakness"]               = ["weakened", "weaken"],
        ["Poison"]                 = ["poisoned"],
        ["Burning"]                = ["burned", "burnt"],
        ["Maintained Enchantment"] = ["upkeep"],
        ["Half Range"]             = ["halfrange"],
        [ConditionsHeaderLabel]    = ["condi", "condis", "condition", "conditions", "inflict",
                                      "inflicting", "apply"],
        [CondRemovalLabel]         = ["condition", "conditions", "removal", "remove", "cleanse",
                                      "cure"],
        [ConditionsFamilyLabel]    = ["condi", "condis"],
        [EnergyFamilyLabel]        = ["energy", "mana"],
        [HealthFamilyLabel]        = ["health", "healing", "life", "hp"],
        // « Knock-down » se découpe en [knock, down] : « knockdown » d'un seul tenant ne matcherait pas.
        [KnockdownLabel]           = ["knockdown", "kd"],
        [SnareLabel]               = ["slow", "movement", "speed", "kite"],
        [SpeedBoostLabel]          = ["ims", "increased", "movement", "faster", "haste", "run"],
        [AtkSpeedUpLabel]          = ["ias"],
        [AtkSpeedDownLabel]        = ["das"],
        [HealingLabel]             = ["heals", "healer"],
        [HealthGainLabel]          = ["hp"],
        [LifeStealLabel]           = ["vampiric", "leech", "drain"],
        [RegenLabel]               = ["pips"],
        [DegenLabel]               = ["pips", "degen"],
        [EnergyRegenLabel]         = ["pips"],
        [EnergyDegenLabel]         = ["pips", "degen"],
        [EnergyStealLabel]         = ["esteal", "denial"],
        [EnergyCostDownLabel]      = ["cheaper", "cost"],
        [EnergyCostUpLabel]        = ["cost"],
        [MaxEnergyLabel]           = ["max"],
        [SignetRelatedLabel]       = ["signets", "related"],
        [MinionAnimationLabel]     = ["minions", "animate", "undead"],
        [MinionRelatedLabel]       = ["minions", "undead", "related"],
        [SummonCreatureLabel]      = ["summons", "summoning", "creature", "creatures", "spawn"],
        [SpiritRelatedLabel]       = ["spirits", "related"],
        [BindingRitualRelLabel]    = ["binding", "rituals", "related"],
        [SummonRelatedLabel]       = ["summons", "creatures", "related"],
        [AntiSummonLabel]          = ["anti", "summons", "counter"],
    };

    private static readonly Dictionary<string, string[]> _aliasesFr = new(StringComparer.Ordinal)
    {
        ["Blind"]                  = ["aveugle", "aveugles"],
        ["Crippled"]               = ["infirme"],
        ["Dazed"]                  = ["etourdi", "hebete"],
        ["Weakness"]               = ["affaiblissement", "faiblesse"],
        ["Burning"]                = ["brule", "feu"],
        ["Maintained Enchantment"] = ["maintien", "maintenu", "maintenus", "upkeep"],
        ["Touch"]                  = ["contact", "toucher"],
        ["Half Range"]             = ["demi", "moitie"],
        // Le libellé FR (« À portée de voix ») donne déjà « portee » et « voix » ; reste le terme
        // anglais, que les joueurs francophones emploient tel quel.
        ["Earshot"]                = ["earshot"],
        // « sceaux » explicite : Words() ne retire que le « s » final, jamais le « x » du pluriel.
        ["Signet"]                 = ["sceau", "sceaux"],
        [ConditionsHeaderLabel]    = ["condi", "condis", "alteration", "alterations", "condition",
                                      "conditions", "infliger", "inflige"],
        [CondRemovalLabel]         = ["condition", "conditions", "alteration", "retrait", "retirer",
                                      "enlever", "nettoyer"],
        [ConditionsFamilyLabel]    = ["condi", "condis", "alteration", "alterations"],
        [EnergyFamilyLabel]        = ["energie", "mana"],
        [HealthFamilyLabel]        = ["vie", "soin", "sante", "pv"],
        [KnockdownLabel]           = ["knockdown", "knock", "kd", "chute", "terre"],
        [SnareLabel]               = ["entrave", "ralentissement", "ralenti", "vitesse",
                                      "deplacement", "slow"],
        [SpeedBoostLabel]          = ["ims", "vitesse", "deplacement", "rapide", "course",
                                      "speed", "boost", "haste"],
        [AtkSpeedUpLabel]          = ["ias", "cadence"],
        [AtkSpeedDownLabel]        = ["das", "lente"],
        [HealingLabel]             = ["heal", "healing", "soigne", "soigner", "guerison"],
        [HealthGainLabel]          = ["health", "gain", "sante", "hp"],
        [LifeStealLabel]           = ["vampirique", "sangsue", "drain", "steal", "life"],
        [RegenLabel]               = ["pips", "regen"],
        [DegenLabel]               = ["pips", "degen"],
        [EnergyRegenLabel]         = ["pips", "regen"],
        [EnergyDegenLabel]         = ["pips", "degen"],
        [EnergyGainLabel]          = ["energy"],
        [EnergyLossLabel]          = ["energy", "denial"],
        [EnergyStealLabel]         = ["energy", "steal"],
        [EnergyCostUpLabel]        = ["energy"],
        [EnergyCostDownLabel]      = ["energy", "moins"],
        [MaxEnergyLabel]           = ["energy", "max"],
        [SignetRelatedLabel]       = ["signet", "sceau", "lie", "liee"],
        [MinionAnimationLabel]     = ["minion", "animer", "mort", "vivant", "squelette"],
        [MinionRelatedLabel]       = ["minion", "mort", "vivant", "lie", "liee"],
        [SummonCreatureLabel]      = ["invoque", "invocations", "creature", "convoque", "cree"],
        [SpiritRelatedLabel]       = ["esprit", "spirit", "lie", "liee"],
        [BindingRitualRelLabel]    = ["rituel", "asservissement", "lie", "liee"],
        [SummonRelatedLabel]       = ["invocation", "creature", "invoquee", "lie", "liee"],
        [AntiSummonLabel]          = ["anti", "invocation", "contre", "creature"],
    };

    /// <summary>
    /// Catégories des deux colonnes reconnues dans une saisie. Règle : chaque MOT de la saisie doit
    /// être préfixe d'un mot du libellé (ou d'un alias) — « flash ench » → « Flash Enchantment
    /// Spell ». Plusieurs catégories peuvent matcher : l'appelant en fait l'UNION.
    /// Les intertitres et les entrées méta « All … » ne sont jamais reconnus.
    /// </summary>
    public static IReadOnlyList<SkillCategoryDef> Recognize(string? query)
    {
        var words = Words(query);
        if (words.Length == 0) return [];
        if ((query ?? string.Empty).Trim().Length < MinCategoryQueryLength) return [];

        var hits = new List<SkillCategoryDef>();
        foreach (var def in TypeDefs.Concat(MechanicDefs))
        {
            if (def.Label == AllTypesLabel || def.Label == AllMechanicsLabel) continue;
            if (MatchesLabel(def.Label, words)) hits.Add(def);
        }
        return hits;
    }

    private static bool MatchesLabel(string label, string[] queryWords)
    {
        var aliases = AppLanguage.IsFr ? _aliasesFr : _aliasesEn;
        var candidates = new List<string>(Words(DisplayName(label)));
        if (aliases.TryGetValue(label, out var extra))
            foreach (var a in extra) candidates.AddRange(Words(a));

        return queryWords.All(q => candidates.Any(c => c.StartsWith(q, StringComparison.Ordinal)));
    }

    // Normalisation : minuscules, accents retirés, apostrophes et tirets = séparateurs, pluriel
    // simple (« s » final) retiré. « Sorts d'enchantement » → [sort, d, enchantement].
    private static string[] Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else sb.Append(' ');
        }

        return sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 1 && w[^1] == 's' ? w[..^1] : w)
            .ToArray();
    }
}
