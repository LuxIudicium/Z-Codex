namespace ZCodex.Core.Data;

/// <summary>
/// Dégâts des ATTAQUES NORMALES d'un perso dans le calcul de spike (lot 6d-2 du chantier infobulle) :
/// N coups d'arme hors compétence, sommés coup par coup.
///
/// ⚠ Pourquoi coup par coup et non « × N » : les buffs à CHARGES (Arme de fractionnement, 3 attaques ;
/// Arme d'éclats, 4 attaques ; « Trouvez leur faiblesse ! », 1 attaque) ne couvrent que les premiers
/// coups — et seulement avec ce que les attaques du spike leur ont laissé (Q22, 27/09/2026). Les termes
/// de la fourchette diffèrent donc d'un coup à l'autre, et seule la somme est juste.
///
/// ⚠ Ce que cette classe ne connaît pas, volontairement : les riders. Conjurations, Ordres, Honneur,
/// Cent lames, Flèches enflammées & co. ont leur propre ligne à compteur dans la fenêtre, où
/// l'utilisateur compte lui-même les déclenchements de ses coups normaux. Les ajouter ici les
/// compterait deux fois.
/// </summary>
public static class SpikeNormalAttack
{
    /// <summary>Charges restantes après les attaques du spike, chacune portant sur les PREMIERS coups
    /// normaux : <paramref name="SunderingHits"/> coups à la pénétration de BASE d'Arme de fractionnement,
    /// <paramref name="SplinterHits"/> coups au +<paramref name="SplinterBonus"/> d'Arme d'éclats (qui
    /// ignore l'armure), <paramref name="FtwHits"/> coups au +<paramref name="FtwBonus"/> de « Trouvez
    /// leur faiblesse ! », plus les effets à charges du lot 6e (<paramref name="Boosts"/>).</summary>
    public readonly record struct Charges(int SunderingHits = 0,
                                          int SplinterHits = 0, int SplinterBonus = 0,
                                          int FtwHits = 0, int FtwBonus = 0,
                                          IReadOnlyList<BoostCharge>? Boosts = null);

    /// <summary>
    /// Un effet à charges du lot 6e : ses <paramref name="Hits"/> premiers coups reçoivent
    /// <paramref name="Flat"/> de dégâts (NÉGATIF pour un malus — l'Arme du tourment retire 10…50) et
    /// <paramref name="Steal"/> de vol de vie.
    ///
    /// ⚠ Les trois champs sont indépendants : l'Arme du tourment en porte deux à la fois (elle retire
    /// des dégâts ET vole de la vie), « Je suis le plus fort ! » n'en porte qu'un.
    /// ⚠ Le CRITIQUE n'est pas ici, et c'est voulu : « Visez les yeux ! » relève une PROBABILITÉ, et la
    /// fenêtre chiffre une fourchette min–max en coup non critique — son bonus ne déplace donc aucun
    /// nombre, seulement la note de la ligne.
    /// </summary>
    public readonly record struct BoostCharge(int Hits, int Flat, int Steal);

    /// <summary>
    /// UN coup normal. <paramref name="penetration"/> = la pénétration TOTALE de ce coup (pool de base
    /// et bonus déjà additionnés par l'appelant, cf. <see cref="Damage"/>), <paramref name="flatBonus"/>
    /// = les +X plats qui s'ajoutent après l'armure. <paramref name="critical"/> = coup critique forcé
    /// (« Visez les yeux ! ») : la fourchette se referme alors sur le dégât critique.
    /// </summary>
    public static (int Min, int Max) Hit(WeaponStrike.Weapon weapon, int masteryRank, int armorLevel,
                                         int penetration, int flatBonus, bool critical,
                                         int attackerLevel = 20, double weaponMultiplier = 1.0)
    {
        // ⚠ Plancher à 0 depuis le lot 6e : un MALUS plat (l'Arme du tourment retire jusqu'à 50) peut
        // dépasser les dégâts d'un coup faible, et un coup ne rend jamais de la vie à sa cible. Sans
        // effet sur les chiffres du 6d-2, où tous les +X étaient positifs.
        if (critical)
        {
            int crit = WeaponStrike.CriticalAt(weapon, masteryRank, armorLevel, penetration,
                                               weaponMultiplier, attackerLevel) + flatBonus;
            return (Math.Max(0, crit), Math.Max(0, crit));
        }
        return (Math.Max(0, WeaponStrike.DamageAt(weapon.Min, masteryRank, armorLevel, penetration,
                                                  weaponMultiplier, attackerLevel) + flatBonus),
                Math.Max(0, WeaponStrike.DamageAt(weapon.Max, masteryRank, armorLevel, penetration,
                                                  weaponMultiplier, attackerLevel) + flatBonus));
    }

    /// <summary>
    /// Somme de <paramref name="hits"/> coups normaux. <paramref name="bonusPen"/> = les pénétrations en
    /// BONUS, permanentes sur la ligne (Clairvoyance du juge, mod de fractionnement qui a proc, arc
    /// corne) ; la pénétration de BASE d'Arme de fractionnement s'y ajoute sur ses seules charges — les
    /// deux catégories du wiki/Armor_penetration, le pool de base valant 0 sans elle (le rang de FORCE
    /// pénètre « with your attack skills », donc pas ici — CONFIRMÉ par Philippe le 27/09/2026). <paramref name="flatPerHit"/> = les +X de
    /// chaque coup (Arme brutale) ; le +X d'ARME de l'Arme du Grand Nain, lui, est déjà dans la plage de
    /// <paramref name="weapon"/>, car il passe avant l'armure et le critique.
    /// </summary>
    /// <param name="stealPerHit">Vol de vie conféré à CHAQUE coup par un effet permanent du lot 6e ; celui
    /// des effets à charges arrive par <see cref="Charges.Boosts"/>. ⚠ Il sort en 3ᵉ terme et n'entre pas
    /// dans la fourchette de dégâts : le vol de vie n'est pas un dégât (règle du chantier, les
    /// multiplicateurs et le flux ne le touchent pas).</param>
    /// <param name="weaponMultiplier">Le multiplicateur qui ne mord QUE sur l'arme (Rafale ×0,75) : il
    /// entre avant l'armure, et jamais sur les +X plats — exactement comme dans l'infobulle.</param>
    public static (int Min, int Max, int Steal) Damage(
        WeaponStrike.Weapon weapon, int masteryRank, int armorLevel,
        int bonusPen, int hits, bool critical, int flatPerHit,
        Charges charges, int attackerLevel = 20,
        int stealPerHit = 0, double weaponMultiplier = 1.0)
    {
        int min = 0, max = 0, steal = 0;
        for (int i = 1; i <= hits; i++)
        {
            int pen = bonusPen + (i <= charges.SunderingHits ? SpikeWeaponBuffs.SunderingBasePen : 0);
            int flat = flatPerHit
                     + (i <= charges.SplinterHits ? charges.SplinterBonus : 0)
                     + (i <= charges.FtwHits ? charges.FtwBonus : 0);
            steal += stealPerHit;
            foreach (var b in charges.Boosts ?? [])
                if (i <= b.Hits) { flat += b.Flat; steal += b.Steal; }
            var (hitMin, hitMax) = Hit(weapon, masteryRank, armorLevel, pen, flat, critical,
                                       attackerLevel, weaponMultiplier);
            min += hitMin; max += hitMax;
        }
        return (min, max, steal);
    }
}
