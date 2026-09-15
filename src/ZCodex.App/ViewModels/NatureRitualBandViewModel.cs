using ZCodex.Core.Data;
using ZCodex.Core.Models;
using System.Collections.ObjectModel;
using R = ZCodex.Core.Data.NatureRitualData.Ritual;

namespace ZCodex.App.ViewModels;

// Une icône du bandeau d'équipe : un esprit (rituel de la nature, Soothing) ou un effet porté par un perso
// (Dark Fury, Mark of Fury, Energizing Chorus), togglable (clic → active/désactive l'environnement). Grisée
// = inactif, cadre vert = actif, rouge pour un effet ennemi (Soothing). Roaring Winds, Tranquility et — en
// PvP seulement — Nature's Renewal et Infuriating Heat portent un rang de simulation réglable (molette) ;
// Mark of Fury et Energizing Chorus un badge au rang du porteur le plus fort, sans molette ; les autres ont
// un effet fixe. La compétence reçue est déjà la variante du mode de jeu courant (PvE ou « (PvP) »), cf.
// NatureRitualBandViewModel.Refresh.
public class NatureRitualIndicatorViewModel : ViewModelBase
{
    private readonly NatureRitualEnvironment _env;
    // Rang du porteur le plus fort, pour un effet « équipé seulement » à rang (Mark of Fury, Energizing Chorus) ;
    // null sinon.
    private readonly int? _wearerRank;

    public NatureRitualIndicatorViewModel(
        NatureRitualData.Descriptor d, Skill skill, NatureRitualEnvironment env, bool isEquipped, bool startsGroup,
        int? wearerRank = null)
    {
        _env = env;
        _wearerRank = wearerRank;
        Ritual = d.Ritual;
        Skill = skill;
        IsEquipped = isEquipped;
        StartsGroup = startsGroup;
        IsEnemyEffect = d.IsEnemyEffect;
        HasRank = NatureRitualData.HasRank(d.Ritual);
        // Effet « équipé seulement » : badge au rang du porteur le plus fort, pas de molette.
        CanAdjustRank = HasRank && !d.EquippedOnly;

        // Tooltip du bandeau résolu au RANG de l'effet (Roaring Winds / Tranquility…) : les plages
        // de la description (« 20…44…50 % », « 1…4…5 more Energy ») deviennent la valeur au rang
        // affiché (badge). Les effets sans rang gardent leurs plages.
        DescriptionOverride = HasRank
            ? SkillProgression.Resolve(SkillText.ConciseBody(skill.Description, skill.SkillType), skill.Progression, Rank)
            : null;
        // Variante AFFICHÉE en mode FR (texte gwiki résolu au même rang) ; null → repli EN.
        DisplayDescriptionOverride = HasRank && AppLanguage.IsFr && skill.DescriptionFr.Length > 0 && !skill.FrSuspect
            ? SkillProgression.Resolve(skill.DescriptionFr, skill.Progression, Rank, frAnchors: true)
            : null;

        // Note ajoutée SOUS le vrai tooltip de la compétence (band uniquement) : rang réglable +
        // instruction de clic. La description/stats viennent du SkillTooltipControl.
        bool fr = AppLanguage.IsFr;
        string state = env.IsActive(d.Ritual) ? (fr ? "désactiver" : "deactivate") : (fr ? "activer" : "activate");
        string rankNote = !HasRank ? string.Empty
            : !CanAdjustRank
                ? (fr ? "Rang du porteur le plus fort.\n" : "Highest carrier's rank.\n")
            : isEquipped
                ? (fr ? "Équipé → rang du porteur ; molette = rang de simulation.\n"
                      : "Equipped → wearer's rank; scroll = simulation rank.\n")
                : (fr ? $"Rang de simulation : {Rank} (molette pour changer).\n"
                      : $"Simulation rank: {Rank} (scroll to change).\n");
        // Sens de l'icône allumée quand il n'est pas évident : Soothing est subi (lancé par l'ennemi),
        // Mark of Fury ne profite qu'à qui frappe la cible marquée.
        string meaningNote = d.IsEnemyEffect
            ? (fr ? "Effet ennemi subi par l'équipe.\n" : "Enemy effect suffered by the team.\n")
            : d.Ritual == R.MarkOfFury
                ? (fr ? "Allumée = on frappe la cible marquée.\n" : "On = hitting the marked target.\n")
                : string.Empty;
        ClickNote = fr ? $"{meaningNote}{rankNote}Cliquer pour {state}" : $"{meaningNote}{rankNote}Click to {state}";
    }

