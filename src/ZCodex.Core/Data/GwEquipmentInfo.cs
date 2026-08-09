using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

// Type d'arme au sens du format template : détermine le slot (main/off-hand), le maniement
// (1 main / 2 mains) et les upgrades compatibles (préfixe/suffixe par type).
public enum WeaponKind
{
    None = 0,
    // Une main (main principale) — se combine avec focus OU bouclier.
    Axe, Sword, Spear, Wand,
    // Deux mains — occupe les deux slots, off-hand désactivé.
    Hammer, Daggers, Scythe, Bow, Staff,
    // Off-hand uniquement.
    Focus, Shield,
}

// Catégorie d'upgrade (wiki/Upgrade_component) : une pièce d'armure porte 1 insigne + 1 rune
// (jamais d'inscription) ; une arme porte préfixe + suffixe + inscription selon son type.
public enum ModCategory
{
    Unknown = 0,
    Insignia,      // préfixe d'armure
    Rune,          // suffixe d'armure
    WeaponPrefix,  // Haft / Hilt / String / Tang / Snathe / Spearhead / Staff Head
    WeaponSuffix,  // Grip / Pommel / Handle / Wrapping / Core
    Inscription,   // noms entre guillemets
}

// Classe de compatibilité d'une inscription (colonne "Attaches to" de wiki/List_of_weapon_upgrades) :
// AnyWeapon = toute arme de main principale (martiale ou caster), jamais focus/bouclier.
public enum InscriptionClass
{
    None = 0,           // pas une inscription
    AnyWeapon,          // "Weapons"
    MartialWeapon,      // "Martial weapons"
    SpellcastingWeapon, // "Spellcasting weapons" (baguette + bâton)
    FocusOnly,          // "Focus items"
    FocusOrShield,      // "Focus items or shields"
}

public sealed record EquipItemInfo(int ItemId, string Name, int Slot, WeaponKind Weapon, Profession Profession);

public sealed record EquipModInfo(int ModId, string Name, ModCategory Category, WeaponKind Weapon, Profession Profession,
    InscriptionClass Inscription = InscriptionClass.None);

// Classification des items/mods de GwEquipmentData (dictionnaires plats Id→Nom) : slot, type
// d'arme, catégorie d'upgrade et profession. Heuristique par mots du nom + overrides explicites
// pour les cas ambigus (même pattern que BreakpointOverrides). Les entrées non classées restent
// accessibles (UnclassifiedItems/UnclassifiedMods) pour la curation manuelle.
public static class GwEquipmentInfo
{
    public const int SlotWeapon  = 0;
    public const int SlotOffHand = 1;
    public const int SlotChest   = 2;
    public const int SlotLegs    = 3;
    public const int SlotHead    = 4;
    public const int SlotFeet    = 5;
    public const int SlotHands   = 6;

    public static bool IsTwoHanded(WeaponKind k) =>
        k is WeaponKind.Hammer or WeaponKind.Daggers or WeaponKind.Scythe or WeaponKind.Bow or WeaponKind.Staff;

    public static bool IsOffHandKind(WeaponKind k) => k is WeaponKind.Focus or WeaponKind.Shield;

    public static bool IsArmorSlot(int slot) => slot is >= SlotChest and <= SlotHands;

    // ── Overrides explicites (cas que l'heuristique ne peut pas trancher) ────────
    // "Cesta" désigne un focus (Grim Cesta) ET une baguette (Deathly Cesta) — à valider Philippe.
    private static readonly Dictionary<int, (int Slot, WeaponKind Weapon)> _itemOverrides = new()
    {
        { 122, (SlotOffHand, WeaponKind.Focus) }, // PvP Grim Cesta
        { 137, (SlotWeapon,  WeaponKind.Wand)  }, // PvP Deathly Cesta
    };

    // Sets de campagne sans tag parenthésé ni mot-clé discriminant : profession dérivée du
    // cluster d'IDs (les voisins immédiats portent un tag explicite, ex. 163-166 Seitung entoure
    // "Shing Jea Shoes (Assassin)" 159-162 et "Imperial Shoes (Assassin)" 167-170). À valider
    // Philippe via docs/equipment_classification.md.
    private static readonly Dictionary<int, Profession> _itemProfessionOverrides = new()
    {
        { 163, Profession.Assassin },     // Seitung Shoes
        { 165, Profession.Assassin },     // Seitung Gloves
        { 166, Profession.Assassin },     // Seitung Leggings
        { 183, Profession.Mesmer },       // Krytan Footwear
        { 204, Profession.Elementalist }, // Krytan Robes
        { 208, Profession.Elementalist }, // Canthan Robes
        { 212, Profession.Elementalist }, // Shing Jea Robes
        { 215, Profession.Monk },         // Canthan Sandals
        { 218, Profession.Monk },         // Canthan Pants
        { 219, Profession.Monk },         // Krytan Sandals
        { 222, Profession.Monk },         // Krytan Pants
        { 223, Profession.Monk },         // Shing Jea Sandals
        { 226, Profession.Monk },         // Shing Jea Pants
        { 252, Profession.Ritualist },    // Exotic Raiment
        { 256, Profession.Ritualist },    // Canthan Raiment
        { 260, Profession.Ritualist },    // Imperial Raiment
        { 264, Profession.Ritualist },    // Shing Jea Raiment
        { 268, Profession.Ritualist },    // Seitung Raiment
        { 270, Profession.Ritualist },    // Seitung Leggings (doublon de nom avec 166, Assassin)
        { 303, Profession.Dervish },      // Sunspear Robes
        { 314, Profession.Paragon },      // Sunspear Sandals
        { 315, Profession.Paragon },      // Sunspear Raiment
        { 318, Profession.Paragon },      // Elonian Sandals
        { 319, Profession.Paragon },      // Elonian Raiment
    };

    // Rune of Absorption : pas un attribut → l'heuristique la voit universelle ; en jeu elle est
    // réservée au Guerrier (CONFIRMÉ Philippe 28/07/2026).
    private static readonly Dictionary<int, Profession> _modProfessionOverrides = new()
    {
        { 159, Profession.Warrior }, // Rune of Minor Absorption
        { 160, Profession.Warrior }, // Rune of Major Absorption
        { 161, Profession.Warrior }, // Rune of Superior Absorption
    };

