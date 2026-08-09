namespace ZCodex.Core.Data;

/// <summary>Mod de PRÉFIXE physique simulé sur une ligne du spike (cadrage Philippe 06/08/2026).</summary>
public enum SpikeWeaponMod
{
    /// <summary>Aucun mod physique déclaré (défaut).</summary>
    None,
    /// <summary>De fractionnement (Sundering) : 20 % de pénétration d'armure, 20 % du temps.</summary>
    Sundering,
    /// <summary>Vampirique (Vampiric) : vol de vie 3 ou 5 par coup selon l'arme.</summary>
    Vampiric,
}

/// <summary>
/// Les deux mods d'arme PHYSIQUES que le calculateur de spike ne modélisait pas — de
/// fractionnement et vampirique — plus la pénétration permanente de l'arc corne.
///
/// Pénétration d'armure (wiki/Armor_penetration) : les deux valeurs ci-dessous sont de catégorie
/// <b>BONUS</b> (« Sundering weapon upgrades » et « Hornbows » y sont listés : « these do stack …
/// and add to the largest base armor penetration »). Elles s'AJOUTENT donc par-dessus le max des
/// pénétrations de BASE, exactement comme Judge's Insight — et se cumulent avec lui.
/// ⚠ Ne pas confondre le mod « de fractionnement » avec le SORT <i>Sundering Weapon</i>
/// (<see cref="SpikeWeaponBuffs"/>), qui est une pénétration de BASE de 10 % : même nom, catégorie
/// opposée.
///
/// Le mod occupe l'emplacement de PRÉFIXE de l'arme, comme les mods élémentaires — d'où la règle
/// d'exclusivité de l'UI : le combo n'est proposé que tant que le type de dégâts de la ligne n'est
/// pas élémentaire. Les types non élémentaires de la liste (hache perforante, épée contondante,
/// faux « Sufferer » en ténèbres) sont des SKINS et laissent le préfixe libre. Judge's Insight,
/// lui, est un buff : il convertit les dégâts en sacré sans consommer le préfixe.
/// </summary>
public static class SpikeWeaponMods
{
    /// <summary>Pénétration BONUS du mod de fractionnement (base de mods : « Armor penetration
    /// +20% (Chance: 20%) ») — appliquée à la ligne quand le proc est coché.</summary>
    public const int SunderingBonusPen = 20;

    /// <summary>Pénétration BONUS permanente d'un arc corne (wiki/Bow : « Hornbow … 10% armor
    /// penetration ») — pas un proc : elle porte sur toutes ses attaques.</summary>
    public const int HornbowBonusPen = 10;

    /// <summary>Vol de vie d'un mod vampirique sur une arme à DEUX mains (marteau, arc, faux).</summary>
    public const int VampiricStealTwoHanded = 5;

    /// <summary>Vol de vie d'un mod vampirique sur une arme à UNE main (hache, épée, dagues, javelot).</summary>
    public const int VampiricStealOneHanded = 3;

    // Clés de persistance (.zcx v18) : stables, indépendantes de l'ordre de l'enum.
    private const string SunderingKey = "sundering";
    private const string VampiricKey = "vampiric";

    /// <summary>Clé persistée d'un mod ; chaîne vide pour <see cref="SpikeWeaponMod.None"/>.</summary>
    public static string ToKey(SpikeWeaponMod mod) => mod switch
    {
        SpikeWeaponMod.Sundering => SunderingKey,
        SpikeWeaponMod.Vampiric => VampiricKey,
        _ => string.Empty,
    };

    /// <summary>Mod d'une clé persistée ; <see cref="SpikeWeaponMod.None"/> si vide ou inconnue
    /// (fichier d'une version future — même politique que les autres champs du spike).</summary>
    public static SpikeWeaponMod FromKey(string? key) => key switch
    {
        SunderingKey => SpikeWeaponMod.Sundering,
        VampiricKey => SpikeWeaponMod.Vampiric,
        _ => SpikeWeaponMod.None,
    };

    // Armes à deux mains, qui portent le vol de vie 5 (base de mods : « Life Draining: 5, 3 »).
    private static readonly HashSet<string> TwoHandedMasteries =
        new(StringComparer.Ordinal) { "Hammer Mastery", "Marksmanship", "Scythe Mastery" };

    /// <summary>Vol de vie par coup d'un mod vampirique sur <paramref name="weapon"/> : 5 à deux
    /// mains, 3 sinon. Aucun attribut, buff ni flux ne le module (le vol de vie n'entre jamais dans
    /// l'assiette de dégâts).</summary>
    public static int VampiricSteal(WeaponStrike.Weapon weapon)
        => TwoHandedMasteries.Contains(weapon.Mastery) ? VampiricStealTwoHanded : VampiricStealOneHanded;
}
