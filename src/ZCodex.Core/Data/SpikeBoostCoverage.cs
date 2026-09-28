namespace ZCodex.Core.Data;

/// <summary>
/// Lot 6e — le filtre ANTI-DOUBLE COMPTE entre l'infobulle et la fenêtre Spike.
///
/// <see cref="DamageBoostData"/> connaît 56 descripteurs d'effets qui modifient les dégâts. La fenêtre
/// Spike en comptait déjà 33 par TROIS chemins qui lui sont propres, et en ignorait 23 (21 compétences).
/// Le lot 6e apporte ces 23 — et rien d'autre : importer les 56 en bloc compterait les 33 DEUX FOIS.
///
/// ⚠⚠ La partition n'est PAS une liste tenue à la main : elle est CALCULÉE depuis les trois chemins
/// réels, ici et nulle part ailleurs. Une compétence ajoutée demain à <see cref="SpikeProcSkills"/> ou à
/// <see cref="SpikeWeaponBuffs"/> sort donc toute seule du périmètre du 6e, au lieu de se mettre à
/// compter double en silence, build vert.
/// </summary>
public static class SpikeBoostCoverage
{
    /// <summary>
    /// Ce descripteur est-il DÉJÀ compté par un autre chemin de la fenêtre Spike ?
    /// <paramref name="skillNameEn"/> = le nom ANGLAIS de la compétence source (les trois chemins
    /// indexent par ce nom-là, jamais par le nom affiché).
    ///
    /// Les quatre chemins, dans l'ordre :
    ///  • <b>texte</b> — un <see cref="DamageBoostKind.TextDamage"/> relève un chiffre DANS la description
    ///    de la compétence touchée, et le Spike passe par <c>ResolveDescription</c> : il l'a déjà, sans
    ///    que personne l'ait écrit (Agression barbare, Sceau de puissance spectrale, Aura de sangsue) ;
    ///  • <b>Procs</b> — le rider a son compteur « Procs : N », où l'utilisateur compte lui-même les
    ///    déclenchements (conjurations, Ordres, Honneur, Cent lames, rituels de la nature…) ;
    ///  • <b>case du perso</b> — l'un des 9 buffs d'arme du chantier 14, allumé par son icône sur la carte ;
    ///  • <b>Grenth</b> — les deux compétences au calcul sur mesure.
    /// </summary>
    public static bool AlreadyCounted(DamageBoostDescriptor descriptor, string skillNameEn)
        => descriptor.Kind == DamageBoostKind.TextDamage
        || SpikeProcSkills.IsProcCapable(skillNameEn)
        || SpikeWeaponBuffs.FromSkillName(skillNameEn) is not null
        || SpikeGrenth.IsBalance(skillNameEn)
        || SpikeGrenth.IsAuraDual(skillNameEn);

    // ── Effets à CHARGES (relevés dans le TEXTE anglais, pas devinés) ─────────
    //
    // ⚠ Le § 6.12 du plan ne les avait pas vus : l'infobulle n'affiche QU'UN coup, donc la question ne s'y
    // pose jamais. La fenêtre Spike, elle, compte une SÉQUENCE — sans ces charges, « Je suis le plus
    // fort ! » donnerait son +20 à toutes les attaques du spike au lieu des 5 à 8 premières.
    //
    // La règle est celle de Q22 (lot 6d-2), étendue et non rouverte : les charges vont aux PREMIÈRES
    // attaques dans l'ordre de cast, et ce qui reste retombe sur les coups normaux.

    /// <summary>Un effet qui s'épuise après N attaques. <paramref name="Fixed"/> &gt; 0 = nombre
    /// littéral ; sinon <paramref name="Index"/> est la colonne de progression qui le porte.</summary>
    public sealed record ChargeRule(int SkillId, int Fixed, int Index);

    /// <summary>Les 4 effets à charges parmi les 23 descripteurs du lot 6e. Les textes anglais qui les
    /// justifient, mot pour mot :
    /// <list type="bullet">
    /// <item>« Esquive ceci ! » — <i>Your <b>next attack</b> is unblockable and deals +14…20 damage</i></item>
    /// <item>« Je suis le plus fort ! » — <i>Your <b>next 5…8 attacks</b> deal +14…20 damage</i> (le compte
    /// est à la colonne 0, les dégâts à la 1 — sondé dans la base, pas lu à l'œil)</item>
    /// <item>« Visez les yeux ! » — <i>Allies in earshot have +30…100% to land a critical hit with their
    /// <b>next attack</b></i> — UNE attaque par allié (confirmé par Philippe le 27/09/2026)</item>
    /// <item>Arme du tourment — <i>Target ally's attacks steal … but deal … less damage. <b>Ends after 3
    /// attacks.</b></i></item>
    /// </list></summary>
    public static readonly IReadOnlyList<ChargeRule> Charges =
    [
        new(DamageBoostData.DodgeThisSkillId,        Fixed: 1, Index: -1),
        new(DamageBoostData.IAmTheStrongestSkillId,  Fixed: 0, Index: 0),
        new(DamageBoostData.GoForTheEyesSkillId,     Fixed: 1, Index: -1),
        new(DamageBoostData.NightmareWeaponSkillId,  Fixed: 3, Index: -1),
    ];

    /// <summary>La règle de charges de cette compétence, ou null si son effet est permanent.</summary>
    public static ChargeRule? ChargeRuleFor(int skillId)
        => Charges.FirstOrDefault(c => c.SkillId == skillId);

    /// <summary>Vrai si l'effet s'épuise après un nombre d'attaques.</summary>
    public static bool IsCharged(int skillId) => ChargeRuleFor(skillId) is not null;