    // ── Inscriptions : classe de compatibilité (wiki/List_of_weapon_upgrades, lu 04/07/2026) ──
    private static readonly Dictionary<int, InscriptionClass> _inscriptionClasses = new()
    {
        // "Weapons" — toute arme de main principale.
        { 338, InscriptionClass.AnyWeapon }, // "Strength and Honor"
        { 283, InscriptionClass.AnyWeapon }, // "Guided by Fate"
        { 287, InscriptionClass.AnyWeapon }, // "Dance with Death"
        { 289, InscriptionClass.AnyWeapon }, // "To the Pain!"
        { 288, InscriptionClass.AnyWeapon }, // "Brawn over Brains"
        { 282, InscriptionClass.AnyWeapon }, // "Too Much Information"
        { 339, InscriptionClass.AnyWeapon }, // "Vengeance is Mine"
        { 286, InscriptionClass.AnyWeapon }, // "Don't Fear the Reaper"
        { 281, InscriptionClass.AnyWeapon }, // "Don't Think Twice"
        // "Martial weapons".
        { 329, InscriptionClass.MartialWeapon }, // "I have the power!"
        { 277, InscriptionClass.MartialWeapon }, // "Let the Memory Live Again"
        // "Spellcasting weapons" — baguette + bâton.
        { 335, InscriptionClass.SpellcastingWeapon }, // "Aptitude not Attitude"
        { 336, InscriptionClass.SpellcastingWeapon }, // "Seize the Day"
        { 337, InscriptionClass.SpellcastingWeapon }, // "Hale and Hearty"
        { 278, InscriptionClass.SpellcastingWeapon }, // "Have Faith"
        { 279, InscriptionClass.SpellcastingWeapon }, // "Don't call it a comeback!"
        { 280, InscriptionClass.SpellcastingWeapon }, // "I am Sorrow"
        // "Focus items".
        { 327, InscriptionClass.FocusOnly }, // "Serenity Now"
        { 328, InscriptionClass.FocusOnly }, // "Forget Me Not"
        { 326, InscriptionClass.FocusOnly }, // "Live for Today"
        { 325, InscriptionClass.FocusOnly }, // "Faith is My Shield"
        { 368, InscriptionClass.FocusOnly }, // "Ignorance is Bliss"
        { 369, InscriptionClass.FocusOnly }, // "Life is Pain"
        { 370, InscriptionClass.FocusOnly }, // "Man for All Seasons"
        { 371, InscriptionClass.FocusOnly }, // "Survival of the Fittest"
        { 372, InscriptionClass.FocusOnly }, // "Might makes Right"
        { 373, InscriptionClass.FocusOnly }, // "Knowing is Half the Battle"
        { 374, InscriptionClass.FocusOnly }, // "Down But Not Out"
        { 375, InscriptionClass.FocusOnly }, // "Hail to the King"
        { 376, InscriptionClass.FocusOnly }, // "Be Just and Fear Not"
        // "Focus items or shields".
        { 330, InscriptionClass.FocusOrShield }, // "Luck of the Draw"
        { 331, InscriptionClass.FocusOrShield }, // "Sheltered by Faith"
        { 332, InscriptionClass.FocusOrShield }, // "Nothing to Fear"
        { 333, InscriptionClass.FocusOrShield }, // "Run For Your Life!"
        { 334, InscriptionClass.FocusOrShield }, // "Master of My Domain"
        { 377, InscriptionClass.FocusOrShield }, // "Not the face!"
        { 378, InscriptionClass.FocusOrShield }, // "Leaf on the Wind"
        { 379, InscriptionClass.FocusOrShield }, // "Like a Rolling Stone"
        { 380, InscriptionClass.FocusOrShield }, // "Riders on the Storm"
        { 381, InscriptionClass.FocusOrShield }, // "Sleep Now in the Fire"
        { 382, InscriptionClass.FocusOrShield }, // "Through Thick and Thin"
        { 383, InscriptionClass.FocusOrShield }, // "The Riddle of Steel"
        { 384, InscriptionClass.FocusOrShield }, // "Fear Cuts Deeper"
        { 385, InscriptionClass.FocusOrShield }, // "I Can See Clearly Now"
        { 386, InscriptionClass.FocusOrShield }, // "Swift as the Wind"
        { 387, InscriptionClass.FocusOrShield }, // "Strength of Body"
        { 388, InscriptionClass.FocusOrShield }, // "Cast Out the Unclean"
        { 389, InscriptionClass.FocusOrShield }, // "Pure of Heart"
        { 284, InscriptionClass.FocusOrShield }, // "Soundness of Mind"
        { 285, InscriptionClass.FocusOrShield }, // "Only the Strong Survive"
    };

    // ── Insignes : profession (wiki/Insignia, lu 04/07/2026). Absent = commune (7 Common). ──
    private static readonly Dictionary<int, Profession> _insigniaProfessions = new()
    {
        // Warrior
        { 316, Profession.Warrior },      // Dreadnought
        { 313, Profession.Warrior },      // Knight's
        { 314, Profession.Warrior },      // Lieutenant's
        { 317, Profession.Warrior },      // Sentinel's
        { 315, Profession.Warrior },      // Stonefist
        // Ranger
        { 318, Profession.Ranger },       // Frostbound
        { 319, Profession.Ranger },       // Pyrebound
        { 321, Profession.Ranger },       // Scout's
        { 320, Profession.Ranger },       // Stormbound
        { 363, Profession.Ranger },       // Earthbound
        { 364, Profession.Ranger },       // Beastmaster's
        // Monk
        { 311, Profession.Monk },         // Wanderer's
        { 312, Profession.Monk },         // Disciple's
        { 362, Profession.Monk },         // Anchorite's
        // Necromancer
        { 306, Profession.Necromancer },  // Blighter's
        { 302, Profession.Necromancer },  // Bloodstained
        { 304, Profession.Necromancer },  // Bonelace
        { 305, Profession.Necromancer },  // Minion Master's
        { 303, Profession.Necromancer },  // Tormentor's
        { 360, Profession.Necromancer },  // Undertaker's
        // Mesmer
        { 301, Profession.Mesmer },       // Virtuoso's
        { 358, Profession.Mesmer },       // Artificer's
        { 359, Profession.Mesmer },       // Prodigy's
        // Elementalist
        { 310, Profession.Elementalist }, // Aeromancer's
        { 308, Profession.Elementalist }, // Geomancer's
        { 307, Profession.Elementalist }, // Hydromancer's
        { 309, Profession.Elementalist }, // Pyromancer's
        { 361, Profession.Elementalist }, // Prismatic
        // Assassin
        { 298, Profession.Assassin },     // Infiltrator's
        { 300, Profession.Assassin },     // Nightstalker's
        { 299, Profession.Assassin },     // Saboteur's
        { 297, Profession.Assassin },     // Vanguard's
        // Ritualist
        { 323, Profession.Ritualist },    // Ghost Forge
        { 324, Profession.Ritualist },    // Mystic's
        { 322, Profession.Ritualist },    // Shaman's
        // Paragon
        { 367, Profession.Paragon },      // Centurion's
        // Dervish
        { 366, Profession.Dervish },      // Forsaken
        { 365, Profession.Dervish },      // Windwalker
    };

    // ── Armure : mot-clé de pièce → slot ─────────────────────────────────────────
    // L'ordre de déclaration compte : premier mot-clé contenu dans le nom gagne.
    private static readonly (string Keyword, int Slot)[] _armorKeywords =
    {
        // "Ascetic X Design" (Moine) : la pièce est dans la phrase, comme les Scar Patterns.
        ("Foot Design", SlotFeet), ("Chest Design", SlotChest), ("Arm Design", SlotHands), ("Leg Design", SlotLegs),
        ("Footwear", SlotFeet), ("Boots", SlotFeet), ("Shoes", SlotFeet), ("Sandals", SlotFeet),
        ("Hose", SlotLegs), ("Pants", SlotLegs), ("Leggings", SlotLegs),
        ("Attire", SlotChest), ("Tunic", SlotChest), ("Robes", SlotChest), ("Vestments", SlotChest),
        ("Cuirass", SlotChest), ("Vest", SlotChest), ("Raiment", SlotChest), ("Guise", SlotChest),
        ("Gloves", SlotHands), ("Gauntlets", SlotHands), ("Handwraps", SlotHands),
        ("Bangles", SlotHands), ("Vambraces", SlotHands), ("Armguards", SlotHands),
        ("Mask", SlotHead), ("Helm", SlotHead), ("Eye", SlotHead), ("Scalp Design", SlotHead),
        ("Hood", SlotHead), ("Crest", SlotHead), ("Headwrap", SlotHead),
    };