    // Premier effet d'une nouvelle famille (hors premier du bandeau) → petit séparateur devant lui.
    public bool StartsGroup { get; }
    // Effet lancé par l'ennemi (Soothing) → cadre rouge une fois allumé.
    public bool IsEnemyEffect { get; }

    // Description résolue au rang (effets à rang) alimentant le SkillTooltipControl du bandeau ;
    // null pour les effets sans rang → le contrôle affiche le corps concis avec ses plages.
    public string? DescriptionOverride { get; }
    // Pendant FR de DescriptionOverride (affichage seul, jamais parsé).
    public string? DisplayDescriptionOverride { get; }

    public R Ritual { get; }
    // Vraie compétence du rituel : alimente le SkillTooltipControl du bandeau (tooltip riche).
    public Skill Skill { get; }
    public string IconPath => Skill.IconPath;
    public bool IsEquipped { get; }
    // L'effet a-t-il un rang DANS LE MODE COURANT (badge) ? Chaque esprit à rang garde un rang de
    // simulation SÉPARÉ (décision Philippe).
    public bool HasRank { get; }
    // Note band-only (rang + « Cliquer pour activer/désactiver »), affichée sous le tooltip riche.
    public string ClickNote { get; }

    public bool IsActive => _env.IsActive(Ritual);
    // Le rang se règle-t-il à la molette ? Seulement pour un esprit à rang : un effet « équipé seulement »
    // (Mark of Fury, Energizing Chorus) prend toujours le rang de son porteur le plus fort.
    public bool CanAdjustRank { get; }

    // Rang affiché (badge) sur l'icône : rang de simulation de l'esprit concerné, ou rang du porteur le plus
    // fort pour un effet « équipé seulement » (0 si personne : il n'est alors pas affiché).
    public int Rank => !CanAdjustRank ? _wearerRank ?? 0 : Ritual switch
    {
        R.Tranquility     => _env.TranquilityRank,
        R.NaturesRenewal  => _env.NaturesRenewalRank,
        R.InfuriatingHeat => _env.InfuriatingHeatRank,
        _                 => _env.RoaringWindsRank,
    };

    // Rang alimentant la mention de caractéristique du tooltip : suit DescriptionOverride ci-dessus
    // (rang affiché pour les effets à rang, plage pour les autres — dont la description n'est pas
    // résolue non plus).
    public int? AttributeRank => HasRank ? Rank : null;

    // Le bandeau est reconstruit à chaque changement d'environnement, donc IsActive/Rank/tooltip
    // sont relus à neuf — pas besoin de notifier ici.
    public void Toggle() => _env.Toggle(Ritual);
    public void AdjustRank(int delta)
    {
        switch (Ritual)
        {
            case R.Tranquility:     _env.TranquilityRank     += delta; break;
            case R.NaturesRenewal:  _env.NaturesRenewalRank  += delta; break;
            case R.RoaringWinds:    _env.RoaringWindsRank    += delta; break;
            case R.InfuriatingHeat: _env.InfuriatingHeatRank += delta; break;
        }
    }
}

/// <summary>
/// Bandeau des effets d'équipe (rituels de la nature + effets d'adrénaline du lot 1b + Energizing Chorus du
/// lot 2b), façon [[project_conditions_band]] mais clic = toggle de l'environnement. Mode « équipés
/// seulement » (défaut) ou « tous » (<see cref="ShowAll"/>, préférence de vue globale), qui n'ajoute que les
/// esprits (rituels de la nature, Soothing) : Dark Fury, Mark of Fury et Energizing Chorus n'apparaissent
/// que portés (décision Philippe, 15/09/2026). Agrège 1..n persos (build simple = 1 ; teambuild = racines).
/// </summary>
public class NatureRitualBandViewModel : ViewModelBase
{
    // Préférence de vue GLOBALE (menu View → « Afficher tous les effets d'équipe »), persistée
    // settings.json. Statique : partagée par tous les bandeaux (teambuild + éditeurs). Le refresh est
    // poussé par MainViewModel au changement.
    public static bool ShowAll { get; set; }

    // Cache des VRAIES compétences des effets du bandeau (résolu une fois depuis le catalogue) : sert
    // l'icône ET le tooltip riche du bandeau.
    private readonly Dictionary<int, Skill> _skillCache = new();
    private string _lastSig = "";

    // Nombre de compétences distinctes à mettre en cache : un id par effet, plus un de plus pour
    // chaque effet splitté PvE/PvP (Tranquility, Nature's Renewal, Infuriating Heat, Soothing).
    private static int VariantCount =>
        NatureRitualData.All.Count + NatureRitualData.All.Count(d => d.HasPvpVariant);

