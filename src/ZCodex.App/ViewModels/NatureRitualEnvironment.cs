using System.Windows.Threading;
using ZCodex.Core.Data;
using R = ZCodex.Core.Data.NatureRitualData.Ritual;

namespace ZCodex.App.ViewModels;

/// <summary>
/// Environnement de simulation « rituels de la nature » : l'ensemble des rituels ACTIFS, GLOBAL au
/// build (un rituel actif s'applique aux infobulles de tous les persos). Partagé par le teambuild
/// (persisté .pn3 v13) et le build simple (volatil, non persisté). Le propriétaire s'abonne à
/// <see cref="Changed"/> pour rafraîchir les infobulles et — côté teambuild — marquer dirty/undo.
/// Même patron que <see cref="FluxIndicatorViewModel"/>, mais un ENSEMBLE (plusieurs rituels
/// simultanés) au lieu d'un flux unique.
/// </summary>
public class NatureRitualEnvironment
{
    private readonly HashSet<R> _active = new();
    private readonly HashSet<R> _equipped = new();

    // Recalcul lourd (infobulles + dirty/undo). Immédiat pour les toggles ; DÉBOUNCÉ pour le rang.
    public event Action? Changed;
    // Mise à jour légère IMMÉDIATE (badge de rang), pendant que la molette tourne.
    public event Action? RankPreview;

    // Débounce du recalcul quand on molette le rang de Roaring Winds : on ne recalcule qu'après
    // 500 ms sans nouveau changement (une rafale de molette = un seul recalcul lourd).
    private readonly DispatcherTimer _rankTimer;

    public NatureRitualEnvironment()
    {
        _rankTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _rankTimer.Tick += (_, _) => { _rankTimer.Stop(); Changed?.Invoke(); };
    }

    public IReadOnlySet<R> Active => _active;

    public bool IsActive(R ritual) => _active.Contains(ritual);

    // Pose un rang de simulation : badge immédiat, recalcul lourd débouncé (500 ms).
    private void SetRank(ref int field, int value)
    {
        var v = Math.Clamp(value, 0, NatureRitualData.MaxRitualRank);
        if (v == field) return;
        field = v;
        RankPreview?.Invoke();                     // badge immédiat
        _rankTimer.Stop(); _rankTimer.Start();     // recalcul lourd débouncé (500 ms)
    }

    // Chargement (.pn3) : pose un rang SANS déclencher de recalcul ni marquer dirty.
    private static int ClampRank(int rank) => Math.Clamp(rank, 0, NatureRitualData.MaxRitualRank);

    // Rang de SIMULATION de Roaring Winds. Utilisé UNIQUEMENT quand Roaring Winds n'est PAS équipé ;
    // équipé → rang du/des porteur(s) (le plus fort).
    private int _roaringWindsRank = 12;
    public int RoaringWindsRank { get => _roaringWindsRank; set => SetRank(ref _roaringWindsRank, value); }
    public void LoadRoaringWindsRank(int rank) => _roaringWindsRank = ClampRank(rank);

    // Rang de SIMULATION de Tranquility (durée d'enchantement) — même patron que Roaring Winds.
    private int _tranquilityRank = 12;
    public int TranquilityRank { get => _tranquilityRank; set => SetRank(ref _tranquilityRank, value); }
    public void LoadTranquilityRank(int rank) => _tranquilityRank = ClampRank(rank);

    // Rang de SIMULATION de Nature's Renewal (surcoût d'incantation) — n'a de sens QU'EN PvP, où
    // l'effet dépend du rang (en PvE le ×2 est fixe).
    private int _naturesRenewalRank = 12;
    public int NaturesRenewalRank { get => _naturesRenewalRank; set => SetRank(ref _naturesRenewalRank, value); }
    public void LoadNaturesRenewalRank(int rank) => _naturesRenewalRank = ClampRank(rank);

    // Rang de SIMULATION d'Infuriating Heat (gain d'adrénaline, rang d'Expertise) — PvP seulement,
    // comme Nature's Renewal : en PvE le doublement est fixe.
    private int _infuriatingHeatRank = 12;
    public int InfuriatingHeatRank { get => _infuriatingHeatRank; set => SetRank(ref _infuriatingHeatRank, value); }
    public void LoadInfuriatingHeatRank(int rank) => _infuriatingHeatRank = ClampRank(rank);

    // Rang de SIMULATION de Ronces (durée du saignement posé sur les créatures assommées, lot 4c) — même patron
    // que Roaring Winds : c'est un esprit, donc proposé à tout moment, et son effet dépend du rang.
    private int _bramblesRank = 12;
    public int BramblesRank { get => _bramblesRank; set => SetRank(ref _bramblesRank, value); }
    public void LoadBramblesRank(int rank) => _bramblesRank = ClampRank(rank);

    // Lien terrestre n'a PAS de rang : son plancher de 3 s est fixe, quelle que soit la Communion du lanceur.
    // Mark of Fury et Energizing Chorus n'ont PAS de rang de simulation (Philippe, 15/09/2026) : ils ne sont
    // proposés que portés, au rang de leur porteur le plus fort.

    // Effet porté par au moins un perso (ensemble tenu par SyncEquipped). Un effet « équipé seulement »
    // (Dark Fury, Mark of Fury, Energizing Chorus) n'est proposé que dans ce cas, menu Sélection compris.
    public bool IsEquipped(R ritual) => _equipped.Contains(ritual);

    // Synchronise l'ensemble ÉQUIPÉ et DÉSACTIVE les rituels qui viennent d'être retirés du build
    // (option B, décision Philippe : « extinction au retrait »). Un rituel activé sans jamais avoir
    // été équipé (menu View / bandeau « tous ») n'entre jamais dans _equipped → n'est PAS purgé.
    public void SyncEquipped(IEnumerable<R> equipped)
    {
        var now = new HashSet<R>(equipped);
        bool pruned = false;
        foreach (var r in _equipped)
            if (!now.Contains(r) && _active.Remove(r)) pruned = true;
        _equipped.Clear();
        _equipped.UnionWith(now);
        if (pruned) Changed?.Invoke();   // se restabilise : le refresh rappelle SyncEquipped (equipped stable → no-op)
    }

    public void Set(R ritual, bool on)
    {
        if (on ? _active.Add(ritual) : _active.Remove(ritual)) Changed?.Invoke();
    }

    public void Toggle(R ritual) => Set(ritual, !_active.Contains(ritual));

    public void Clear()
    {
        if (_active.Count == 0) return;
        _active.Clear();
        Changed?.Invoke();
    }

    /// <summary>Recharge l'ensemble depuis des SkillId persistés (ignore les inconnus).</summary>
    public void LoadFromSkillIds(IEnumerable<int> skillIds)
    {
        _active.Clear();
        foreach (var id in skillIds)
            if (NatureRitualData.BySkillId(id) is { } d) _active.Add(d.Ritual);
        Changed?.Invoke();
    }

    /// <summary>SkillId des rituels actifs, pour la persistance.</summary>
    public List<int> ToSkillIds() => _active.Select(NatureRitualData.SkillIdOf).ToList();
}
