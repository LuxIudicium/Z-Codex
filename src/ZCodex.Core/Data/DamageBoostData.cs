using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Ce qu'un effet actif fait aux dégâts (chantier infobulle, lot 6a).</summary>
public enum DamageBoostKind
{
    /// <summary>Paquet de dégâts ajouté à l'attaque → ligne « bonus d'effets » de la table (option B
    /// de la maquette, Q2 du cadrage).</summary>
    Damage,
    /// <summary>Chiffre RELEVÉ DANS LE TEXTE de la description de la compétence touchée (Q12) : les deux
    /// seuls cas du lot sont les attaques du familier et celles des esprits, dont aucun paquet n'est un
    /// bonus de l'attaque du perso.</summary>
    TextDamage,
    /// <summary>Chance de coup critique en points de pourcentage, ajoutée au taux affiché (Q7).</summary>
    CriticalChance,
    /// <summary>Pénétration d'armure de BASE : NON cumulable, seul le MAX compte (Q8).</summary>
    BasePenetration,
    /// <summary>Pénétration d'armure en BONUS (« adds +X% ») : se CUMULE par-dessus le max de base
    /// (Q8). Clairvoyance du juge reçue, et le mod d'arme « de fractionnement ».</summary>
    BonusPenetration,
    /// <summary>Multiplicateur en POINTS DE POURCENTAGE, appliqué à la valeur de BASE avant l'armure
    /// (Q10) : +25 pour Vengeance, −30 pour l'Affinité vitale. Plusieurs multiplicateurs se
    /// composent (×1,25 puis ×0,70).</summary>
    Multiplier,
    /// <summary>
    /// Multiplicateur qui ne porte QUE sur les dégâts de l'ARME équipée — la part « 7–17 » de la dague,
    /// pas les bonus de la compétence, pas ses paquets typés, pas les bonus des autres effets. Le seul cas
    /// de toute la base est la Rafale (« You do 25% less damage »), et sa mécanique est bien celle-là :
    /// tous les autres multiplicateurs du chantier (Vengeance, Affinité vitale, Ural) portent sur TOUT
    /// (règle de Philippe, § 6.11 du plan).
    ///
    /// ⚠ Il vit donc dans un champ SÉPARÉ de <see cref="Multiplier"/> dans <see cref="DamageBoosts"/> :
    /// les mélanger reviendrait à faire baisser le « +20 » d'un Coup de taille, que la Rafale ne touche pas.
    /// </summary>
    WeaponMultiplier,
    /// <summary>
    /// VOL DE VIE conféré aux attaques par un effet (Ordre du vampire, Arme du tourment). Sa propre
    /// ligne sous la table : ce ne sont pas des dégâts, donc ni colonne d'AL, ni multiplicateur
    /// (Vengeance, Ural), ni flux — le vol de vie est hors de tout ça, règle déjà en vigueur.
    ///
    /// ⚠ Il ne contredit PAS la décision du chantier 10 (« aucun vol de vie dans l'infobulle »), qui
    /// vaut toujours pour le vol de vie qu'une compétence fait ELLE-MÊME : celui-là est déjà écrit en
    /// clair dans sa description, l'afficher deux fois serait une redite. Celui-ci n'est écrit nulle
    /// part sur la compétence survolée — même raisonnement que la ligne « bonus d'effets ».
    /// (Arbitrage de Philippe, 27/09/2026.)
    /// </summary>
    LifeSteal,
}

/// <summary>Qui l'effet touche. Les périmètres d'ARME réutilisent <see cref="ConditionWeaponScope"/>
/// (lot 4b) pour ne pas re-coder « vos flèches » ni « en mêlée » une seconde fois.</summary>
public enum DamageBoostScope
{
    /// <summary>« Your attacks » : toute attaque d'ARME du perso. Exclut le familier (glossaire G1 :
    /// Preneur d'Âmes ne touche que les attaques du LANCEUR) et les esprits.</summary>
    Attacks,
    /// <summary>« Your arrows » / « your bow attack skills ». Dans l'infobulle les deux désignent le même
    /// ensemble (glossaire G4) : les 48 compétences d'attaque à l'arc tirent toutes une flèche.</summary>
    BowAttacks,
    /// <summary>« In melee » (glossaire G2).</summary>
    MeleeAttacks,
    /// <summary>« While wielding daggers » (Méthode de l'Assassin).</summary>
    DaggerAttacks,
    /// <summary>« When you hit with a scythe » (Aura de Grenth, lot 6c-3). Le périmètre de la FAUX existait
    /// déjà au lot 4b (<see cref="ConditionWeaponScope.Scythe"/>, l'Avatar de Grenth qui convertit) : rien à
    /// re-coder. ⚠ Ne pas confondre les deux compétences, l'Aura et l'Avatar.</summary>
    ScytheAttacks,
    /// <summary>« While holding a non-dagger weapon » (Méthode du Maître) : l'exact complément de
    /// <see cref="DaggerAttacks"/> — les deux ne peuvent donc jamais agir sur la même attaque.</summary>
    NonDaggerAttacks,
    /// <summary>Attaques du familier (Agression barbare).</summary>
    PetAttacks,
    /// <summary>Attaques des esprits contrôlés (Sceau de puissance spectrale, Aura de sangsue de l'esprit).
    /// ⚠ « Attaquer » couvre les DEUX formes : infliger des dégâts, ou VOLER DE LA VIE — trois esprits ne
    /// font que la seconde (cf. <see cref="DamageBoostData.AttacksAsSpirit"/>).</summary>
    SpiritAttacks,
    /// <summary>« Your Ritualist skills » (Glaive était destructrice, Daoshen était cruel).</summary>
    RitualistSkills,
    /// <summary>TOUS les dégâts que le perso inflige, sorts compris (Vengeance : « this ally deals
    /// 25% more damage »). ⚠ Vol de vie et perte de vie sèche en sont exclus — ce ne sont pas des
    /// dégâts (<see cref="SkillDamage.RowKind"/>), décision déjà prise pour la fenêtre Spike. Le
    /// familier et les esprits en sont exclus aussi : l'effet enchante le perso, pas ses créatures
    /// (glossaire G1).</summary>
    AllDamage,
    /// <summary>
    /// Tout paquet de dégâts SOUMIS À L'ARMURE, quelle que soit la façon dont il arrive — immédiat, effet
    /// de fin, piège, tic périodique (les 4 ambiguïtés tranchées par Philippe le 26/09/2026). Périmètre de
    /// l'Étendard d'honneur, le seul effet du chantier qui déborde des attaques.
    ///
    /// ⚠ Le tri se fait sur une donnée qui existait DÉJÀ : <see cref="SkillDamage.Row.IgnoresArmor"/> et
    /// <see cref="SkillDamage.RowKind"/>. En sortent donc tout seuls les paquets qui ignorent l'armure
    /// (Flamme d'obsidienne, les conjurations, l'Arme du Grand Nain…), le vol de vie et la perte de vie
    /// sèche. C'est aussi ce qui écarte les 3 exemples que Philippe cite comme « déclenchés par une action
    /// de l'ENNEMI » — Barbelés, Esprit malveillant, Marque de douleur annoncent tous les trois des dégâts
    /// SANS TYPE, donc qui ignorent l'armure : rien à coder pour eux (vérifié au harnais, pas supposé).
    /// Seule exception relevée par le balayage : <see cref="_enemyTriggeredDamage"/>.
    /// </summary>
    ArmorRespectingDamage,
}

/// <summary>
/// Un effet actif du perso qui modifie les dégâts d'autres compétences (lot 6a). La valeur n'est PAS
/// recopiée ici : elle se lit dans la progression de la compétence, à l'index
/// <paramref name="Index"/> — tous SONDÉS dans la base réelle (§ 6.5 du plan), aucun deviné à l'œil,
/// et l'ordre des lignes de progression ne suit PAS celui de la description (« Craignez-moi ! » porte
/// son critique à l'index 2 alors que le texte le cite en second). <paramref name="Fixed"/> &gt; 0 =
/// valeur littérale de la description, sans progression (Sceau de force +5, les pénétrations).
/// <paramref name="RequiresElement"/> = l'effet exige que l'arme inflige ce type de dégâts (les 3
/// conjurations et l'Aura de poussière d'ébène) : le § 6.1 du plan dit comment on en juge.
/// <paramref name="BaseSkillId"/> = id PvE d'une variante « (PvP) » : l'icône est mémorisée sur l'id
/// de base. <paramref name="Received"/> = effet REÇU D'UN ALLIÉ (lot 6b) : la compétence n'est pas sur
/// la barre du perso qui en profite, donc son icône ne sort pas du balayage « équipée » et son rang se
/// lit chez le LANCEUR le plus fort de l'équipe. <paramref name="Malus"/> = l'effet RETIRE des dégâts
/// (Affinité vitale, Arme du tourment) : même descripteur, valeur négative.
/// <paramref name="LostWhenEnchanted"/> = « no effect while target ally is enchanted » (Arme brute).
/// <paramref name="LostWhenNecroEnchanted"/> = version ÉTROITE de la précédente : « party members under
/// another Necromancer enchantment are not affected » (Ordre du vampire). En pratique, l'Ordre de la
/// douleur et la Fureur noire l'annulent — les deux sont des enchantements de Nécromant du bandeau.
/// <paramref name="Band"/> = effet du BANDEAU D'ÉQUIPE (lot 6c) : son icône vit dans le bandeau et non
/// sur la carte d'un perso, donc il ne passe ni par le balayage « équipée » ni par les effets reçus — c'est
/// l'état du bandeau qui l'allume, pour TOUS les persos à la fois.
/// <paramref name="RequiresPhysical"/> = le bonus ne vaut que tant que les dégâts sont encore PHYSIQUES
/// (glossaire G3) : il SAUTE dès qu'un convertisseur agit, et il doit le dire là où le chiffre manque.
/// <paramref name="CannotSelfTarget"/> = son lanceur ne peut jamais en profiter : un perso seul qui la
/// porte n'a donc pas d'icône du tout (patron de l'Arme du Grand Nain, lot 4c). La caractéristique d'échelle n'est jamais listée : c'est TOUJOURS celle de la compétence
/// source elle-même, lue par l'appelant (donc la substitution du lot 5 s'y applique gratuitement).
/// </summary>
public sealed record DamageBoostDescriptor(
    int SkillId, DamageBoostScope Scope, DamageBoostKind Kind,
    int Index = -1, int Fixed = 0, string? DamageType = null, bool IsBonus = true,
    string? RequiresElement = null, int BaseSkillId = 0,
    bool Received = false, bool Malus = false, bool LostWhenEnchanted = false,
    bool CannotSelfTarget = false, bool Band = false, bool RequiresPhysical = false,
    bool LostWhenNecroEnchanted = false)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