    public ObservableCollection<NatureRitualIndicatorViewModel> Items { get; } = new();
    public bool HasItems => Items.Count > 0;

    // Effets du bandeau proposés par des compétences équipées. Soothing est lancé par l'ennemi : le
    // porter dans l'équipe le vise, lui, et ne compte donc jamais ; il est proposé dès qu'un perso
    // porte une compétence d'adrénaline (décision Philippe, 14/09/2026).
    public static IEnumerable<R> EquippedRituals(IEnumerable<Skill> equipped)
    {
        var skills = equipped as IReadOnlyCollection<Skill> ?? equipped.ToList();
        var found = skills.Select(s => NatureRitualData.BySkillId(s.Id))
            .Where(d => d is { IsEnemyEffect: false }).Select(d => d!.Ritual);
        return skills.Any(s => s.Adrenaline > 0) ? found.Append(R.Soothing) : found;
    }

    public void Clear()
    {
        _lastSig = "";
        if (Items.Count == 0) return;
        Items.Clear();
        OnPropertyChanged(nameof(HasItems));
    }

    // Bascule de langue : les indicateurs bakent ClickNote et la description résolue (FR/EN) à la
    // construction. Rien de « visible » n'ayant changé, la garde de signature sauterait le rebuild →
    // on l'invalide pour que le prochain Refresh reconstruise dans la langue courante.
    public void InvalidateLanguage() => _lastSig = "";

    /// <param name="wearerRank">Rang du porteur le plus fort d'un effet (null si personne ne l'équipe) : badge des
    /// effets « équipés seulement » (Mark of Fury, Energizing Chorus).</param>
    public void Refresh(IEnumerable<Skill> equipped, IEnumerable<Skill> catalog, NatureRitualEnvironment env,
                        Func<R, int?>? wearerRank = null)
    {
        var equippedList = equipped.ToList();
        var equippedSet = EquippedRituals(equippedList).ToHashSet();
        // Rangs des porteurs des effets « équipés seulement » à rang : un changement d'ATTRIBUT ne concerne ce
        // bandeau que par eux.
        var wearerRanks = NatureRitualData.All
            .Where(d => d.EquippedOnly && NatureRitualData.HasRank(d.Ritual))
            .Select(d => wearerRank?.Invoke(d.Ritual));
        // Garde « inchangé » : ne reconstruit que si les entrées visibles changent (équipés, actifs,
        // rangs, mode « tous », mode de jeu).
        // Le mode PvE/PvP en fait partie : il change l'icône et les chiffres des rituels splittés.
        string sig = $"{ShowAll}|{NatureRitualData.PvpVariants}|{env.RoaringWindsRank}|{env.TranquilityRank}|{env.NaturesRenewalRank}|"
            + $"{env.InfuriatingHeatRank}|{string.Join(",", wearerRanks)}|"
            + string.Join(",", equippedSet.Select(r => (int)r).OrderBy(x => x)) + "|"
            + string.Join(",", env.Active.Select(r => (int)r).OrderBy(x => x));
        if (sig == _lastSig) return;
        _lastSig = sig;

        // Cache des compétences des rituels, VARIANTES « (PvP) » COMPRISES (résolu une fois depuis
        // le catalogue — stable après chargement) : le bandeau affiche celle du mode courant.
        if (_skillCache.Count < VariantCount)
            foreach (var s in catalog)
                if (NatureRitualData.BySkillId(s.Id) is not null)
                    _skillCache[s.Id] = s;

        Items.Clear();
        NatureRitualData.BandGroup? lastGroup = null;
        foreach (var d in NatureRitualData.All)
        {
            bool isEquipped = equippedSet.Contains(d.Ritual);
            // Mode « équipés seulement » ; en mode « tous », seuls les esprits s'ajoutent : un effet « équipé
            // seulement » (Dark Fury, Mark of Fury, Energizing Chorus — Philippe 15/09/2026) reste caché sans porteur.
            if (!isEquipped && (!ShowAll || d.EquippedOnly)) continue;
            if (!_skillCache.TryGetValue(d.DisplaySkillId, out var skill)) continue;
            // Séparateur devant le premier effet VISIBLE d'une nouvelle famille.
            bool startsGroup = lastGroup is { } g && g != d.Group;
            lastGroup = d.Group;
            Items.Add(new NatureRitualIndicatorViewModel(d, skill, env, isEquipped, startsGroup,
                d.EquippedOnly ? wearerRank?.Invoke(d.Ritual) : null));
        }
        OnPropertyChanged(nameof(HasItems));
    }
}
