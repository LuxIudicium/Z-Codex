using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Qui provoque l'assommement — l'insigne Poing-de-fer ne rallonge que ce que le PERSO cause lui-même
/// (décision Philippe, Q1 du cadrage 4c) : ses compétences et ses pièges, jamais son familier ni un esprit.</summary>
public enum KnockdownSource { Own, Trap, Pet, Spirit }

/// <summary>Nature d'une ligne d'assommement de l'infobulle. On n'affiche JAMAIS la durée résolue d'un
/// assommement (décision Philippe du 16/09/2026) : seulement l'EFFET.</summary>
public enum KnockdownLineKind
{
    /// <summary>Insigne Poing-de-fer : « Assommement : +1 s ».</summary>
    StonefistBonus,
    /// <summary>Lien terrestre : « Assommement : au moins 3 s ».</summary>
    EarthbindFloor,
    /// <summary>Insigne Poing-de-fer sur NOTRE propre assommement : « Votre assommement : +1 s » (rouge).</summary>
    SelfStonefistBonus,
    /// <summary>Notre propre assommement, qu'aucun effet actif ne rallonge : « Votre assommement : non
    /// rallongé » (rouge). Seule ligne du lot qui s'affiche alors que rien n'agit — voir
    /// <see cref="KnockdownData.Lines"/>.</summary>
    SelfNotExtended,
    /// <summary>Saignement posé par Ronces sur la cible assommée.</summary>
    Bleeding,
    /// <summary>Saignement posé par Ronces sur NOTRE perso, qui s'assomme lui-même (effet à double tranchant).</summary>
    SelfBleeding,
}

/// <summary>Une ligne d'assommement : sa nature et son chiffre (secondes ; 1 pour le bonus de l'insigne,
/// 0 pour « non rallongé », qui n'en annonce aucun).</summary>
public sealed record KnockdownLine(KnockdownLineKind Kind, int Seconds)
{
    /// <summary>Ligne qui parle de NOTRE personnage — rendue en rouge par l'infobulle.</summary>
    public bool IsSelf => Kind is KnockdownLineKind.SelfStonefistBonus or KnockdownLineKind.SelfNotExtended
                                or KnockdownLineKind.SelfBleeding;
}

/// <summary>
/// Ce que les effets actifs font aux assommements de UNE compétence (chantier infobulle, lot 4c).
/// <paramref name="Stonefist"/> = l'armure du perso porte l'insigne Poing-de-fer (détecté tout seul, sans icône,
/// comme le mod « of Enchanting » — décision Q7 du lot 4). <paramref name="Earthbind"/> = Lien terrestre est actif
/// au bandeau d'équipe (« porté seulement »). <paramref name="BleedSeconds"/> = durée du saignement de Ronces au
/// rang de Survie en pleine nature, 0 si l'esprit est éteint. <paramref name="StrengthRank"/> sert à la seule
/// Brise-échine, dont les 4 s exigent Force 8. <paramref name="GreatDwarfWeapon"/> = Arme du Grand Nain reçue et
/// allumée : TOUTES les attaques d'arme du perso peuvent alors assommer (décision Q2).
/// </summary>
public readonly record struct KnockdownEffects(
    bool Stonefist = false,
    bool Earthbind = false,
    int BleedSeconds = 0,
    int StrengthRank = 0,
    bool GreatDwarfWeapon = false)
{
    /// <summary>Au moins un effet à dire — sinon l'infobulle n'a aucune ligne d'assommement à écrire.
    /// L'Arme du Grand Nain seule n'en est pas un : elle élargit le périmètre, elle ne change aucun chiffre.</summary>
    public bool Any => Stonefist || Earthbind || BleedSeconds > 0;
}

