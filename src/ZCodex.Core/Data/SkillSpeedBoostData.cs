using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Nature d'un effet sur la recharge ou l'incantation (chantier infobulle, lot 3) : pourcentage de réduction,
/// recharge ou incantation instantanée, secondes de recharge ajoutées (Glyph of Sacrifice, Auspicious Incantation, Rage
/// of the Ntouka), secondes d'incantation retirées (Jaundiced Gaze), incantation ramenée à ¼ s (Glyph of Sacrifice : bug
/// du jeu constaté par Philippe le 15/09/2026, le texte dit « instantanément »).</summary>
public enum SpeedEffectKind { None, Percent, Instant, AddedSeconds, FlatSeconds, QuarterSecond }

/// <summary>
/// Compétence qui change la recharge et/ou l'incantation d'autres compétences, togglée par icône sur la carte du perso
/// (simulation Z-Codex : icône allumée = effet actif, charge disponible ou condition remplie). Valeur fixe (<c>…Value</c>),
/// ou progression[<c>…Index</c>] au rang de <paramref name="ScalingAttribute"/>. <paramref name="BlockSeconds"/> : secondes
/// pendant lesquelles TOUTES les attaques sont bloquées (Deadly Paradox), affichées « 10+recharge » et jamais réduites.
/// <paramref name="AttackSpeedValue"/> / <paramref name="AttackSpeedIndex"/> : vitesse d'attaque en % (IAS), qui raccourcit
/// l'incantation des attaques. <paramref name="RawRank"/> : rang lu sans les boosts de compétence (Ritual Lord ne profite
/// pas de son propre bonus). <paramref name="Received"/> : effet reçu d'un allié (Weapon of Quickening, patron Weapon of
/// Fury). <paramref name="BaseSkillId"/> = id PvE d'une variante « (PvP) » : l'icône est mémorisée sur l'id de base.
/// </summary>
public sealed record SkillSpeedBoostDescriptor(
    int SkillId, Func<Skill, bool> Applies,
    SpeedEffectKind Recharge = SpeedEffectKind.None, int RechargeValue = 0, int RechargeIndex = -1,
    SpeedEffectKind Cast = SpeedEffectKind.None, int CastValue = 0, int CastIndex = -1,
    int BlockSeconds = 0, int AttackSpeedValue = 0, int AttackSpeedIndex = -1,
    string? ScalingAttribute = null, bool RawRank = false, bool Received = false, int BaseSkillId = 0)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;

    /// <summary>Effet de vitesse d'attaque (IAS) : ne touche que l'incantation des attaques.</summary>
    public bool HasAttackSpeed => AttackSpeedValue != 0 || AttackSpeedIndex >= 0;
}

/// <summary>Effets cumulés qui touchent UNE compétence. <c>…Cut</c> = part retirée par les pourcentages multipliés entre
/// eux (0 = aucune) ; <c>…Strongest</c> = plus forte réduction seule, qui peut franchir le plafond de 50 % ;
/// <c>RechargeBlock</c> = secondes de blocage des attaques (Deadly Paradox), affichées à part ;
/// <c>AttackSpeedCut</c> = part de la DURÉE d'attaque retirée par les effets de vitesse d'attaque (0,33 = « attaquer
/// 33 % plus vite »), qui raccourcit d'autant l'incantation des attaques.</summary>
public readonly record struct SkillSpeed(
    decimal RechargeCut = 0m, int RechargeStrongest = 0, bool RechargeInstant = false, int RechargeAdded = 0,
    decimal CastCut = 0m, int CastStrongest = 0, bool CastInstant = false, bool CastQuarter = false, int CastFlat = 0,
    int RechargeBlock = 0, decimal AttackSpeedCut = 0m)
{
    public bool ChangesRecharge => RechargeCut > 0m || RechargeInstant || RechargeAdded != 0 || RechargeBlock > 0;
    public bool ChangesCast => CastCut > 0m || CastInstant || CastQuarter || CastFlat > 0 || AttackSpeedCut > 0m;
}

