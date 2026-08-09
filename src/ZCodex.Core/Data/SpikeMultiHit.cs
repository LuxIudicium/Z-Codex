using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Attaques frappant plusieurs fois la MÊME cible (« coups » intrinsèques du chantier spike).
/// Le nombre de coups multiplie TOUTE la contribution de l'attaque (coup d'arme + bonus +
/// vol/perte) — décision Philippe : « chaque frappe profite des bonus de dégâts ou effets ».
/// Le malus par coup (« 25% less damage » de Dual/Triple Shot) reste lu par
/// <see cref="WeaponStrike.ModsFor"/>, pas ici : cette table ne porte que le NOMBRE de coups.
/// Ambidextres = toutes les « Dual Attack » (2 coups, une frappe par main). Liste nommée
/// validée par Philippe (il complètera au besoin).
/// </summary>
public static class SpikeMultiHit
{
    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dual Shot"] = 2,
        ["Forked Arrow"] = 2,
        ["Sun and Moon Slash"] = 2,
        ["Twin Moon Sweep"] = 2,
        ["Twin Moon Sweep (PvP)"] = 2,
        ["Triple Shot"] = 3,
        ["Triple Shot (Kurzick)"] = 3,
        ["Triple Shot (Luxon)"] = 3,
    };

    /// <summary>Nombre de coups sur la cible (1 = frappe simple). Nom prioritaire, sinon
    /// type « Dual Attack » = 2 (ambidextre), sinon 1.</summary>
    public static int Hits(Skill skill)
        => ByName.TryGetValue(skill.Name, out var n) ? n
           : skill.SkillType == "Dual Attack" ? 2
           : 1;
}