/// <summary>
/// Assommement (chantier infobulle, lot 4c). Deux effets le rallongent — l'insigne Poing-de-fer (+1 s, plafond
/// 3 s) et Lien terrestre (plancher 3 s) — et un troisième en tire une condition : Ronces fait saigner tout ce
/// qui est assommé dans sa portée.
///
/// Règles tranchées par Philippe (16 et 17/09/2026) :
///  • Base de <see cref="BaseSeconds"/> secondes (le wiki fait foi), mais **jamais affichée** : l'infobulle
///    montre l'effet (« +1 s », « au moins 3 s »), pas le total. La base ne sert qu'à savoir si l'effet agit.
///  • Ligne visible SEULEMENT quand un effet agit (Q9) : une Brise-échine à 4 s n'en a aucune, son assommement
///    dépassant déjà les deux plafonds.
///  • L'insigne et Lien terrestre donnent TOUJOURS le même résultat (2 s → 3 s des deux côtés ; 3 s et 4 s ne
///    bougent ni pour l'un ni pour l'autre) : les deux actifs ensemble n'écrivent donc qu'UNE ligne, celle de
///    Lien terrestre.
///  • Le saignement de Ronces ne subit AUCUN allongeur du lot 4b (Q5) : il vient de l'esprit, pas de nous — ni le
///    Sceau de l'Archer (« conditions YOU apply ») ni les préfixes d'arme (« with this weapon ») ne le visent.
///
/// Rien n'est recopié de la base : les compétences qui assomment, celles qui s'assomment elles-mêmes et les
/// quatre durées annoncées se LISENT dans les descriptions (même idiome que <see cref="ConditionDurationData"/>).
/// </summary>
public static class KnockdownData
{
    /// <summary>Durée d'un assommement sans mention contraire (wiki, tranché le 16/09/2026). Interne : jamais
    /// affichée.</summary>
    public const int BaseSeconds = 2;

    /// <summary>Insigne Poing-de-fer (armure Guerrier) : « Increases knockdown time on foes by 1 second.
    /// ; (Maximum: 3 seconds) » — lu dans <see cref="GwEquipmentModDetails"/>.</summary>
    public const int StonefistModId = 315;
    public const int StonefistBonusSeconds = 1;
    public const int StonefistMaxSeconds = 3;

    /// <summary>Lien terrestre (esprit d'asservissement) : les ennemis assommés à portée le restent au moins 3 s.
    /// Aucun rang — le plancher est fixe, quelle que soit la Communion du lanceur.</summary>
    public const int EarthbindSkillId = 1252;
    public const int EarthbindPvpSkillId = 3015;
    public const int EarthbindMinSeconds = 3;

    /// <summary>Ronces (rituel de la nature) : « Knocked-down creatures take 5 damage and begin Bleeding
    /// (5…17…20 seconds) ». Les 5 points de dégâts relèvent du lot 6.</summary>
    public const int BramblesSkillId = 947;
    public const string BramblesAttribute = "Wilderness Survival";

    /// <summary>Condition posée par Ronces, sous le nom anglais de <see cref="GwConditionData"/>.</summary>
    public const string BramblesCondition = "Bleeding";

    /// <summary>Arme du Grand Nain : sort d'arme REÇU (« Cannot self-target ») qui donne 28…40 % de chance
    /// d'assommer à toutes les attaques du receveur. Aucun rang à résoudre — seul compte le fait qu'elle assomme
    /// (idiome du chantier : icône allumée = l'effet a proc).</summary>
    public const int GreatDwarfWeaponSkillId = 2219;

    // ── Détection dans les descriptions ───────────────────────────────────────