public static class SkillSpeedBoostData
{
    /// <summary>Weapon of Quickening : seule compétence du lot 3 reçue d'un allié (champ « target = allies » du wiki).</summary>
    public const int WeaponOfQuickeningSkillId = 1268;
    public const int GhostlyHasteSkillId = 1244;
    public const int SignetOfMysticSpeedSkillId = 2200;
    public const int DeadlyParadoxSkillId = 572;

    /// <summary>Plafond de vitesse d'attaque : « +33 % » toutes sources confondues, c'est-à-dire 33 % de la DURÉE d'attaque
    /// en moins (wiki *Attack speed* : « An effect that states that a creature "attacks X% faster" reduces the duration of
    /// each of that creature's attacks by that amount » ; table : hache 1,33 s → 0,8911 s à +33 %, soit ~+49 % d'attaques).</summary>
    public const decimal MaxAttackSpeedCut = 0.33m;

    // ── Compétences touchées (valeurs SkillType exactes de la base) ──────────
    private static bool IsSpell(Skill s) => EnergyCostBoostData.IsSpell(s);
    private static bool IsBindingRitual(Skill s) => s.SkillType == "Binding Ritual";
    private static bool IsHalfRangeSpell(Skill s) => s.SkillType.StartsWith("Half Range", StringComparison.Ordinal) && IsSpell(s);
    private static bool IsHealingPrayersSpell(Skill s) => IsSpell(s) && s.Attribute == "Healing Prayers";
    private static bool IsDervishEnchantment(Skill s) => s.Profession == Profession.Dervish && NatureRitualData.IsEnchantment(s);

    // Compétences d'Assassin (Deadly Paradox) : les compétences d'allégeance Kurzick/Luxon sont stockées sans profession
    // mais verrouillées à une profession — c'est GwAllegianceData qui le dit (retouche demandée par Philippe le 16/09/2026).
    private static bool IsAssassinSkill(Skill s) =>
        s.Profession == Profession.Assassin || GwAllegianceData.RequiredProfession(s) == Profession.Assassin;

    // Signet of Mystic Speed : « self-targeting enchantments » = ceux lancés sur soi ET les enchantements sans cible (note
    // du wiki, Aegis compris). Qui un enchantement peut viser vient du champ « target » des pages du wiki (relevé du
    // 15/09/2026 sur les 222 enchantements non éclair de la base) : seuls ceux-ci ne peuvent pas être lancés sur soi.
    private static readonly HashSet<string> NotSelfTargetable = new(StringComparer.Ordinal)
    {
        // autre allié seulement (« Cannot self-target »)
        "Aegis (PvP)", "Air of Enchantment", "Aura of Stability", "Blood Ritual", "Blood is Power", "Healing Seed",
        "Holy Wrath", "Life Barrier", "Life Bond", "Recall", "Seed of Life", "Shadow Meld", "Spotless Mind",
        "Spotless Soul", "Succor",
        // ennemi
        "Aura of Displacement", "Ice Spear", "Jaundiced Gaze", "Magnetic Surge", "Vampiric Spirit",
        // allié mort, serviteur
        "Unyielding Aura (PvP)", "Vengeance", "Jagged Bones",
    };

    public static bool IsSelfTargetableEnchantment(Skill s) =>
        NatureRitualData.IsEnchantment(s) && !NotSelfTargetable.Contains(s.Name);