/// <summary>
/// Pourquoi un effet ALLUMÉ ne fait rien sur CETTE compétence. Sans ça, son chiffre disparaît de la
/// table sans un mot — l'explication vivait dans l'infobulle de l'ICÔNE, c'est-à-dire ailleurs que là
/// où l'utilisateur regarde (relevé par Philippe à la QA du lot 6b).
/// </summary>
public enum DamageBoostSuppression
{
    /// <summary>Arme brute : « no effect while target ally is enchanted » (lot 6b, Q4).</summary>
    Enchanted,
    /// <summary>Conjuration ou Aura de poussière d'ébène : l'arme n'inflige pas ce type de dégâts
    /// (§ 6.1 du plan).</summary>
    WrongElement,
    /// <summary>Préparation annulée : Tir de barrage et Volée les retirent avant de frapper.</summary>
    PreparationRemoved,
    /// <summary>Bonus « aux dégâts physiques » perdu parce que l'arme a été convertie (glossaire G3) :
    /// Vannage, et l'Ordre de la douleur au 6c-2.</summary>
    NoLongerPhysical,
    /// <summary>Ordre du vampire : « party members under another Necromancer enchantment are not
    /// affected ». Version étroite de <see cref="Enchanted"/> — seuls les enchantements de NÉCROMANT
    /// comptent, et en pratique ce sont l'Ordre de la douleur et la Fureur noire.</summary>
    NecromancerEnchanted,
}

/// <summary>
/// Phrase « vos attaques infligent des dégâts de X » de l'infobulle. <paramref name="Pet"/> = elle parle
/// des attaques du FAMILIER et non de celles du perso. <paramref name="Natural"/> = c'est le type d'origine,
/// que rien n'a converti : l'infobulle le dit quand même pour le familier (son type dépend de l'espèce et
/// n'apparaît nulle part ailleurs), jamais pour le perso (le type vient de son arme, qu'il a choisie).
/// </summary>
public readonly record struct DamageTypeNote(string Type, bool Pet, bool Natural);

/// <summary>Un effet allumé qui ne s'applique pas ici, et pourquoi. <paramref name="SkillName"/> est
/// déjà dans la langue affichée.</summary>
public readonly record struct SuppressedBoost(string SkillName, DamageBoostSuppression Reason);

/// <summary>Un paquet de dégâts ajouté par un effet actif : <paramref name="IgnoresArmor"/> suit la
/// règle maison (un bonus « +X » ignore l'armure, un paquet typé sans « + » la subit), donc les
/// conjurations rejoignent la ligne « bonus d'effets » sans colonne typée (Q3) tandis que les Flèches
/// de feu, seul paquet du lot sans « + », varient bien d'une colonne d'AL à l'autre.</summary>
public readonly record struct DamageBoostPacket(int Value, string? DamageType, bool IgnoresArmor);

/// <summary>
/// Ce que les effets actifs du perso font aux dégâts de UNE compétence (lot 6a).
/// <paramref name="Packets"/> = la ligne « bonus d'effets » ; <paramref name="CriticalPercent"/> =
/// points de pourcentage à ajouter au taux de critique affiché (plafonné à 100 par l'affichage, Q7) ;
/// <paramref name="BasePenetration"/> = pénétration de BASE la plus forte apportée par un effet (elle
/// entre en MAX avec celle de la description et le rang de Force) ; <paramref name="BonusPenetration"/>
/// = pénétration en BONUS, qui se CUMULE par-dessus le max de base (mod d'arme « de fractionnement »).
/// <paramref name="ElementalToCold"/> = Hiver est posé : tout paquet ÉLÉMENTAIRE s'affiche en froid. C'est
/// une simple ÉTIQUETTE — aucun chiffre ne bouge, et surtout ça ne déclenche aucune conjuration (§ 6.1).
/// <paramref name="StoneStriker"/> = Briseur de pierre est allumé sur ce perso : tout paquet élémentaire ou
/// physique s'affiche en TERRE, **sorts compris**, et il garde le dernier mot sur Hiver.
/// </summary>
public readonly record struct DamageBoosts(
    IReadOnlyList<DamageBoostPacket>? Packets = null,
    int CriticalPercent = 0,
    int BasePenetration = 0,
    int BonusPenetration = 0,
    double MultiplierOffset = 0,
    IReadOnlyList<SuppressedBoost>? Suppressed = null,
    bool ElementalToCold = false,
    bool StoneStriker = false,
    DamageTypeNote? TypeNote = null,
    int LifeSteal = 0,
    double WeaponMultiplierOffset = 0)
{
    /// <summary>
    /// Multiplicateur de dégâts reçu (Vengeance ×1,25, Affinité vitale ×0,70).
    ///
    /// ⚠ Il est stocké en ÉCART À 1 (<see cref="MultiplierOffset"/>), et surtout pas comme un facteur
    /// valant 1 au repos : la valeur par défaut d'un paramètre de constructeur primaire ne s'applique
    /// PAS à <c>default(DamageBoosts)</c>, qui met tous les champs à zéro. Un facteur « par défaut 1 »
    /// vaudrait donc 0 pour la valeur par défaut de la propriété de dépendance de l'infobulle
    /// (<c>PropertyMetadata(default(DamageBoosts), …)</c>) et METTRAIT TOUS LES DÉGÂTS À ZÉRO, sans
    /// erreur ni build rouge. Relevé par le harnais du lot 6b.
    /// </summary>
    public double Multiplier => 1.0 + MultiplierOffset;

    /// <summary>
    /// Multiplicateur qui ne touche QUE les dégâts de l'arme équipée (Rafale ×0,75). Stocké en écart à 1
    /// pour la même raison que <see cref="Multiplier"/> — voir le piège expliqué juste au-dessus.
    ///
    /// ⚠ Il n'entre NI dans <see cref="TotalAt"/> (les bonus d'effets ne sont pas des dégâts d'arme), NI
    /// sur les paquets propres de la compétence, NI sur son « +X » absorbé. Seule la table d'arme le voit.
    /// </summary>
    public double WeaponMultiplier => 1.0 + WeaponMultiplierOffset;

    /// <summary>Au moins un effet à afficher — sinon l'infobulle n'ajoute ni ligne ni chiffre.</summary>
    public bool Any => Packets is { Count: > 0 } || CriticalPercent > 0
                       || BasePenetration > 0 || BonusPenetration > 0 || HasMultiplier
                       || Suppressed is { Count: > 0 } || TypeNote is not null || LifeSteal > 0
                       || HasWeaponMultiplier;

    /// <summary>Un multiplicateur de dégâts est-il en jeu ? Comparaison par écart, pas par égalité de
    /// doubles.</summary>
    public bool HasMultiplier => Math.Abs(MultiplierOffset) > 0.0001;

    /// <summary>Un multiplicateur de dégâts d'ARME est-il en jeu (Rafale) ?</summary>
    public bool HasWeaponMultiplier => Math.Abs(WeaponMultiplierOffset) > 0.0001;

    /// <summary>Somme des paquets contre une armure donnée : les paquets qui ignorent l'armure passent
    /// tels quels, les autres par la formule de dégâts. Un seul chiffre par colonne d'AL, comme la
    /// maquette (option B). Le multiplicateur s'applique à la valeur de BASE, avant l'armure (Q10),
    /// et un seul arrondi tombe à la fin — comme <see cref="WeaponStrike.DamageAt"/>.</summary>
    public int TotalAt(int armorLevel, int armorPenetration, int casterLevel)
    {
        int total = 0;
        foreach (var p in Packets ?? [])
            total += p.IgnoresArmor ? (int)Math.Floor(p.Value * Multiplier)
                   : SkillDamage.DamageAt(p.Value, armorLevel, armorPenetration, casterLevel, Multiplier);
        return total;
    }
}

