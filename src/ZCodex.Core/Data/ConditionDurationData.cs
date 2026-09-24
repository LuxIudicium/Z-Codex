using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Armes visées par un effet qui touche les attaques du perso (chantier infobulle, lot 4b). « Vos attaques
/// physiques » (Apply Poison) et « vos flèches » (Barbed Arrows) sont deux périmètres DIFFÉRENTS — décision de
/// Philippe du 16/09/2026. Sert aussi bien aux ajouteurs de condition qu'aux convertisseurs de type de dégâts
/// (Avatar de Grenth ne convertit que les attaques à la <see cref="Scythe"/>) et, depuis le lot 6a, aux
/// bonus de dégâts. <see cref="Melee"/> = le « in melee » du glossaire G2 (lot 6a) : les 5 armes de corps
/// à corps, plus la lance sur la SEULE Frappe du javelot (« This attack has melee range »).
/// </summary>
public enum ConditionWeaponScope { Physical, Bow, Daggers, Scythe, Melee }

/// <summary>
/// Compétence qui AJOUTE une condition aux attaques du perso (Apply Poison, Sharpen Daggers…), togglée par
/// icône sur la carte du perso comme les lots 1 à 4a. La condition et sa durée ne sont PAS recopiées ici :
/// elles se lisent dans la description de l'ajouteur, résolue au rang de <paramref name="ScalingAttribute"/>
/// (<see cref="ConditionInfliction"/>). <paramref name="Received"/> : sort d'arme reçu d'un allié (patron
/// Weapon of Fury). <paramref name="RequiresPhysical"/> : l'effet exige des dégâts PHYSIQUES et ne pose donc
/// plus rien sur une attaque dont un effet actif a converti les dégâts (Apply Poison, la seule des sept :
/// « your PHYSICAL attacks » — remarque de Philippe du 16/09/2026). <paramref name="BaseSkillId"/> = id PvE
/// d'une variante « (PvP) » : l'icône est mémorisée sur l'id de base.
/// </summary>
public sealed record ConditionAdderDescriptor(
    int SkillId, ConditionWeaponScope Weapon, string ScalingAttribute,
    bool Received = false, bool RequiresPhysical = false, int BaseSkillId = 0)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

