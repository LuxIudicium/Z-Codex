namespace ZCodex.Core.Data;

/// <summary>
/// Liste blanche des compétences « riders » proc-capable du chantier spike : celles qui, une
/// fois cadre-vert, reçoivent un compteur « Procs : N » (dégâts/vol additionnels par
/// coup/déclenchement, comptés N fois à la main). Les ATTAQUES ont leur compteur de procs
/// indépendamment de cette liste (via WeaponStrike.IsWeaponAttack) ; les SORTS DIRECTS
/// (Shatter Enchantment, Boule de feu…) n'en ont pas — décision Philippe.
///
/// Match par nom EXACT (pas sous-chaîne) pour ne pas capter « Cry of Frustration » (sort direct)
/// avec « Frustration », ni « Spiritual Pain » avec « Pain ». Variantes PvP/Kurzick/Luxon
/// listées séparément. Semé de l'audit sur la vraie DB (harnais ProcScan) ; Philippe complète.
/// </summary>
public static class SpikeProcSkills
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // Conjurations (dégât élémentaire par coup)
        "Conjure Flame", "Conjure Frost", "Conjure Lightning",
        // Enchantements/formes « par coup »
        "Ebon Dust Aura", "Hundred Blades", "Vow of Strength", "Sand Shards",
        "Strength of Honor", "Strength of Honor (PvP)", "Spirit's Strength",
        "Avatar of Grenth", // vol de vie 12 par coup de faux (retype ténèbres ignoré — décision Philippe)
        // Ordres (dégât/vol par coup physique du groupe)
        "Order of Pain", "Order of the Vampire", "Order of Undeath",
        // Hex à dégât déclenché
        "Barbs", "Mark of Pain", "Soul Barbs", "Fragility", "Fragility (PvP)",
        "Frustration", "Frustration (PvP)", "Mind Wrack", "Mind Wrack (PvP)", "Wither",
        // Rituels de nature
        "Edge of Extinction", "Brambles", "Famine", "Favorable Winds", "Winnowing",
        // Binding rituals (attaques d'esprit : dégât ou vol par attaque)
        "Pain", "Pain (PvP)", "Dissonance", "Dissonance (PvP)",
        "Shadowsong", "Shadowsong (PvP)", "Bloodsong", "Bloodsong (PvP)", "Vampirism",
        // Divers riders
        "Ebon Battle Standard of Honor", "Signet of Strength", "Keystone Signet",
        "Ignite Arrows", "Kindle Arrows", "Zealot's Fire", "Dark Aura", "Balthazar's Aura",
        // Hex à dégât par compétence utilisée (« X damage whenever they use a skill ») — la part
        // « +41 additional damage if not under another Mesmer hex » est captée conditionnelle.
        "Visions of Regret", "Visions of Regret (PvP)",
    };

    /// <summary>Vrai si la compétence porte un compteur « Procs » (rider de la liste blanche).</summary>
    public static bool IsProcCapable(string skillName) => Names.Contains(skillName);

    /// <summary>Mind Wrack (version core/PvE) : 2 déclencheurs indépendants (dégât par point
    /// d'énergie perdu + dégât à énergie 0) → 2 lignes, 2 compteurs. La version PvP n'a que le
    /// second paquet.</summary>
    public static bool IsMindWrackDual(string skillName)
        => string.Equals(skillName, "Mind Wrack", StringComparison.OrdinalIgnoreCase);
}
