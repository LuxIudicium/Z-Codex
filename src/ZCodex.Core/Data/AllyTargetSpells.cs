namespace ZCodex.Core.Data;

/// <summary>
/// Sorts qui peuvent viser un AUTRE allié : ceux dont Selfless Spirit réduit le coût (« spells you cast that
/// target another ally », chantier infobulle, lot 2). La base ne dit pas qui un sort vise, et son texte concis
/// ne le nomme souvent pas (Cure Hex, Weapon of Warding) → liste relevée sur le wiki le 15/09/2026 (172 sorts) :
/// champ « target » de l'infobox = allies / other allies (catégories « Skills that target (other) allies ») ou cible
/// alliée particulière (membre du groupe mort, esprit allié, serviteur, créature invoquée, allié ou ennemi) ; plus
/// les 16 sorts de Moine au champ vide dont la description longue vise un allié (relecture demandée par Philippe ;
/// celle des Ritualistes et des autres professions n'en a trouvé aucun). Noms exacts du catalogue, variantes
/// « (PvP) » relevées sur leur propre page.
/// </summary>
public static class AllyTargetSpells
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        // Moine (96)
        "Aegis (PvP)", "Air of Enchantment", "Aura of Faith", "Aura of Stability", "Balthazar's Aura",
        "Balthazar's Pendulum", "Balthazar's Spirit", "Blessed Light", "Convert Hexes", "Cure Hex",
        "Deny Hexes", "Dismiss Condition", "Divert Hexes", "Divine Intervention", "Draw Conditions",
        "Dwayna's Kiss", "Dwayna's Sorrow", "Empathic Removal", "Essence Bond", "Ethereal Light",
        "Gift of Health", "Glimmer of Light", "Guardian", "Heal Other", "Healing Breeze", "Healing Burst",
        "Healing Hands", "Healing Light", "Healing Ribbon", "Healing Seed", "Healing Touch",
        "Healing Whisper", "Holy Veil", "Holy Wrath", "Infuse Health", "Jamei's Gaze", "Judge's Insight",
        "Judge's Intervention", "Life Attunement", "Life Barrier", "Life Bond", "Life Sheath",
        "Live Vicariously", "Mark of Protection", "Mend Ailment", "Mend Condition", "Mending",
        "Mending Touch", "Orison of Healing", "Patient Spirit", "Peace and Harmony",
        "Peace and Harmony (PvP)", "Pensive Guardian", "Protective Bond", "Protective Spirit",
        "Purge Conditions", "Purifying Veil", "Rebirth", "Remove Hex", "Renew Life", "Restful Breeze",
        "Restore Condition", "Restore Life", "Resurrect", "Resurrection Chant", "Retribution",
        "Reversal of Damage", "Reversal of Fortune", "Reverse Hex", "Seed of Life", "Shield of Absorption",
        "Shield of Deflection", "Shield of Judgment", "Shield of Regeneration", "Shielding Hands",
        "Smite Condition", "Smite Hex", "Spell Breaker", "Spirit Bond", "Spirit Bond (PvP)",
        "Spotless Mind", "Spotless Soul", "Strength of Honor", "Strength of Honor (PvP)", "Succor",
        "Supportive Spirit", "Unyielding Aura (PvP)", "Vengeance", "Vigorous Spirit", "Vital Blessing",
        "Watchful Healing", "Watchful Spirit", "Withdraw Hexes", "Word of Healing", "Words of Comfort",
        "Zealous Benediction",
        // Ritualiste (34)
        "Brutal Weapon", "Draw Spirit", "Flesh of My Flesh", "Flesh of My Flesh (PvP)", "Ghostly Weapon",
        "Ghostmirror Light", "Guided Weapon", "Guided Weapon (PvP)", "Mend Body and Soul", "Mending Grip",
        "Nightmare Weapon", "Resilient Weapon", "Rupture Soul", "Soothing Memories", "Spirit Light",
        "Spirit Light Weapon", "Spirit Transfer", "Spirit to Flesh", "Splinter Weapon",
        "Splinter Weapon (PvP)", "Sundering Weapon", "Vengeful Weapon", "Vital Weapon", "Wailing Weapon",
        "Warmonger's Weapon", "Weapon of Fury", "Weapon of Quickening", "Weapon of Remedy",
        "Weapon of Renewal", "Weapon of Shadow", "Weapon of Warding", "Weapon of Warding (PvP)",
        "Wielder's Boon", "Xinrae's Weapon",
        // Nécromant (12)
        "Blood Ritual", "Blood is Power", "Dark Aura", "Death Nova", "Feast for the Dead", "Foul Feast",
        "Jagged Bones", "Putrid Explosion", "Putrid Flesh", "Taste of Death", "Verata's Gaze",
        "Withering Aura",
        // Envoûteur (8)
        "Ancestor's Visage", "Arcane Mimicry", "Expel Hexes", "Hex Eater Vortex", "Inspired Hex",
        "Revealed Hex", "Shatter Hex", "Sympathetic Visage",
        // Élémentaliste (8)
        "Double Dragon", "Energy Boon", "Gust", "Mirror of Ice", "Ride the Lightning",
        "Ride the Lightning (PvP)", "Stone Sheath", "Windborne Speed",
        // Assassin (8)
        "Death's Retreat", "Heart of Shadow", "Recall", "Return", "Shadow Meld", "Spirit Walk", "Swap",
        "Viper's Defense",
        // Derviche (3)
        "Dwayna's Touch", "Imbue Health", "Watchful Intervention",
        // Sans profession (3)
        "Ebon Escape", "Great Dwarf Armor", "Great Dwarf Weapon",
    };
}
