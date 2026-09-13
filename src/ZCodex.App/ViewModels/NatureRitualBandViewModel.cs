using ZCodex.Core.Data;
using ZCodex.Core.Models;
using System.Collections.ObjectModel;
using R = ZCodex.Core.Data.NatureRitualData.Ritual;

namespace ZCodex.App.ViewModels;

// Une icône du bandeau : un rituel de la nature (équipé, ou n'importe lequel en mode « tous »),
// togglable (clic → active/désactive l'environnement). Grisée = inactif, cadre vert = actif.
// Roaring Winds, Tranquility et — en PvP seulement — Nature's Renewal portent un rang réglable
// (molette, rang de Survie) ; les autres ont un effet fixe. La compétence reçue est déjà la
// variante du mode de jeu courant (PvE ou « (PvP) »), cf. NatureRitualBandViewModel.Refresh.
public class NatureRitualIndicatorViewModel : ViewModelBase
{
    private readonly NatureRitualEnvironment _env;

    public NatureRitualIndicatorViewModel(NatureRitualData.Descriptor d, Skill skill, NatureRitualEnvironment env, bool isEquipped)
    {
        _env = env;
        Ritual = d.Ritual;
        Skill = skill;
        IsEquipped = isEquipped;
        HasRank = NatureRitualData.HasRank(d.Ritual);

        // Tooltip du bandeau résolu au RANG du rituel (Roaring Winds / Tranquility) : les plages
        // de la description (« 20…44…50 % », « 1…4…5 more Energy ») deviennent la valeur au rang de
        // simulation affiché (badge/molette). Les 6 autres rituels (sans rang) gardent leurs plages.
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
        string rankNote = HasRank
            ? (isEquipped
                ? (fr ? "Équipé → rang du porteur ; molette = rang de simulation.\n"
                      : "Equipped → wearer's rank; scroll = simulation rank.\n")
                : (fr ? $"Rang de simulation : {Rank} (molette pour changer).\n"
                      : $"Simulation rank: {Rank} (scroll to change).\n"))
            : string.Empty;
        ClickNote = fr ? $"{rankNote}Cliquer pour {state}" : $"{rankNote}Click to {state}";
    }

    // Description résolue au rang (rituels à rang) alimentant le SkillTooltipControl du bandeau ;
    // null pour les rituels sans rang → le contrôle affiche le corps concis avec ses plages.
    public string? DescriptionOverride { get; }
    // Pendant FR de DescriptionOverride (affichage seul, jamais parsé).
    public string? DisplayDescriptionOverride { get; }

    public R Ritual { get; }
    // Vraie compétence du rituel : alimente le SkillTooltipControl du bandeau (tooltip riche).
    public Skill Skill { get; }
    public string IconPath => Skill.IconPath;
    public bool IsEquipped { get; }
    // Le rituel a-t-il un rang de Survie réglable DANS LE MODE COURANT (badge + molette) ? Tous
    // lisent le même attribut mais gardent un rang de simulation SÉPARÉ (décision Philippe).
    public bool HasRank { get; }
    // Note band-only (rang + « Cliquer pour activer/désactiver »), affichée sous le tooltip riche.
    public string ClickNote { get; }

    public bool IsActive => _env.IsActive(Ritual);
    // Rang affiché (badge) sur l'icône : le rang de simulation courant du rituel concerné.
    public int Rank => Ritual switch
    {
        R.Tranquility    => _env.TranquilityRank,
        R.NaturesRenewal => _env.NaturesRenewalRank,
        _                => _env.RoaringWindsRank,
    };

    // Rang alimentant la mention de caractéristique du tooltip : suit DescriptionOverride ci-dessus
    // (rang de simulation pour les 2 rituels à rang, plage pour les 6 autres — dont la description
    // n'est pas résolue non plus).
    public int? AttributeRank => HasRank ? Rank : null;

    // Le bandeau est reconstruit à chaque changement d'environnement, donc IsActive/Rank/tooltip
    // sont relus à neuf — pas besoin de notifier ici.
    public void Toggle() => _env.Toggle(Ritual);
    public void AdjustRank(int delta)
    {
        switch (Ritual)
        {
            case R.Tranquility:    _env.TranquilityRank    += delta; break;
            case R.NaturesRenewal: _env.NaturesRenewalRank  += delta; break;
            case R.RoaringWinds:   _env.RoaringWindsRank    += delta; break;
        }
    }
}

/// <summary>
/// Bandeau des rituels de la nature, façon [[project_conditions_band]] mais clic = toggle de
/// l'environnement. Mode « équipés seulement » (défaut) ou « tous les 8 » (<see cref="ShowAll"/>,
/// préférence de vue globale). Agrège 1..n persos (build simple = 1 ; teambuild = racines).
/// </summary>
public class NatureRitualBandViewModel : ViewModelBase
{
    // Préférence de vue GLOBALE (menu View → « Afficher tous les rituels »), persistée settings.json.
    // Statique : partagée par tous les bandeaux (teambuild + éditeurs). Le refresh est poussé par
    // MainViewModel au changement.
    public static bool ShowAll { get; set; }

    // Cache des VRAIES compétences des 8 rituels (résolu une fois depuis le catalogue) : sert l'icône
    // ET le tooltip riche du bandeau.
    private readonly Dictionary<int, Skill> _skillCache = new();
    private string _lastSig = "";

    // Nombre de compétences distinctes à mettre en cache : un id par rituel, plus un de plus pour
    // chaque rituel splitté PvE/PvP (Tranquility, Nature's Renewal).
    private static int VariantCount =>
        NatureRitualData.All.Count + NatureRitualData.All.Count(d => d.HasPvpVariant);

    public ObservableCollection<NatureRitualIndicatorViewModel> Items { get; } = new();
    public bool HasItems => Items.Count > 0;

    // Rituels de la nature (en périmètre) présents parmi des compétences équipées.
    public static IEnumerable<R> EquippedRituals(IEnumerable<Skill> equipped) =>
        equipped.Select(s => NatureRitualData.BySkillId(s.Id)).Where(d => d is not null).Select(d => d!.Ritual);

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

    public void Refresh(IEnumerable<Skill> equipped, IEnumerable<Skill> catalog, NatureRitualEnvironment env)
    {
        var equippedList = equipped.ToList();
        var equippedSet = EquippedRituals(equippedList).ToHashSet();
        // Garde « inchangé » : ne reconstruit que si les entrées visibles changent (équipés, actifs,
        // rangs, mode « tous », mode de jeu). Un changement d'ATTRIBUT ne concerne pas ce bandeau → skip.
        // Le mode PvE/PvP en fait partie : il change l'icône et les chiffres des rituels splittés.
        string sig = $"{ShowAll}|{NatureRitualData.PvpVariants}|{env.RoaringWindsRank}|{env.TranquilityRank}|{env.NaturesRenewalRank}|"
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
        foreach (var d in NatureRitualData.All)
        {
            bool isEquipped = equippedSet.Contains(d.Ritual);
            if (!isEquipped && !ShowAll) continue;            // mode « équipés seulement »
            if (!_skillCache.TryGetValue(d.DisplaySkillId, out var skill)) continue;
            Items.Add(new NatureRitualIndicatorViewModel(d, skill, env, isEquipped));
        }
        OnPropertyChanged(nameof(HasItems));
    }
}
