namespace ZCodex.Core.Models;

public record GwAttribute(int Id, string Name, string NameFr, Profession Profession, bool IsPrimary)
{
    // Nom affiché (langue courante). Name (EN) reste la clé de matching/logique/.pn3.
    public string DisplayName => AppLanguage.IsFr ? NameFr : Name;
}

public static class GwAttributeData
{
    public static readonly IReadOnlyList<GwAttribute> All = new GwAttribute[]
    {
        // Mesmer
        new(0,  "Fast Casting",        "Incantation rapide",     Profession.Mesmer,        true),
        new(1,  "Illusion Magic",      "Magie de l'illusion",    Profession.Mesmer,        false),
        new(2,  "Domination Magic",    "Magie de domination",    Profession.Mesmer,        false),
        new(3,  "Inspiration Magic",   "Magie de l'inspiration", Profession.Mesmer,        false),
        // Necromancer
        new(4,  "Blood Magic",         "Magie du sang",          Profession.Necromancer,   false),
        new(5,  "Death Magic",         "Magie de la mort",       Profession.Necromancer,   false),
        new(6,  "Soul Reaping",        "Moisson des âmes",       Profession.Necromancer,   true),
        new(7,  "Curses",              "Malédictions",           Profession.Necromancer,   false),
        // Elementalist
        new(8,  "Air Magic",           "Magie de l'air",         Profession.Elementalist,  false),
        new(9,  "Earth Magic",         "Magie de la terre",      Profession.Elementalist,  false),
        new(10, "Fire Magic",          "Magie du feu",           Profession.Elementalist,  false),
        new(11, "Water Magic",         "Magie de l'eau",         Profession.Elementalist,  false),
        new(12, "Energy Storage",      "Stockage d'énergie",     Profession.Elementalist,  true),
        // Monk
        new(13, "Healing Prayers",     "Prières de soins",       Profession.Monk,          false),
        new(14, "Smiting Prayers",     "Prières de châtiment",   Profession.Monk,          false),
        new(15, "Protection Prayers",  "Prières de protection",  Profession.Monk,          false),
        new(16, "Divine Favor",        "Faveur divine",          Profession.Monk,          true),
        // Warrior
        new(17, "Strength",            "Force",                  Profession.Warrior,       true),
        new(18, "Axe Mastery",         "Maîtrise de la hache",   Profession.Warrior,       false),
        new(19, "Hammer Mastery",      "Maîtrise du marteau",    Profession.Warrior,       false),
        new(20, "Swordsmanship",       "Maîtrise de l'épée",     Profession.Warrior,       false),
        new(21, "Tactics",             "Tactique",               Profession.Warrior,       false),
        // Ranger
        new(22, "Beast Mastery",       "Maîtrise des bêtes",     Profession.Ranger,        false),
        new(23, "Expertise",           "Expertise",              Profession.Ranger,        true),
        new(24, "Wilderness Survival", "Survie",                 Profession.Ranger,        false),
        new(25, "Marksmanship",        "Adresse au tir",         Profession.Ranger,        false),
        // IDs 26–28 : trous officiels dans le format template GW1 (non assignés).
        // IDs 26–44 corrigés le 2026-06-28 d'après wiki.guildwars.com/wiki/Skill_template_format.
        // Le code original avait décalé toutes les professions Factions/Nightfall à partir de 26 :
        //   Assassin non-prim. 26-28 (→ 29-31), Paragon 29-32 (→ 37-40), Ritualist 33-36 (→ 32-34+36),
        //   Critical Strikes 37 (→ 35), Dervish 38-41 (→ 41-44), IDs 42-44 manquants.
        // Seul Spawning Power (36) était accidentellement correct.
        // QA à faire pour valider le décodage de templates réels.

        // Assassin (non-primary)
        new(29, "Dagger Mastery",      "Maîtrise de la dague",   Profession.Assassin,      false),
        new(30, "Deadly Arts",         "Arts mortels",           Profession.Assassin,      false),
        new(31, "Shadow Arts",         "Arts de l'ombre",        Profession.Assassin,      false),
        // Ritualist
        new(32, "Communing",           "Communion",              Profession.Ritualist,     false),
        new(33, "Restoration Magic",   "Magie de restauration",  Profession.Ritualist,     false),
        new(34, "Channeling Magic",    "Magie de la canalisation", Profession.Ritualist,   false),
        // Assassin (primary)
        new(35, "Critical Strikes",    "Coups critiques",        Profession.Assassin,      true),
        // Ritualist (primary)
        new(36, "Spawning Power",      "Puissance de l'invocation", Profession.Ritualist,  true),
        // Paragon
        new(37, "Spear Mastery",       "Maîtrise du javelot",    Profession.Paragon,       false),
        new(38, "Command",             "Commandement",           Profession.Paragon,       false),
        new(39, "Motivation",          "Motivation",             Profession.Paragon,       false),
        new(40, "Leadership",          "Charisme",               Profession.Paragon,       true),
        // Dervish
        new(41, "Scythe Mastery",      "Maîtrise de la faux",    Profession.Dervish,       false),
        new(42, "Wind Prayers",        "Prières du vent",        Profession.Dervish,       false),
        new(43, "Earth Prayers",       "Prières de la terre",    Profession.Dervish,       false),
        new(44, "Mysticism",           "Mysticisme",             Profession.Dervish,       true),
    };