    // ── Effets que le COUP NORMAL ne reçoit pas ──────────────────────────────

    /// <summary>
    /// Effets dont le texte réserve le bonus aux COMPÉTENCES d'attaque : un coup normal n'en est pas une
    /// (règle Q20 du lot 6d-2). ⚠ Portée par le TEXTE, pas par un arbitrage — Concentration experte dit
    /// « Your bow <b>attack skills</b> … do +1…8…10 damage ».
    ///
    /// Les deux autres exclusions de Q20 (Hymne d'envie « next attack <b>skill</b> », Glaive était
    /// destructrice « your <b>Ritualist skills</b> ») n'ont pas besoin d'y figurer : la première est
    /// comptée par sa case, la seconde par son périmètre <see cref="DamageBoostScope.RitualistSkills"/>,
    /// qu'un coup normal ne satisfait jamais.
    /// </summary>
    public static bool SkillOnly(int skillId) => skillId == DamageBoostData.ExpertFocusSkillId;

    // ── Les 2 VERROUS de robustesse (28/09/2026, validés par Philippe) ──────────────────
    //
    // Deux angles morts repérés à la relecture des lots 6e/6f. Aucun des deux ne change un chiffre
    // aujourd'hui : ce sont des chemins MORTS, et c'est précisément pour ça qu'ils sont dangereux — le
    // jour où les données les réveillent, le mauvais chiffre sort en silence, build vert.
    //
    // ⚠ Les deux listes ci-dessous sont CALCULÉES depuis les descripteurs réels, jamais tenues à la
    // main : elles se remplissent toutes seules le jour du danger. Le harnais les exige VIDES, et un
    // harnais de session étant jetable, c'est leur présence ICI, dans le dépôt et à côté de la donnée
    // qu'elles surveillent, qui fait le verrou : la prochaine passe les retrouve, pas la mémoire.

    /// <summary>
    /// <b>Verrou 1 — charges brûlées par la mauvaise arme.</b> Les règles de <see cref="Charges"/> dont
    /// l'effet est réservé à certaines armes. <b>Doit rester VIDE.</b>
    ///
    /// <c>SpikeViewModel.ComputeBoostCharges</c> donne les charges aux N premières attaques d'arme dans
    /// l'ordre de cast, <b>sans regarder l'arme</b>. C'est juste tant que tout effet à charges porte sur
    /// « les attaques » sans distinction. Sinon la charge est perdue DEUX fois : l'attaque hors périmètre
    /// la consomme et n'affiche rien (le filtre de périmètre la jette en aval), et l'attaque suivante,
    /// celle qui y avait droit, ne l'a plus.
    ///
    /// ⚠ Le patron du danger existe déjà : Concentration experte est réservée à l'ARC, simplement elle
    /// n'a pas de charges. Si cette liste se remplit, il faut d'abord apprendre l'arme de la LIGNE à
    /// <c>ComputeBoostCharges</c> — et non l'arme équipée, qui divergerait du forçage manuel de la ligne.
    /// </summary>
    public static IReadOnlyList<ChargeRule> ChargeRulesWithWeaponScope() =>
        [.. Charges.Where(c => DamageBoostData.All
                .Where(d => d.SkillId == c.SkillId)
                .Any(d => DamageBoostData.IsWeaponRestrictedScope(d.Scope)))];

    /// <summary>
    /// <b>Verrou 2 — exigence d'élément sur un coup normal.</b> Les descripteurs qui exigent un type de
    /// dégâts d'arme (<c>RequiresElement</c>) et qu'aucun autre chemin du Spike ne compte déjà — donc
    /// ceux qui peuvent atteindre une ligne de COUP NORMAL par le lot 6e. <b>Doit rester VIDE.</b>
    ///
    /// Un coup normal n'a pas d'élément résolu, et <see cref="DamageBoostData.ElementSatisfied"/> est
    /// PERMISSIF sur l'inconnu. <c>BoostsFor</c> écarte donc maintenant ces effets d'un coup normal par
    /// PRUDENCE, exactement comme son voisin <c>RequiresPhysical</c> — sans quoi une conjuration était
    /// refusée sur la ligne d'une compétence et accordée sur la ligne de coup normal du même perso, même
    /// arme. Aujourd'hui les 4 seuls concernés (les 3 conjurations + l'Aura de poussière d'ébène) sont
    /// tous proc-comptés, donc la prudence ne coûte aucun chiffre.
    ///
    /// ⚠ Si cette liste se remplit, la prudence devient un chiffre MANQUANT : il faut alors faire
    /// descendre le type de dégâts CHOISI sur la ligne (<c>SpikeWeaponDamageType</c>) jusqu'à
    /// <c>BoostsFor</c>, au lieu de lui passer « je ne sais pas ».
    /// </summary>
    /// <remarks>⚠ <paramref name="englishNameOf"/> : les trois chemins de couverture indexent par nom
    /// ANGLAIS et le descripteur ne porte qu'un id. Le résolveur est donc fourni par l'appelant — le
    /// harnais y branche la VRAIE base, pour que le verrou ne puisse pas se rassurer sur une liste de
    /// noms recopiée à la main.</remarks>
    public static IReadOnlyList<DamageBoostDescriptor> ElementEffectsReachingPlainAttack(
        Func<int, string?> englishNameOf) =>
        [.. DamageBoostData.All.Where(d => d.RequiresElement is not null
                                          && !AlreadyCounted(d, englishNameOf(d.SkillId) ?? string.Empty))];
}