/// <summary>
/// Les sources de bonus de dégâts portées par la CARTE DU PERSO (lot 6a du chantier infobulle,
/// recensement du § 6.2 du plan). Les effets reçus d'un allié (lot 6b), le bandeau d'équipe (lot 6c)
/// et la fenêtre Spike (lot 6d) viendront s'ajouter ici.
///
/// ⚠ Les trois noms français en « fractionnement / fragmentation » désignent trois choses
/// différentes (§ 6.4 du plan) : le mod d'arme Sundering (ici, pénétration en BONUS), le sort d'arme
/// Sundering Weapon 2148 (pénétration de BASE, lot 6b) et Splinter Weapon 792 (écartée du chantier).
/// </summary>
public static class DamageBoostData
{
    public const int ConjureFlameSkillId     = 182;
    public const int ConjureFrostSkillId     = 207;
    public const int ConjureLightningSkillId = 221;
    public const int IgniteArrowsSkillId     = 431;
    public const int ReadTheWindSkillId      = 432;
    public const int ChokingGasSkillId       = 434;
    public const int FeralAggressionSkillId  = 2142;
    public const int GhostlyMightSkillId     = 1742;
    public const int GhostlyMightPvpSkillId  = 2966;
    public const int FearMeSkillId           = 366;
    public const int SiphonStrengthSkillId   = 827;
    // ── Lot 6b : effets de dégâts REÇUS D'UN ALLIÉ ────────────────────────────
    public const int StrengthOfHonorSkillId    = 243;
    public const int StrengthOfHonorPvpSkillId = 2999;
    public const int BrutalWeaponSkillId       = 1258;
    public const int GreatDwarfWeaponSkillId   = 2219;
    public const int FindTheirWeaknessSkillId  = 1781;
    public const int VengeanceSkillId          = 315;
    public const int LifeAttunementSkillId     = 244;
    public const int NightmareWeaponSkillId    = 795;
    // ── Lot 6c : effets du BANDEAU D'ÉQUIPE ───────────────────────────────────
    public const int FavorableWindsSkillId = 472;
    public const int WinnowingSkillId      = 463;
    public const int WinterSkillId         = 462;
    // Lot 6c-2 : les 5 effets PORTÉS du bandeau, plus « Par le marteau d'Ural ! » qui, lui, est un effet
    // REÇU (cf. son descripteur). Ids et index de progression relevés dans la base réelle le 27/09/2026.
    public const int OrderOfPainSkillId        = 134;
    public const int AnthemOfEnvySkillId       = 1559;
    public const int AnthemOfEnvyPvpSkillId    = 3148;
    public const int GoForTheEyesSkillId       = 1558;
    public const int GoForTheEyesPvpSkillId    = 3026;
    public const int TogetherAsOneSkillId      = 3427;
    public const int EbonStandardOfHonorSkillId = 2233;
    public const int UralsHammerSkillId        = 2217;
    /// <summary>Ordre du vampire : le seul effet du bandeau qui confère du VOL DE VIE (lot 6c-2b).</summary>
    public const int OrderOfTheVampireSkillId  = 148;
    // ── Lot 6c-3 : les trois trous HORS BANDEAU ───────────────────────────────
    /// <summary>Rafale : le seul multiplicateur de toute la base qui ne porte que sur l'ARME.</summary>
    public const int FlurrySkillId          = 344;
    /// <summary>Aura de sangsue de l'esprit : le miroir du Sceau de puissance spectrale, en négatif.</summary>
    public const int SpiritleechAuraSkillId = 2203;
    /// <summary>Aura de Grenth : dernier trou du lot 6a, ni son malus ni son vol de vie n'étaient lus.</summary>
    public const int GrenthsAuraSkillId     = 2013;

    /// <summary>Id d'icône du mod d'arme « de fractionnement » : ce n'est pas une compétence, donc un id
    /// réservé négatif, comme le mod « Furieux » du lot 1a (qui occupe −1).</summary>
    public const int SunderingModToggleId = -2;