    // Pistes de titre PvE traitées comme des caractéristiques (rang 0–10, hors budget, sans bonus).
    // Fonctionnellement des attributs : la progression des skills est indexée par rang de titre,
    // et le wiki confirme qu'elles répondent à Sceau des Illusions comme un attribut.
    // Allegiance = titre Kurzick/Luxon (Factions). Hors GwAttributeData (pas de profession).
    public static readonly IReadOnlyList<string> TitleRanks = new[]
    {
        "Allegiance rank", "Asura rank", "Deldrimor rank", "Ebon Vanguard rank",
        "Lightbringer rank", "Norn rank", "Sunspear rank",
    };

    private static readonly HashSet<string> _titleRankSet =
        new(TitleRanks, StringComparer.OrdinalIgnoreCase);

    public static bool IsTitleRank(string? name) => name != null && _titleRankSet.Contains(name);

    // Catégories "PvE only" transverses de la page wiki/PvE-only_skill (0 skill en base
    // actuellement, mais conservées pour robustesse si le scrape en remonte plus tard).
    private static readonly HashSet<string> _pveOnlyCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Factions PvE only (all professions)",
        "Nightfall PvE only (all professions)",
        "Eye of the North PvE only",
    };

    // Un attribut est "PvE only" s'il s'agit d'un rang de titre ou d'une catégorie transverse.
    // Sert à scinder la liste des caractéristiques de profession du panneau PvE-only du catalogue.
    public static bool IsPveOnlyAttribute(string? name) =>
        IsTitleRank(name) || (name != null && _pveOnlyCategories.Contains(name));

    // Catégorie synthétique du panneau PvE-only : les elites anniversaire gardent l'attribut de
    // leur profession (donc visibles dans le filtre de carac) ET sont regroupées ici par nom.
    public const string AnniversaryEliteCategory = "Anniversary Celebration Elite Skills";

    private static readonly HashSet<string> _anniversaryElites = new(StringComparer.OrdinalIgnoreCase)
    {
        "\"Together as One!\"", "Heroic Refrain", "Judgment Strike", "Over the Limit",
        "Seven Weapons Stance", "Shadow Theft", "Soul Taker", "Time Ward",
        "Vow of Revolution", "Weapons of Three Forges",
    };

    public static bool IsAnniversaryElite(string? name) =>
        name != null && _anniversaryElites.Contains(name);

    // Compétences INTRINSÈQUEMENT PvE-only (n'existent qu'en PvE) : attribut rang de titre / catégorie
    // transverse PvE, ou élite anniversaire (qui garde son attribut de profession). = l'union exacte des
    // catégories du panneau "PvE only". NB : la version PvE d'une compétence SPLITTÉE est aussi utilisable
    // uniquement en PvE, mais ça dépend du catalogue chargé (nom "(PvP)" jumeau) → géré au niveau du
    // filtre de mode du catalogue, pas ici.
    public static bool IsPveOnlySkill(Skill s) =>
        IsPveOnlyAttribute(s.Attribute) || IsAnniversaryElite(s.Name);

    // Entrée méta du panneau caractéristiques : aucune restriction d'attribut.
    public const string AllAttributesLabel = "All attributes";

    // Valeur d'attribut des compétences qui n'en ont aucune (78 en base) : une VRAIE valeur stockée,
    // jamais une chaîne vide — d'où le test par nom. La garde sur le vide couvre un scrape futur.
    public const string NoAttributeName = "No Attribute";
    public static bool IsNoAttribute(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Equals(NoAttributeName, StringComparison.OrdinalIgnoreCase);

    // Regroupements campagne du panneau PvE-only. Le champ Campaign des skills cross-profession
    // (Profession.None) est vide en base, d'où le mapping explicite rang de titre → campagne.
    public const string FactionsPveCategory = "Factions PvE only skills";
    public const string NightfallPveCategory = "Nightfall PvE only skills";
    public const string EotnPveCategory = "Eye of the North PvE only skills";

    // Définition d'une entrée du panneau PvE-only : libellé, niveau d'indentation (0 = umbrella
    // campagne, 1 = sous-catégorie) et prédicat d'appartenance. L'umbrella matche l'union de ses
    // sous-catégories. Allegiance est split Kurzick/Luxon par suffixe de nom (l'umbrella Factions
    // couvre déjà l'ensemble, donc un seul "Allegiance rank" serait redondant).
    public record PveCategoryDef(string Label, int Indent, Func<Skill, bool> Matches);

    public static IReadOnlyList<PveCategoryDef> PveCategoryDefs { get; } = BuildPveCategoryDefs();

    private static IReadOnlyList<PveCategoryDef> BuildPveCategoryDefs()
    {
        static Func<Skill, bool> Attr(string a) =>
            s => string.Equals(s.Attribute, a, StringComparison.OrdinalIgnoreCase);
        static Func<Skill, bool> KurzickLuxon(string suffix) =>
            s => string.Equals(s.Attribute, "Allegiance rank", StringComparison.OrdinalIgnoreCase)
                 && s.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        var lightbringer = Attr("Lightbringer rank");
        var sunspear     = Attr("Sunspear rank");
        var asura        = Attr("Asura rank");
        var deldrimor    = Attr("Deldrimor rank");
        var ebon         = Attr("Ebon Vanguard rank");
        var norn         = Attr("Norn rank");

        return new PveCategoryDef[]
        {
            new(FactionsPveCategory, 0, Attr("Allegiance rank")),
            new("Kurzick", 1, KurzickLuxon("(Kurzick)")),
            new("Luxon",   1, KurzickLuxon("(Luxon)")),

            new(NightfallPveCategory, 0, s => lightbringer(s) || sunspear(s)),
            new("Lightbringer rank", 1, lightbringer),
            new("Sunspear rank",     1, sunspear),

            new(EotnPveCategory, 0, s => asura(s) || deldrimor(s) || ebon(s) || norn(s)),
            new("Asura rank",         1, asura),
            new("Deldrimor rank",     1, deldrimor),
            new("Ebon Vanguard rank", 1, ebon),
            new("Norn rank",          1, norn),

            new(AnniversaryEliteCategory, 0, s => IsAnniversaryElite(s.Name)),
        };
    }

    private static readonly Dictionary<int, GwAttribute> _byId =
        All.ToDictionary(a => a.Id);

    private static readonly Dictionary<string, GwAttribute> _byName =
        All.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

    // Libellés FR des chaînes portées par les consommateurs (clé EN = canon de filtrage/logique).
    // Couvre : les 44 caractéristiques, la méta « All attributes », les rangs de titre et les
    // catégories du panneau PvE-only. NB : « PvE only » reste identique en FR (choix Philippe).
    private static readonly Dictionary<string, string> _frLabels = BuildFrLabels();

    private static Dictionary<string, string> BuildFrLabels()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in All) d[a.Name] = a.NameFr;

        d[AllAttributesLabel] = "Toutes les caractéristiques";
        d[NoAttributeName]    = "Sans caractéristique";   // confirmé Philippe 25/07/2026

        // Rangs de titre (traités comme des caractéristiques dans le panneau PvE-only).
        // Sunspear/Lightbringer/Ebon Vanguard confirmés Philippe 21/07 ; Allégeance/Asura/Deldrimor/Norn à confirmer.
        d["Allegiance rank"]     = "Allégeance";
        d["Asura rank"]          = "Asura";
        d["Deldrimor rank"]      = "Deldrimor";
        d["Ebon Vanguard rank"]  = "Avant-garde d'Ebon";
        d["Lightbringer rank"]   = "Porteur de lumière";
        d["Norn rank"]           = "Norn";
        d["Sunspear rank"]       = "Lanciers du Soleil";

        // Catégories campagne du panneau PvE-only (umbrellas + entrée anniversaire).
        d[FactionsPveCategory]      = "Compétences PvE de Factions";
        d[NightfallPveCategory]     = "Compétences PvE de Nightfall";
        d[EotnPveCategory]          = "Compétences PvE d'Eye of the North";
        d[AnniversaryEliteCategory] = "Compétences élite anniversaire";
        // « Kurzick »/« Luxon » : identiques en FR.

        return d;
    }

    // Libellé affiché (langue courante) pour un nom d'attribut / rang de titre / catégorie PvE
    // ANGLAIS. Ne traduit qu'à l'affichage ; l'EN reste la clé de filtrage. Repli = la chaîne EN.
    public static string DisplayName(string? english) =>
        AppLanguage.IsFr && english != null && _frLabels.TryGetValue(english, out var fr) ? fr : english ?? string.Empty;

    public static GwAttribute? ById(int id)         => _byId.GetValueOrDefault(id);
    public static GwAttribute? ByName(string name)  => _byName.GetValueOrDefault(name);
    public static IEnumerable<GwAttribute> ForProfession(Profession p) => All.Where(a => a.Profession == p);
    public static GwAttribute? PrimaryFor(Profession p) => All.FirstOrDefault(a => a.Profession == p && a.IsPrimary);
}