/// <summary>
/// Compétence qui CONVERTIT le type de dégâts des attaques du perso (forme, enchantement éclair, préparation,
/// Peau de pierre, Clairvoyance du juge reçue) : ses attaques ne sont plus physiques, donc Apply Poison
/// n'empoisonne plus tant qu'elle est allumée — « ça n'annule pas la préparation, ça suspend ses effets »
/// (Philippe, 16/09/2026). <paramref name="Weapon"/> = les attaques réellement converties : tout le physique,
/// ou seulement les flèches (Flèches enflammées), ou seulement la faux (Avatar de Grenth).
/// <paramref name="Element"/> = le type de dégâts qui SORT de la conversion (lot 6a) : c'est lui qui décide
/// si une conjuration s'applique encore (chaîne du § 6.1 du plan).
/// </summary>
public sealed record DamageConverterDescriptor(
    int SkillId, string Element, ConditionWeaponScope Weapon = ConditionWeaponScope.Physical,
    bool Received = false, int BaseSkillId = 0)
{
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

/// <summary>Une ligne « Durée de X effective » de l'infobulle : la condition et sa durée FINALE (secondes).</summary>
public sealed record ConditionLine(string Condition, int Seconds);

/// <summary>
/// Ce que les effets actifs du perso font aux conditions de UNE compétence (chantier infobulle, lot 4b).
/// <paramref name="Added"/> = conditions ajoutées à ses attaques par un effet allumé, avec leur durée de base
/// au rang de l'ajouteur. <paramref name="AllPercent"/> = Sceau de l'Archer, qui allonge TOUTE condition
/// appliquée tant qu'un arc est au set actif (décision Philippe, Q10 du lot 4).
/// <paramref name="ModConditions"/> = conditions allongées de 33 % par un préfixe d'arme du set actif.
/// Les allongeurs se MULTIPLIENT (Q3 du cadrage 4b), ils ne s'additionnent pas.
/// </summary>
public readonly record struct ConditionDurations(
    IReadOnlyList<ConditionInfliction.Inflicted>? Added = null,
    int AllPercent = 0,
    IReadOnlySet<string>? ModConditions = null)
{
    /// <summary>Au moins un effet à dire — sinon l'infobulle n'a aucune ligne de condition à écrire.</summary>
    public bool Any => Added is { Count: > 0 } || AllPercent > 0 || ModConditions is { Count: > 0 };

    /// <summary>Durée allongée d'une condition : la base multipliée par CHAQUE allongeur applicable, plancher
    /// (décision Q4 du lot 4), plafonnée à 12 heures (Q3 du cadrage 4b). Exemple de Philippe : poison 10 s ×
    /// Sceau de l'Archer (2,5) × corde Poisonous (1,33) = 33 s.</summary>
    public int Extend(string condition, int baseSeconds)
    {
        if (baseSeconds <= 0) return baseSeconds;
        double seconds = baseSeconds;
        if (AllPercent > 0) seconds *= 1 + AllPercent / 100.0;
        if (ModConditions?.Contains(condition) == true)
            seconds *= 1 + ConditionDurationData.WeaponModPercent / 100.0;
        return (int)Math.Floor(Math.Min(seconds, ConditionDurationData.MaxSeconds));
    }
}

public static class ConditionDurationData
{
    /// <summary>Sceau de l'Archer : « Conditions you apply while wielding a bow last 150% longer ».</summary>
    public const int ArcherSignetSkillId = 1200;
    public const int ArcherSignetPercent = 150;

    /// <summary>Sundering Weapon : seul ajouteur REÇU d'un allié (sort d'arme lancé sur la cible).</summary>
    public const int SunderingWeaponSkillId = 2148;
    public const string SunderingWeaponAttribute = "Communing";

    /// <summary>Les 6 préfixes d'arme « Lengthens X duration on foes by 33% ».</summary>
    public const int WeaponModPercent = 33;

    /// <summary>Plafond absolu d'une condition (12 h — jamais atteint en pratique, mais tranché le 16/09).</summary>
    public const int MaxSeconds = 12 * 3600;

    // Ids, périmètres d'arme et caractéristiques relevés dans la base réelle (16/09/2026). Le texte du jeu donne
    // le périmètre : « your physical attacks » (toutes armes physiques), « your arrows » (arc seul), « your
    // dagger attacks » (dagues seules), « with your attack skills » et « next 3 attacks » (toutes armes).
    // Les 4 préparations sont déjà mutuellement exclusives (ExclusiveSkillTypes, lot 3 Q5).
    public static readonly IReadOnlyList<ConditionAdderDescriptor> All = new ConditionAdderDescriptor[]
    {
        new(435,  ConditionWeaponScope.Physical, "Wilderness Survival", RequiresPhysical: true), // Apply Poison : Poison
        new(1470, ConditionWeaponScope.Bow,      "Wilderness Survival"),                 // Barbed Arrows : saignement
        new(429,  ConditionWeaponScope.Bow,      "Wilderness Survival"),                 // Melandru's Arrows : saignement
        new(1199, ConditionWeaponScope.Bow,      "Expertise"),                           // Glass Arrows : saignement si bloqué
        new(3145, ConditionWeaponScope.Bow,      "Expertise", BaseSkillId: 1199),        // Glass Arrows (PvP) : même mécanique
        new(926,  ConditionWeaponScope.Daggers,  "Critical Strikes"),                    // Sharpen Daggers : saignement
        new(1756, ConditionWeaponScope.Physical, "Wind Prayers"),                        // Grenth's Grasp : infirmité
        new(SunderingWeaponSkillId, ConditionWeaponScope.Physical, SunderingWeaponAttribute,
            Received: true),                                                             // Sundering Weapon (reçue) : armure brisée
    };

    /// <summary>Clairvoyance du juge : seul convertisseur REÇU d'un allié (« Converts target ally's attacks to
    /// holy damage »). Aucun rang à résoudre — la conversion est binaire.</summary>
    public const int JudgesInsightSkillId = 267;

    // Les 13 convertisseurs personnels + le reçu, relevés dans la base réelle le 16/09/2026 (toutes les
    // descriptions qui changent le type de dégâts des attaques DU PERSO). Écartés : Hiver, qui convertit
    // l'élémentaire en froid et ne touche donc jamais le physique ; les conversions subies (Reversal of
    // Fortune, Mark of Protection…), qui portent sur les dégâts REÇUS.
    public static readonly IReadOnlyList<DamageConverterDescriptor> Converters = new DamageConverterDescriptor[]
    {
        new(433,  "fire",  ConditionWeaponScope.Bow),    // Flèches enflammées (préparation — déjà exclusive avec Apply Poison)
        new(StoneStrikerSkillId, "earth"),               // Briseur de pierre
        new(1493, "cold"),                               // Doigts de Grenth
        new(1497, "earth"),                              // Manteau de poussière
        new(3347, "earth", BaseSkillId: 1497),           // Manteau de poussière (PvP)
        new(1498, "earth"),                              // Force stupéfiante
        new(1507, "holy"),                               // Cœur de la Flamme sacrée
        new(1518, "holy"),                               // Avatar de Balthazar
        new(1519, "holy"),                               // Avatar de Dwayna
        new(3270, "holy", BaseSkillId: 1519),            // Avatar de Dwayna (PvP)
        new(1520, "dark", ConditionWeaponScope.Scythe),  // Avatar de Grenth : ATTAQUES À LA FAUX seulement
        new(1521, "chaos"),                              // Avatar de Lyssa
        new(1522, "earth"),                              // Avatar de Melandru
        new(3271, "earth", BaseSkillId: 1522),           // Avatar de Melandru (PvP)
        new(JudgesInsightSkillId, "holy", Received: true), // Clairvoyance du juge (reçue)
    };

    /// <summary>Briseur de pierre : le SEUL convertisseur qui a toujours le dernier mot sur la chaîne de
    /// conversion — terre, quels que soient les esprits posés (règle de Philippe du 16/09/2026, § 6.1).</summary>
    public const int StoneStrikerSkillId = 1371;

    private static readonly Dictionary<int, ConditionAdderDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);
    private static readonly Dictionary<int, DamageConverterDescriptor> _converterBySkillId =
        Converters.ToDictionary(d => d.SkillId);
    private static readonly HashSet<int> _toggleIds =
        All.Select(d => d.ToggleId).Concat(Converters.Select(d => d.ToggleId))
           .Append(ArcherSignetSkillId).ToHashSet();

    public static ConditionAdderDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    public static DamageConverterDescriptor? ConverterBySkillId(int skillId) =>
        _converterBySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    // ── Préfixes d'arme qui allongent une condition de 33 % ────────────────────
    // Lus dans la table GÉNÉRÉE des mods (31 entrées : Barbed, Crippling, Cruel, Poisonous, Heavy, Silencing,
    // déclinés par type d'arme) plutôt que recopiés — une régénération de GwEquipmentModDetails reste suivie.
    // Les noms de condition du wiki sont déjà ceux de GwConditionData (Crippled, Dazed, Deep Wound…).
    private static readonly Regex LengthensRegex = new(
        @"^Lengthens\s+(.+?)\s+durations?\s+on\s+foes\s+by\s+\d+%$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<int, string> _conditionByModId =
        GwEquipmentModDetails.ByModId
            .Select(kv => (kv.Key, Match: LengthensRegex.Match(kv.Value.Description)))
            .Where(t => t.Match.Success)
            .ToDictionary(t => t.Key, t => t.Match.Groups[1].Value);

    /// <summary>Condition allongée par ce mod d'arme, null s'il n'en allonge aucune.</summary>
    public static string? ModCondition(int modId) => _conditionByModId.GetValueOrDefault(modId);

    /// <summary>Conditions allongées de 33 % par les mods d'un set d'armes. Null = aucune (rien à porter dans
    /// l'infobulle). Un doublon (deux préfixes de la même condition) ne compte qu'une fois : en jeu, deux mods
    /// identiques ne se cumulent pas.</summary>
    public static IReadOnlySet<string>? ModConditionsOf(IEnumerable<int> modIds)
    {
        var set = modIds.Select(ModCondition).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Count == 0 ? null : set;
    }

    // ── Mods d'arme élémentaires (Fiery, Icy, Ebon, Shocking) ─────────────────
    // Ils CONVERTISSENT le type de dégâts de l'arme qui les porte : les 28 entrées « <élément> damage » de la
    // table générée (4 éléments × 7 armes martiales), lues et non recopiées, comme les préfixes « +33 % ».
    private static readonly Regex ElementalRegex = new(
        @"^(Fire|Cold|Earth|Lightning)\s+damage$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<int, string> _elementalModTypes =
        GwEquipmentModDetails.ByModId
            .Select(kv => (kv.Key, Match: ElementalRegex.Match(kv.Value.Description)))
            .Where(t => t.Match.Success)
            .ToDictionary(t => t.Key, t => t.Match.Groups[1].Value.ToLowerInvariant());

    /// <summary>Mod qui rend l'arme élémentaire (donc NON physique).</summary>
    public static bool IsElementalMod(int modId) => _elementalModTypes.ContainsKey(modId);

    /// <summary>Type de dégâts qu'un mod élémentaire donne à l'arme qui le porte (« fire », « cold »,
    /// « earth », « lightning »), null s'il n'est pas élémentaire. Le lot 4b n'avait besoin que du
    /// « oui/non » ; la chaîne de conversion du lot 6a a besoin de l'ÉLÉMENT (§ 6.1).</summary>
    public static string? ElementalModType(int modId) => _elementalModTypes.GetValueOrDefault(modId);

    // ── Mod d'arme « de fractionnement » (Sundering) ──────────────────────────
    // +20 % de pénétration d'armure en BONUS (chance 20 %), sur les 7 armes martiales — lu dans la table
    // générée comme les autres mods. ⚠ NE PAS confondre avec les deux sorts d'arme homonymes du § 6.4 du
    // plan : Arme de fractionnement (2148, pénétration de BASE) et Arme à fragmentation (792, écartée).
    private static readonly Regex ArmorPenetrationModRegex = new(
        @"^Armor penetration \+(\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<int, int> _penetrationModIds =
        GwEquipmentModDetails.ByModId
            .Select(kv => (kv.Key, Match: ArmorPenetrationModRegex.Match(kv.Value.Description)))
            .Where(t => t.Match.Success)
            .ToDictionary(t => t.Key, t => int.Parse(t.Match.Groups[1].Value));

    /// <summary>Pénétration d'armure en BONUS qu'apporte ce mod d'arme (0 s'il n'en apporte pas).</summary>
    public static int PenetrationModPercent(int modId) => _penetrationModIds.GetValueOrDefault(modId);

    // ── Attaques qui RETIRENT les préparations ────────────────────────────────
    // Barrage (395) et Volée (2144) : « All your preparations are removed ». La préparation saute AVANT que les
    // flèches touchent, donc ces deux attaques ne portent aucun effet de préparation — oubli signalé par Philippe
    // le 16/09/2026. Détecté par le TEXTE, pas par id en dur : une mise à jour du catalogue reste suivie.
    private static readonly Regex RemovesPreparationsRegex = new(
        @"\ball\s+your\s+preparations\s+are\s+removed\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Cette attaque retire-t-elle les préparations du perso avant de frapper ?</summary>
    public static bool RemovesPreparations(Skill skill) => RemovesPreparationsRegex.IsMatch(skill.Description);

    // ── Arme d'un coup ─────────────────────────────────────────────────────────

    /// <summary>Arme qui inflige des dégâts physiques — baguette et bâton sont dehors (décision Q2 du cadrage
    /// 4b : « rien avec une baguette ou un bâton »).</summary>
    public static bool IsPhysical(WeaponKind kind) =>
        kind is WeaponKind.Axe or WeaponKind.Sword or WeaponKind.Spear or WeaponKind.Hammer
             or WeaponKind.Daggers or WeaponKind.Scythe or WeaponKind.Bow;

    /// <summary>Le perso tient-il l'arc qu'exige le Sceau de l'Archer ? <see cref="WeaponKind.None"/> = aucune
    /// arme renseignée : on fait alors confiance à l'icône, comme partout dans ce chantier (icône allumée =
    /// effet actif) — sinon le sceau resterait sans effet sur tout build sans équipement.</summary>
    public static bool BowWielded(WeaponKind equipped) =>
        equipped is WeaponKind.Bow or WeaponKind.None;

    // Arme du coup : celle qu'IMPOSE le type d'attaque (Bow Attack → arc), sauf pour les deux types à arme
    // LIBRE (« Melee Attack », « Ranged Attack ») où c'est l'arme du set actif qui décide — à condition
    // qu'elle sache porter ce coup-là (un arc au set ne transforme pas une attaque de mêlée en tir) ; sinon
    // la maîtrise par défaut du type. None = indéterminée.
    private static WeaponKind AttackWeapon(Skill target, WeaponKind equipped) =>
        WeaponStrike.IsFreeWeaponAttack(target)
        && WeaponStrike.ChoicesFor(target).Any(w => KindOf(w) == equipped)
            ? equipped
            : KindOf(WeaponStrike.For(target));

    private static WeaponKind KindOf(WeaponStrike.Weapon? weapon) => weapon?.Mastery switch
    {
        "Axe Mastery"     => WeaponKind.Axe,
        "Swordsmanship"   => WeaponKind.Sword,
        "Hammer Mastery"  => WeaponKind.Hammer,
        "Scythe Mastery"  => WeaponKind.Scythe,
        "Spear Mastery"   => WeaponKind.Spear,
        "Marksmanship"    => WeaponKind.Bow,
        "Dagger Mastery"  => WeaponKind.Daggers,
        "Wand"            => WeaponKind.Wand,
        "Staff"           => WeaponKind.Staff,
        _                 => WeaponKind.None,
    };

    /// <summary>
    /// L'ajouteur <paramref name="source"/> pose-t-il sa condition sur <paramref name="target"/> ? La cible doit
    /// être une attaque d'arme (Pet Attack exclue : c'est le familier qui frappe, pas le perso) dont l'arme entre
    /// dans le périmètre de l'ajouteur. Une attaque de mêlée à arme LIBRE reste physique même sans arme
    /// renseignée — ses 5 armes possibles le sont toutes ; « Ranged Attack » (Deft Strike, la seule) peut être une
    /// baguette, donc sans arme renseignée on n'affirme rien. ⚠ Une PRÉPARATION ne porte rien sur Barrage ni sur
    /// Volée, qui la retirent avant de frapper.
    /// </summary>
    public static bool AddsTo(ConditionAdderDescriptor descriptor, Skill source, Skill target, WeaponKind equipped) =>
        !(source.SkillType == "Preparation" && RemovesPreparations(target))
        && InScope(descriptor.Weapon, target, equipped);

    /// <summary>Ce convertisseur change-t-il le type de dégâts de <paramref name="target"/> ? Même règle d'arme
    /// que <see cref="AddsTo"/> : Avatar de Grenth ne touche que les attaques à la faux, Flèches enflammées et
    /// Brasier que les flèches, tous les autres l'ensemble des attaques physiques.</summary>
    public static bool Converts(ConditionWeaponScope scope, Skill target, WeaponKind equipped) =>
        InScope(scope, target, equipped);

    /// <summary>Périmètre d'arme d'un effet, partagé par tous les lots : <paramref name="target"/> est-elle une
    /// attaque de l'arme visée par <paramref name="scope"/> ? Les bonus de dégâts du lot 6a le réutilisent tel
    /// quel — « vos flèches », « en mêlée » et « vos attaques » ne sont pas re-codés ailleurs.</summary>
    public static bool InWeaponScope(ConditionWeaponScope scope, Skill target, WeaponKind equipped) =>
        InScope(scope, target, equipped);

    /// <summary><paramref name="target"/> se lance-t-elle avec l'ARME du set actif ? Un mod élémentaire convertit
    /// les dégâts de l'arme qui le porte, pas ceux des autres : une corde Fiery ne rend pas une attaque à l'épée
    /// élémentaire.</summary>
    public static bool UsesEquippedWeapon(Skill target, WeaponKind equipped) =>
        equipped != WeaponKind.None && WeaponStrike.IsWeaponAttack(target)
        && AttackWeapon(target, equipped) == equipped;

    private static bool InScope(ConditionWeaponScope scope, Skill target, WeaponKind equipped)
    {
        if (!WeaponStrike.IsWeaponAttack(target)) return false;
        var kind = AttackWeapon(target, equipped);
        return scope switch
        {
            ConditionWeaponScope.Bow     => kind == WeaponKind.Bow,
            ConditionWeaponScope.Daggers => kind == WeaponKind.Daggers,
            ConditionWeaponScope.Scythe  => kind == WeaponKind.Scythe,
            // « en mêlée » (glossaire G2) : les 5 armes de corps à corps, plus la lance sur la seule
            // Frappe du javelot, qui est bien un « Spear Melee Attack ». Sans arme renseignée, une
            // « Melee Attack » à arme libre reste de la mêlée — ses 5 armes possibles le sont toutes.
            ConditionWeaponScope.Melee   => IsMeleeKind(kind)
                                            || target.SkillType == "Spear Melee Attack"
                                            || (kind == WeaponKind.None && target.SkillType == "Melee Attack"),
            _ => IsPhysical(kind) || (kind == WeaponKind.None && target.SkillType == "Melee Attack"),
        };
    }

    private static bool IsMeleeKind(WeaponKind kind) =>
        kind is WeaponKind.Axe or WeaponKind.Sword or WeaponKind.Hammer
             or WeaponKind.Daggers or WeaponKind.Scythe;

    // ── Lignes d'infobulle ─────────────────────────────────────────────────────

    /// <summary>
    /// Lignes « Durée de X effective » à afficher pour <paramref name="skill"/>, dans l'ordre canonique des
    /// conditions. Les conditions AJOUTÉES entrent dans la MÊME liste que les conditions propres de la
    /// compétence (simplification validée par Philippe le 16/09) : les allongeurs s'y appliquent alors sans
    /// cas particulier. Quand les deux posent la même condition, la plus longue gagne — règle du jeu quand on
    /// réapplique une condition. Une condition PROPRE que rien n'allonge n'a pas de ligne : sa durée est déjà
    /// écrite dans la description.
    /// </summary>
    public static IReadOnlyList<ConditionLine> Lines(Skill skill, string resolved, ConditionDurations effects)
    {
        if (!effects.Any) return [];

        var baseSeconds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var own in ConditionInfliction.ForResolved(skill, resolved))
            baseSeconds[own.Condition] = Math.Max(baseSeconds.GetValueOrDefault(own.Condition), own.BaseSeconds);

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inflicted in effects.Added ?? [])
        {
            baseSeconds[inflicted.Condition] =
                Math.Max(baseSeconds.GetValueOrDefault(inflicted.Condition), inflicted.BaseSeconds);
            added.Add(inflicted.Condition);
        }

        var lines = new List<ConditionLine>();
        foreach (var condition in GwConditionData.All)
        {
            if (!baseSeconds.TryGetValue(condition.Name, out int seconds) || seconds <= 0) continue;
            int final = effects.Extend(condition.Name, seconds);
            if (final == seconds && !added.Contains(condition.Name)) continue;
            lines.Add(new ConditionLine(condition.Name, final));
        }
        return lines;
    }
}
