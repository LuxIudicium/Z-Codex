using System.Collections.ObjectModel;
using System.Diagnostics;

namespace ZCodex.App.ViewModels;

internal static class CollectionSync
{
    /// <summary>
    /// Aligne <paramref name="items"/> sur <paramref name="target"/> par retraits, déplacements et insertions,
    /// SANS vider la collection. Un élément déjà présent garde son conteneur WPF : un déplacement réinsère
    /// la carte existante (Panel.MoveChildren), alors que vider puis remplir (Reset) régénère TOUTES les
    /// cartes — c'est ce qui coûtait ~40 ms par personnage à chaque Ctrl+Z, variante ou repli.
    /// <paramref name="target"/> ne contient aucun doublon (nœuds d'un arbre).
    /// </summary>
    public static void SyncTo<T>(this ObservableCollection<T> items, IReadOnlyList<T> target) where T : class
    {
        var keep = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        for (int i = items.Count - 1; i >= 0; i--)
            if (!keep.Contains(items[i])) items.RemoveAt(i);

        for (int i = 0; i < target.Count; i++)
        {
            if (i < items.Count && ReferenceEquals(items[i], target[i])) continue;
            int from = -1;
            for (int k = i + 1; k < items.Count; k++)
                if (ReferenceEquals(items[k], target[i])) { from = k; break; }
            if (from >= 0) items.Move(from, i);
            else items.Insert(i, target[i]);
        }
        Debug.Assert(items.Count == target.Count, "SyncTo : la cible contenait un doublon");
    }
}