    // Assommement causé sur un ENNEMI. Le « (?!\s+for\s+at\s+least) » écarte Lien terrestre, qui n'assomme
    // personne : il rallonge les assommements des autres (« they are knocked down FOR AT LEAST 3 seconds »).
    private static readonly Regex CausesRegex = new(
        @"causes?\s+knock[-\s]?down"
        + @"|knocks?[-\s]?down\s+(?:target|all|foes|moving|Charr|and\s+inflicts)"
        + @"|knocks\s+down\s"
        + @"|they\s+are\s+knocked\s+down(?!\s+for\s+at\s+least)"
        + @"|cause\s+knock\s?down",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Le perso s'assomme LUI-MÊME : Grappin (en plus de sa cible), Frappe désespérée et Attaque d'ivresse (elles,
    // n'assomment QUE lui). Le lookbehind écarte « I Meant to Do That! » (« …IF you are knocked-down »), qui
    // réagit à un assommement subi au lieu d'en causer un.
    private static readonly Regex SelfRegex = new(
        @"(?<!if\s)(?<!when\s)(?<!while\s)you\s+are\s+(?:also\s+)?knocked[-\s]?down",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // « cause knockdown for N seconds » (Instabilité psychique et sa variante PvP, Frappe du moissonneur).
    // Les marqueurs de valeur résolue encadrent le nombre dans une description de build.
    private static readonly Regex StatedRegex = new(
        $@"knock\s?down\s+for\s+[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+seconds",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // « Knockdown lasts 4 seconds with Strength 8 or higher » (Brise-échine, la seule). Sous le seuil, la
    // compétence retombe à la base — et l'insigne agit alors.
    private static readonly Regex StatedIfRankRegex = new(
        $@"knock\s?down\s+lasts\s+[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+seconds"
        + $@"\s+with\s+([A-Za-z' ]+?)\s+[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+or\s+higher",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Cette compétence assomme-t-elle un ennemi ?</summary>
    public static bool CausesKnockdown(Skill skill) => CausesRegex.IsMatch(skill.Description);

    /// <summary>Cette compétence assomme-t-elle le perso lui-même ?</summary>
    public static bool KnocksSelfDown(Skill skill) => SelfRegex.IsMatch(skill.Description);

    /// <summary>Qui porte le coup : le perso, son piège, son familier ou un esprit qu'il a posé.</summary>
    public static KnockdownSource SourceOf(Skill skill) =>
        skill.SkillType == "Pet Attack"                                   ? KnockdownSource.Pet
        : skill.SkillType.Contains("Trap", StringComparison.Ordinal)      ? KnockdownSource.Trap
        : skill.SkillType.Contains("Ritual", StringComparison.Ordinal)    ? KnockdownSource.Spirit
        : KnockdownSource.Own;

    /// <summary>L'insigne Poing-de-fer rallonge-t-il un assommement de cette provenance ? Ses pièges comptent
    /// (c'est son piège), son familier et ses esprits non (ce n'est pas lui qui frappe) — décision Q1.</summary>
    public static bool StonefistApplies(KnockdownSource source) =>
        source is KnockdownSource.Own or KnockdownSource.Trap;

    /// <summary>
    /// Durée d'assommement ANNONCÉE par la description résolue, <see cref="BaseSeconds"/> si elle n'en annonce
    /// aucune. Quatre compétences seulement en annoncent une ; l'une d'elles (Brise-échine) la conditionne à un
    /// rang de caractéristique, évalué contre <paramref name="strengthRank"/>. ⚠ Une condition portant sur une
    /// AUTRE caractéristique (aucune aujourd'hui) est tenue pour remplie : mieux vaut taire une ligne que d'en
    /// afficher une fausse.
    /// </summary>
    public static int StatedSeconds(string resolved, int strengthRank)
    {
        if (StatedIfRankRegex.Match(resolved) is { Success: true } conditional)
        {
            int seconds = int.Parse(conditional.Groups[1].Value);
            int needed = int.Parse(conditional.Groups[3].Value);
            bool isStrength = conditional.Groups[2].Value.Trim()
                .Equals("Strength", StringComparison.OrdinalIgnoreCase);
            return !isStrength || strengthRank >= needed ? seconds : BaseSeconds;
        }
        return StatedRegex.Match(resolved) is { Success: true } stated
            ? int.Parse(stated.Groups[1].Value)
            : BaseSeconds;
    }

    /// <summary>Durée après l'insigne Poing-de-fer : +1 s, sans jamais dépasser 3 s — et sans jamais RACCOURCIR
    /// un assommement qui dépasse déjà ce plafond (Brise-échine à 4 s reste à 4 s, décision Q8).</summary>
    public static int WithStonefist(int seconds) =>
        Math.Max(seconds, Math.Min(seconds + StonefistBonusSeconds, StonefistMaxSeconds));

    /// <summary>L'Arme du Grand Nain donne une chance d'assommer aux attaques d'ARME du receveur. Les attaques
    /// de familier sont dehors : c'est le familier qui frappe, pas le porteur du sort.</summary>
    public static bool GreatDwarfWeaponReaches(Skill skill) => WeaponStrike.IsWeaponAttack(skill);

    // ── Lignes d'infobulle ────────────────────────────────────────────────────

    /// <summary>
    /// Lignes d'assommement de <paramref name="skill"/>. Vide quand rien n'agit — la ligne n'est PAS permanente
    /// (décision Q9). L'insigne et Lien terrestre ne produisent jamais deux lignes : quand les deux agissent,
    /// leur résultat est identique et seule la ligne de Lien terrestre est écrite.
    /// </summary>
    public static IReadOnlyList<KnockdownLine> Lines(Skill skill, string resolved, KnockdownEffects effects)
    {
        if (!effects.Any) return [];

        bool self = KnocksSelfDown(skill);
        // ⚠ L'Arme du Grand Nain n'assomme RIEN elle-même : elle donne une chance d'assommer aux ATTAQUES de
        // l'allié qui la reçoit. Sa description dit « cause knock-down », mais les lignes vont sur ces attaques,
        // pas sur le sort d'arme — et son porteur ne peut même pas la recevoir (« Cannot self-target »).
        bool causesFoe = CausesKnockdown(skill) && skill.Id != GreatDwarfWeaponSkillId;
        bool foe = causesFoe || (effects.GreatDwarfWeapon && GreatDwarfWeaponReaches(skill));
        if (!self && !foe) return [];

        var lines = new List<KnockdownLine>();

        if (foe)
        {
            int stated = StatedSeconds(resolved, effects.StrengthRank);
            bool earthbind = effects.Earthbind && stated < EarthbindMinSeconds;
            bool stonefist = effects.Stonefist && StonefistApplies(SourceOf(skill))
                             && WithStonefist(stated) > stated;

            if (earthbind)
                lines.Add(new KnockdownLine(KnockdownLineKind.EarthbindFloor, EarthbindMinSeconds));
            else if (stonefist)
                lines.Add(new KnockdownLine(KnockdownLineKind.StonefistBonus, StonefistBonusSeconds));

            if (effects.BleedSeconds > 0)
                lines.Add(new KnockdownLine(KnockdownLineKind.Bleeding, effects.BleedSeconds));
        }

        // Le perso s'assomme LUI-MÊME (Grappin, Frappe désespérée, Attaque d'ivresse) : sa propre mise au sol a
        // sa ligne, en rouge — rester au sol est un mauvais coup pour lui. ⚠ C'est la SEULE exception à « une
        // ligne seulement quand un effet agit » (Q9) : dès qu'un effet d'assommement est actif, la ligne dit
        // aussi ce qui N'arrive PAS (« non rallongé »), pour qu'on n'ait pas à le déduire du silence.
        // Maquette de Philippe du 17/09 : l'insigne rallonge cet assommement-là aussi, Lien terrestre non
        // (il ne tient au sol que les ennemis).
        if (self && (effects.Stonefist || effects.Earthbind))
        {
            // ⚠ L'insigne ne rallonge NOTRE chute que lorsqu'elle accompagne un assommement qu'on CAUSE :
            // Grappin dit « You are ALSO knocked down », sa chute fait partie du même coup. Quand on se
            // renverse tout seul (Frappe désespérée, Attaque d'ivresse), il ne fait rien — wiki, confirmé par
            // Philippe le 17/09 : « Contrary to Grapple, Stonefist Insignia does not prolong the time you are
            // knocked down. » Grappin est la SEULE compétence de la base à causer un assommement ET à s'en
            // infliger un, donc cette règle textuelle recouvre exactement les trois cas.
            // Aucune des trois n'annonce de durée pour SA propre chute : c'est la base.
            bool extended = effects.Stonefist && causesFoe && WithStonefist(BaseSeconds) > BaseSeconds;
            lines.Add(extended
                ? new KnockdownLine(KnockdownLineKind.SelfStonefistBonus, StonefistBonusSeconds)
                : new KnockdownLine(KnockdownLineKind.SelfNotExtended, 0));
        }

        // Ronces ne fait pas la différence entre un ennemi et nous : un perso qui s'assomme saigne aussi
        // (décision Q4 du cadrage 4b, confirmée au 4c).
        if (self && effects.BleedSeconds > 0)
            lines.Add(new KnockdownLine(KnockdownLineKind.SelfBleeding, effects.BleedSeconds));

        return lines;
    }
}