    public static readonly IReadOnlyList<DamageBoostDescriptor> All = new DamageBoostDescriptor[]
    {
        // ── Paquets de dégâts ajoutés aux attaques du perso ───────────────────
        // Conjurations : « Your attacks hit for +X <élément> damage. No effect unless your weapon deals
        // <élément> damage. » Le bonus est un « +X » → il ignore l'armure (Q3), pas de colonne typée.
        new(ConjureFlameSkillId,     DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 0, DamageType: "fire",      RequiresElement: "fire"),
        new(ConjureFrostSkillId,     DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 0, DamageType: "cold",      RequiresElement: "cold"),
        new(ConjureLightningSkillId, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 0, DamageType: "lightning", RequiresElement: "lightning"),
        // Flèches enflammées (Kindle Arrows) : la SEULE qui convertit les flèches en feu — elle est donc
        // aussi dans ConditionDurationData.Converters, et c'est elle qui fait passer une Conjuration de
        // flamme. ⚠ Ne pas la confondre avec les Flèches de feu (glossaire G5).
        new(433,  DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 0, DamageType: "fire"),
        // Flèches de feu (Ignite Arrows) : « Your arrows deal X fire damage to target and foes adjacent »
        // — pas de « + », donc le SEUL paquet du lot qui SUBIT l'armure. Elle ne convertit rien.
        new(IgniteArrowsSkillId, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 0, DamageType: "fire", IsBonus: false),
        new(1199, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),                       // Flèches de verre
        new(3145, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1, BaseSkillId: 1199),    // Flèches de verre (PvP)
        new(2145, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),                       // Concentration experte
        new(429,  DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),                       // Flèches de Melandru (cible enchantée : Q9)
        // ⚠ Ces deux-là avaient été MANQUÉES par le recensement du § 6.2, parce qu'elles annoncent leur bonus
        // dans une phrase NUE (« +3…9…10 damage. ») et non « your arrows deal +X ». Trouvées le 24/09/2026 en
        // balayant la famille « préparation » en entier, sur demande de Philippe.
        new(ReadTheWindSkillId, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 0),         // Lecture du vent
        new(ChokingGasSkillId,  DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),         // Gaz suffocant (l'index 0 est sa durée)
        // ⚠ Lecture du vent (PvP) (2969) n'est VOLONTAIREMENT pas ici : sa variante a PERDU les dégâts, il ne
        // lui reste que la vitesse de projectile. Cas d'école de la divergence des variantes « (PvP) » — lui
        // donner un BaseSkillId lui collerait une icône qui ne ferait rien.
        // Preneur d'Âmes : « Attacks deal +X damage » = les attaques du LANCEUR seul (glossaire G1).
        // ⚠ L'index 2 porte le SACRIFICE, aux chiffres identiques : sonde obligatoire.
        new(3423, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // Preneur d'Âmes (PvE)
        new(1736, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // Force de l'esprit (sous sort d'arme : Q9)
        new(1760, DamageBoostScope.MeleeAttacks, DamageBoostKind.Damage, Index: 0, DamageType: "earth", RequiresElement: "earth"), // Aura de poussière d'ébène
        new(944,  DamageBoostScope.Attacks,     DamageBoostKind.Damage, Fixed: 5),                       // Sceau de force (+5 littéral, charges non comptées)
        new(2355, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // « Je suis le plus fort ! » (PvE)
        new(2354, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // « Esquive ceci ! » (PvE)

        // ── Chiffres relevés DANS LE TEXTE de la compétence touchée (Q12) ─────
        new(FeralAggressionSkillId, DamageBoostScope.PetAttacks,    DamageBoostKind.TextDamage, Index: 1),
        new(GhostlyMightSkillId,    DamageBoostScope.SpiritAttacks, DamageBoostKind.TextDamage, Index: 1),
        // ⚠ Mécanique DIVERGENTE en PvP (cas d'école des variantes « (PvP) ») : le core donne +5…9…10 à
        // TOUS les esprits contrôlés, la PvP +5…29…35 à UNE SEULE créature invoquée alliée, détruite au
        // bout de 10 s. Décision du 24/09/2026 : on l'applique quand même à tous les esprits, et la note
        // d'icône dit qu'un seul en profite en jeu — même idiome que « icône allumée = condition remplie ».
        new(GhostlyMightPvpSkillId, DamageBoostScope.SpiritAttacks, DamageBoostKind.TextDamage, Index: 0, BaseSkillId: GhostlyMightSkillId),

        // ── Critique ─────────────────────────────────────────────────────────
        // Recensement CLOS le 24/09/2026 : balayage des 1517 descriptions sur « chance … critical »,
        // 5 sources en tout dont 4 sur la carte du perso (la 5e, « Visez les yeux ! », est au bandeau →
        // lot 6c). ⚠ Elles s'ADDITIONNENT (Q7 : « s'ajoute tel quel au taux affiché »), contrairement aux
        // pénétrations de base — seul l'AFFICHAGE plafonne à 100 %.
        // « Craignez-moi ! » : en mêlée contre un ennemi IMMOBILE (Q9 : icône allumée = il l'est).
        // ⚠ Index 2, pas 0 ni 1.
        new(FearMeSkillId, DamageBoostScope.MeleeAttacks, DamageBoostKind.CriticalChance, Index: 2),
        new(1018, DamageBoostScope.Attacks,         DamageBoostKind.CriticalChance, Index: 1),  // Œil critique : toutes armes
        new(1649, DamageBoostScope.DaggerAttacks,   DamageBoostKind.CriticalChance, Index: 1),  // Méthode de l'Assassin : dagues
        new(2187, DamageBoostScope.NonDaggerAttacks, DamageBoostKind.CriticalChance, Index: 0), // Méthode du Maître : hors dagues
        // Siphon de force : maléfice posé sur l'ennemi, mais c'est NOTRE taux de critique qui monte, et
        // seulement contre cet ennemi-là (Q9 : icône allumée = c'est bien lui qu'on frappe). +50 littéral.
        new(SiphonStrengthSkillId, DamageBoostScope.Attacks, DamageBoostKind.CriticalChance, Fixed: 50),

        // ── Pénétration d'armure de BASE (non cumulable, seul le max compte) ──
        new(1732, DamageBoostScope.RitualistSkills, DamageBoostKind.BasePenetration, Fixed: 20),                     // Glaive était destructrice
        new(3157, DamageBoostScope.RitualistSkills, DamageBoostKind.BasePenetration, Fixed: 10, BaseSkillId: 1732),  // Glaive était destructrice (PvP)
        new(1218, DamageBoostScope.RitualistSkills, DamageBoostKind.BasePenetration, Fixed: 10),                     // Daoshen était cruel

        // ══ LOT 6b — effets reçus d'un allié (recensement CLOS le 26/09/2026) ══
        // Tous marqués Received : leur compétence n'est PAS sur la barre du perso qui en profite, donc
        // l'icône vient du balayage d'équipe et la valeur se lit au rang du LANCEUR le plus fort.
        //
        // ⚠ Les trois bonus plats ci-dessous IGNORENT l'armure (tranché par Philippe le 26/09/2026)
        // MALGRÉ leur description, qui est approximative : Force de l'honneur dit « deals X more
        // damage » sans « + », et l'Arme du Grand Nain dit « +X weapon damage » comme s'il s'agissait
        // d'un coup d'arme. Ce sont des bonus → IsBonus, comme les conjurations du 6a.
        new(StrengthOfHonorSkillId,    DamageBoostScope.MeleeAttacks, DamageBoostKind.Damage, Index: 0, Received: true),  // Force de l'honneur (en mêlée, glossaire G2)
        new(StrengthOfHonorPvpSkillId, DamageBoostScope.MeleeAttacks, DamageBoostKind.Damage, Index: 0, Received: true,
            BaseSkillId: StrengthOfHonorSkillId),                                                                        // Force de l'honneur (PvP) : +1…4…5
        // Arme brute : l'index 0 est sa DURÉE, le bonus est à l'index 1. « No effect while target ally
        // is enchanted » → LostWhenEnchanted (Q4 du cadrage 6b).
        new(BrutalWeaponSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 1, Received: true,
            LostWhenEnchanted: true),
        // ⚠ « Cannot self-target » : son porteur ne peut JAMAIS la recevoir (déjà la règle du lot 4c).
        new(GreatDwarfWeaponSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 0, Received: true,
            CannotSelfTarget: true),                                                                                     // Arme du Grand Nain (rang de titre Deldrimor)
        // « Trouvez leur faiblesse ! » : index 1 (l'index 0 est la durée du cri, l'index 2 celle de la
        // Blessure profonde). ⚠ La variante PvP (3034) n'est VOLONTAIREMENT pas ici : elle a PERDU ses
        // dégâts, il ne lui reste que la Blessure profonde — même cas que Lecture du vent (PvP) au 6a.
        new(FindTheirWeaknessSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 1, Received: true),
        // Vengeance : ×1,25 sur la valeur de BASE, avant l'armure, sur TOUS les paquets de dégâts, dégâts
        // d'arme compris (Q10). Le 25 % est un LITTÉRAL — la compétence n'a aucune progression.
        // ⚠ Même règle que celle DÉJÀ tranchée pour la fenêtre Spike (cf. SpikeWeaponBuffs) : le vol de
        // vie et la perte de vie sèche restent en dehors du multiplicateur.
        // ⚠ CannotSelfTarget sans que le texte le dise : elle RESSUSCITE sa cible, donc son lanceur —
        // vivant, puisqu'il incante — ne peut pas en être le bénéficiaire.
        new(VengeanceSkillId, DamageBoostScope.AllDamage, DamageBoostKind.Multiplier, Fixed: 25, Received: true,
            CannotSelfTarget: true),
        // Clairvoyance du juge : +20 % de pénétration en BONUS (littéral), cumulée par-dessus le max de
        // base. Elle est DÉJÀ un convertisseur (lot 4b) : les deux rôles cohabitent sur la même icône.
        new(ConditionDurationData.JudgesInsightSkillId, DamageBoostScope.Attacks, DamageBoostKind.BonusPenetration,
            Fixed: 20, Received: true),
        // Arme de fractionnement : 10 % de pénétration de BASE (littéral, non cumulable). Elle porte
        // DÉJÀ l'armure brisée du lot 4b, sur la même icône.
        new(ConditionDurationData.SunderingWeaponSkillId, DamageBoostScope.Attacks, DamageBoostKind.BasePenetration,
            Fixed: 10, Received: true),

        // ── Les deux MALUS reçus (Q2 du cadrage 6b, validés le 26/09/2026) ────
        // Ils n'étaient dans AUCUN recensement : trouvés en balayant les sorts posables sur un allié.
        // Une infobulle qui ne montrerait que les bonus mentirait quand l'un des deux est actif.
        // Affinité vitale : « this ally deals 30% less damage with attacks » — 30 % LITTÉRAL (sa seule
        // colonne de progression porte le soin, pas le malus).
        new(LifeAttunementSkillId, DamageBoostScope.Attacks, DamageBoostKind.Multiplier, Fixed: 30,
            Received: true, Malus: true),
        // Arme du tourment : « attacks ... deal 10…42…50 less damage ». ⚠ Ses DEUX colonnes de
        // progression sont identiques à tous les rangs (le vol de vie et le malus suivent la même
        // échelle), donc l'index ne prête pas à conséquence — vérifié dans la base, pas supposé.
        new(NightmareWeaponSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 0,
            Received: true, Malus: true),
        // ⚠ L'Arme du tourment a DEUX moitiés, et le lot 6b n'en montrait qu'une : « Target ally's attacks
        // STEAL 10…42…50 Health but deal 10…42…50 less damage ». Le vol de vie arrive au 6c-2b (27/09/2026).
        // Même index, et surtout PAS de Malus : le vol de vie est un GAIN, il ne porte pas le signe négatif.
        new(NightmareWeaponSkillId, DamageBoostScope.Attacks, DamageBoostKind.LifeSteal, Index: 0,
            Received: true),

        // ── « Par le marteau d'Ural ! » (lot 6c-2) : un effet REÇU, pas un effet de bandeau ─────
        // ⚠ Il SORT du bandeau d'équipe où le recensement du § 6.2 l'avait mis (Q3, tranchée le
        // 26/09/2026) : le texte anglais ne donne le ×1,25…1,33 qu'aux RESSUSCITÉS, et le crieur, bien
        // vivant puisqu'il crie, n'en profite jamais. C'est donc le patron EXACT de Vengeance —
        // Received + CannotSelfTarget —, à un détail près : Ural a une PROGRESSION (rang de titre
        // Deldrimor, échelle 0→10), là où le 25 % de Vengeance est un littéral. Avec deux porteurs,
        // chacun l'a au rang du meilleur AUTRE porteur : c'est déjà ce que fait ReceivedDamageBoostsFor.
        // ⚠ Sa description FR est celle d'une AUTRE version de la compétence ; la base la marque déjà
        // FrSuspect, donc l'infobulle retombe sur l'anglais toute seule — rien à faire ici.
        new(UralsHammerSkillId, DamageBoostScope.AllDamage, DamageBoostKind.Multiplier, Index: 0,
            Received: true, CannotSelfTarget: true),

        // ══ LOT 6c-1 — les 3 esprits du bandeau d'équipe ══════════════════════
        // Recensement du bandeau CLOS par balayage de FAMILLE le 26/09/2026 (28 rituels de la nature,
        // 45 rituels d'asservissement, 13 sorts de protection, tous les cris/chants/échos, tout ce qui
        // enchante le groupe, les 14 « % de dégâts », les 33 pénétrations) : rien d'autre dans la base ne
        // modifie un chiffre d'équipe.
        //
        // Leurs deux chiffres sont des LITTÉRAUX : les progressions de ces trois compétences ne portent que
        // le niveau et la durée de vie de l'esprit (sondé dans la base réelle, pas lu à l'œil).
        //
        // Vents favorables : « Arrows […] hit for +6 damage for creatures in range ». Un familier ne tire
        // pas de flèches → rien à ajouter pour lui.
        new(FavorableWindsSkillId, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Fixed: 6, Band: true),
        // Vannage : « Increases physical damage by +4 for creatures in range ». ⚠ Il SAUTE dès que l'arme
        // est convertie (glossaire G3), comme l'Ordre de la douleur.
        new(WinnowingSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Fixed: 4, Band: true,
            RequiresPhysical: true),
        // ⚠ Le familier EST une « créature à portée » (règle de Philippe du 26/09 : créature et allié oui,
        // membre du groupe non) et il n'inflige que du physique → son chiffre monte aussi, relevé DANS LE
        // TEXTE de son attaque comme l'Agression barbare (Q12).
        new(WinnowingSkillId, DamageBoostScope.PetAttacks, DamageBoostKind.TextDamage, Fixed: 4, Band: true,
            RequiresPhysical: true),
        // Hiver (462) n'a PAS de descripteur : il ne porte aucun chiffre. Il ne fait que ré-étiqueter en
        // froid les dégâts élémentaires reçus, et surtout il ne déclenche pas les conjurations — voir le
        // commentaire de EffectiveElement plus bas.

        // ══ LOT 6c-2 — les 5 effets PORTÉS du bandeau ═════════════════════════
        // Tous Band (l'icône vit au bandeau) et tous à RANG, celui de leur porteur le plus fort : c'est la
        // nouveauté par rapport au 6c-1, dont les trois esprits ne portaient que des littéraux.
        // ⚠ Aucun descripteur « (PvP) » ici, à la différence des lots 6a/6b : pour un effet de bandeau,
        // c'est NatureRitualData.Descriptor qui connaît les deux ids, et l'appelant lui demande la
        // compétence du mode courant. Les deux variantes splittées (Hymne d'envie, « Visez les yeux ! »)
        // portent leur chiffre au MÊME index que leur jumelle PvE — vérifié dans la base, pas supposé.
        //
        // Ordre de la douleur : « 3…13…16 more damage whenever these party members hit with physical
        // damage ». ⚠ « Membres du groupe » → le FAMILIER n'en profite pas (allié et créature, jamais
        // membre du groupe). Et le bonus SAUTE dès que l'arme est convertie (glossaire G3).
        new(OrderOfPainSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 0, Band: true,
            RequiresPhysical: true),
        // Hymne d'envie : « Allies in earshot do +10…22…25 damage with their next attack skill » (cible à
        // plus de 50 % de santé → Q9, icône allumée = la condition est remplie).
        new(AnthemOfEnvySkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 0, Band: true),
        // ⚠ « Alliés à portée de voix » : le familier EN EST UN (règle de Philippe du 26/09/2026), et il a
        // bien des compétences d'attaque. Son chiffre se relève DANS LE TEXTE de son attaque, comme le
        // Vannage et l'Agression barbare (Q12).
        new(AnthemOfEnvySkillId, DamageBoostScope.PetAttacks, DamageBoostKind.TextDamage, Index: 0, Band: true),
        // « Visez les yeux ! » : du CRITIQUE, pas des dégâts (Q7 — les points de pourcentage s'ajoutent tels
        // quels au taux affiché). ⚠ Aucun descripteur pour le familier, bien qu'il soit un allié à portée de
        // voix : une attaque de familier n'a pas de table d'arme, donc aucun taux de critique à relever.
        new(GoForTheEyesSkillId, DamageBoostScope.Attacks, DamageBoostKind.CriticalChance, Index: 0, Band: true),
        // « Ensemble et unis ! » : ATTAQUES uniquement (Q1a) — sa régénération de santé ne concerne pas ce
        // chantier. ⚠ Index 1 : l'index 0 porte la DURÉE et l'index 2 la régénération. « Membres du groupe »
        // → pas le familier, même si le texte le cite comme repère de distance.
        new(TogetherAsOneSkillId, DamageBoostScope.Attacks, DamageBoostKind.Damage, Index: 1, Band: true),
        // Étendard de bataille d'honneur : le SEUL effet du chantier qui déborde des attaques — « Allies in
        // this ward deal +8…15 damage », sur tout paquet soumis à l'armure (§ 6.11 du plan). ⚠ Index 1 :
        // l'index 0 porte la durée et l'index 2 la part contre les Charrs, IGNORÉE (Q2).
        new(EbonStandardOfHonorSkillId, DamageBoostScope.ArmorRespectingDamage, DamageBoostKind.Damage,
            Index: 1, Band: true),
        // ⚠ Le familier est un « allié dans la zone » → son chiffre monte aussi. Les ESPRITS, eux, sont
        // exclus par le texte de la compétence lui-même (« Spirits are unaffected ») : pas de descripteur
        // SpiritAttacks, et c'est voulu.
        new(EbonStandardOfHonorSkillId, DamageBoostScope.PetAttacks, DamageBoostKind.TextDamage,
            Index: 1, Band: true),
        // Ordre du vampire (lot 6c-2b, demande de Philippe du 27/09/2026) : « These party members STEAL
        // 3…13…16 Health with each physical damage attack. Party members under another Necromancer
        // enchantment are not affected. » Même progression et même périmètre que l'Ordre de la douleur —
        // attaques d'arme, physiques, et pas le familier (« membres du groupe ») —, mais du VOL DE VIE.
        // ⚠ C'est son propre frère qui l'annule le plus souvent : l'Ordre de la douleur est lui aussi un
        // enchantement de Nécromant, donc les deux Ordres ne se cumulent jamais (vrai comportement du jeu).
        new(OrderOfTheVampireSkillId, DamageBoostScope.Attacks, DamageBoostKind.LifeSteal, Index: 0,
            Band: true, RequiresPhysical: true, LostWhenNecroEnchanted: true),

        // ══ LOT 6c-3 — les trois trous HORS BANDEAU ═══════════════════════════
        // Balayage de FAMILLE refait le 27/09/2026 sur les 1517 descriptions, trois motifs (« X % more/less
        // damage », « N less damage », « steal N Health when/with … attack »). Il CONFIRME que la liste est
        // close, et dit pourquoi les autres résultats sortent :
        //  • 11 des 18 « % de dégâts » parlent des dégâts SUBIS (Forme de brume, Frénésie, Rage primitive,
        //    Aura de foi, Armure de l'impassible, « Ils sont enflammés ! »…) — hors chantier, qui n'affiche
        //    que ce que le perso INFLIGE ;
        //  • 4 autres (Tir double, Fauchage des deux lunes (PvP), les 2 Tir triple) sont le multiplicateur
        //    PROPRE de la compétence, déjà porté par WeaponStrike.ModsFor — ce n'est pas un effet ;
        //  • Empathie retire des dégâts aux attaques de l'ENNEMI, pas aux nôtres ;
        //  • Parasite sournois et « Khanhei était revanchard » volent de la vie quand l'ENNEMI frappe.
        //
        // Rafale : « You attack 33% faster. You do 25% less damage. » Aucune progression du tout dans la base
        // → le 25 % est un LITTÉRAL, comme celui de Vengeance. ⚠ Son icône EXISTE DÉJÀ, posée par le lot 3
        // pour la vitesse d'attaque (SkillSpeedBoostData) : PersonalToggleIdOf la trouve avant d'arriver ici,
        // donc une seule icône pour les deux rôles — et l'exclusivité des POSTURES joue déjà dessus.
        // ⚠ WeaponMultiplier et non Multiplier : le malus ne mord QUE sur les dégâts d'arme.
        new(FlurrySkillId, DamageBoostScope.Attacks, DamageBoostKind.WeaponMultiplier, Fixed: 25,
            Malus: true),

        // Aura de sangsue de l'esprit : « All of your spirits within earshot deal 5…17…20 less damage and
        // steal 5…17…20 Health when they attack. » Le miroir EXACT du Sceau de puissance spectrale
        // (SpiritAttacks + TextDamage), en négatif, plus le vol de vie du 6c-2b.
        // ⚠ Ses DEUX colonnes de progression sont identiques à TOUS les rangs (sondé, pas supposé : sa durée
        // vaut elle aussi 5…17…20), donc l'index ne prête pas à conséquence — même situation que l'Arme du
        // tourment. Le harnais verrouille cette identité : si un futur catalogue les désolidarise, il rougit.
        new(SpiritleechAuraSkillId, DamageBoostScope.SpiritAttacks, DamageBoostKind.TextDamage, Index: 0,
            Malus: true),
        // Le vol de vie n'est PAS un malus : c'est un gain, il ne porte pas le signe négatif (règle du 6c-2b).
        new(SpiritleechAuraSkillId, DamageBoostScope.SpiritAttacks, DamageBoostKind.LifeSteal, Index: 0),

        // Aura de Grenth : « You deal 5…21…25 less damage and steal 5…21…25 Health when you hit with a
        // scythe. » Dernier trou du lot 6a, trouvé par le balayage du 6c-2b. Patron de l'Arme du tourment
        // (malus PLAT en paquet négatif + vol de vie), au périmètre de la FAUX.
        // ⚠ Son « Initial effect: steal 5…21…25 Health from all adjacent foes » reste INVISIBLE : c'est le vol
        // de vie que la compétence fait ELLE-MÊME, déjà écrit dans sa description (décision du chantier 10).
        // ⚠ Sa description FR décrit une TOUTE AUTRE compétence, mais la base la marque déjà FrSuspect :
        // l'infobulle retombe sur l'anglais toute seule, rien à faire ici (même cas qu'Ural).
        new(GrenthsAuraSkillId, DamageBoostScope.ScytheAttacks, DamageBoostKind.Damage, Index: 0,
            Malus: true),
        new(GrenthsAuraSkillId, DamageBoostScope.ScytheAttacks, DamageBoostKind.LifeSteal, Index: 0),
    };

    /// <summary>
    /// Les dégâts déclenchés par une ACTION DE L'ENNEMI, qui ne profitent pas de l'Étendard d'honneur
    /// (règle de Philippe, Q1b du 26/09/2026) — liste CLOSE par balayage de la base réelle le 27/09/2026.
    ///
    /// ⚠ Le balayage a trouvé 58 compétences dont les dégâts sont déclenchés par un évènement
    /// (« whenever », « each time », riposte sur blocage, seuil d'énergie de la cible…), Barbelés, Esprit
    /// malveillant et Marque de douleur comprises. Une SEULE porte un paquet réellement soumis à l'armure :
    /// toutes les autres annoncent des dégâts SANS TYPE — ou sacrés —, donc qui ignorent déjà l'armure et
    /// sortent d'eux-mêmes du périmètre. La liste des exclusions à écrire à la main se réduit à celle-ci.
    ///
    /// Armure d'éclats : « Deals 5…29…35 EARTH damage to one nearby foe whenever you are the target of a
    /// hostile spell or attack » — de la terre, donc soumis à l'armure, et déclenché par l'ennemi qui
    /// vous prend pour cible.
    /// </summary>
    private static readonly HashSet<int> _enemyTriggeredDamage =
    [
        1084,   // Armure d'éclats (Sliver Armor)
    ];

    /// <summary>Les dégâts de cette compétence sont-ils déclenchés par une action de l'ENNEMI ? → hors
    /// périmètre de l'Étendard d'honneur.</summary>
    public static bool IsEnemyTriggeredDamage(Skill target) => _enemyTriggeredDamage.Contains(target.Id);

    /// <summary>
    /// Cette compétence a-t-elle de quoi profiter d'un bonus « sur tout paquet soumis à l'armure »
    /// (Étendard d'honneur) ? Une attaque d'arme, oui : ses dégâts d'arme sont soumis à l'armure sans
    /// apparaître comme un paquet de la description. Sinon il faut au moins un paquet de dégâts qui
    /// SUBIT l'armure — ni vol de vie, ni perte de vie sèche, ni paquet qui l'ignore.
    ///
    /// ⚠ Sans ce test, l'Étendard allumé collerait une ligne « bonus d'effets +8 » sur des infobulles qui
    /// n'affichent aucun dégât du tout (un soin, une Flamme d'obsidienne) : la section s'ouvre dès qu'un
    /// effet a quelque chose à dire.
    /// </summary>
    public static bool BenefitsFromArmorRespectingBonus(Skill target, SkillDamage.Analysis analysis) =>
        WeaponStrike.IsWeaponAttack(target)
        || analysis.Rows.Any(r => r.Kind == SkillDamage.RowKind.Damage && !r.IgnoresArmor);

    // ⚠⚠ UNE COMPÉTENCE PEUT PORTER PLUSIEURS DESCRIPTEURS, donc ceci groupe au lieu d'indexer.
    // Deux occasions de se faire prendre, et les deux sont arrivées :
    //  • le Vannage porte deux descripteurs sur le même id (le perso et son familier) — d'où l'exclusion
    //    des effets de BANDEAU, qui se lisent par BandAll et jamais par id ;
    //  • l'Arme du tourment inflige un malus de dégâts ET confère du vol de vie (lot 6c-2b), et elle
    //    n'est PAS un effet de bandeau : l'exclusion ci-dessus ne la couvrait pas.
    // Un ToDictionary LÈVE au chargement de la classe → crash au démarrage, build vert, aucun avertissement.
    // Le harnais l'a attrapé le 27/09/2026 avant que l'application ne le voie.
    private static readonly Dictionary<int, List<DamageBoostDescriptor>> _bySkillId =
        All.Where(d => !d.Band).GroupBy(d => d.SkillId).ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>Les effets REÇUS d'un allié (lot 6b) : le balayage d'équipe s'appuie sur cette liste au
    /// lieu d'une suite de cas en dur, donc ajouter une source ne demande qu'un descripteur.</summary>
    public static readonly IReadOnlyList<DamageBoostDescriptor> ReceivedAll =
        All.Where(d => d.Received).ToList();

    /// <summary>Ids d'ICÔNE des effets reçus, sans doublon et dans l'ordre des descripteurs. ⚠ Deux
    /// descripteurs peuvent partager une icône (Force de l'honneur et sa variante PvP) : c'est cette
    /// liste-ci, pas <see cref="ReceivedAll"/>, qu'il faut parcourir pour afficher un bandeau.</summary>
    public static readonly IReadOnlyList<int> ReceivedToggleIds =
        ReceivedAll.Select(d => d.ToggleId).Distinct().ToList();

    /// <summary>Les effets du BANDEAU D'ÉQUIPE (lot 6c) : c'est l'état du bandeau qui les allume, pas une
    /// icône de carte. Une même compétence peut en avoir plusieurs (le Vannage vise le perso ET son
    /// familier), donc on parcourt les descripteurs, pas les ids.</summary>
    public static readonly IReadOnlyList<DamageBoostDescriptor> BandAll =
        All.Where(d => d.Band).ToList();

    // ⚠ Les effets de BANDEAU sont exclus : leur id ne doit jamais devenir une icône de carte de perso
    // (sinon un Rôdeur qui porte le Vannage sur sa barre en aurait DEUX, une au bandeau et une chez lui).
    private static readonly HashSet<int> _toggleIds =
        All.Where(d => !d.Band).Select(d => d.ToggleId).Append(SunderingModToggleId).ToHashSet();

    /// <summary>TOUS les descripteurs d'une compétence, hors bandeau — c'est cette voie-ci qu'il faut
    /// prendre pour CALCULER, parce qu'une compétence peut en porter plusieurs (l'Arme du tourment, malus
    /// de dégâts + vol de vie). Liste vide si la compétence n'en a aucun.</summary>
    public static IReadOnlyList<DamageBoostDescriptor> DescriptorsFor(int skillId) =>
        _bySkillId.GetValueOrDefault(skillId) ?? [];

    /// <summary>Le PREMIER descripteur d'une compétence, pour les appelants qui ne veulent qu'un id d'icône
    /// ou savoir si la compétence en porte un. ⚠ Ne JAMAIS s'en servir pour calculer une valeur : une
    /// compétence peut en porter plusieurs, et on n'en verrait qu'un — passer par
    /// <see cref="DescriptorsFor"/>.</summary>
    public static DamageBoostDescriptor? BySkillId(int skillId) =>
        _bySkillId.GetValueOrDefault(skillId) is { Count: > 0 } list ? list[0] : null;

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    /// <summary>Valeur de l'effet au rang donné : le littéral de la description, ou la colonne de
    /// progression sondée. 0 = rien à afficher (progression absente, ou rang inconnu — un rang de titre
    /// que le perso n'a pas laisse la description en plage, donc l'effet n'a pas de chiffre non plus).</summary>
    public static int ValueOf(DamageBoostDescriptor descriptor, Skill source, int rank)
    {
        int sign = descriptor.Malus ? -1 : 1;
        if (descriptor.Fixed > 0) return sign * descriptor.Fixed;
        if (descriptor.Index < 0 || source.Progression is not { } prog || descriptor.Index >= prog.Length)
            return 0;
        return sign * (SkillProgression.IntAt(prog[descriptor.Index], rank) ?? 0);
    }

    // ── Périmètres ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Une PRÉPARATION ne porte AUCUN effet sur Tir de barrage (395) ni sur Volée (2144) : elles
    /// retirent toutes les préparations AVANT que les flèches touchent. Vaut pour le bonus de dégâts
    /// comme pour la CONVERSION du type de dégâts — sur un Tir de barrage, les Flèches enflammées ne
    /// convertissent rien, donc c'est le mod d'arme (une corde du froid, par exemple) qui décide.
    /// Bug relevé par Philippe à la QA du lot 6a ; la règle existait déjà au lot 4b pour les conditions,
    /// elle est ici partagée par les deux côtés au lieu d'être réécrite.
    /// </summary>
    public static bool PreparationLost(Skill source, Skill target) =>
        source.SkillType == "Preparation" && ConditionDurationData.RemovesPreparations(target);

    /// <summary>L'effet de <paramref name="source"/> touche-t-il <paramref name="target"/> ?
    /// <paramref name="equipped"/> = l'arme de main du set actif (elle décide du périmètre des attaques à
    /// arme libre, comme au lot 4b).</summary>
    public static bool Affects(DamageBoostDescriptor descriptor, Skill source, Skill target, WeaponKind equipped) =>
        !PreparationLost(source, target) && AffectsScope(descriptor, target, equipped);

    /// <summary>Le PÉRIMÈTRE seul : <paramref name="target"/> est-elle le genre de compétence que cet
    /// effet vise ? Sans la règle de la préparation perdue, qui n'est pas une question de périmètre mais
    /// d'annulation — l'appelant qui veut EXPLIQUER pourquoi un effet allumé ne fait rien doit pouvoir
    /// distinguer « ça ne la visait pas » (rien à dire) de « ça la visait, et c'est annulé » (à dire).</summary>
    public static bool AffectsScope(DamageBoostDescriptor descriptor, Skill target, WeaponKind equipped) =>
        descriptor.Scope switch
        {
            DamageBoostScope.Attacks         => WeaponStrike.IsWeaponAttack(target),
            DamageBoostScope.BowAttacks      => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Bow, target, equipped),
            DamageBoostScope.MeleeAttacks    => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Melee, target, equipped),
            DamageBoostScope.DaggerAttacks   => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Daggers, target, equipped),
            DamageBoostScope.ScytheAttacks   => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Scythe, target, equipped),
            DamageBoostScope.NonDaggerAttacks => WeaponStrike.IsWeaponAttack(target)
                                                && !ConditionDurationData.InWeaponScope(ConditionWeaponScope.Daggers, target, equipped),
            DamageBoostScope.PetAttacks      => target.SkillType == "Pet Attack",
            // ⚠ AttacksAsSpirit, et non la seule clause de DÉGÂTS : trois esprits attaquent en volant de la
            // vie (lot 6c-3b). Sans ça, l'Aura de sangsue ne leur donnait rien du tout.
            DamageBoostScope.SpiritAttacks   => AttacksAsSpirit(target),
            DamageBoostScope.RitualistSkills => IsRitualistSkill(target),
            // Vengeance enchante LE PERSO : ses sorts comptent, mais ni son familier ni ses esprits
            // (glossaire G1, déjà appliqué au Preneur d'Âmes). Le vol de vie et la perte de vie sèche
            // sont écartés plus loin, à l'affichage, parce que c'est la NATURE du paquet qui décide.
            // ⚠ Même élargissement : sans lui, Vengeance « visait » la Mélodie du sang, et comme elle n'a
            // aucun paquet de dégâts à montrer, l'infobulle ouvrait une section de dégâts VIDE.
            DamageBoostScope.AllDamage       => target.SkillType != "Pet Attack"
                                                && !AttacksAsSpirit(target),
            // L'Étendard d'honneur. Le familier a son propre descripteur (TextDamage) et les esprits sont
            // exclus par le texte de la compétence (« Spirits are unaffected »). ⚠ Le test « y a-t-il
            // vraiment un paquet soumis à l'armure ? » N'EST PAS ICI : il demande la description RÉSOLUE,
            // que le périmètre n'a pas — c'est BenefitsFromArmorRespectingBonus, que l'appelant enchaîne.
            DamageBoostScope.ArmorRespectingDamage => target.SkillType != "Pet Attack"
                                                && !AttacksAsSpirit(target)
                                                && !IsEnemyTriggeredDamage(target),
            _                                => false,
        };

