namespace ZCodex.Core.Data;

/// <summary>
/// Dégâts PROPRES d'un effet du bandeau d'équipe, comptés dans le spike par une ligne artificielle à
/// compteur (demande de Philippe du 28/09/2026, née de la relecture des lots 6e/6e-b).
///
/// ⚠⚠ Ce n'est PAS un bonus aux attaques : les effets du bandeau qui ajoutent des dégâts à ce que les
/// persos infligent (Vents favorables, Vannage, les deux Ordres, Hymne d'envie, « Ensemble et unis ! »,
/// l'Étendard d'honneur) passent par <see cref="DamageBoostData"/> et sont déjà comptés sur chaque ligne.
/// Ici, c'est l'ESPRIT lui-même qui frappe, à un rythme que l'application ne peut pas deviner — d'où le
/// compteur laissé à l'utilisateur, exactement comme les lignes de vol de vie vampirique.
///
/// <b>Recensement du 28/09/2026</b> : les 28 descriptions anglaises du bandeau relues une par une dans la
/// base réelle. **Ronces est le SEUL cas.** Les autres sortent pour une raison lisible :
/// <list type="bullet">
/// <item>bonus aux attaques → <see cref="DamageBoostData"/> (6 effets) ;</item>
/// <item>vol de vie (Ordre du vampire) ou taux de critique (« Visez les yeux ! ») → déjà à leur place ;</item>
/// <item>conversions de type (Hiver, les deux Conflagrations) : aucun chiffre propre ;</item>
/// <item>Lien terrestre : sa seule perte de vie est celle de l'ESPRIT, pas de la cible ;</item>
/// <item>tout le reste (énergie, recharge, adrénaline, durées) ne touche pas les dégâts.</item>
/// </list>
/// Un 2ᵉ cas se traiterait ici, et nulle part ailleurs.
/// </summary>
public static class SpikeBandDamage
{
    /// <summary>
    /// Dégâts de Ronces par créature renversée. ⚠ <b>5, un LITTÉRAL</b> — sondé dans la base, pas lu à
    /// l'œil : les trois colonnes de progression de Ronces portent le niveau de l'esprit, la durée du
    /// saignement (5…17…20 s) et la durée de vie, jamais les dégâts. Le rang de Survie en pleine nature
    /// ne les fait donc PAS monter.
    ///
    /// ⚠ Ils IGNORENT l'armure (tranché par Philippe le 28/09/2026) : la description n'annonce aucun type
    /// de dégâts, et c'est la règle du chantier pour ce cas-là.
    /// ⚠ Le saignement, lui, reste hors du spike : une condition n'est pas un dégât instantané.
    /// </summary>
    public const int BramblesDamage = 5;

    /// <summary>Cette compétence est-elle un effet de bandeau à dégâts propres ? Sert à DEUX endroits du
    /// spike, et c'est volontaire : la ligne artificielle qui la compte, et le refus de lui en faire une
    /// seconde depuis la barre d'un perso (le cadre vert) — le même effet compterait deux fois.</summary>
    public static bool IsBandDamageSkill(int skillId) => skillId == KnockdownData.BramblesSkillId;
}
