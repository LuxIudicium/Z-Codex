using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Nature d'une réduction de coût : pourcentage du coût de BASE (Attuned Was Songkai, Renewing
/// Memories), points retirés (la plupart), ou coût ramené à zéro (Way of the Empty Palm).</summary>
public enum EnergyCostKind { PercentOfBase, Flat, Free }

/// <summary>
/// Compétence du perso qui réduit le coût en énergie de ses autres compétences, togglée par icône sur sa
/// carte (chantier infobulle, lot 2 ; simulation Z-Codex : icône allumée = effet actif, charge disponible
/// ou condition remplie). <paramref name="Applies"/> = compétences touchées. Valeur = <paramref name="FixedValue"/>,
/// ou progression[<paramref name="ProgressionIndex"/>] au rang de <paramref name="ScalingAttribute"/>.
/// <paramref name="Minimum"/> = « minimum N » du texte (0 sinon). <paramref name="BaseSkillId"/> = id PvE d'une
/// variante « (PvP) » : l'icône est mémorisée sur l'id de base (patron AdrenalineBoostDescriptor).
/// </summary>
public sealed record EnergyCostBoostDescriptor(
    int SkillId, EnergyCostKind Kind, Func<Skill, bool> Applies,
    int FixedValue = 0, string? ScalingAttribute = null, int ProgressionIndex = 0,
    int Minimum = 0, bool RemovesOvercast = false, int BaseSkillId = 0)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

/// <summary>Réductions cumulées qui s'appliquent à UNE compétence. PercentOfBase : somme des % ;
/// Flat : points retirés ; Minimum : plus haut « minimum N » des réductions en points actives ;
/// Free : coût ramené à 0 ; RemovesOvercast : la compétence ne cause pas d'afflux (Glyph of Energy).</summary>
public readonly record struct EnergyReduction(
    int PercentOfBase = 0, int Flat = 0, int Minimum = 0, bool Free = false, bool RemovesOvercast = false)
{
    public bool LowersCost => Free || PercentOfBase > 0 || Flat > 0;
}

public static class EnergyCostBoostData
{
    // ── Compétences touchées (valeurs SkillType exactes de la base) ──────────
    // « Sort » = tout type « … Spell » ; la base contient une coquille « Hex spell » → insensible à la casse.
    // Les compétences Élémentaliste qui ne sont pas des sorts (glyphes, Shock…) ne profitent PAS des
    // glyphes d'énergie : testé en jeu par Philippe le 14/09/2026, contre la note du wiki.
    public static bool IsSpell(Skill s) => s.SkillType.Contains("Spell", StringComparison.OrdinalIgnoreCase);
    private static bool IsBindingRitual(Skill s) => s.SkillType == "Binding Ritual";
    private static bool IsBowAttack(Skill s) => s.SkillType.Contains("Bow Attack", StringComparison.Ordinal);

    // Selfless Spirit : « spells you cast that target another ally » (choix B de Philippe, 14/09/2026 : icône
    // allumée = « je vise un autre allié »). Qui un sort peut viser vient du wiki (AllyTargetSpells) : le texte
    // concis ne le dit pas assez souvent (Cure Hex, Weapon of Warding manquaient au repérage par le texte).
    public static bool TargetsAnotherAlly(Skill s) => IsSpell(s) && AllyTargetSpells.Names.Contains(s.Name);

    /// <summary>Id des deux Selfless Spirit (Kurzick / Luxon) : même compétence, deux entrées.</summary>
    public const int SelflessSpiritKurzickSkillId = 900009;
    public const int SelflessSpiritLuxonSkillId = 900010;