    // "X Scar Pattern" (Necromancer) : la partie du corps est dans le premier mot, sinon tête.
    private static readonly Dictionary<string, int> _scarPatternParts = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Foot", SlotFeet }, { "Chest", SlotChest }, { "Arm", SlotHands }, { "Leg", SlotLegs },
    };

    // ── Armure : famille d'art → profession ──────────────────────────────────────
    // Sets core (Prophecies/PvP) dont le nom de famille est propre à une profession.
    private static readonly Dictionary<string, Profession> _armorFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        // Mesmer
        { "Enchanter", Profession.Mesmer }, { "Theatrical", Profession.Mesmer }, { "Rogue", Profession.Mesmer },
        { "Imposing", Profession.Mesmer }, { "Sleek", Profession.Mesmer }, { "Costume", Profession.Mesmer },
        { "Animal", Profession.Mesmer },
        // Necromancer
        { "Wretched", Profession.Necromancer }, { "Banished", Profession.Necromancer },
        { "Ragged", Profession.Necromancer }, { "Wicked", Profession.Necromancer },
        { "Vile", Profession.Necromancer }, { "Devilish", Profession.Necromancer },
        // Elementalist
        { "Stormcloud", Profession.Elementalist }, { "Granite", Profession.Elementalist },
        { "Magma", Profession.Elementalist }, { "Frozen", Profession.Elementalist },
        { "Storm's", Profession.Elementalist }, { "Stone's", Profession.Elementalist },
        { "All-Seeing", Profession.Elementalist }, { "Flame's", Profession.Elementalist },
        { "Glacier's", Profession.Elementalist },
        // Monk
        { "Consecrated", Profession.Monk }, { "Ascetic", Profession.Monk }, { "Militant", Profession.Monk },
        { "Divine", Profession.Monk }, { "Healer", Profession.Monk }, { "Protector", Profession.Monk },
        { "Smiting", Profession.Monk },
        // Warrior
        { "Dragon", Profession.Warrior }, { "Plated", Profession.Warrior }, { "Gladiator", Profession.Warrior },
        { "Executioner", Profession.Warrior }, { "Dwarven", Profession.Warrior },
        { "Duelist", Profession.Warrior }, { "Brute", Profession.Warrior },
        // Ranger
        { "Druidic", Profession.Ranger }, { "Fur-Lined", Profession.Ranger },
        { "Studded", Profession.Ranger }, { "Drakescale", Profession.Ranger },
        { "Tamer", Profession.Ranger }, { "Archer", Profession.Ranger },
        { "Hunter", Profession.Ranger }, { "Traveler", Profession.Ranger },
        // Assassin
        { "Bladed", Profession.Assassin }, { "Keen", Profession.Assassin },
        { "Deadly", Profession.Assassin }, { "Shadow", Profession.Assassin },
        // Ritualist
        { "Communing", Profession.Ritualist }, { "Summoning", Profession.Ritualist },
        { "Restoring", Profession.Ritualist }, { "Channeling", Profession.Ritualist },
        // Dervish
        { "Reaper", Profession.Dervish }, { "Mystic", Profession.Dervish },
        { "Dunewalker", Profession.Dervish }, { "Windwalker", Profession.Dervish },
        // Paragon
        { "Spear", Profession.Paragon }, { "Exalted", Profession.Paragon },
        { "Vocal", Profession.Paragon }, { "Command", Profession.Paragon },
    };

    // Mot-clé de pièce propre à une profession (sets campagne sans tag parenthésé : "Krytan Attire"
    // est forcément Mesmer car "Attire" n'existe que chez le Mesmer, etc.).
    private static readonly Dictionary<string, Profession> _pieceKeywordProfession = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Attire", Profession.Mesmer }, { "Hose", Profession.Mesmer },
        { "Tunic", Profession.Necromancer }, { "Scar Pattern", Profession.Necromancer },
        { "Vestments", Profession.Monk }, { "Handwraps", Profession.Monk }, { "Scalp Design", Profession.Monk },
        { "Cuirass", Profession.Warrior }, { "Gauntlets", Profession.Warrior },
        { "Vest", Profession.Ranger },
        { "Guise", Profession.Assassin },
        { "Bangles", Profession.Ritualist }, { "Headwrap", Profession.Ritualist },
        { "Vambraces", Profession.Dervish }, { "Hood", Profession.Dervish },
        { "Armguards", Profession.Paragon }, { "Crest", Profession.Paragon },
    };

    // ── Armes : mot-clé → type ────────────────────────────────────────────────────
    private static readonly (string Keyword, WeaponKind Kind)[] _weaponKeywords =
    {
        ("Flatbow", WeaponKind.Bow), ("Hornbow", WeaponKind.Bow), ("Longbow", WeaponKind.Bow),
        ("Shortbow", WeaponKind.Bow), ("Recurve Bow", WeaponKind.Bow),
        ("Daggers", WeaponKind.Daggers),
        ("Hammer", WeaponKind.Hammer),
        ("Scythe", WeaponKind.Scythe),
        ("Axe", WeaponKind.Axe),
        ("Sword", WeaponKind.Sword),
        ("Spear", WeaponKind.Spear),
        ("Staff", WeaponKind.Staff), ("Spire", WeaponKind.Staff),
        ("Wand", WeaponKind.Wand), ("Cane", WeaponKind.Wand), ("Truncheon", WeaponKind.Wand),
        ("Rod", WeaponKind.Wand), ("Scepter", WeaponKind.Wand),
        ("Shield", WeaponKind.Shield),
        ("Chakram", WeaponKind.Focus), ("Artifact", WeaponKind.Focus), ("Chalice", WeaponKind.Focus),
        ("Idol", WeaponKind.Focus), ("Icon", WeaponKind.Focus), ("Scroll", WeaponKind.Focus),
        ("Symbol", WeaponKind.Focus), ("Ankh", WeaponKind.Focus), ("Focus", WeaponKind.Focus),
        ("Cesta", WeaponKind.Focus), // ambigu — les deux Cesta sont dans _itemOverrides
    };

    // ── Mods d'arme : phrase → (catégorie, type) ─────────────────────────────────
    // "Staff Head" avant "Spearhead" serait inutile ("Spearhead" est un seul mot) mais l'ordre
    // général reste : phrases les plus spécifiques d'abord.
    private static readonly (string Phrase, ModCategory Category, WeaponKind Kind)[] _weaponModPhrases =
    {
        ("Staff Head",     ModCategory.WeaponPrefix, WeaponKind.Staff),
        ("Staff Wrapping", ModCategory.WeaponSuffix, WeaponKind.Staff),
        ("Wand Wrapping",  ModCategory.WeaponSuffix, WeaponKind.Wand),
        ("Focus Core",     ModCategory.WeaponSuffix, WeaponKind.Focus),
        ("Shield Handle",  ModCategory.WeaponSuffix, WeaponKind.Shield),
        ("Axe Haft",       ModCategory.WeaponPrefix, WeaponKind.Axe),
        ("Hammer Haft",    ModCategory.WeaponPrefix, WeaponKind.Hammer),
        ("Bow String",     ModCategory.WeaponPrefix, WeaponKind.Bow),
        ("Sword Hilt",     ModCategory.WeaponPrefix, WeaponKind.Sword),
        ("Dagger Tang",    ModCategory.WeaponPrefix, WeaponKind.Daggers),
        ("Scythe Snathe",  ModCategory.WeaponPrefix, WeaponKind.Scythe),
        ("Spearhead",      ModCategory.WeaponPrefix, WeaponKind.Spear),
        ("Axe Grip",       ModCategory.WeaponSuffix, WeaponKind.Axe),
        ("Bow Grip",       ModCategory.WeaponSuffix, WeaponKind.Bow),
        ("Hammer Grip",    ModCategory.WeaponSuffix, WeaponKind.Hammer),
        ("Scythe Grip",    ModCategory.WeaponSuffix, WeaponKind.Scythe),
        ("Spear Grip",     ModCategory.WeaponSuffix, WeaponKind.Spear),
        ("Sword Pommel",   ModCategory.WeaponSuffix, WeaponKind.Sword),
        ("Dagger Handle",  ModCategory.WeaponSuffix, WeaponKind.Daggers),
    };

    // ── Tables publiques (construites au premier accès depuis GwEquipmentData) ───

    public static IReadOnlyDictionary<int, EquipItemInfo> Items { get; }
    public static IReadOnlyDictionary<int, EquipModInfo> Mods { get; }
    public static IReadOnlyList<EquipItemInfo> UnclassifiedItems { get; }
    public static IReadOnlyList<EquipModInfo> UnclassifiedMods { get; }

    static GwEquipmentInfo()
    {
        var items = new Dictionary<int, EquipItemInfo>();
        foreach (var (id, name) in GwEquipmentData.Items)
            items[id] = ClassifyItem(id, name);
        Items = items;
        UnclassifiedItems = items.Values.Where(i => i.Slot < 0).OrderBy(i => i.ItemId).ToList();

        var mods = new Dictionary<int, EquipModInfo>();
        foreach (var (id, name) in GwEquipmentData.Modifiers)
            mods[id] = ClassifyMod(id, name);
        Mods = mods;
        UnclassifiedMods = mods.Values.Where(m => m.Category == ModCategory.Unknown).OrderBy(m => m.ModId).ToList();
    }

    // ── Requêtes pour l'UI ───────────────────────────────────────────────────────

    public static IEnumerable<EquipItemInfo> ItemsForSlot(int slot) =>
        Items.Values.Where(i => i.Slot == slot).OrderBy(i => i.Name);

    // Mods proposables pour un slot donné. Armure → insignes + runes (runes filtrées par
    // profession si fournie : une rune se pose sur l'armure de SA profession, les runes
    // universelles — Vigor, Vitae, Attunement… — passent toujours). Arme → préfixe/suffixe du
    // type + inscriptions (compatibilité fine des inscriptions par type : curation à venir).
    public static IEnumerable<EquipModInfo> ModsFor(int slot, WeaponKind weapon, ModCategory category, Profession profession = Profession.None)
    {
        if (IsArmorSlot(slot))
        {
            if (category == ModCategory.Insignia)
                return Mods.Values.Where(m => m.Category == ModCategory.Insignia
                    && (m.Profession == Profession.None || profession == Profession.None || m.Profession == profession))
                    .OrderBy(m => m.Name);
            if (category == ModCategory.Rune)
                return Mods.Values.Where(m => m.Category == ModCategory.Rune
                    && (m.Profession == Profession.None || profession == Profession.None || m.Profession == profession))
                    .OrderBy(m => m.Name);
            return Enumerable.Empty<EquipModInfo>();
        }

        return category switch
        {
            ModCategory.WeaponPrefix => Mods.Values
                .Where(m => m.Category == ModCategory.WeaponPrefix && m.Weapon == weapon).OrderBy(m => m.Name),
            ModCategory.WeaponSuffix => Mods.Values
                .Where(m => m.Category == ModCategory.WeaponSuffix && m.Weapon == weapon).OrderBy(m => m.Name),
            ModCategory.Inscription => Mods.Values
                .Where(m => m.Category == ModCategory.Inscription && InscriptionFits(m.Inscription, weapon))
                .OrderBy(m => m.Name),
            _ => Enumerable.Empty<EquipModInfo>(),
        };
    }

    // ── Coiffes : attribut du +1 inhérent ─────────────────────────────────────────
    // Sources (04/07/2026) : pages wiki "<Profession> headgear" (ligne PvP de "Naming and
    // bonuses" : Ele, Paragon), pages d'art ("Mesmer Animal Mask"…, "Necromancer Ragged Scar
    // Pattern" : table PvP Equipment, "Ranger Archer's Mask"). Les noms Warrior/Monk/Assassin/
    // Ritualist/Dervish n'existent QUE sur Equipment_template_format (recherche plein-texte) →
    // DÉDUITS du nom, à valider Philippe (marqués "deduit").
    private static readonly Dictionary<int, string> _headgearAttributes = new()
    {
        // Mesmer (pages d'art)
        {  63, "Domination Magic" },    // Imposing Mask
        {  64, "Fast Casting" },        // Sleek Mask
        {  65, "Illusion Magic" },      // Costume Mask
        {  66, "Inspiration Magic" },   // Animal Mask
        // Necromancer (page Ragged Scar Pattern, ligne PvP Equipment)
        {  67, "Blood Magic" },         // Ragged Scar Pattern
        {  68, "Curses" },              // Wicked Scar Pattern
        {  69, "Death Magic" },         // Vile Scar Pattern
        {  70, "Soul Reaping" },        // Devilish Scar Pattern
        // Elementalist (Naming and bonuses, ligne PvP)
        {  71, "Air Magic" },           // Storm's Eye
        {  72, "Earth Magic" },         // Stone's Eye
        {  73, "Energy Storage" },      // All-Seeing Eye
        {  74, "Fire Magic" },          // Flame's Eye
        {  75, "Water Magic" },         // Glacier's Eye
        // Monk — deduit du nom
        {  76, "Divine Favor" },        // Divine Scalp Design
        {  77, "Healing Prayers" },     // Healer Scalp Design
        {  78, "Protection Prayers" },  // Protector Scalp Design
        {  79, "Smiting Prayers" },     // Smiting Scalp Design
        // Warrior — deduit du nom (le moins sûr)
        {  80, "Axe Mastery" },         // Executioner Helm
        {  81, "Hammer Mastery" },      // Dwarven Helm
        {  83, "Swordsmanship" },       // Duelist Helm
        {  84, "Strength" },            // Brute Helm
        {  85, "Tactics" },             // Gladiator Helm
        // Ranger (Tamer/Traveler/Archer via wiki, Hunter par élimination)
        {  86, "Beast Mastery" },       // Tamer Mask
        {  87, "Marksmanship" },        // Archer Mask
        {  88, "Expertise" },           // Hunter Mask
        {  89, "Wilderness Survival" }, // Traveler Mask
        // Assassin — deduit du nom
        { 271, "Dagger Mastery" },      // Bladed Mask
        { 272, "Critical Strikes" },    // Keen Mask
        { 273, "Deadly Arts" },         // Deadly Mask
        { 274, "Shadow Arts" },         // Shadow Mask
        // Ritualist — deduit du nom
        { 275, "Communing" },           // Communing Headwrap
        { 276, "Spawning Power" },      // Summoning Headwrap
        { 277, "Restoration Magic" },   // Restoring Headwrap
        { 278, "Channeling Magic" },    // Channeling Headwrap
        // Dervish — deduit du nom
        { 290, "Scythe Mastery" },      // Reaper Hood
        { 291, "Mysticism" },           // Mystic Hood
        { 292, "Earth Prayers" },       // Dunewalker Hood
        { 293, "Wind Prayers" },        // Windwalker Hood
        // Paragon (Naming and bonuses, ligne PvP)
        { 306, "Spear Mastery" },       // Spear Crest
        { 307, "Leadership" },          // Exalted Crest
        { 308, "Motivation" },          // Vocal Crest
        { 309, "Command" },             // Command Crest
    };

    // Attribut du +1 inhérent d'une coiffe (null si l'item n'est pas une coiffe connue).
    public static string? HeadgearAttribute(int itemId) =>
        _headgearAttributes.GetValueOrDefault(itemId);

    // ── Armes : attribut REQUIS ───────────────────────────────────────────────────
    // Source (30/07/2026) : wiki "PvP equipment", tables par type d'arme, qui apparient chaque
    // objet à son attribut. Le format de template ne stocke QUE l'id de l'objet : le requis n'y
    // est pas encodé, il tient à l'objet lui-même — d'où cette table.
    //
    // Le niveau requis vaut 9 pour TOUT l'équipement PvP (« a requirement of 9 in an appropriate
    // attribute ») : constant, donc non stocké et non affiché — il ne distingue rien.
    //
    // Familles à variante nommée : l'objet SANS parenthèse est celui qui n'a pas eu besoin d'être
    // désambiguïsé (Cane = Domination, Holy Rod/Staff = Faveur divine) ; les autres portent leur
    // attribut dans le nom.
    private static readonly Dictionary<int, string> _weaponAttributes = new()
    {
        // Guerrier / arcs / martiales
        { 110, "Axe Mastery" },        // Axe
        { 133, "Hammer Mastery" },     // Hammer
        { 158, "Swordsmanship" },      // Sword
        { 279, "Dagger Mastery" },     // Daggers
        { 322, "Scythe Mastery" },     // Scythe
        { 325, "Spear Mastery" },      // Spear
        { 111, "Marksmanship" },       // Flatbow
        { 112, "Marksmanship" },       // Hornbow
        { 113, "Marksmanship" },       // Longbow
        { 114, "Marksmanship" },       // Shortbow
        { 115, "Marksmanship" },       // Recurve Bow

        // Boucliers
        { 145, "Strength" },           // Strength Shield
        { 146, "Tactics" },            // Tactics Shield
        { 323, "Motivation" },         // Motivation Shield
        { 324, "Command" },            // Command Shield

        // Baguettes / sceptres / verges
        { 134, "Domination Magic" },   // Cane
        { 327, "Illusion Magic" },     // Cane (Illusion Magic)
        { 135, "Inspiration Magic" },  // Crystal Wand
        { 326, "Fast Casting" },       // Arcane Scepter
        { 136, "Blood Magic" },        // Truncheon
        { 137, "Death Magic" },        // Deathly Cesta
        { 138, "Curses" },             // Accursed Rod
        { 328, "Soul Reaping" },       // Crimson Claw Scepter
        { 139, "Air Magic" },          // Air Wand
        { 140, "Earth Magic" },        // Earth Wand
        { 141, "Fire Magic" },         // Fire Wand
        { 142, "Water Magic" },        // Water Wand
        { 329, "Energy Storage" },     // Voltaic Wand
        { 143, "Divine Favor" },       // Holy Rod
        { 330, "Healing Prayers" },    // Holy Rod (Healing)
        { 331, "Protection Prayers" }, // Holy Rod (Protection)
        { 144, "Smiting Prayers" },    // Smiting Rod
        { 284, "Communing" },          // Communing Scepter
        { 285, "Spawning Power" },     // Spawning Scepter
        { 286, "Channeling Magic" },   // Channeling Scepter
        { 338, "Restoration Magic" },  // Restoration Scepter

        // Bâtons
        { 147, "Domination Magic" },   // Inscribed Staff
        { 148, "Illusion Magic" },     // Jeweled Staff
        { 333, "Inspiration Magic" },  // Scrying Glass Staff
        { 332, "Fast Casting" },       // Arcane Staff
        { 149, "Blood Magic" },        // Blood Staff
        { 150, "Death Magic" },        // Dead Staff
        { 151, "Curses" },             // Accursed Staff
        { 334, "Soul Reaping" },       // Soul Spire
        { 152, "Air Magic" },          // Air Staff
        { 153, "Earth Magic" },        // Earth Staff
        { 154, "Fire Magic" },         // Fire Staff
        { 155, "Water Magic" },        // Water Staff
        { 335, "Energy Storage" },     // Ether Staff
        { 156, "Divine Favor" },       // Holy Staff
        { 336, "Healing Prayers" },    // Holy Staff (Healing)
        { 337, "Protection Prayers" }, // Holy Staff (Protection)
        { 157, "Smiting Prayers" },    // Smiting Staff
        { 287, "Communing" },          // Communing Staff
        { 288, "Spawning Power" },     // Spawning Staff
        { 289, "Channeling Magic" },   // Channeling Staff
        { 339, "Restoration Magic" },  // Restoration Staff

        // Objets tenus (focus)
        { 116, "Domination Magic" },   // Inscribed Chakram
        { 118, "Illusion Magic" },     // Jeweled Chakram
        { 119, "Inspiration Magic" },  // Jeweled Chalice
        { 117, "Fast Casting" },       // Gilded Artifact
        { 120, "Blood Magic" },        // Idol
        { 122, "Death Magic" },        // Grim Cesta
        { 121, "Curses" },             // Accursed Icon
        { 123, "Soul Reaping" },       // Bone Idol
        { 124, "Air Magic" },          // Storm Artifact
        { 125, "Earth Magic" },        // Earth Scroll
        { 127, "Fire Magic" },         // Flame Artifact
        { 128, "Water Magic" },        // Frost Artifact
        { 126, "Energy Storage" },     // Golden Chalice
        { 129, "Divine Favor" },       // Divine Symbol
        { 130, "Healing Prayers" },    // Healing Ankh
        { 131, "Protection Prayers" }, // Protective Icon
        { 132, "Smiting Prayers" },    // Hallowed Idol
        { 280, "Communing" },          // Communing Focus
        { 281, "Spawning Power" },     // Spawning Focus
        { 282, "Restoration Magic" },  // Restoration Focus
        { 283, "Channeling Magic" },   // Channeling Focus
    };

    /// <summary>Attribut requis par une arme / un objet tenu / un bouclier (null si l'objet n'en
    /// exige aucun : armure, emplacement vide, objet inconnu). Nom EN = clé logique ; passer par
    /// <c>GwAttributeData.DisplayName</c> pour l'affichage.</summary>
    public static string? WeaponAttribute(int itemId) =>
        _weaponAttributes.GetValueOrDefault(itemId);

    // Profession probable d'un équipement : celle de la première pièce classée à profession
    // (l'armure est par profession ; armes et objets tenus sont neutres). None si l'équipement
    // ne contient rien de discriminant — le code P n'encode pas la profession.
    public static Profession GuessProfession(EquipmentBuild build)
    {
        var items = build.Armor.Concat(build.WeaponSets.SelectMany(s => s.Items)).ToList();

        var byItem = items
            .Select(i => Items.TryGetValue(i.ItemId, out var info) ? info.Profession : Profession.None)
            .FirstOrDefault(p => p != Profession.None);
        if (byItem != Profession.None) return byItem;

        // Repli sur les upgrades : une armure faite de pièces communes ne dit rien, mais ses
        // insignes et ses runes d'attribut, eux, sont par profession (rune d'Absorption comprise).
        return items.SelectMany(i => i.ModifierIds)
            .Select(id => Mods.TryGetValue(id, out var m) ? m.Profession : Profession.None)
            .FirstOrDefault(p => p != Profession.None);
    }

    // Nom d'affichage : les items du format template sont tous PvP → préfixe "PvP " retiré
    // (décision Philippe 04/07/2026). Le nom complet reste utilisé pour les URLs wiki.
    public static string DisplayItemName(string name) =>
        name.StartsWith("PvP ", StringComparison.Ordinal) ? name[4..] : name;

    // Adjectif d'un préfixe d'arme : "Poisonous Spearhead" → "Poisonous".
    public static string? PrefixAdjective(string modName)
    {
        foreach (var (phrase, category, _) in _weaponModPhrases)
            if (category == ModCategory.WeaponPrefix && modName.EndsWith(phrase, StringComparison.Ordinal))
                return modName[..^phrase.Length].TrimEnd();
        return null;
    }

    // Partie "of X" d'un suffixe ou d'une rune : "Spear Grip of Defense" → "of Defense",
    // "Rune of Superior Vigor" → "of Superior Vigor".
    public static string? OfPart(string modName)
    {
        int idx = modName.IndexOf(" of ", StringComparison.Ordinal);
        return idx < 0 ? null : modName[(idx + 1)..];
    }

    // Nom composé façon paw·ned² : insigne/adjectif de préfixe + objet + "of X" (rune/suffixe).
    // L'inscription n'entre pas dans le nom (affichée à part, comme dans paw·ned²).
    // En FR la grammaire diffère (cf. ComposeItemNameFr) : le suffixe anglais devient une
    // parenthèse. Le nom EN reste inchangé — il sert aussi de clé ailleurs.
    public static string ComposeItemName(int itemId, IEnumerable<int> modIds)
    {
        string enBaseName = itemId != 0 && GwEquipmentData.Items.TryGetValue(itemId, out var n)
            ? DisplayItemName(n) : "?";

        if (AppLanguage.IsFr) return ComposeItemNameFr(enBaseName, modIds);

        string? front = null, back = null;
        foreach (var id in modIds)
        {
            if (!Mods.TryGetValue(id, out var mi)) continue;
            switch (mi.Category)
            {
                case ModCategory.Insignia:
                    front = mi.Name.EndsWith(" Insignia", StringComparison.Ordinal)
                        ? mi.Name[..^9].TrimEnd() : mi.Name;
                    break;
                case ModCategory.WeaponPrefix:
                    front = PrefixAdjective(mi.Name);
                    break;
                case ModCategory.Rune or ModCategory.WeaponSuffix:
                    back = OfPart(mi.Name);
                    break;
            }
        }

        var parts = new List<string>(3);
        if (front != null) parts.Add(front);
        parts.Add(enBaseName);
        if (back != null) parts.Add(back);
        return string.Join(' ', parts);
    }

    // Grammaire FR validée par Philippe (27/07) — la MÊME pour l'armure et pour l'arme :
    //     « <base> <fragment avant> (<fragment entre parenthèses>) »
    //   armure : « Robe krytienne du survivant (Vigueur : bonus supérieur) »
    //   arme   : « Hache vampirique (de Courage) », « Arc long de poison (du Protecteur) »
    // Avant = insigne (armure) ou préfixe d'arme ; parenthèse = rune (sans le mot « Rune »)
    // ou suffixe d'arme. L'inscription n'entre pas dans le nom, comme en anglais.
    private static string ComposeItemNameFr(string enBaseName, IEnumerable<int> modIds)
    {
        string baseName = GwEquipmentItemsFr.DisplayName(enBaseName);
        string? front = null, back = null;

        foreach (var id in modIds)
        {
            if (!Mods.TryGetValue(id, out var mi)) continue;
            switch (mi.Category)
            {
                case ModCategory.Insignia:
                    front = GwInsigniaFr.ComposedForm(id);
                    break;
                case ModCategory.WeaponPrefix:
                    front = GwEquipmentModsFr.ComposedFragment(id);
                    break;
                case ModCategory.Rune:
                    // « Rune (Vigueur : bonus supérieur) » → « (Vigueur : bonus supérieur) ».
                    var fr = GwEquipmentModsFr.DisplayName(id, mi.Name);
                    back = fr.StartsWith("Rune ", StringComparison.Ordinal) ? fr[5..] : $"({fr})";
                    break;
                case ModCategory.WeaponSuffix:
                    if (GwEquipmentModsFr.ComposedFragment(id) is { } sfx) back = $"({sfx})";
                    break;
            }
        }

        var parts = new List<string>(3) { baseName };
        if (front != null) parts.Add(front);
        if (back != null) parts.Add(back);
        return string.Join(' ', parts);
    }

    // Page wiki générique d'une catégorie d'upgrade (bouton Wiki sans sélection).
    public static string CategoryWikiPath(ModCategory c) => c switch
    {
        ModCategory.Insignia     => "Insignia",
        ModCategory.Rune         => "Rune",
        ModCategory.Inscription  => "Inscription",
        ModCategory.WeaponPrefix or ModCategory.WeaponSuffix => "Upgrade_component",
        _ => "Equipment",
    };

    // Une inscription se pose-t-elle sur ce type d'arme ? (matrice wiki "Attaches to")
    public static bool InscriptionFits(InscriptionClass c, WeaponKind k) => c switch
    {
        InscriptionClass.AnyWeapon          => k != WeaponKind.None && !IsOffHandKind(k),
        InscriptionClass.MartialWeapon      => k is WeaponKind.Axe or WeaponKind.Sword or WeaponKind.Spear
                                                 or WeaponKind.Hammer or WeaponKind.Daggers
                                                 or WeaponKind.Scythe or WeaponKind.Bow,
        InscriptionClass.SpellcastingWeapon => k is WeaponKind.Wand or WeaponKind.Staff,
        InscriptionClass.FocusOnly          => k == WeaponKind.Focus,
        InscriptionClass.FocusOrShield      => k is WeaponKind.Focus or WeaponKind.Shield,
        _ => false,
    };

    // ── Heuristiques ─────────────────────────────────────────────────────────────

    private static EquipItemInfo ClassifyItem(int id, string name)
    {
        if (_itemOverrides.TryGetValue(id, out var ov))
            return new EquipItemInfo(id, name, ov.Slot, ov.Weapon, Profession.None);

        // "X Scar Pattern" : partie du corps dans le premier mot, sinon tête (masques Necro).
        if (name.Contains("Scar Pattern", StringComparison.OrdinalIgnoreCase))
        {
            var first = name.Split(' ')[0];
            int scarSlot = _scarPatternParts.GetValueOrDefault(first, SlotHead);
            return new EquipItemInfo(id, name, scarSlot, WeaponKind.None, Profession.Necromancer);
        }

        // Armure ? (mots-clés de pièce testés avant les armes : "Spear Crest" est un casque
        // Paragon, pas une lance — "Crest" gagne car les mots d'armure sont testés d'abord.)
        foreach (var (keyword, slot) in _armorKeywords)
        {
            if (!ContainsWord(name, keyword)) continue;
            return new EquipItemInfo(id, name, slot, WeaponKind.None, ArmorProfession(id, name, keyword));
        }

        // Arme / off-hand ?
        foreach (var (keyword, kind) in _weaponKeywords)
        {
            if (!ContainsWord(name, keyword)) continue;
            int slot = IsOffHandKind(kind) ? SlotOffHand : SlotWeapon;
            return new EquipItemInfo(id, name, slot, kind, Profession.None);
        }

        return new EquipItemInfo(id, name, -1, WeaponKind.None, Profession.None);
    }

    private static Profession ArmorProfession(int id, string name, string pieceKeyword)
    {
        // 0. Override par ID (sets de campagne ambigus).
        if (_itemProfessionOverrides.TryGetValue(id, out var ovProf))
            return ovProf;

        // 1. Tag parenthésé explicite : "Canthan Gloves (Mesmer)".
        int open = name.IndexOf('(');
        if (open >= 0)
        {
            var tag = name[(open + 1)..].TrimEnd(')');
            if (Enum.TryParse<Profession>(tag, ignoreCase: true, out var tagged))
                return tagged;
        }

        // 2. Famille d'art propre à une profession : "Enchanter Footwear" → Mesmer.
        var family = name.Split(' ')[0];
        if (_armorFamilies.TryGetValue(family, out var byFamily))
            return byFamily;

        // 3. Mot-clé de pièce propre à une profession : "Krytan Attire" → Mesmer.
        if (_pieceKeywordProfession.TryGetValue(pieceKeyword, out var byPiece))
            return byPiece;

        return Profession.None;
    }

    private static EquipModInfo ClassifyMod(int id, string name)
    {
        // Runes d'armure : "Rune of [Minor|Major|Superior] <attribut>" ou rune universelle.
        if (name.StartsWith("Rune of ", StringComparison.Ordinal))
        {
            var rest = name[8..];
            foreach (var tier in new[] { "Minor ", "Major ", "Superior " })
                if (rest.StartsWith(tier, StringComparison.Ordinal)) { rest = rest[tier.Length..]; break; }

            var prof = _modProfessionOverrides.TryGetValue(id, out var ovProf)
                ? ovProf
                : GwAttributeData.ByName(rest)?.Profession ?? Profession.None;
            return new EquipModInfo(id, name, ModCategory.Rune, WeaponKind.None, prof);
        }

        // Insignes d'armure : profession curée depuis wiki/Insignia (absente = commune).
        if (name.EndsWith("Insignia", StringComparison.Ordinal))
            return new EquipModInfo(id, name, ModCategory.Insignia, WeaponKind.None,
                _insigniaProfessions.GetValueOrDefault(id, Profession.None));

        // Inscriptions : noms entre guillemets, classe curée depuis wiki/List_of_weapon_upgrades.
        // Classe absente = AnyWeapon (filet : visible sur toute arme).
        if (name.StartsWith('"'))
            return new EquipModInfo(id, name, ModCategory.Inscription, WeaponKind.None, Profession.None,
                _inscriptionClasses.GetValueOrDefault(id, InscriptionClass.AnyWeapon));

        // Préfixes/suffixes d'arme par phrase.
        foreach (var (phrase, category, kind) in _weaponModPhrases)
            if (name.Contains(phrase, StringComparison.Ordinal))
                return new EquipModInfo(id, name, category, kind, Profession.None);

        return new EquipModInfo(id, name, ModCategory.Unknown, WeaponKind.None, Profession.None);
    }

    // Match sur mot entier pour éviter les faux positifs de sous-chaîne ("Axe" dans un nom
    // composé, "Vest" dans "Vestments" — l'ordre du tableau ne suffit pas dans les deux sens).
    private static bool ContainsWord(string name, string keyword)
        => IndexOfWord(name, keyword) >= 0;

    private static int IndexOfWord(string name, string keyword)
    {
        int idx = name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool startOk = idx == 0 || !char.IsLetter(name[idx - 1]);
            int end = idx + keyword.Length;
            bool endOk = end >= name.Length || !char.IsLetter(name[end]);
            if (startOk && endOk) return idx;
            idx = name.IndexOf(keyword, idx + 1, StringComparison.OrdinalIgnoreCase);
        }
        return -1;
    }

    // ── Familles d'art d'armure (bouton "appliquer le style aux 5 pièces") ───────

    // Famille d'art d'un skin d'armure : le nom affiché moins le mot de pièce ("Seitung
    // Guise" → "Seitung", "Ascetic Chest Design" → "Ascetic"). Les Scar Patterns Necro
    // partagent la famille "Scar Pattern" (un seul style par pièce, la partie du corps est
    // dans le nom). Null si le nom ne contient pas de mot de pièce connu ou s'il se réduit
    // au mot de pièce.
    public static string? ArmorFamilyOf(int itemId)
    {
        if (!Items.TryGetValue(itemId, out var info) || !IsArmorSlot(info.Slot)) return null;
        var name = DisplayItemName(info.Name);

        // Suffixe de désambiguïsation "(Profession)" des pièces partagées ("Shing Jea Boots
        // (Warrior)") : redondant ici, la profession est un critère de match séparé.
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && name.EndsWith(")", StringComparison.Ordinal))
            name = name[..paren];

        if (name.Contains("Scar Pattern", StringComparison.OrdinalIgnoreCase))
            return "Scar Pattern";

        foreach (var (keyword, _) in _armorKeywords)
        {
            int idx = IndexOfWord(name, keyword);
            if (idx < 0) continue;
            var family = (name[..idx] + name[(idx + keyword.Length)..]).Trim();
            return family.Length > 0 ? family : null;
        }
        return null;
    }

    // Pièce d'armure du même style pour un autre slot — ex. famille "Seitung" + Assassin +
    // SlotHands → "PvP Seitung Gloves". Null si la famille n'existe pas sur ce slot.
    public static EquipItemInfo? ArmorPieceOfFamily(string family, Profession profession, int slot) =>
        ItemsForSlot(slot).FirstOrDefault(i =>
            i.Profession == profession
            && string.Equals(ArmorFamilyOf(i.ItemId), family, StringComparison.OrdinalIgnoreCase));

    // ── Lot D bis : famille en jeu → famille wiki (Lot 1 lien de style ET Lot 2 noms de fichier
    // d'image partagent cette table). Aucun article wiki sous le nom de l'item lui-même (confirmé
    // 404) — les deux styles PARTAGENT le même skin visuel. Toutes VALIDÉES par Philippe (05/07/2026).
    private static readonly Dictionary<(Profession Profession, string Family), string> _wikiFamilyOverrides = new()
    {
        { (Profession.Ranger, "Druidic"),          "Druid" },
        { (Profession.Elementalist, "Stormcloud"), "Stormforged" },
        { (Profession.Elementalist, "Granite"),    "Stoneforged" },
        { (Profession.Elementalist, "Magma"),      "Flameforged" },
        { (Profession.Elementalist, "Frozen"),     "Iceforged" },
        { (Profession.Mesmer, "Theatrical"),       "Performer" },
        { (Profession.Warrior, "Dragon"),          "Wyvern" },
        { (Profession.Warrior, "Plated"),          "Templar" },
        { (Profession.Necromancer, "Wretched"),    "Cabal" },
        { (Profession.Necromancer, "Banished"),    "Profane" },
        { (Profession.Monk, "Ascetic"),            "Dragon" },
        { (Profession.Monk, "Consecrated"),        "Woven" },
        { (Profession.Monk, "Militant"),           "Censor" },
    };

    // Famille wiki (après override) d'une pièce d'armure classée — utilisée à la fois pour
    // construire la page de style (Lot 1) et le nom de fichier de skin (Lot 2).
    private static string WikiFamilyOf(Profession profession, string family) =>
        _wikiFamilyOverrides.TryGetValue((profession, family), out var w) ? w : family;

    // Pages de style "{Profession} {Famille} armor" réellement présentes dans Category:PvP_armor
    // (wiki, vérifié 05/07/2026) — garde-fou : n'utiliser une construction directe que si son
    // résultat correspond à une page connue, jamais de lien mort.
    private static readonly HashSet<string> _armorStylePages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Assassin Canthan armor", "Assassin Exotic armor", "Assassin Imperial armor",
        "Assassin Seitung armor", "Assassin Shing Jea armor",
        "Dervish Elonian armor", "Dervish Istani armor", "Dervish Sunspear armor",
        "Elementalist Canthan armor", "Elementalist Flameforged armor", "Elementalist Iceforged armor",
        "Elementalist Krytan armor", "Elementalist Shing Jea armor", "Elementalist Stoneforged armor",
        "Elementalist Stormforged armor",
        "Mesmer Canthan armor", "Mesmer Enchanter armor", "Mesmer Krytan armor",
        "Mesmer Performer armor", "Mesmer Rogue armor", "Mesmer Shing Jea armor",
        "Monk Canthan armor", "Monk Censor armor", "Monk Dragon armor", "Monk Krytan armor",
        "Monk Shing Jea armor", "Monk Woven armor",
        "Necromancer Cabal armor", "Necromancer Canthan armor", "Necromancer Krytan armor",
        "Necromancer Profane armor", "Necromancer Scar Pattern armor", "Necromancer Shing Jea armor",
        "Paragon Elonian armor", "Paragon Istani armor", "Paragon Sunspear armor",
        "Ranger Canthan armor", "Ranger Drakescale armor", "Ranger Druid armor",
        "Ranger Fur-Lined armor", "Ranger Krytan armor", "Ranger Shing Jea armor",
        "Ranger Studded Leather armor",
        "Ritualist Canthan armor", "Ritualist Exotic armor", "Ritualist Imperial armor",
        "Ritualist Seitung armor", "Ritualist Shing Jea armor",
        "Warrior Canthan armor", "Warrior Gladiator armor", "Warrior Krytan armor",
        "Warrior Shing Jea armor", "Warrior Templar armor", "Warrior Wyvern armor",
    };

    // Pages "Mask" dédiées (masques d'attribut Mesmer/Ranger — suffixe différent de "armor").
    private static readonly HashSet<string> _armorMaskPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mesmer Animal Mask", "Mesmer Costume Mask", "Mesmer Imposing Mask", "Mesmer Sleek Mask",
        "Ranger Hunter Mask", "Ranger Tamer Mask", "Ranger Traveler Mask",
    };

    // Coiffes Necro "X Scar Pattern" : seule "Ragged" a une page dédiée confirmée (Wicked/Vile/
    // Devilish non trouvées dans Category:PvP_armor — à élucider avec Philippe).
    private static readonly HashSet<string> _armorScarPatternHeadPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Necromancer Ragged Scar Pattern",
    };

    // Page wiki de style d'une pièce d'armure (table de fabrication), pour le bouton Wiki de
    // l'éditeur — préférée à la page d'item plate quand elle est identifiée. Null = aucune page
    // de style connue (fallback = page d'item plate, comportement actuel).
    public static string? ArmorStyleWikiPath(int itemId)
    {
        if (!Items.TryGetValue(itemId, out var info) || !IsArmorSlot(info.Slot)
            || info.Profession == Profession.None) return null;

        var family = ArmorFamilyOf(itemId);
        if (family == null) return null;

        // Necro : les 4 coiffes Scar Pattern partagent la famille "Scar Pattern" (utile pour le
        // ×5) mais chacune a son propre adjectif (Ragged/Wicked/Vile/Devilish) et sa propre page.
        if (info.Slot == SlotHead && family == "Scar Pattern")
        {
            var adjective = DisplayItemName(info.Name).Split(' ')[0];
            var scarPage = $"{info.Profession} {adjective} Scar Pattern";
            return _armorScarPatternHeadPages.Contains(scarPage) ? scarPage.Replace(' ', '_') : null;
        }

        var wikiFamily = WikiFamilyOf(info.Profession, family);

        var maskPage = $"{info.Profession} {wikiFamily} Mask";
        if (_armorMaskPages.Contains(maskPage)) return maskPage.Replace(' ', '_');

        var armorPage = $"{info.Profession} {wikiFamily} armor";
        return _armorStylePages.Contains(armorPage) ? armorPage.Replace(' ', '_') : null;
    }

    // ── Lot D bis / Lot 2 : construction directe des noms de fichiers de skins (armure genrée +
    // icône d'arme) — même mécanisme que ProfessionIconService (Special:FilePath), sans scrape
    // HTML au runtime. Le mot de pièce du nom de fichier == mot de pièce en jeu (_armorKeywords)
    // PARTOUT, sauf le Guerrier "Cuirass" qui devient "Hauberk" pour 2 familles wiki seulement
    // (Gladiator, Wyvern) — Templar/Krytan/Shing Jea/Canthan gardent "Cuirass". Vérifié par
    // probe HTTP direct sur les 586 pièces d'armure + armes du catalogue (05/07/2026) :
    // 504/586 (86 %) résolvent en 200 ; les 82 restantes sont soit les coiffes "template-only"
    // (déjà sans page wiki au Lot 1, fallback assumé), soit une poignée d'armes isolées.
    private static readonly HashSet<(Profession, string WikiFamily)> _hauberkFamilies = new()
    {
        (Profession.Warrior, "Gladiator"), (Profession.Warrior, "Wyvern"),
    };

    // Écarts de mot de pièce au niveau ITEM (le nom de fichier wiki n'utilise pas le mot de pièce
    // en jeu) — trouvés par le probe HTTP du 05/07/2026. Monk Krytan chest : "Vestments" en jeu →
    // "Raiment" au fichier wiki (unique parmi les sets Monk, les autres gardent "Vestments").
    private static readonly Dictionary<int, string> _armorPieceWordItemOverrides = new()
    {
        { 220, "Raiment" }, // Krytan Vestments (Monk)
    };

    // Mot de pièce (wiki) d'une pièce d'armure classée par mot-clé. Null pour les Scar Patterns
    // (traités à part dans ArmorSkinFile, l'adjectif tenant lieu de "famille") ou les items non
    // classés par _armorKeywords.
    private static string? ArmorPieceWordOf(int itemId, Profession profession, string wikiFamily)
    {
        if (_armorPieceWordItemOverrides.TryGetValue(itemId, out var itemOv)) return itemOv;
        if (!Items.TryGetValue(itemId, out var info) || !IsArmorSlot(info.Slot)) return null;
        var name = DisplayItemName(info.Name);
        foreach (var (keyword, _) in _armorKeywords)
        {
            if (IndexOfWord(name, keyword) < 0) continue;
            if (keyword == "Cuirass" && _hauberkFamilies.Contains((profession, wikiFamily))) return "Hauberk";
            return keyword;
        }
        return null;
    }

    // Nom de fichier du skin visuel d'une pièce d'armure (galerie H/F du wiki), genré. Null si
    // la pièce ne résout à aucun style connu (fallback = placeholder neutre, cf. plan Lot D bis).
    public static string? ArmorSkinFile(int itemId, bool female)
    {
        if (!Items.TryGetValue(itemId, out var info) || !IsArmorSlot(info.Slot)
            || info.Profession == Profession.None) return null;

        var family = ArmorFamilyOf(itemId);
        if (family == null) return null;
        var g = female ? "f" : "m";

        // Necro "X Scar Pattern" : le PREMIER mot du nom tient lieu de famille dans le fichier —
        // coiffe (Ragged/Wicked/Vile/Devilish) ET corps (Chest/Arm/Leg/Foot). Fichiers de corps
        // vérifiés 200 (05/07/2026) ; coiffes Wicked/Vile/Devilish restent 404 (cf. Lot 1).
        if (family == "Scar Pattern")
        {
            var firstWord = DisplayItemName(info.Name).Split(' ')[0];
            return $"{info.Profession}_{firstWord}_Scar_Pattern_{g}.png";
        }

        var wikiFamily = WikiFamilyOf(info.Profession, family);
        var pieceWord = ArmorPieceWordOf(itemId, info.Profession, wikiFamily);
        if (pieceWord == null) return null;

        return $"{info.Profession}_{wikiFamily}_{pieceWord}_{g}.png".Replace(' ', '_');
    }

    // Écarts ponctuels nom d'item (jeu) → nom de fichier wiki, trouvés par le probe HTTP du
    // 05/07/2026. (a) Casters dont le nom plat n'a pas d'icône propre → variante d'attribut :
    // "PvP Cane" partage l'icône Illusion, "PvP Holy Staff" celle Healing. (b) Les variantes
    // parenthésées Holy Rod (Healing/Protection) retombent sur l'icône de base commune.
    //
    // L'item 117 figurait ici sous "Glided Artifact" : orthographe corrigée en "Gilded" dans
    // GwEquipmentData le 30/07/2026 (confirmée par Philippe), le nom de fichier en dérive donc
    // désormais tout seul — plus d'écart à rattraper.
    private static readonly Dictionary<int, string> _weaponIconOverrides = new()
    {
        { 134, "PvP_Cane_(Illusion).jpg" },       // PvP Cane (pas d'icône plate → Illusion)
        { 327, "PvP_Cane_(Illusion).jpg" },       // PvP Cane (Illusion Magic) → wiki abrège "Illusion"
        { 156, "PvP_Holy_Staff_(Healing).jpg" },  // PvP Holy Staff (pas d'icône plate → Healing)
        { 330, "PvP_Holy_Rod.jpg" },              // PvP Holy Rod (Healing) → icône de base
        { 331, "PvP_Holy_Rod.jpg" },              // PvP Holy Rod (Protection) → icône de base
    };

    // Nom de fichier de l'icône d'une arme/objet tenu (page PvP_Equipment) : le nom complet de
    // l'item EN JEU porte déjà le préfixe "PvP " (cf. GwEquipmentData, ex. "PvP Axe"). Déterministe,
    // pas de scrape. Null si le slot n'est pas arme/off-hand.
    public static string? WeaponIconFile(int itemId)
    {
        if (_weaponIconOverrides.TryGetValue(itemId, out var overridden)) return overridden;
        if (!Items.TryGetValue(itemId, out var info) || IsArmorSlot(info.Slot)) return null;
        return $"{info.Name.Replace(' ', '_')}.jpg";
    }
}
