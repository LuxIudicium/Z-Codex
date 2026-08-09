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

    // Rang de SIMULATION de Roaring Winds (le seul rituel dont l'effet dépend du rang). Utilisé
    // UNIQUEMENT quand Roaring Winds n'est PAS équipé ; équipé → rang du/des porteur(s) (le plus fort).
    private int _roaringWindsRank = 12;
    public int RoaringWindsRank
    {
        get => _roaringWindsRank;
        set
        {
            var v = Math.Clamp(value, 0, NatureRitualData.MaxRitualRank);
            if (v == _roaringWindsRank) return;
            _roaringWindsRank = v;
            RankPreview?.Invoke();                     // badge immédiat
            _rankTimer.Stop(); _rankTimer.Start();     // recalcul lourd débouncé (500 ms)
        }
    }

    // Chargement (.pn3) : pose le rang SANS déclencher de recalcul ni marquer dirty.
    public void LoadRoaringWindsRank(int rank)
        => _roaringWindsRank = Math.Clamp(rank, 0, NatureRitualData.MaxRitualRank);

    // Rang de SIMULATION de Tranquility (durée d'enchantement) — même patron que Roaring Winds :
    // utilisé UNIQUEMENT quand Tranquility n'est pas équipé ; équipé → rang du/des porteur(s).
    private int _tranquilityRank = 12;
    public int TranquilityRank
    {
        get => _tranquilityRank;
        set
        {
            var v = Math.Clamp(value, 0, NatureRitualData.MaxRitualRank);
            if (v == _tranquilityRank) return;
            _tranquilityRank = v;
            RankPreview?.Invoke();                     // badge immédiat
            _rankTimer.Stop(); _rankTimer.Start();     // recalcul lourd débouncé (500 ms)
        }
    }

    // Chargement (.pn3) : pose le rang SANS déclencher de recalcul ni marquer dirty.
    public void LoadTranquilityRank(int rank)
        => _tranquilityRank = Math.Clamp(rank, 0, NatureRitualData.MaxRitualRank);

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
