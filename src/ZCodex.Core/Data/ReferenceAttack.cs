using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Chantier 14 (calculateur d'armure, Lot D) — une attaque de référence : dégâts subis calculés
/// à AL 60 (valeur catalogue, cible « nue ») vs à l'AL du build, pour lire d'un coup d'œil ce
/// qu'un build encaisserait. Réutilise <see cref="SkillDamage"/> et <see cref="WeaponStrike"/>
/// À L'IDENTIQUE de la tooltip du chantier 10 (parité stricte d'arrondi/troncature) : la formule
/// de dégâts n'est JAMAIS réimplémentée ici, seule <c>DamageAt</c> est appelée.
/// </summary>
public static class ReferenceAttack
{
    /// <summary>Résultat pour une attaque à une localisation donnée (ou pré-agrégé « espérance »).
    /// Attaque d'arme → intervalle min–max (<see cref="IsRange"/>) ; sort → lo==hi. Les dégâts
    /// @AL 60 sont la référence catalogue SANS réduction plate ; @AL du build INCLUENT la
    /// réduction plate (Absorption/Knight's/…). Une attaque entièrement armor-ignoring
    /// (<see cref="IgnoresArmor"/>) affiche des valeurs égales des deux côtés.</summary>
    public sealed record Result(
        bool IgnoresArmor, bool IsRange,
        int Lo60, int Hi60, int LoCalc, int HiCalc,
        int Penetration, string? PrimaryTypeFr, string Notes)
    {
        /// <summary>Réduction relative des dégâts au build vs AL 60 (borne haute), en % (négatif =
        /// dégâts réduits). 0 si la référence est nulle.</summary>
        public int DeltaPercent => Hi60 <= 0 ? 0 : (int)Math.Round((HiCalc - Hi60) * 100.0 / Hi60);
    }

    /// <summary>Types de dégâts armor-respecting → clé de colonne du calculateur. Génériques
    /// « physical »/« chaos » repliés sur une colonne représentative (rare). Sacré/ombre/sans
    /// type n'arrivent pas ici (classés armor-ignoring par <see cref="SkillDamage"/>).</summary>
    public static string ColumnKey(string type) => type switch
    {
        "slashing" => "slashing", "piercing" => "piercing", "blunt" => "blunt",
        "physical" => "slashing",                 // physique générique → colonne physique
        "fire" => "fire", "cold" => "cold", "earth" => "earth", "lightning" => "lightning",
        "dark" or "chaos" => "dark",
        _ => "dark",
    };

    private static bool IsPhysical(string type)
        => type is "slashing" or "piercing" or "blunt" or "physical";

    // Note d'attaque dans la langue affichee (les notes sont construites cote Core).
    private static string L(string fr, string en) => AppLanguage.IsFr ? fr : en;

    /// <summary>Attaque à PROJECTILE (arc, lance, jet) — cible de « Shields Up! » et des autres
    /// sources d'armure Scope.Projectile. « Spear Melee Attack » est un coup de mêlée, exclu ;
    /// les sorts-projectiles (Fireball, Lightning Orb…) ne sont PAS des attaques.</summary>
    public static bool IsProjectile(Skill skill) => skill.SkillType
        is "Bow Attack" or "Half Range Bow Attack" or "Spear Attack" or "Ranged Attack";

    /// <summary>Une attaque d'arme est-elle mappée à une table d'arme (donc dégâts d'arme) ?</summary>
    public static bool HasWeaponTable(Skill skill, string resolvedDescription)
        => WeaponStrike.For(skill) is not null
           && !WeaponStrike.ModsFor(skill.Name, resolvedDescription).NoWeaponDamage;

    /// <summary>Vrai si la skill produit au moins un paquet calculable au rang donné — filtre de
    /// la recherche d'ajout. Rework 17/07 : inclut désormais le vol de vie et la perte de vie
    /// sèche (Vampiric Gaze…) — toute skill offensive chiffrable est proposable.</summary>
    public static bool DealsDamage(Skill skill, int rank)
    {
        var resolved = Resolve(skill, rank);
        if (HasWeaponTable(skill, resolved)) return true;
        return SkillDamage.Analyze(resolved, skill.Name).Rows.Count > 0;
    }

    private static string Resolve(Skill skill, int rank)
    {
        var body = SkillText.ConciseBody(skill.Description, skill.SkillType);
        return SkillProgression.Resolve(body, skill.Progression, rank);
    }

    /// <summary>Description RÉSOLUE au rang (corps concis + plages remplacées, valeurs entre
    /// marqueurs) — partagée avec la tooltip « calculée » des attaques de référence et le parseur
    /// de durées de conditions (<see cref="ConditionInfliction"/>).</summary>
    public static string ResolveDescription(Skill skill, int rank) => Resolve(skill, rank);

    // Un paquet dont le segment de phrase ([.;:]) contient « if » ET « blocked » est conditionné
    // au blocage. « cannot be blocked » (attaques imblocables) ne matche pas : pas de « if ».
    private static readonly char[] SegmentBounds = ['.', ';', ':'];
    private static readonly Regex BlockedSegmentRegex = new(
        @"\bif\b.*\bblocked\b|\bblocked\b.*\bif\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool InBlockedSegment(string resolved, int index)
    {
        if (index < 0 || index >= resolved.Length) return false;
        int start = resolved.LastIndexOfAny(SegmentBounds, index) + 1;
        int end = resolved.IndexOfAny(SegmentBounds, index);
        return BlockedSegmentRegex.IsMatch(resolved[start..(end < 0 ? resolved.Length : end)]);
    }

    /// <summary>
    /// Calcule les dégâts subis d'une attaque à une localisation.
    /// <paramref name="alForType"/> : AL du build pour une clé de colonne (voir <see cref="ColumnKey"/>)
    /// à la localisation choisie. <paramref name="flatPhysical"/> : réduction plate (points) à
    /// retrancher aux dégâts PHYSIQUES armor-respecting du build (Absorption + Knight's + inscriptions
    /// actives + Luck en espérance, déjà agrégée, ≥ 0), plancher 0 ; jamais appliquée à AL 60 ni
    /// aux paquets armor-ignoring. <paramref name="holyIncrease"/> : vulnérabilité sacrée GLOBALE
    /// (Tormentor's, cumul des pièces, ≥ 0) AJOUTÉE aux paquets de type sacré côté build seulement.
    /// </summary>
    public static Result Compute(Skill skill, int rank, Func<string, int> alForType, int flatPhysical,
                                 int holyIncrease = 0)
    {
        string resolved = Resolve(skill, rank);
        var analysis = SkillDamage.Analyze(resolved, skill.Name);
        int pen = analysis.ArmorPenetration;

        // Paquets « si bloqué » (Swift Chop, Irresistible Blow…) : le total affiché est le cas
        // SANS blocage (décision Philippe 16/07) — ces paquets sortent du total et passent en note.
        var damage = new List<SkillDamage.Row>();
        var blocked = new List<SkillDamage.Row>();
        // Vol de vie / perte de vie sèche (rework 17/07) : ignorent l'armure par nature, comptés
        // dans le total des deux côtés (la vie perdue est la même mesure), avec note.
        var steals = new List<SkillDamage.Row>();
        foreach (var r in analysis.Rows)
        {
            if (r.Kind != SkillDamage.RowKind.Damage) { steals.Add(r); continue; }
            (InBlockedSegment(resolved, r.Index) ? blocked : damage).Add(r);
        }
        // Paquets à SEUIL (« X for each … », liste blanche SpikeThresholdSkills) : comptés à leur
        // PLAFOND (décision Philippe 16/07) — même « défaut optimiste » que le Spike. Traités à
        // part : jamais absorbés dans le total d'arme, note « seuil » systématique.
        var thresholds = damage.Where(r => r.IsThreshold).ToList();
        var respecting = damage.Where(r => !r.IgnoresArmor && !r.IsThreshold).ToList();
        var ignoring   = damage.Where(r => r.IgnoresArmor && !r.IsThreshold).ToList();

        var weapon = WeaponStrike.For(skill);
        var mods = WeaponStrike.ModsFor(skill.Name, resolved);
        int bonus = ignoring.FirstOrDefault(r => r.IsBonus)?.Value ?? 0;
        bool weaponTable = weapon is not null && !mods.NoWeaponDamage;
        // Sur une attaque d'arme le 1er bonus « +X » est absorbé dans le total d'arme (comme la
        // tooltip) : on le retire des lignes armor-ignoring pour ne pas le compter deux fois.
        if (WeaponStrike.IsWeaponAttack(skill)) ignoring.RemoveAll(r => r.IsBonus);

        int lo60 = 0, hi60 = 0, loCalc = 0, hiCalc = 0;
        string? primaryTypeFr = null; int primaryWeight = -1;
        var notes = new List<string>();

        // ── Coup d'arme (armor-respecting, type natif de l'arme, customisée ×1.2) ──
        if (weaponTable)
        {
            string type = weapon!.DamageType;
            int alCalc = alForType(ColumnKey(type));
            bool phys = IsPhysical(type);

            int lo60w, hi60w, loCw, hiCw;
            if (mods.AlwaysCritical)
            {
                // Critique forcé (Keen Chop) : la valeur subie EST le critique — comme la tooltip
                // (ligne critique seule) et le Spike (min = max = CriticalAt).
                lo60w = hi60w = WeaponStrike.CriticalAt(weapon, rank, 60, pen, mods.Multiplier);
                loCw  = hiCw  = WeaponStrike.CriticalAt(weapon, rank, alCalc, pen, mods.Multiplier);
            }
            else
            {
                lo60w = WeaponStrike.DamageAt(weapon.Min, rank, 60, pen, mods.Multiplier);
                hi60w = WeaponStrike.DamageAt(weapon.Max, rank, 60, pen, mods.Multiplier);
                loCw  = WeaponStrike.DamageAt(weapon.Min, rank, alCalc, pen, mods.Multiplier);
                hiCw  = WeaponStrike.DamageAt(weapon.Max, rank, alCalc, pen, mods.Multiplier);
            }
            if (phys) { loCw = Math.Max(0, loCw - flatPhysical); hiCw = Math.Max(0, hiCw - flatPhysical); }

            // Bonus « +X » (ignore l'armure) ajouté APRÈS la réduction plate, des deux côtés.
            lo60 += lo60w + bonus; hi60 += hi60w + bonus;
            loCalc += loCw + bonus; hiCalc += hiCw + bonus;

            primaryTypeFr = SkillDamage.DisplayType(type); primaryWeight = weapon.Max;

            if (mods.AlwaysCritical)
            {
                notes.Add(L("critique forcé", "forced critical"));
            }
            else
            {
                // Aligné sur le coup normal : réduction plate soustraite AVANT le bonus (physique).
                int crit = WeaponStrike.CriticalAt(weapon, rank, alCalc, pen, mods.Multiplier);
                if (phys) crit = Math.Max(0, crit - flatPhysical);
                notes.Add($"{L("critique", "critical")} {crit + bonus}");
            }
        }

        // ── Paquets armor-respecting (sorts typés) ──
        foreach (var r in respecting.Where(r => !r.Conditional && !r.IsThreshold))
        {
            string type = r.DamageType ?? "physical";
            int alCalc = alForType(ColumnKey(type));
            bool phys = IsPhysical(type);

            int d60 = SkillDamage.DamageAt(r.Value, 60, pen);
            int dC  = SkillDamage.DamageAt(r.Value, alCalc, pen);
            if (phys) dC = Math.Max(0, dC - flatPhysical);

            lo60 += d60; hi60 += d60; loCalc += dC; hiCalc += dC;
            if (r.Value > primaryWeight) { primaryWeight = r.Value; primaryTypeFr = SkillDamage.DisplayType(type); }
        }

        // ── Paquets armor-ignoring (sacré/ombre/sans type ; bonus déjà retiré pour les armes) ──
        // Vulnérabilité sacrée (Tormentor's) : +N sur les paquets SACRÉS, côté build seulement.
        bool holyAffected = false;
        foreach (var r in ignoring.Where(r => !r.Conditional && !r.IsThreshold))
        {
            int holy = holyIncrease > 0 && r.DamageType == "holy" ? holyIncrease : 0;
            if (holy > 0) holyAffected = true;
            lo60 += r.Value; hi60 += r.Value; loCalc += r.Value + holy; hiCalc += r.Value + holy;
        }

        // ── Paquets à seuil : plafond compté (max annoncé, ou unité × 8 sans plafond annoncé) ──
        foreach (var r in thresholds)
        {
            int cap = r.MaxDamage > 0 ? r.MaxDamage
                                      : r.Value * SpikeThresholdSkills.FallbackCap;
            if (!r.IgnoresArmor)
            {
                // Seuil armor-respecting (Doom : foudre/rituel en recharge) : le plafond porte sur
                // les dégâts LISTÉS, l'armure/pénétration s'appliquent au total (comme le Spike).
                string type = r.DamageType ?? "physical";
                int alCalc = alForType(ColumnKey(type));
                int d60 = SkillDamage.DamageAt(cap, 60, pen);
                int dC  = SkillDamage.DamageAt(cap, alCalc, pen);
                if (IsPhysical(type)) dC = Math.Max(0, dC - flatPhysical);
                lo60 += d60; hi60 += d60; loCalc += dC; hiCalc += dC;
                if (cap > primaryWeight) { primaryWeight = cap; primaryTypeFr = SkillDamage.DisplayType(type); }
            }
            else
            {
                int holy = holyIncrease > 0 && r.DamageType == "holy" ? holyIncrease : 0;
                if (holy > 0) holyAffected = true;
                lo60 += cap; hi60 += cap; loCalc += cap + holy; hiCalc += cap + holy;
            }
            notes.Add(L($"seuil {(r.IsBonus ? "+" : "")}{r.Value}/unité — plafond {cap} compté",
                        $"threshold {(r.IsBonus ? "+" : "")}{r.Value}/unit — cap {cap} counted"));
        }

        // ── Vol de vie / perte de vie sèche : total des deux côtés (ignore l'armure), avec note ──
        foreach (var r in steals)
        {
            lo60 += r.Value; hi60 += r.Value; loCalc += r.Value; hiCalc += r.Value;
            notes.Add($"{(r.Kind == SkillDamage.RowKind.LifeSteal ? L("vol de vie", "life steal") : L("perte de vie", "Health loss"))} {r.Value}");
        }
        if (holyAffected) notes.Add(L($"vulnérabilité sacrée +{holyIncrease}", $"holy vulnerability +{holyIncrease}"));

        // ── Paquets « si bloqué » → note seulement, hors total (le total = cas sans blocage) ──
        foreach (var r in blocked)
            notes.Add($"{(r.IsBonus ? "+" : "")}{r.Value}"
                      + $"{(r.DamageType is { } t ? " " + SkillDamage.DisplayType(t) : "")} {L("si bloqué", "if blocked")}");

        // « Melee Attack » à attribut libre : arme non déductible → coup d'arme non compté, signalé
        // (Symbolic Strike, Griffon's/Leviathan's Sweep… — la tooltip n'affiche rien non plus).
        bool freeWeapon = WeaponStrike.IsWeaponAttack(skill) && weapon is null;
        if (freeWeapon) notes.Add(L("arme indéterminée — coup d'arme non compté",
                                    "undetermined weapon — weapon hit not counted"));

        // La vulnérabilité sacrée différencie les deux côtés → l'attaque n'est plus « égale des
        // deux côtés » et le Δ doit s'afficher.
        bool ignoresAll = !weaponTable && !freeWeapon && respecting.Count == 0
                          && thresholds.All(r => r.IgnoresArmor)
                          && ignoring.Count + thresholds.Count + steals.Count > 0
                          && !holyAffected;
        bool isRange = weaponTable && !mods.AlwaysCritical && hi60 != lo60;
        if (pen > 0) notes.Insert(0, L($"pénétration {pen} %", $"penetration {pen}%"));

        return new Result(ignoresAll, isRange, lo60, hi60, loCalc, hiCalc, pen, primaryTypeFr,
                          string.Join(" · ", notes));
    }
}
