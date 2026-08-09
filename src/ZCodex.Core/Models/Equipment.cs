namespace ZCodex.Core.Models;

public class EquipmentItem
{
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int Dye { get; set; }
    public List<int> ModifierIds { get; set; } = new();
}

// Un set d'armes = les items des slots 0 (Main hand) et 1 (Off-hand).
public class WeaponSet
{
    public List<EquipmentItem> Items { get; set; } = new();

    public bool IsEmpty => Items.Count == 0;
}

// Équipement d'un personnage : armure PARTAGÉE (slots 2-6) + jusqu'à 4 sets d'armes (F1-F4,
// slots 0-1 chacun). Les sets sont positionnels : F3 reste F3 même si F1 est vide ; seuls les
// sets vides en FIN de liste sont élagués (TrimWeaponSets). Un code P par set (décision
// Philippe 04/07/2026) : la vue plate pour le codec est ItemsOfSet(k).
public class EquipmentBuild
{
    public const int MaxWeaponSets = 4;

    public List<EquipmentItem> Armor { get; set; } = new();
    public List<WeaponSet> WeaponSets { get; set; } = new();
    public int ActiveSet { get; set; }

    public bool IsEmpty => Armor.Count == 0 && WeaponSets.All(s => s.IsEmpty);

    private static bool IsWeaponSlot(int slot) => slot is 0 or 1;

    // Vue plate "un code P" : armes du set demandé (s'il existe) + armure partagée.
    public IEnumerable<EquipmentItem> ItemsOfSet(int set)
    {
        var weapons = set >= 0 && set < WeaponSets.Count
            ? WeaponSets[set].Items : Enumerable.Empty<EquipmentItem>();
        return weapons.Concat(Armor);
    }

    // Indices (0-3) des sets non vides ; [0] si l'équipement se résume à l'armure,
    // pour qu'un export produise toujours au moins un code.
    public IReadOnlyList<int> ExportSets()
    {
        var sets = WeaponSets
            .Select((s, i) => (Set: s, Index: i))
            .Where(t => !t.Set.IsEmpty)
            .Select(t => t.Index)
            .ToList();
        if (sets.Count == 0) sets.Add(0);
        return sets;
    }

    // Une vue plate (code P décodé, ancien .pn3) → armure + armes dans un seul set F1.
    public static EquipmentBuild FromFlat(IEnumerable<EquipmentItem> items)
    {
        var build = new EquipmentBuild();
        var set   = new WeaponSet();
        foreach (var item in items)
        {
            if (IsWeaponSlot(item.Slot)) set.Items.Add(item);
            else                         build.Armor.Add(item);
        }
        if (!set.IsEmpty) build.WeaponSets.Add(set);
        return build;
    }

    // Fusionne plusieurs codes P décodés (ligne .txt multi-sets) : l'armure vient du premier
    // code qui en a une, les armes de chaque code deviennent F1, F2, ... (cap à 4).
    public static EquipmentBuild Combine(IEnumerable<EquipmentBuild> builds)
    {
        var result = new EquipmentBuild();
        foreach (var b in builds)
        {
            if (result.Armor.Count == 0 && b.Armor.Count > 0)
                result.Armor = b.Armor;
            foreach (var set in b.WeaponSets.Where(s => !s.IsEmpty))
            {
                if (result.WeaponSets.Count >= MaxWeaponSets) break;
                result.WeaponSets.Add(set);
            }
        }
        return result;
    }

    // Élague les sets vides en fin de liste (les trous intermédiaires sont conservés :
    // les sets sont positionnels) et ramène ActiveSet dans les bornes.
    public void TrimWeaponSets()
    {
        while (WeaponSets.Count > 0 && WeaponSets[^1].IsEmpty)
            WeaponSets.RemoveAt(WeaponSets.Count - 1);
        ActiveSet = Math.Clamp(ActiveSet, 0, Math.Max(0, WeaponSets.Count - 1));
    }
}
