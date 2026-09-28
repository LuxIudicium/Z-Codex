namespace ZCodex.Core.Data;

/// <summary>
/// Sorts qui peuvent viser un ENNEMI (chantier infobulle, lot 7b : Vol de vitesse accélère « spells targeting these
/// foes »). La base ne dit pas qui un sort vise → liste relevée sur le wiki le 28/09/2026 (351 sorts) : champ
/// « target » de l'infobox = foes ; les 288 sorts SANS ce champ (Vents glaciaux, Énigme des arcanes…) classés d'après
/// leur description longue (« target [touched] foe », projectile) puis relus un par un ; Verata's Gaze (« minions »,
/// mais un serviteur HOSTILE) ; et 4 « allies or foes » qui visent souvent un ennemi (Heart of Shadow, Viper's Defense,
/// Ride the Lightning et sa variante) — Putrid Explosion écartée, elle vise un cadavre (arbitrages de Philippe).
/// Noms exacts du catalogue, variantes « (PvP) » relevées sur leur propre page.
/// </summary>
public static class FoeTargetSpells
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        // Envoûteur (111)
        "Accumulated Pain", "Accumulated Pain (PvP)", "Air of Disenchantment", "Aneurysm", "Arcane Conundrum",
        "Arcane Languor", "Arcane Larceny", "Arcane Thievery", "Backfire", "Calculated Risk",
        "Calculated Risk (PvP)", "Chaos Storm", "Clumsiness", "Complicate", "Confusing Images", "Conjure Nightmare",
        "Conjure Phantasm", "Crippling Anguish", "Crippling Anguish (PvP)", "Cry of Frustration", "Cry of Pain",
        "Discharge Enchantment", "Diversion", "Drain Delusions", "Drain Enchantment", "Empathy", "Empathy (PvP)",
        "Enchanter's Conundrum", "Enchanter's Conundrum (PvP)", "Energy Burn", "Energy Drain", "Energy Surge",
        "Energy Tap", "Epidemic", "Ether Feast", "Ether Lord", "Ether Phantom", "Ethereal Burden",
        "Extend Conditions", "Feedback", "Fevered Dreams", "Fevered Dreams (PvP)", "Fragility", "Fragility (PvP)",
        "Frustration", "Frustration (PvP)", "Guilt", "Hypochondria", "Ignorance", "Illusion of Pain",
        "Illusion of Pain (PvP)", "Images of Remorse", "Imagined Burden", "Ineptitude", "Inspired Enchantment",
        "Kitah's Burden", "Lyssa's Balance", "Migraine", "Migraine (PvP)", "Mind Wrack", "Mind Wrack (PvP)",
        "Mirror of Disenchantment", "Mirror of Disenchantment (PvP)", "Mistrust", "Mistrust (PvP)", "Overload",
        "Panic", "Phantom Pain", "Power Block", "Power Drain", "Power Flux", "Power Leak", "Power Leech",
        "Power Lock", "Power Return", "Power Spike", "Price of Pride", "Psychic Distraction", "Psychic Instability",
        "Psychic Instability (PvP)", "Recurring Insecurity", "Revealed Enchantment", "Shame", "Shared Burden",
        "Shared Burden (PvP)", "Shatter Delusions", "Shatter Delusions (PvP)", "Shatter Enchantment",
        "Shatter Storm", "Shrinking Armor", "Simple Thievery", "Soothing Images", "Spirit of Failure",
        "Spirit Shackles", "Spiritual Pain", "Spiritual Pain (PvP)", "Stolen Speed", "Stolen Speed (PvP)",
        "Sum of All Fears", "Tease", "Tease (PvP)", "Visions of Regret", "Visions of Regret (PvP)", "Wandering Eye",
        "Wandering Eye (PvP)", "Waste Not, Want Not", "Wastrel's Demise", "Wastrel's Worry",
        "Wastrel's Worry (PvP)", "Web of Disruption", "Web of Disruption (PvP)",
        // Élémentaliste (86)
        "Arc Lightning", "Ash Blast", "Bed of Coals", "Blinding Flash", "Blinding Surge", "Blurred Vision",
        "Breath of Fire", "Chain Lightning", "Chilling Winds", "Churning Earth", "Deep Freeze", "Dragon's Stomp",
        "Earthen Shackles", "Earthquake", "Ebon Hawk", "Energy Blast", "Enervating Charge", "Eruption",
        "Fire Storm", "Fireball", "Flare", "Freezing Gust", "Gale", "Glimmering Mark", "Glowing Gaze",
        "Glowing Ice", "Glowstone", "Ice Prison", "Ice Spear", "Ice Spikes", "Icy Prism", "Icy Shackles",
        "Immolate", "Incendiary Bonds", "Invoke Lightning", "Lava Arrows", "Lightning Bolt", "Lightning Hammer",
        "Lightning Hammer (PvP)", "Lightning Javelin", "Lightning Orb", "Lightning Strike", "Lightning Surge",
        "Liquid Flame", "Maelstrom", "Magnetic Surge", "Mark of Rodgort", "Meteor", "Meteor Shower", "Mind Blast",
        "Mind Burn", "Mind Freeze", "Mind Freeze (PvP)", "Mind Shock", "Mind Shock (PvP)", "Obsidian Flame",
        "Obsidian Flame (PvP)", "Phoenix", "Ride the Lightning", "Ride the Lightning (PvP)", "Rodgort's Invocation",
        "Rust", "Sandstorm", "Savannah Heat", "Savannah Heat (PvP)", "Searing Flames", "Searing Heat",
        "Shard Storm", "Shatterstone", "Shell Shock", "Shock Arrow", "Slippery Ground", "Slippery Ground (PvP)",
        "Smoldering Embers", "Star Burst", "Steam", "Stone Daggers", "Stoning", "Teinai's Crystals",
        "Teinai's Prison", "Teinai's Wind", "Thunderclap", "Unsteady Ground", "Vapor Blade", "Water Trident",
        "Winter's Embrace",
        // Nécromant (84)
        "Angorodon's Gaze", "Atrophy", "Barbs", "Bitter Chill", "Blood Bond", "Blood Drinker",
        "Blood of the Aggressor", "Cacophony", "Chilblains", "Corrupt Enchantment", "Dark Pact", "Deathly Chill",
        "Deathly Swarm", "Defile Defenses", "Defile Enchantments", "Defile Flesh", "Depravity",
        "Desecrate Enchantments", "Discord", "Discord (PvP)", "Enfeeble", "Enfeeble (PvP)", "Enfeebling Blood",
        "Enfeebling Blood (PvP)", "Envenom Enchantments", "Faintheartedness", "Feast of Corruption", "Fetid Ground",
        "Gaze of Contempt", "Grenth's Balance", "Icy Veins", "Insidious Parasite", "Jaundiced Gaze", "Life Siphon",
        "Life Transfer", "Lifebane Strike", "Lingering Curse", "Malaise", "Malign Intervention", "Mark of Fury",
        "Mark of Pain", "Mark of Subversion", "Meekness", "Oppressive Gaze", "Pain of Disenchantment",
        "Parasitic Bond", "Plague Sending", "Price of Failure", "Putrid Bile", "Ravenous Gaze", "Reaper's Mark",
        "Reckless Haste", "Rend Enchantments", "Rigor Mortis", "Rip Enchantment", "Rising Bile", "Rotting Flesh",
        "Shadow of Fear", "Shadow Strike", "Shivers of Dread", "Soul Barbs", "Soul Bind", "Soul Leech",
        "Spinal Shivers", "Spiteful Spirit", "Spoil Victor", "Spoil Victor (PvP)", "Strip Enchantment", "Suffering",
        "Taste of Pain", "Toxic Chill", "Ulcerous Lungs", "Unholy Feast", "Vampiric Gaze", "Vampiric Spirit",
        "Vampiric Swarm", "Verata's Gaze", "Vile Miasma", "Virulence", "Vocal Minority", "Wail of Doom",
        "Weaken Armor", "Weaken Knees", "Wither",
        // Assassin (32)
        "Assassin's Promise", "Augury of Death", "Aura of Displacement", "Beguiling Haze", "Blinding Powder",
        "Caltrops", "Crippling Dagger", "Dancing Daggers", "Dark Prison", "Death's Charge", "Disrupting Dagger",
        "Enduring Toxin", "Entangling Asp", "Expose Defenses", "Heart of Shadow", "Hidden Caltrops",
        "Mark of Death", "Mark of Insecurity", "Mark of Instability", "Mirrored Stance", "Scorpion Wire",
        "Seeping Wound", "Shadow Fang", "Shadow Prison", "Shadow Shroud", "Shadow Shroud (PvP)", "Shadowy Burden",
        "Shameful Fear", "Shroud of Silence", "Siphon Speed", "Siphon Strength", "Viper's Defense",
        // Ritualiste (16)
        "Binding Chains", "Caretaker's Charge", "Channeled Strike", "Clamor of Souls", "Consume Soul", "Doom",
        "Dulled Weapon", "Essence Strike", "Gaze from Beyond", "Lamentation", "Painful Bond", "Renewing Surge",
        "Spirit Boon Strike", "Spirit Burn", "Spirit Rift", "Wielder's Strike",
        // Sans profession (10)
        "A Touch of Guile", "Alkar's Alchemical Acid", "Asuran Scan", "Ebon Vanguard Assassin Support",
        "Ebon Vanguard Sniper Support", "Ether Nightmare (Luxon)", "Pain Inverter", "Smooth Criminal", "Snow Storm",
        "Technobabble",
        // Moine (10)
        "Banish", "Defender's Zeal", "Pacifism", "Ray of Judgment", "Scourge Enchantment", "Scourge Healing",
        "Scourge Sacrifice", "Smite", "Spear of Light", "Word of Censure",
        // Derviche (2)
        "Rending Touch", "Test of Faith",
    };
}