    /// <summary>Compétence Ritualiste au sens de « Your Ritualist skills ». ⚠ Les compétences
    /// d'allégeance sont stockées <see cref="Profession.None"/> mais verrouillées à une profession :
    /// Convocation des esprits est bien une compétence Ritualiste.</summary>
    public static bool IsRitualistSkill(Skill skill) =>
        skill.Profession == Profession.Ritualist
        || GwAllegianceData.RequiredProfession(skill) == Profession.Ritualist;

    // ── Chiffre relevé dans le texte (Q12) ────────────────────────────────────

    // Le paquet de dégâts des attaques d'un esprit se repère à sa clause, telle quelle (§ 6.6) : ni Union
    // ni Destruction ne l'ont (réduction de dégâts subis, nova de mort) → le Sceau de puissance spectrale
    // ne les touche pas, sans avoir à les nommer.
    private static readonly Regex SpiritAttackRegex = new(
        @"\bits attacks deal\s+(?<range>\d+(?:\.\.\.\d+)+)\s+damage\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ⚠ TROIS esprits de la base n'infligent PAS de dégâts : leur attaque EST un vol de vie — Mélodie du
    // sang (1253), sa variante PvP (3019) et Vampirisme (2110). Relevé par Philippe le 27/09/2026 : « ce
    // n'est pas vraiment mentionné dans leur description, mais les attaques de ces esprits volent de la
    // vie ». Deux conséquences, et elles vont en sens INVERSE :
    //  • le malus de dégâts de l'Aura de sangsue n'a **rien à mordre** chez eux (pas de dégâts du tout) ;
    //  • son vol de vie, lui, **s'AJOUTE au leur** — donc chez ces trois-là l'Aura est purement un gain.
    // ⚠ Voyage (1255) attaque aussi, mais sa description ne chiffre **ni** dégât **ni** vol de vie : aucun
    // nombre à relever, donc il reste dehors — règle du § 6.6, on lit la clause et on ne tient pas de liste.
    private static readonly Regex SpiritStealRegex = new(
        @"\bits attacks steal\s+(?<range>\d+(?:\.\.\.\d+)+)\s+Health\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Premier paquet de dégâts d'une attaque de familier. « [^.;]*? » borne la recherche de la clause
    // conditionnelle à la PHRASE : sur les 16 attaques de familier, 2 seulement ont deux paquets (Coup
    // brutal, Assaut de Melandru PvP) et dans les deux le second est explicitement conditionnel, donc
    // aucun arbitrage à rendre (§ 6.6). Le paquet PAR-UNITÉ du Coup enragé (PvP) — « for each of your
    // recharging Beast Mastery skills » — est écarté : y ajouter le bonus le multiplierait par le compte.
    private static readonly Regex PetDamageRegex = new(
        @"\+?(?<range>\d+(?:\.\.\.\d+)+)\s+(?:more\s+)?damage\b(?<tail>[^.;]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConditionalTailRegex = new(
        @"\bif\b|\bfor each\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Plage du paquet « Its attacks deal X damage » d'un rituel d'asservissement, ou null.</summary>
    public static string? SpiritAttackRange(Skill skill) =>
        SpiritAttackRegex.Match(skill.Description) is { Success: true } m ? m.Groups["range"].Value : null;

    /// <summary>Plage du VOL DE VIE « Its attacks steal X Health » d'un rituel d'asservissement, ou null :
    /// les 3 esprits dont l'attaque vole de la vie au lieu d'infliger des dégâts (lot 6c-3b).</summary>
    public static string? SpiritStealRange(Skill skill) =>
        SpiritStealRegex.Match(skill.Description) is { Success: true } m ? m.Groups["range"].Value : null;

    /// <summary>
    /// Cet esprit ATTAQUE-t-il, avec une sortie chiffrée dans sa description ? Les DEUX formes comptent :
    /// des DÉGÂTS (Douleur, Angoisse, Mélodie des Ombres, Dissonance, Désenchantement, Regard de fureur)
    /// ou un VOL DE VIE (Mélodie du sang, Vampirisme).
    ///
    /// C'est le périmètre de <see cref="DamageBoostScope.SpiritAttacks"/>, et c'est aussi ce qui EXCLUT les
    /// esprits des effets posés sur le perso (glossaire G1). ⚠ Union, Destruction, Refuge et les autres
    /// rituels utilitaires n'attaquent pas ; Voyage attaque mais ne chiffre rien.
    /// </summary>
    public static bool AttacksAsSpirit(Skill skill) =>
        SpiritAttackRange(skill) is not null || SpiritStealRange(skill) is not null;

    /// <summary>
    /// Le vol de vie conféré par cet effet se lit-il DANS LE TEXTE de la compétence survolée, au lieu
    /// d'aller sur sa propre ligne sous la table ?
    ///
    /// Vrai pour les 3 esprits dont l'attaque EST un vol de vie : leur chiffre existe DÉJÀ dans leur
    /// description, donc l'effet le RELÈVE — exactement comme le Sceau de puissance spectrale relève les
    /// dégâts de la Douleur (Q12). Une ligne « Vol de vie : 17 » posée à côté d'un « vole 21 points de
    /// vie » laisserait le lecteur additionner lui-même deux chiffres qui décrivent le MÊME coup.
    /// (Correction demandée par Philippe le 27/09/2026, après la QA du lot 6c-3.)
    /// </summary>
    public static bool LifeStealReadInText(DamageBoostDescriptor descriptor, Skill target) =>
        descriptor.Kind == DamageBoostKind.LifeSteal
        && descriptor.Scope == DamageBoostScope.SpiritAttacks
        && SpiritStealRange(target) is not null;

    /// <summary>Plage du PREMIER paquet non conditionnel d'une attaque de familier, ou null (Morsure
    /// empoisonnée n'annonce aucun dégât ; le Coup enragé (PvP) n'a qu'un paquet par-unité).</summary>
    public static string? PetDamageRange(Skill skill)
    {
        foreach (Match m in PetDamageRegex.Matches(skill.Description))
            if (!ConditionalTailRegex.IsMatch(m.Groups["tail"].Value))
                return m.Groups["range"].Value;
        return null;
    }

    /// <summary>Index de la colonne de progression de <paramref name="target"/> que l'effet relève, ou −1 :
    /// la clause de l'esprit ou le premier paquet du familier, apparié à la progression par les ancres
    /// (donc jamais un index deviné). ⚠ Il faut le DESCRIPTEUR et pas seulement son périmètre : sur un
    /// esprit qui vole de la vie, un effet relève la colonne du VOL DE VIE et non celle des dégâts.</summary>
    public static int TextBonusColumn(DamageBoostDescriptor descriptor, Skill target) => descriptor.Scope switch
    {
        // Le vol de vie conféré à un esprit qui vole DÉJÀ de la vie relève SA colonne (lot 6c-3b). C'est le
        // seul cas où deux descripteurs du même effet visent deux colonnes différentes de la même cible.
        DamageBoostScope.SpiritAttacks when LifeStealReadInText(descriptor, target)
                                       => SkillProgression.ColumnOf(target.Progression, SpiritStealRange(target)),
        DamageBoostScope.SpiritAttacks => SkillProgression.ColumnOf(target.Progression, SpiritAttackRange(target)),
        DamageBoostScope.PetAttacks    => SkillProgression.ColumnOf(target.Progression, PetDamageRange(target)),
        _                              => -1,
    };

    // ── Type de dégâts effectif (§ 6.1 du plan) ───────────────────────────────

    private static readonly string[] Elemental = ["fire", "cold", "earth", "lightning"];

    /// <summary>
    /// Les attaques qui portent leur type de dégâts TOUTES SEULES : leur paquet typé convertit l'attaque
    /// ENTIÈRE (règle de Philippe, 26/09/2026). Conséquence directe : le Grand brasier, qui ne convertit
    /// que le PHYSIQUE, ne les touche pas — mais Hiver, lui, passe leur élémentaire en froid.
    ///
    /// ⚠ Recensement CLOS par balayage de famille sur les 1517 descriptions (motif « ⟨nombre⟩ ⟨type⟩
    /// damage » sur les seules compétences d'attaque) : **9 résultats, dont 5 sont des paquets qui
    /// PROQUENT à côté** et gardent donc leur type sans rien convertir — Victoire frissonnante (1539),
    /// Clivage (335), Victoire paralysante (2147), Javelot sacré (2209) et Moisson des impuretés (1486),
    /// tous sur le patron de Cent lames (un paquet qui frappe quelqu'un d'AUTRE que la cible de
    /// l'attaque, ou sous condition d'événement). Les 4 ci-dessous sont les seules à convertir.
    ///
    /// ⚠ Ce que ça ne fait PAS encore : les CONDITIONS (lot 4b). Une attaque auto-convertie n'est plus
    /// physique, donc l'Application de poison ne devrait pas l'empoisonner — Philippe a demandé le
    /// 26/09/2026 que cette partie-là soit **notée pour plus tard**, pas faite ici.
    /// </summary>
    private static readonly Dictionary<int, string> _intrinsicTypes = new()
    {
        [1551] = "lightning",  // Javelot d'éclair — « +10…18…20 dégâts de foudre »
        [3425] = "holy",       // Frappe du jugement — « comme Clivage mais full dégâts sacrés »
        [1483] = "holy",       // Coup de bannissement
        [3263] = "holy",       // Coup de bannissement (PvP) — ⚠ la base la nomme « Frappe implacable (PvP) »
    };

    /// <summary>Type que cette attaque impose d'elle-même, null si elle suit son arme.</summary>
    public static string? IntrinsicType(Skill target) => _intrinsicTypes.GetValueOrDefault(target.Id);

    /// <summary>Type de dégâts des attaques du FAMILIER. Un familier n'inflige que du physique (perforant
    /// pour les oiseaux, tranchant pour les loups et félins — l'app ne sait pas lequel) ; la seule exception
    /// est le Molosse de Balthazar, qui inflige du feu, et que l'app ne connaît pas non plus. Le Grand
    /// brasier le convertit (c'est une « créature à portée ») ; les convertisseurs du perso, eux, ne
    /// touchent que SON arme, et Hiver ne convertit pas le physique.</summary>
    public static string? PetEffectiveElement(ConversionState state) =>
        state.GreaterConflagration ? "fire" : null;

    /// <summary>Type de dégâts élémentaire ? (Hiver ne convertit QUE l'élémentaire.)</summary>
    public static bool IsElemental(string? type) => type is not null && Elemental.Contains(type);

    /// <summary>Type de dégâts PHYSIQUE, au sens de Briseur de pierre (« elemental or physical damage »)
    /// — les trois types d'arme et le « physique » générique. ⚠ Le sacré, l'ombre, les ténèbres et le
    /// chaos n'en sont PAS : un Coup de bannissement reste sacré sous Briseur de pierre.</summary>
    public static bool IsPhysicalType(string? type) =>
        type is "physical" or "slashing" or "piercing" or "blunt";

    /// <summary>
    /// Tout ce qui peut convertir le type de dégâts des attaques d'un perso, au moment présent.
    /// <paramref name="LitConverters"/> = ses convertisseurs personnels ALLUMÉS, dans l'ordre de la barre ;
    /// <paramref name="ElementalMod"/> = l'élément du mod d'arme du set actif (null s'il n'y en a pas) ;
    /// <paramref name="JudgesInsight"/> = Clairvoyance du juge reçue et allumée ; les deux brasiers et
    /// Briseur de pierre sont à part parce que la chaîne leur donne une place FIXE, pas celle de la barre.
    /// </summary>
    public readonly record struct ConversionState(
        IReadOnlyList<DamageConverterDescriptor>? LitConverters = null,
        string? ElementalMod = null,
        bool JudgesInsight = false,
        bool GreaterConflagration = false,
        bool Conflagration = false,
        bool StoneStriker = false);

    /// <summary>
    /// Type de dégâts EFFECTIF des attaques de <paramref name="target"/> ; null = rien ne les a
    /// converties, donc l'application ne sait rien et l'on croit l'icône (§ 6.1 du plan).
    ///
    /// ⚠ La chaîne n'est PAS commutative (règle de Philippe du 16/09/2026) : le mod élémentaire de
    /// l'arme, puis les convertisseurs personnels dans l'ordre de la barre, puis la Clairvoyance du juge
    /// reçue, puis les esprits qui ne convertissent que ce qui est ENCORE physique (Grand brasier sur
    /// tout le physique, Brasier sur les seules flèches) — et Briseur de pierre a TOUJOURS le dernier mot.
    ///
    /// ⚠⚠ HIVER (462) N'EST PAS DANS CETTE CHAÎNE, ET C'EST VOLONTAIRE (tranché le 26/09/2026, source :
    /// note de mécanique de la page wiki *Winter*). Il convertit les dégâts élémentaires **REÇUS**, il ne
    /// change pas le type que l'ARME inflige — il ne déclenche donc JAMAIS une conjuration. Sous Grand
    /// brasier + Hiver, c'est bien la Conjuration de FLAMME qui marche (l'arme sort du feu) ; les dégâts
    /// arrivent froids chez la cible, et ça ne regarde que l'étiquette de type, jamais la validité d'une
    /// conjuration. ⚠ La base ne permet pas de deviner la différence : Hiver et Grand brasier écrivent
    /// tous les deux « … for creatures in range ». Ne pas « réparer » ceci.
    /// </summary>
    public static string? EffectiveElement(ConversionState state, Skill target, WeaponKind equipped)
    {
        // ⚠ Le type que l'attaque porte elle-même gagne sur l'arme ET sur les esprits qui ne
        // convertissent que le physique : un Javelot d'éclair reste de la foudre sous Grand brasier
        // (tranché le 26/09/2026). Les deux conversions qui suivent, elles, l'écrasent encore — un
        // enchantement qui dit « vos attaques infligent des dégâts de X » vise toutes les attaques.
        string? element = IntrinsicType(target);

        // Un mod élémentaire ne convertit QUE les attaques de l'arme qui le porte — et il ne peut rien
        // contre une attaque qui impose déjà son type.
        if (element is null && state.ElementalMod is { } mod
            && ConditionDurationData.UsesEquippedWeapon(target, equipped))
            element = mod;

        foreach (var k in state.LitConverters ?? [])
            if (ConditionDurationData.InWeaponScope(k.Weapon, target, equipped))
                element = k.Element;

        if (state.JudgesInsight && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Physical, target, equipped))
            element = "holy";

        if (element is null && state.GreaterConflagration
            && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Physical, target, equipped))
            element = "fire";
        if (element is null && state.Conflagration
            && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Bow, target, equipped))
            element = "fire";

        // ⚠ Briseur de pierre a le dernier mot, MAIS seulement sur ce qui est élémentaire ou physique
        // (son texte : « elemental or physical damage »). Un Coup de bannissement est SACRÉ : il reste
        // sacré. Conséquence voulue et conforme au wiki : sous Briseur de pierre les conjurations ne
        // s'appliquent plus (aucune n'est de terre) et l'Aura de poussière d'ébène, si, s'applique.
        if (state.StoneStriker && (element is null || IsElemental(element) || IsPhysicalType(element))
            && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Physical, target, equipped))
            element = "earth";

