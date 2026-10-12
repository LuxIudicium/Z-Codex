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
    /// <para><see cref="HealLo"/> / <see cref="HealHi"/> = soin que le défenseur tire de l'attaque côté build (conversions
    /// en soin, soins déclenchés par un coup — calculateur d'armure, lot 5), à côté des dégâts : « 0 (soin 49) ».</para>
    public sealed record Result(
        bool IgnoresArmor, bool IsRange,
        int Lo60, int Hi60, int LoCalc, int HiCalc,
        int Penetration, string? PrimaryTypeFr, string Notes, int HealLo = 0, int HealHi = 0)
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
        // Coup d'ARME sacré (Clairvoyance du juge, Judgment Strike — lot 4) : il respecte l'armure, mais aucun bonus
        // typé ne joue contre lui (wiki *Holy_damage*) → armure de base, comme ténèbres / chaos.
        "holy" => "dark",
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
    /// <paramref name="mitigation"/> : réductions de dégâts reçus cochées (chantier « réductions », lot 1),
    /// appliquées côté build aux seuls paquets qui respectent l'armure ; null = comportement d'avant. Elle porte aussi
    /// les conversions de type (lot 4) : le type REÇU de chaque paquet choisit sa colonne d'armure, ses réductions par
    /// type, la réduction physique de l'équipement et la vulnérabilité sacrée.
    /// <paramref name="weaponType"/> : type choisi sur la ligne pour le coup d'arme (mod élémentaire ou skin, lot 4) ;
    /// null ou vide = type natif de l'arme.
    /// </summary>
    public static Result Compute(Skill skill, int rank, Func<string, int> alForType, int flatPhysical,
                                 int holyIncrease = 0, DamageMitigation.Context? mitigation = null,
                                 string? weaponType = null)
    {
        string resolved = Resolve(skill, rank);
        var analysis = SkillDamage.Analyze(resolved, skill.Name);
        int pen = analysis.ArmorPenetration;

        // Conversions de type (lot 4). La Clairvoyance du juge ajoute +20 % de pénétration BONUS aux attaques, des deux
        // côtés (c'est une propriété de l'attaque, comme sa pénétration propre) ; son sacré passe par le type reçu.
        var conv = DamageMitigation.ConversionsOf(mitigation);
        pen += DamageMitigation.JudgesBonusPen(conv, WeaponStrike.IsWeaponAttack(skill));
        var convertedTo = new List<string>();   // types reçus après une conversion, pour la note
        string? Received(string? type)
        {
            string? t = DamageMitigation.ReceivedType(conv, type);
            if (t is not null && t != type && !convertedTo.Contains(t)) convertedTo.Add(t);
            return t;
        }
        // Vulnérabilité sacrée (Tormentor's) d'un paquet selon son type REÇU — un sacré passé en terre n'y est plus.
        bool holyAffected = false;
        int HolyOn(string? receivedType)
        {
            if (holyIncrease <= 0 || receivedType != "holy") return 0;
            holyAffected = true;
            return holyIncrease;
        }

        // Réductions de dégâts reçus : facteur par paquet (type, sort, attaque), passé au multiplicateur de
        // DamageAt — un seul arrondi —, puis les réductions FIXES (compétences + équipement) retirées du paquet
        // entier, plancher 0 (ordre armure → % → fixe confirmé en jeu, cf. DamageMitigation). Ce qui a été
        // appliqué va en note.
        bool isSpell = DamageMitigation.IsSpell(skill);
        bool isAttack = DamageMitigation.IsAttack(skill);
        var applied = new AppliedMitigation();
        int healLo = 0, healHi = 0;
        // Un paquet qui respecte l'armure, tout le pipeline (lot 5) — voir Packet.
        DamageMitigation.Outcome Hit(Func<double, double, double, int> at, double baseMult, string type, bool spell,
                                     bool attack, int extra, int flats, bool record = true)
            => Packet(mitigation, applied, at, baseMult, type, spell, attack, extra, flats, record);
        // Points retirés d'un paquet : réductions fixes des compétences + équipement (physique seulement).
        int FlatFor(string type, bool spell, bool attack)
        {
            int skillFlat = DamageMitigation.Flat(mitigation, type, spell, attack);
            applied.AddFlat(skillFlat);
            return skillFlat + (IsPhysical(type) ? flatPhysical : 0);
        }
        var critRule = DamageMitigation.CriticalRule(mitigation);

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

        // ── Coup d'arme (armor-respecting, customisée ×1.2) : type REÇU = type de l'arme ou choisi sur la ligne, type
        // propre de la compétence, puis les conversions (lot 4) ──
        if (weaponTable && weapon is not null)
        {
            var hit = DamageMitigation.WeaponHitType(conv, skill, weapon, weaponType);
            string type = hit.Received;
            if (hit.Converted && !convertedTo.Contains(type)) convertedTo.Add(type);
            int alCalc = alForType(ColumnKey(type));
            // Un coup d'arme n'est jamais un sort, même porté par une attaque élémentaire ; c'est toujours une attaque.
            int flats = FlatFor(type, spell: false, attack: true);
            // Bonus « +X » (ignore l'armure, aucun %) et vulnérabilité sacrée : des points AJOUTÉS au paquet. Ils ne
            // reçoivent pas les %, mais la réduction fixe porte sur le TOUT (arme + bonus), plancher 0 à la fin — réponse
            // de Philippe du 10/10/2026 —, et le plafond aussi (lot 5).
            int extra = bonus + HolyOn(type);
            int AtMin(double m, double c, double a) => WeaponStrike.DamageAt(weapon.Min, rank, alCalc, pen, m, cap: c, afterCap: a);
            int AtMax(double m, double c, double a) => WeaponStrike.DamageAt(weapon.Max, rank, alCalc, pen, m, cap: c, afterCap: a);
            // Critique côté build (lot 3) : ordinaire (max ×√2), sans surplus (Balanced Stance : max de l'arme), ou
            // plus de critique du tout (Stoneflesh Aura, Stone Sheath). Le côté @AL 60 reste la référence nue.
            Func<double, double, double, int> atCrit = critRule == DamageMitigation.CritRule.NoExtra
                ? AtMax
                : (m, c, a) => WeaponStrike.CriticalAt(weapon, rank, alCalc, pen, m, cap: c, afterCap: a);

            int lo60w, hi60w;
            DamageMitigation.Outcome loC, hiC;
            if (mods.AlwaysCritical)
            {
                // Critique forcé (Keen Chop) : la valeur subie EST le critique — comme la tooltip
                // (ligne critique seule) et le Spike (min = max = CriticalAt). Immunisé : un coup ordinaire.
                lo60w = hi60w = WeaponStrike.CriticalAt(weapon, rank, 60, pen, mods.Multiplier);
                if (critRule == DamageMitigation.CritRule.Immune)
                {
                    loC = Hit(AtMin, mods.Multiplier, type, false, true, extra, flats);
                    hiC = Hit(AtMax, mods.Multiplier, type, false, true, extra, flats);
                }
                else loC = hiC = Hit(atCrit, mods.Multiplier, type, false, true, extra, flats);
            }
            else
            {
                lo60w = WeaponStrike.DamageAt(weapon.Min, rank, 60, pen, mods.Multiplier);
                hi60w = WeaponStrike.DamageAt(weapon.Max, rank, 60, pen, mods.Multiplier);
                loC = Hit(AtMin, mods.Multiplier, type, false, true, extra, flats);
                hiC = Hit(AtMax, mods.Multiplier, type, false, true, extra, flats);
            }

            lo60 += lo60w + bonus; hi60 += hi60w + bonus;
            loCalc += loC.Taken; hiCalc += hiC.Taken;
            healLo += loC.Heal; healHi += hiC.Heal;

            primaryTypeFr = SkillDamage.DisplayType(type); primaryWeight = weapon.Max;

            if (mods.AlwaysCritical)
                notes.Add(critRule switch
                {
                    DamageMitigation.CritRule.Immune  => L("critique forcé annulé (immunité)", "forced critical cancelled (immunity)"),
                    DamageMitigation.CritRule.NoExtra => L("critique forcé, sans surplus", "forced critical, no extra damage"),
                    _ => L("critique forcé", "forced critical"),
                });
            else if (critRule == DamageMitigation.CritRule.Immune)
                notes.Add(L("immunisé aux critiques", "immune to critical hits"));
            else
                notes.Add($"{L("critique", "critical")} {Hit(atCrit, mods.Multiplier, type, false, true, extra, flats, record: false).Taken}"
                          + (critRule == DamageMitigation.CritRule.NoExtra ? L(" (sans surplus)", " (no extra damage)") : ""));
        }

        // ── Paquets armor-respecting (sorts typés) ──
        foreach (var r in respecting.Where(r => !r.Conditional && !r.IsThreshold))
        {
            string type = Received(r.DamageType ?? "physical")!;
            int alCalc = alForType(ColumnKey(type));

            int d60 = SkillDamage.DamageAt(r.Value, 60, pen);
            var dC = Hit((m, c, a) => SkillDamage.DamageAt(r.Value, alCalc, pen, multiplier: m, cap: c, afterCap: a),
                         1.0, type, isSpell, isAttack, HolyOn(type), FlatFor(type, isSpell, isAttack));

            lo60 += d60; hi60 += d60; loCalc += dC.Taken; hiCalc += dC.Taken; healLo += dC.Heal; healHi += dC.Heal;
            if (r.Value > primaryWeight) { primaryWeight = r.Value; primaryTypeFr = SkillDamage.DisplayType(type); }
        }

        // ── Paquets armor-ignoring (sacré/ombre/sans type ; bonus déjà retiré pour les armes) ──
        // Vulnérabilité sacrée (Tormentor's) : +N sur les paquets SACRÉS, côté build seulement. Sous le Briseur de pierre
        // du défenseur, un sacré reçu devient terre (lot 4) : il ignore toujours l'armure, mais n'est plus sacré.
        // Ni % ni fixes (décision 6.2), mais le plafond et les soins, oui (Q23, lot 5).
        bool ignoringChanged = false;
        void Ignoring(int value, string? receivedType)
        {
            int full = value + HolyOn(receivedType);
            var o = Unarmored(mitigation, applied, full, receivedType, isSpell, isAttack);
            if (o.Taken != full) ignoringChanged = true;
            lo60 += value; hi60 += value; loCalc += o.Taken; hiCalc += o.Taken; healLo += o.Heal; healHi += o.Heal;
        }
        foreach (var r in ignoring.Where(r => !r.Conditional && !r.IsThreshold))
            Ignoring(r.Value, Received(r.DamageType));

        // ── Paquets à seuil : plafond compté (max annoncé, ou unité × 8 sans plafond annoncé) ──
        foreach (var r in thresholds)
        {
            int cap = r.MaxDamage > 0 ? r.MaxDamage
                                      : r.Value * SpikeThresholdSkills.FallbackCap;
            if (!r.IgnoresArmor)
            {
                // Seuil armor-respecting (Doom : foudre/rituel en recharge) : le plafond porte sur
                // les dégâts LISTÉS, l'armure/pénétration s'appliquent au total (comme le Spike).
                string type = Received(r.DamageType ?? "physical")!;
                int alCalc = alForType(ColumnKey(type));
                int d60 = SkillDamage.DamageAt(cap, 60, pen);
                var dC = Hit((m, c, a) => SkillDamage.DamageAt(cap, alCalc, pen, multiplier: m, cap: c, afterCap: a),
                             1.0, type, isSpell, isAttack, HolyOn(type), FlatFor(type, isSpell, isAttack));
                lo60 += d60; hi60 += d60; loCalc += dC.Taken; hiCalc += dC.Taken; healLo += dC.Heal; healHi += dC.Heal;
                if (cap > primaryWeight) { primaryWeight = cap; primaryTypeFr = SkillDamage.DisplayType(type); }
            }
            else Ignoring(cap, Received(r.DamageType));
            notes.Add(L($"seuil {(r.IsBonus ? "+" : "")}{r.Value}/unité — plafond {cap} compté",
                        $"threshold {(r.IsBonus ? "+" : "")}{r.Value}/unit — cap {cap} counted"));
        }

        // ── Vol de vie / perte de vie sèche : total des deux côtés (ignore l'armure), avec note ──
        // Q14 (10/10/2026) : le VOL de vie reçoit les seules réductions fixes qui le citent (Shielding Hands, Union).
        int stealFlat = DamageMitigation.FlatLifeSteal(mitigation);
        bool stealReduced = false;
        foreach (var r in steals)
        {
            int taken = r.Value, heal = 0;
            if (r.Kind == SkillDamage.RowKind.LifeSteal && stealFlat > 0)
            {
                taken = Math.Max(0, r.Value - stealFlat);
                applied.AddFlat(stealFlat);
                stealReduced = true;
            }
            // Lot 5 : Reversal of Fortune et Life Sheath convertissent aussi le vol de vie, Weapon of Remedy se déclenche
            // dessus ; jamais de plafond (ce ne sont pas des dégâts). La perte de vie sèche n'est touchée par rien.
            if (r.Kind == SkillDamage.RowKind.LifeSteal)
            {
                var o = DamageMitigation.Heal(mitigation, taken, taken, lifeSteal: true);
                if (o.Taken != taken) stealReduced = true;
                (taken, heal) = (o.Taken, o.Heal);
            }
            lo60 += r.Value; hi60 += r.Value; loCalc += taken; hiCalc += taken; healLo += heal; healHi += heal;
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
        // Idem pour un vol de vie réduit (Shielding Hands, Union) et, au lot 5, pour un paquet plafonné ou converti en soin.
        // Un soin seul ne change pas les dégâts : il s'affiche à côté.
        bool ignoresAll = !weaponTable && !freeWeapon && respecting.Count == 0
                          && thresholds.All(r => r.IgnoresArmor)
                          && ignoring.Count + thresholds.Count + steals.Count > 0
                          && !holyAffected && !stealReduced && !ignoringChanged;
        bool isRange = weaponTable && !mods.AlwaysCritical && hi60 != lo60;
        if (convertedTo.Count > 0) notes.Insert(0, ReceivedAsNote(convertedTo));
        if (pen > 0) notes.Insert(0, L($"pénétration {pen} %", $"penetration {pen}%"));
        notes.AddRange(applied.Notes());

        return new Result(ignoresAll, isRange, lo60, hi60, loCalc, hiCalc, pen, primaryTypeFr,
                          string.Join(" · ", notes), healLo, healHi);
    }

    /// <summary>
    /// Un paquet qui respecte l'armure, côté build, dans l'ordre tranché par Philippe (P1, P2, Q21) : armure → hausses →
    /// plafond → % → fixes → soins, un seul arrondi sur la part soumise à l'armure. <paramref name="at"/> = le DamageAt
    /// de ce paquet (multiplicateur, plafond, facteur d'après-plafond) : la formule n'est jamais recopiée ici.
    /// <paramref name="extra"/> = points AJOUTÉS au paquet sans passer par l'armure ni les % (« +X » d'une attaque d'arme,
    /// vulnérabilité sacrée) ; ils comptent dans le plafond (paquet entier : la part d'arme est plafonnée à
    /// plafond − extra), dans les fixes et dans les soins. <paramref name="flats"/> = réductions fixes (compétences et
    /// équipement). Sans plafond, le calcul est exactement celui des lots 1 à 4.
    /// </summary>
    private static DamageMitigation.Outcome Packet(DamageMitigation.Context? mitigation, AppliedMitigation applied,
        Func<double, double, double, int> at, double baseMult, string type, bool spell, bool attack, int extra, int flats,
        bool record = true)
    {
        const double None = double.PositiveInfinity;
        double factor = DamageMitigation.Factor(mitigation, type, spell, attack);
        if (record) applied.AddFactor(factor);
        double? cap = DamageMitigation.Cap(mitigation, type, spell, attack);
        bool heals = DamageMitigation.HasHeals(mitigation);
        // Le paquet après l'armure et les hausses, AVANT plafond et réductions : seuil de Spirit Bond, et test du plafond.
        int before = cap is null && !heals ? 0
            : at(baseMult * DamageMitigation.IncreaseFactor(mitigation, type, spell, attack), None, 1.0) + extra;

        int taken;
        if (cap is not { } c) taken = Math.Max(0, at(baseMult * factor, None, 1.0) + extra - flats);
        else
        {
            double weaponCap = Math.Max(0, c - extra);
            int part = at(baseMult * DamageMitigation.IncreaseFactor(mitigation, type, spell, attack), weaponCap,
                          DamageMitigation.PercentFactor(mitigation, type, spell, attack));
            taken = Math.Max(0, part + (int)Math.Floor(Math.Min(extra, c)) - flats);
            if (record && before > c) applied.AddCap(c);
        }
        return heals ? DamageMitigation.Heal(mitigation, taken, before) : new(taken, 0);
    }

    /// <summary>Un paquet qui IGNORE l'armure (sacré, ombre, sans type) : ni % ni fixes (décision 6.2), mais le plafond
    /// et les soins (Q23, Philippe, 11/10/2026). <paramref name="value"/> = le paquet, vulnérabilité sacrée comprise.</summary>
    private static DamageMitigation.Outcome Unarmored(DamageMitigation.Context? mitigation, AppliedMitigation applied,
        int value, string? type, bool spell, bool attack)
    {
        int taken = value;
        if (DamageMitigation.Cap(mitigation, type, spell, attack) is { } c && value > c)
        {
            taken = (int)Math.Floor(c);
            applied.AddCap(c);
        }
        return DamageMitigation.HasHeals(mitigation) ? DamageMitigation.Heal(mitigation, taken, value) : new(taken, 0);
    }

    /// <summary>Note « reçu en terre » d'une ligne dont une conversion a changé le type (lot 4).</summary>
    private static string ReceivedAsNote(IEnumerable<string> types)
    {
        string list = string.Join(" / ", types.Select(SkillDamage.DisplayType));
        return L($"reçu en {list}", $"taken as {list}");
    }

    /// <summary>Ce que les réductions de dégâts reçus ont effectivement changé sur une ligne, pour la note :
    /// « plafond 48 », « réduction −45 % », « augmentation +75 % », « réduction fixe −25 ».</summary>
    private sealed class AppliedMitigation
    {
        private readonly SortedSet<int> _percents = [];   // signé : négatif = réduction, positif = hausse
        private readonly SortedSet<int> _flats = [];
        private readonly SortedSet<int> _caps = [];       // plafonds qui ont mordu (points)

        public void AddCap(double cap) => _caps.Add((int)Math.Floor(cap));

        public void AddFactor(double factor)
        {
            if (factor != 1.0) _percents.Add((int)Math.Round((factor - 1) * 100, MidpointRounding.AwayFromZero));
        }

        public void AddFlat(int points)
        {
            if (points > 0) _flats.Add(points);
        }

        public IEnumerable<string> Notes()
        {
            var down = _percents.Where(p => p < 0).ToList();
            var up = _percents.Where(p => p > 0).ToList();
            if (_caps.Count > 0)
                yield return L($"plafond {string.Join(" / ", _caps)}", $"cap {string.Join(" / ", _caps)}");
            if (down.Count > 0)
                yield return L($"réduction {string.Join(" / ", down.Select(p => $"−{-p} %"))}",
                               $"reduction {string.Join(" / ", down.Select(p => $"−{-p}%"))}");
            if (up.Count > 0)
                yield return L($"augmentation {string.Join(" / ", up.Select(p => $"+{p} %"))}",
                               $"increase {string.Join(" / ", up.Select(p => $"+{p}%"))}");
            if (_flats.Count > 0)
                yield return L($"réduction fixe {string.Join(" / ", _flats.Select(p => $"−{p}"))}",
                               $"flat reduction {string.Join(" / ", _flats.Select(p => $"−{p}"))}");
        }
    }

    /// <summary>
    /// ATTAQUE LIBRE (chantier « réductions », lot 1) : un paquet de <paramref name="value"/> dégâts de type
    /// <paramref name="type"/> contre AL 60 (attaquant niveau 20), sans compétence derrière — pour tester un
    /// coup quelconque (« 1000 dégâts de terre »). Même formule que les sorts du catalogue
    /// (<see cref="SkillDamage.DamageAt"/>), mêmes réductions que <see cref="Compute"/>.
    /// <paramref name="isSpell"/> = le coup vient d'un sort (Veil of Thorns ne réduit que ceux-là) ; sinon c'est une
    /// attaque (Life Bond ne réduit que celles-là). Lot 4 : le type REÇU (Clairvoyance du juge sur une attaque,
    /// conversions de zone et du défenseur) choisit la colonne et les réductions ; <paramref name="holyIncrease"/> =
    /// vulnérabilité sacrée, qui touche une attaque passée en sacré.
    /// </summary>
    public static Result ComputeFree(int value, string type, bool isSpell, Func<string, int> alForType,
                                     int flatPhysical, DamageMitigation.Context? mitigation = null,
                                     int holyIncrease = 0)
    {
        bool isAttack = !isSpell;
        var conv = DamageMitigation.ConversionsOf(mitigation);
        var hit = DamageMitigation.FreeAttackType(conv, type, isSpell);
        string received = hit.Received;
        int pen = DamageMitigation.JudgesBonusPen(conv, isAttack);
        int alCalc = alForType(ColumnKey(received));
        int skillFlat = DamageMitigation.Flat(mitigation, received, isSpell, isAttack);
        int holy = holyIncrease > 0 && received == "holy" ? holyIncrease : 0;

        var applied = new AppliedMitigation();
        int d60 = SkillDamage.DamageAt(value, 60, pen);
        var dC = Packet(mitigation, applied, (m, c, a) => SkillDamage.DamageAt(value, alCalc, pen, multiplier: m, cap: c, afterCap: a),
                        1.0, received, isSpell, isAttack, holy, skillFlat + (IsPhysical(received) ? flatPhysical : 0));

        var notes = new List<string>();
        if (isSpell) notes.Add(L("sort", "spell"));
        if (pen > 0) notes.Add(L($"pénétration {pen} %", $"penetration {pen}%"));
        if (hit.Converted) notes.Add(ReceivedAsNote([received]));
        applied.AddFlat(skillFlat);
        notes.AddRange(applied.Notes());
        if (holy > 0) notes.Add(L($"vulnérabilité sacrée +{holy}", $"holy vulnerability +{holy}"));
        return new Result(false, false, d60, d60, dC.Taken, dC.Taken, pen, SkillDamage.DisplayType(received),
                          string.Join(" · ", notes), dC.Heal, dC.Heal);
    }
}
