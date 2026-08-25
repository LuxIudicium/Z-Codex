using ZCodex.Core.Models;

namespace ZCodex.Core.Sync;

/// <summary>
/// Extraction d'une composition « cadenassée » en teambuild autonome.
///
/// Un cadenas (<see cref="VariantLock"/>) relie des lignes — racines ET variantes — pour former une
/// équipe cohérente parmi toutes les combinaisons possibles d'un teambuild de travail. C'est cette
/// équipe-là qu'on veut pouvoir publier, et non l'atelier complet avec ses dizaines de variantes.
/// </summary>
public static class TeamBuildSubset
{
    /// <summary>
    /// Construit le teambuild correspondant au cadenas <paramref name="lockDef"/> de
    /// <paramref name="source"/>. Renvoie null si le cadenas ne désigne aucune ligne existante.
    ///
    /// Les membres deviennent des lignes RACINES et perdent leurs propres variantes : le cadenas
    /// désigne une équipe jouable, pas un sous-atelier. Les cadenas et le roster de spike du
    /// teambuild d'origine sont recalculés en conséquence — laisser des références vers des
    /// personnages absents produirait un document incohérent.
    /// </summary>
    public static TeamBuild? FromLock(TeamBuild source, VariantLock lockDef)
    {
        var wanted = new HashSet<Guid>(lockDef.MemberIds);
        if (wanted.Count == 0) return null;

        var found = new Dictionary<Guid, CharacterBuild>();
        Collect(source.Characters, wanted, found);
        if (found.Count == 0) return null;

        // L'ordre du cadenas fait foi : c'est celui que l'utilisateur a composé.
        var members = lockDef.MemberIds
            .Where(found.ContainsKey)
            .Select(id => Flatten(found[id]))
            .ToList();

        var keptIds = members.Select(m => m.Id).ToHashSet();

        return new TeamBuild
        {
            // L'identité est posée par l'appelant : elle doit rester STABLE d'un envoi à l'autre
            // sans jamais être celle du teambuild complet, sous peine de l'écraser côté serveur.
            Id         = Guid.Empty,
            Name       = source.Name,
            Tags       = [.. source.Tags],
            Notes      = source.Notes,
            CreatedAt  = source.CreatedAt,
            UpdatedAt  = source.UpdatedAt,
            Characters = members,
            // Le sous-ensemble EST la composition : un cadenas qui se désignerait lui-même n'aurait
            // plus de sens, et tout autre cadenas pointerait vers des lignes qu'on vient d'écarter.
            Locks = [],
            // Le roster de spike ne garde que les membres retenus.
            Spike = source.Spike.Where(s => keptIds.Contains(s.CharacterId)).ToList(),
            ActiveFlux             = source.ActiveFlux,
            ActiveNatureRituals    = [.. source.ActiveNatureRituals],
            RoaringWindsRitualRank = source.RoaringWindsRitualRank,
            TranquilityRitualRank  = source.TranquilityRitualRank,
            GameMode               = source.GameMode,
            VampiricHits3          = source.VampiricHits3,
            VampiricHits5          = source.VampiricHits5,
        };
    }

    /// <summary>Parcourt tout l'arbre — un membre de cadenas peut être une variante nichée à
    /// n'importe quelle profondeur, pas seulement une ligne racine.</summary>
    private static void Collect(List<CharacterBuild> list, HashSet<Guid> wanted,
                                Dictionary<Guid, CharacterBuild> found)
    {
        foreach (var c in list)
        {
            if (wanted.Contains(c.Id)) found.TryAdd(c.Id, c);
            if (c.Variants.Count > 0) Collect(c.Variants, wanted, found);
        }
    }

    /// <summary>Copie du personnage SANS ses variantes. Les collections sont recopiées pour que le
    /// document produit ne partage rien de mutable avec le teambuild ouvert à l'écran.</summary>
    private static CharacterBuild Flatten(CharacterBuild c) => new()
    {
        Id                      = c.Id,
        Name                    = c.Name,
        PrimaryProfession       = c.PrimaryProfession,
        SecondaryProfession     = c.SecondaryProfession,
        IsFavorite              = c.IsFavorite,
        Assignment              = c.Assignment,
        Gender                  = c.Gender,
        Attributes              = new Dictionary<string, int>(c.Attributes),
        TitleRanks              = new Dictionary<string, int>(c.TitleRanks),
        Skills                  = (Skill?[])c.Skills.Clone(),
        Equipment               = c.Equipment,
        Notes                   = c.Notes,
        DurationBoostersEnabled = c.DurationBoostersEnabled,
        ActiveAttributeBoosts   = [.. c.ActiveAttributeBoosts],
        Variants                = [],
    };
}