        return element;
    }

    /// <summary>
    /// L'effet qui exige un type d'arme s'applique-t-il ? <paramref name="effectiveElement"/> = ce que la
    /// chaîne de conversion donne pour CETTE attaque, null = rien ne l'a convertie.
    ///
    /// C'est la seule entorse de tout le chantier à « icône allumée = l'effet s'applique » (§ 6.1,
    /// validée le 23/09/2026) : tant qu'aucun convertisseur n'est allumé et qu'aucun mod élémentaire
    /// n'est au set actif, on CROIT l'icône — le perso a peut-être une baguette de feu que
    /// l'application ne modélise pas. Dès qu'un convertisseur agit, l'application SAIT le type réel et
    /// seule la conjuration de ce type-là s'applique.
    /// </summary>
    /// <summary>
    /// Type de dégâts AFFICHÉ.
    ///
    /// ⚠ **Briseur de pierre FORCE la terre et garde le dernier mot** (règle de Philippe du 16/09/2026,
    /// confirmée par la note de la page wiki *Stone_Striker* : « this skill does not convert damage but
    /// instead forces the damage type of all dealt damage to be earth damage ») : Hiver ne repasse pas
    /// derrière. Mais il ne touche que **l'élémentaire et le physique**, exactement comme son texte le
    /// dit — un paquet SACRÉ (Coup de bannissement, Clairvoyance du juge) n'est ni l'un ni l'autre et
    /// reste sacré. ⚠ Et il vaut pour **TOUS les dégâts que le perso inflige, sorts compris**, pas
    /// seulement ses attaques.
    ///
    /// Hiver, lui, ne convertit que l'élémentaire : le physique n'est jamais touché.
    /// </summary>
    public static string? DisplayedType(string? type, bool elementalToCold, bool stoneStriker = false)
    {
        if (stoneStriker && (IsElemental(type) || IsPhysicalType(type))) return "earth";
        return elementalToCold && IsElemental(type) ? "cold" : type;
    }

    /// <summary>Cet id est-il celui d'un effet du BANDEAU (lot 6c) ? Sert à ne mettre en cache que les
    /// compétences utiles quand on cherche leur nom d'affichage.</summary>
    public static bool IsBandSkillId(int skillId) => _bandSkillIds.Contains(skillId);

    private static readonly HashSet<int> _bandSkillIds = BandAll.Select(d => d.SkillId).ToHashSet();

    public static bool ElementSatisfied(string? requiredElement, string? effectiveElement) =>
        requiredElement is null || effectiveElement is null || effectiveElement == requiredElement;

    /// <summary>
    /// Nom de la famille EXCLUSIVE des effets qui exigent un type d'arme, null pour les autres.
    ///
    /// ⚠ Règle relevée par Philippe à la QA du lot 6a : **une arme n'inflige qu'UN type de dégâts à la
    /// fois**. Les 3 conjurations et l'Aura de poussière d'ébène portent donc sur la même arme et sont
    /// mutuellement exclusives : au plus une peut agir. Sans ça, allumer les trois conjurations (ou une
    /// conjuration + l'Aura) additionnait leurs dégâts, ce qui est impossible en jeu — et le manquait
    /// précisément dans le seul cas où l'application ne connaît PAS le type de l'arme (aucun mod
    /// élémentaire au set actif), donc là où elle croit l'icône.
    ///
    /// En jeu les enchantements peuvent bel et bien tenir tous les trois en même temps ; c'est leur
    /// EFFET qui ne peut pas. Les rendre exclusives à l'allumage est donc une simplification de
    /// simulation — la même que pour les préparations —, et elle dit la vérité : l'icône allumée
    /// signifie « mon arme inflige ce type de dégâts ».
    /// </summary>
    public static string? ExclusiveElementFamily(int toggleId) =>
        DescriptorsFor(toggleId).Any(d => d.RequiresElement is not null) ? "WeaponElement" : null;
}