    // SkillId, colonnes de progression et caractéristiques relevés dans la base réelle (15-16/09/2026). Iron Mist touche
    // TOUS les sorts (bug du jeu, décision Philippe) ; Rage of the Ntouka AJOUTE 3 s (note du wiki, décision Philippe) ;
    // Deadly Paradox bloque 10 s TOUTES les attaques, réduction ou non (décision Philippe du 16/09).
    public static readonly IReadOnlyList<SkillSpeedBoostDescriptor> All = new SkillSpeedBoostDescriptor[]
    {
        new(1240, IsBindingRitual, Recharge: SpeedEffectKind.Instant),                                        // Soul Twisting : recharge instantanée
        new(3461, IsBindingRitual, Recharge: SpeedEffectKind.Percent, RechargeIndex: 2,
            ScalingAttribute: "Spawning Power", BaseSkillId: 1240),                                            // Soul Twisting (PvP) : −25…45…50 %
        new(987,  s => s.SkillType is "Off-Hand Attack" or "Dual Attack",
            Recharge: SpeedEffectKind.Percent, RechargeIndex: 1, ScalingAttribute: "Deadly Arts"),              // Way of the Empty Palm : −25…45…50 %
        new(763,  NatureRitualData.IsEnchantment, Cast: SpeedEffectKind.FlatSeconds, CastIndex: 1,
            ScalingAttribute: "Blood Magic"),                                                                   // Jaundiced Gaze : −0…1…1 s
        new(1638, IsHalfRangeSpell, Recharge: SpeedEffectKind.Percent, RechargeIndex: 2,
            Cast: SpeedEffectKind.Percent, CastIndex: 1, ScalingAttribute: "Critical Strikes"),                 // Deadly Haste : −5…49…60 % / −5…41…50 %
        new(DeadlyParadoxSkillId, IsAssassinSkill,
            Recharge: SpeedEffectKind.Percent, RechargeValue: 33, Cast: SpeedEffectKind.Percent, CastValue: 33,
            BlockSeconds: 10),                                                                                  // Deadly Paradox (+ blocage des attaques)
        new(1096, IsSpell, Cast: SpeedEffectKind.Instant),                                                       // Glyph of Essence
        new(202,  IsSpell, Recharge: SpeedEffectKind.AddedSeconds, RechargeValue: 30,
            Cast: SpeedEffectKind.QuarterSecond),                                                               // Glyph of Sacrifice : ¼ s, +30 s
        new(203,  IsSpell, Recharge: SpeedEffectKind.Instant),                                                   // Glyph of Renewal
        new(1393, IsHealingPrayersSpell, Cast: SpeedEffectKind.Percent, CastValue: 50),                          // Healer's Boon
        new(1685, IsHealingPrayersSpell, Cast: SpeedEffectKind.Percent, CastValue: 50),                          // Holy Haste
        new(216,  IsSpell, Recharge: SpeedEffectKind.Percent, RechargeValue: 33,
            Cast: SpeedEffectKind.Percent, CastValue: 33),                                                      // Iron Mist
        new(2411, IsSpell, Cast: SpeedEffectKind.Percent, CastValue: 20),                                        // Mindbender
        new(3424, IsSpell, Recharge: SpeedEffectKind.Percent, RechargeIndex: 1,
            Cast: SpeedEffectKind.Percent, CastIndex: 0, ScalingAttribute: "Energy Storage"),                   // Over the Limit : −40…72…80 % / −20…44…50 %
        new(SignetOfMysticSpeedSkillId, IsSelfTargetableEnchantment, Cast: SpeedEffectKind.Instant),             // Signet of Mystic Speed
        new(1475, s => s.SkillType == "Trap", Recharge: SpeedEffectKind.Percent, RechargeValue: 25,
            Cast: SpeedEffectKind.Percent, CastValue: 25),                                                      // Trapper's Speed
        new(1521, IsDervishEnchantment, Recharge: SpeedEffectKind.Percent, RechargeValue: 50),                   // Avatar of Lyssa
        new(1512, IsDervishEnchantment, Recharge: SpeedEffectKind.Percent, RechargeValue: 33),                   // Lyssa's Haste
        new(3348, IsDervishEnchantment, Recharge: SpeedEffectKind.Percent, RechargeValue: 33, BaseSkillId: 1512), // Lyssa's Haste (PvP)
        new(GhostlyHasteSkillId, IsSpell, Recharge: SpeedEffectKind.Percent, RechargeValue: 25),                 // Ghostly Haste (à portée d'un esprit)
        new(13,   IsSpell, Recharge: SpeedEffectKind.Percent, RechargeValue: 33),                                // Mantra of Recovery
        new(2002, IsSpell, Recharge: SpeedEffectKind.Percent, RechargeValue: 25),                                // Glyph of Swiftness
        new(15,   NatureRitualData.IsSignet, Recharge: SpeedEffectKind.Percent, RechargeIndex: 1,
            ScalingAttribute: "Inspiration Magic"),                                                             // Mantra of Inscriptions : −10…34…40 %
        new(1658, NatureRitualData.IsSignet, Recharge: SpeedEffectKind.Percent, RechargeIndex: 1,
            ScalingAttribute: "Fast Casting"),                                                                  // Symbolic Posture : −20…68…80 %
        new(1408, s => s.Adrenaline > 0, Recharge: SpeedEffectKind.AddedSeconds, RechargeValue: 3),              // Rage of the Ntouka : +3 s
        new(1217, IsBindingRitual, Recharge: SpeedEffectKind.Percent, RechargeIndex: 2,
            ScalingAttribute: "Spawning Power", RawRank: true),                                                 // Ritual Lord : −10…50…60 %
        new(456,  _ => true, Recharge: SpeedEffectKind.Percent, RechargeValue: 33),                              // Serpent's Quickness
        new(449,  s => s.SkillType == "Preparation", Recharge: SpeedEffectKind.Percent, RechargeValue: 50),      // Practiced Stance
        new(930,  IsSpell, Recharge: SpeedEffectKind.AddedSeconds, RechargeIndex: 0,
            ScalingAttribute: "Inspiration Magic"),                                                             // Auspicious Incantation : +10…6…5 s
        new(WeaponOfQuickeningSkillId, s => IsSpell(s) || IsBindingRitual(s),
            Recharge: SpeedEffectKind.Percent, RechargeValue: 33, Received: true),                              // Weapon of Quickening (reçue)

        // ── Vitesse d'attaque (IAS, ajout au lot 3 le 16/09/2026) : raccourcit l'incantation des attaques ────
        // « Vous attaquez X % plus vite » → l'attaque s'active en base ÷ (1 + X %). Les 37 compétences taguées « ias:self »
        // de la base ; les IAS de familier, d'esprit ou d'ennemi restent dehors. Valeurs lues dans la description réelle.
        new(333,  NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // "I Will Avenge You!"
        new(1774, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Aggressive Refrain
        new(370,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Berserker Stance
        new(1209, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Bestial Fury
        new(1413, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Burst of Aggression
        new(2101, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Critical Agility
        new(2218, NatureRitualData.IsAttack, AttackSpeedIndex: 2, ScalingAttribute: "Deldrimor rank"),          // Drunken Master : 25…33 % (ivre)
        new(375,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Dwarven Battle Stance
        new(1724, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Expert's Dexterity
        new(2959, NatureRitualData.IsAttack, AttackSpeedValue: 15, BaseSkillId: 1724),                          // Expert's Dexterity (PvP)
        new(1404, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Flail
        new(3464, NatureRitualData.IsAttack, AttackSpeedValue: 33, BaseSkillId: 1404),                          // Flail (PvP)
        new(344,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Flurry
        new(346,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Frenzy
        new(3443, NatureRitualData.IsAttack, AttackSpeedValue: 33, BaseSkillId: 346),                           // Frenzy (PvP)
        new(1762, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Heart of Fury
        new(3366, NatureRitualData.IsAttack, AttackSpeedValue: 25, BaseSkillId: 1762),                          // Heart of Fury (PvP)
        new(1728, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Heket's Rampage
        new(453,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Lightning Reflexes
        new(3141, NatureRitualData.IsAttack, AttackSpeedValue: 33, BaseSkillId: 453),                           // Lightning Reflexes (PvP)
        new(2108, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Never Rampage Alone
        new(1754, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Onslaught
        new(3365, NatureRitualData.IsAttack, AttackSpeedValue: 25, BaseSkillId: 1754),                          // Onslaught (PvP)
        new(2146, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Pious Fury
        new(3368, NatureRitualData.IsAttack, AttackSpeedValue: 25, BaseSkillId: 2146),                          // Pious Fury (PvP)
        new(831,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Primal Rage
        new(3458, NatureRitualData.IsAttack, AttackSpeedValue: 33, BaseSkillId: 831),                           // Primal Rage (PvP)
        new(1721, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Rampage as One
        new(2068, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Rapid Fire (à l'arc)
        new(3426, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Seven Weapons Stance
        new(1773, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Soldier's Fury (sous un cri ou un chant)
        new(1698, NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Soldier's Stance (sous un cri ou un chant)
        new(3156, NatureRitualData.IsAttack, AttackSpeedValue: 33, BaseSkillId: 1698),                          // Soldier's Stance (PvP)
        new(995,  NatureRitualData.IsAttack, AttackSpeedValue: 33),                                             // Tiger Stance
        new(454,  NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Tiger's Fury
        new(1649, NatureRitualData.IsAttack, AttackSpeedIndex: 0, ScalingAttribute: "Critical Strikes"),        // Way of the Assassin : 5…17…20 % (dagues)
        new(2073, NatureRitualData.IsAttack, AttackSpeedValue: 25),                                             // Weapon of Aggression (sur soi)
    };

    private static readonly Dictionary<int, SkillSpeedBoostDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);
    private static readonly HashSet<int> _toggleIds = All.Select(d => d.ToggleId).ToHashSet();

    public static SkillSpeedBoostDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    /// <summary>Effets actifs qui touchent <paramref name="target"/>, cumulés : chaque actif = le descripteur, la compétence
    /// équipée qui le porte (pour sa progression) et le rang de sa caractéristique d'échelle.</summary>
    public static SkillSpeed SpeedFor(
        Skill target, IEnumerable<(SkillSpeedBoostDescriptor Descriptor, Skill Source, int Rank)> active)
    {
        decimal rechargeKept = 1m, castKept = 1m, attackKept = 1m;
        int rechargeStrongest = 0, castStrongest = 0, added = 0, flat = 0, block = 0;
        bool rechargeInstant = false, castInstant = false, quarter = false;
        // Aucun effet d'incantation ne touche une attaque (wiki *Activation time* ; confirmé en jeu par Philippe pour les
        // attaques à la dague sous Deadly Paradox). Seule la vitesse d'attaque le fait. Leur recharge, elle, est réduite.
        bool attack = NatureRitualData.IsAttack(target);

        foreach (var (d, source, rank) in active.DistinctBy(a => a.Descriptor.ToggleId))
        {
            // Blocage et vitesse d'attaque : indépendants de la cible du descripteur (Deadly Paradox bloque TOUTES les
            // attaques, pas seulement les compétences d'Assassin qu'il accélère).
            if (attack && d.BlockSeconds > 0) block = Math.Max(block, d.BlockSeconds);
            if (attack && d.HasAttackSpeed)
                attackKept *= (100 - Math.Clamp(ValueOf(d.AttackSpeedIndex, d.AttackSpeedValue, source, rank), 0, 100)) / 100m;

            if (!d.Applies(target)) continue;
            // Une posture ne touche jamais une posture : la nouvelle la remplace (wiki *Serpent's Quickness* : « it does not
            // affect itself or any stances that replace it » — règle confirmée en jeu par Philippe le 16/09/2026).
            if (source.SkillType == "Stance" && target.SkillType == "Stance") continue;

            int rv = ValueOf(d.RechargeIndex, d.RechargeValue, source, rank);
            switch (d.Recharge)
            {
                case SpeedEffectKind.Percent:
                    rv = Math.Clamp(rv, 0, 100);
                    rechargeKept *= (100 - rv) / 100m;
                    rechargeStrongest = Math.Max(rechargeStrongest, rv);
                    break;
                case SpeedEffectKind.Instant: rechargeInstant = true; break;
                case SpeedEffectKind.AddedSeconds: added += rv; break;
            }

            if (attack) continue;
            int cv = ValueOf(d.CastIndex, d.CastValue, source, rank);
            switch (d.Cast)
            {
                case SpeedEffectKind.Percent:
                    cv = Math.Clamp(cv, 0, 100);
                    castKept *= (100 - cv) / 100m;
                    castStrongest = Math.Max(castStrongest, cv);
                    break;
                case SpeedEffectKind.Instant: castInstant = true; break;
                case SpeedEffectKind.QuarterSecond: quarter = true; break;
                case SpeedEffectKind.FlatSeconds: flat += cv; break;
            }
        }

        return new(1m - rechargeKept, rechargeStrongest, rechargeInstant, added,
                   1m - castKept, castStrongest, castInstant, quarter, flat,
                   block, 1m - Math.Max(attackKept, 1m - MaxAttackSpeedCut));
    }

    private static int ValueOf(int index, int fixedValue, Skill source, int rank) =>
        index < 0
            ? fixedValue
            : SkillProgression.IntAt(source.Progression is { } p && index < p.Length ? p[index] : null, rank) ?? 0;
}