    // SkillId, colonnes de progression et caractéristiques relevés dans la base réelle (14/09/2026).
    public static readonly IReadOnlyList<EnergyCostBoostDescriptor> All = new EnergyCostBoostDescriptor[]
    {
        new(1220, EnergyCostKind.PercentOfBase, s => IsSpell(s) || IsBindingRitual(s),
            ScalingAttribute: "Spawning Power"),                                                    // Attuned Was Songkai : −5…41…50 %
        new(1739, EnergyCostKind.PercentOfBase, s => s.SkillType is "Weapon Spell" or "Item Spell",
            ScalingAttribute: "Spawning Power", ProgressionIndex: 1),                               // Renewing Memories : −5…29…35 %
        new(1223, EnergyCostKind.Flat, s => s.Profession == Profession.Ritualist && NatureRitualData.IsHex(s),
            ScalingAttribute: "Spawning Power", ProgressionIndex: 1),                               // Anguished Was Lingwah : −1…4…5
        new(806,  EnergyCostKind.Flat, s => s.Profession == Profession.Necromancer && IsSpell(s),
            ScalingAttribute: "Blood Magic"),                                                       // Cultist's Fervor : −1…5…6
        new(310,  EnergyCostKind.Flat, s => s.Profession == Profession.Monk && IsSpell(s),
            FixedValue: 5, Minimum: 1),                                                             // Divine Spirit : −5, minimum 1
        new(2145, EnergyCostKind.Flat, IsBowAttack, ScalingAttribute: "Expertise"),                 // Expert Focus : −1…2…2
        new(199,  EnergyCostKind.Flat, IsSpell, ScalingAttribute: "Energy Storage", ProgressionIndex: 1,
            RemovesOvercast: true),                                                                 // Glyph of Energy : −10…22…25, pas d'afflux
        new(200,  EnergyCostKind.Flat, IsSpell, ScalingAttribute: "Energy Storage"),                // Glyph of Lesser Energy : −10…16…18
        new(1394, EnergyCostKind.Flat, s => IsSpell(s) && s.Attribute == "Healing Prayers",
            ScalingAttribute: "Healing Prayers"),                                                   // Healer's Covenant : −1…3…4
        new(763,  EnergyCostKind.Flat, NatureRitualData.IsEnchantment,
            ScalingAttribute: "Blood Magic", ProgressionIndex: 2),                                  // Jaundiced Gaze : −1…8…10
        new(SelflessSpiritKurzickSkillId, EnergyCostKind.Flat, TargetsAnotherAlly, FixedValue: 3), // Selfless Spirit (Kurzick)
        new(SelflessSpiritLuxonSkillId,   EnergyCostKind.Flat, TargetsAnotherAlly, FixedValue: 3), // Selfless Spirit (Luxon)
        new(1240, EnergyCostKind.Flat, IsBindingRitual, FixedValue: 15, Minimum: 5),                // Soul Twisting : −15, minimum 5
        new(3461, EnergyCostKind.Flat, IsBindingRitual, FixedValue: 15, Minimum: 5, BaseSkillId: 1240), // Soul Twisting (PvP)
        new(987,  EnergyCostKind.Free, s => s.SkillType is "Off-Hand Attack" or "Dual Attack"),     // Way of the Empty Palm : coût 0
    };

    private static readonly Dictionary<int, EnergyCostBoostDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);
    private static readonly HashSet<int> _toggleIds = All.Select(d => d.ToggleId).ToHashSet();

    public static EnergyCostBoostDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    /// <summary>Valeur de la réduction portée par <paramref name="source"/> (la compétence équipée), au rang
    /// <paramref name="rank"/> de sa caractéristique d'échelle (ignoré pour une valeur fixe).</summary>
    public static int ValueOf(EnergyCostBoostDescriptor d, Skill source, int rank) =>
        d.ScalingAttribute is null
            ? d.FixedValue
            : SkillProgression.IntAt(
                source.Progression is { } p && d.ProgressionIndex < p.Length ? p[d.ProgressionIndex] : null, rank) ?? 0;

    /// <summary>Réductions actives qui touchent <paramref name="target"/>, cumulées : les % s'additionnent
    /// (tests en jeu de Philippe, 14/09/2026), les points aussi, et le plus haut minimum tient quel que soit
    /// l'ordre de lancement (Divine Spirit + Healer's Covenant sur un sort à 5 = 1, testé en jeu).</summary>
    public static EnergyReduction ReductionFor(
        Skill target, IEnumerable<(EnergyCostBoostDescriptor Descriptor, int Value)> active)
    {
        int percent = 0, flat = 0, minimum = 0;
        bool free = false, removesOvercast = false;
        foreach (var (d, value) in active.DistinctBy(a => a.Descriptor.ToggleId))
        {
            if (!d.Applies(target)) continue;
            switch (d.Kind)
            {
                case EnergyCostKind.PercentOfBase: percent += value; break;
                case EnergyCostKind.Flat: flat += value; minimum = Math.Max(minimum, d.Minimum); break;
                case EnergyCostKind.Free: free = true; break;
            }
            removesOvercast |= d.RemovesOvercast;
        }
        return new(percent, flat, minimum, free, removesOvercast);
    }
}
