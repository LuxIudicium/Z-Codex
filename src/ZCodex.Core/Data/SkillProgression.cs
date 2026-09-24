using System.Text.RegularExpressions;

namespace ZCodex.Core.Data;

/// <summary>
/// Résolution des plages de variables d'une description (ex: "110...182...200") en valeur
/// unique au rang d'attribut courant, via la table de progression scrapée du wiki.
/// Matching par ancres (rangs 0/12/15) → robuste au réordonnancement concise↔table.
/// </summary>
public static class SkillProgression
{
    private static readonly Regex RangeRegex = new(@"\d+(?:\.\.\.\d+)+", RegexOptions.Compiled);

    /// <summary>Marqueur (SOH, U+0001) entourant une valeur résolue → SkillMarkup la rend en vert/gras.</summary>
    public const char Mark = (char)1;

    /// <summary>Marqueur (STX, U+0002) entourant une valeur résolue à un rang RELEVÉ PAR UN FLUX
    /// (Hidden Talent / Meek Shall Inherit) → SkillMarkup la rend dans la couleur du flux.</summary>
    public const char MarkFlux = (char)2;

    /// <summary>Marqueur (ETX, U+0003) entourant une valeur RELEVÉE PAR LA PUISSANCE DE L'INVOCATION
    /// (bloc « Invocation » de l'infobulle) → SkillMarkup la rend en bleu clair, la valeur de base
    /// restant en vert. N'apparaît JAMAIS dans une description (donc absent de <see cref="MarkChars"/>,
    /// que seuls les parseurs de description utilisent).</summary>
    public const char MarkSummon = (char)3;

    /// <summary>Marqueur (EOT, U+0004) entourant une valeur d'infobulle MODIFIÉE PAR UN RITUEL DE
    /// LA NATURE (énergie/recharge/cast/upkeep/overcast) → SkillMarkup la rend dans la couleur
    /// « rituel ». N'apparaît JAMAIS dans une description (absent de <see cref="MarkChars"/>).</summary>
    public const char MarkRitual = (char)4;

    /// <summary>Marqueur (ENQ, U+0005) entourant un bonus de compétence équipée ACTIVE (Aura of
    /// the Lich, Awaken the Blood...) dans l'overlay d'attributs du teambuild → SkillMarkup la
    /// rend dans la couleur du boost (violet). N'apparaît JAMAIS dans une description (absent de
    /// <see cref="MarkChars"/>).</summary>
    public const char MarkSkillBoost = (char)5;

    /// <summary>Marqueur (ACK, U+0006) entourant un attribut FIXÉ par une compétence override
    /// active (Master of Magic : « set to X », remplace la base au lieu de s'y additionner) dans
    /// l'overlay d'attributs du teambuild → SkillMarkup la rend en ambre (même couleur que
    /// <see cref="MarkRitual"/>). N'apparaît JAMAIS dans une description (absent de
    /// <see cref="MarkChars"/>).</summary>
    public const char MarkOverride = (char)6;

    /// <summary>Marqueur (BEL, U+0007) entourant un texte d'AVERTISSEMENT d'infobulle → SkillMarkup le
    /// rend en rouge. Posé sur les effets à double tranchant, qui frappent notre propre personnage :
    /// Ronces fait saigner tout ce qui est assommé dans sa portée, nous compris (lot 4c). N'apparaît
    /// JAMAIS dans une description (absent de <see cref="MarkChars"/>).</summary>
    public const char MarkWarning = (char)7;

    /// <summary>Marqueur (BS, U+0008) entourant une valeur resolue sur une caracteristique
    /// SUBSTITUEE par une competence active (Sceau des illusions, Celerite symbolique, lot 5) :
    /// SkillMarkup la rend en rose. /!\ Contrairement aux marqueurs 3 a 7, celui-ci apparait bel
    /// et bien DANS une description resolue : il DOIT donc rester dans <see cref="MarkChars"/>,
    /// faute de quoi les sept parseurs qui lisent la description resolue (degats, conditions,
    /// enchantements, assommement, invocation, durees, rituels) cessent de voir ses valeurs.</summary>
    public const char MarkSubst = (char)8;

    /// <summary>Marqueur (SO, U+000E) entourant une valeur de description RELEVÉE PAR UN EFFET ACTIF
    /// (lot 6a : Agression barbare sur les attaques du familier, Sceau de puissance spectrale sur les
    /// attaques des esprits) : SkillMarkup la rend en VIOLET, la couleur que l'application donne déjà
    /// aux bonus de compétence équipée active (<see cref="MarkSkillBoost"/>). Comme
    /// <see cref="MarkSubst"/>, il apparaît DANS une description résolue et DOIT donc rester dans
    /// <see cref="MarkChars"/> — sans quoi les sept parseurs de description cessent de voir la valeur
    /// relevée (la ligne « ignore l'armure » de l'infobulle montrerait la valeur de base).
    /// ⚠ Pourquoi U+000E et pas le premier code libre : U+0009 à U+000D (\t \n \v \f \r) sont TOUS
    /// matchés par <c>\s</c> en .NET, donc un marqueur pris là-dedans ferait matcher les regex en
    /// <c>{Value}\s+damage</c> sur le marqueur lui-même.</summary>
    public const char MarkEffect = (char)14;

    /// <summary>Les QUATRE marqueurs de valeur résolue (normal + flux + substitution + effet actif), à placer dans
    /// une classe regex <c>[…]</c> par tout parseur de description résolue (SkillDamage,
    /// WeaponStrike…) pour détecter une valeur quel que soit son marquage. ⚠ Tout nouveau marqueur
    /// posé DANS une description doit être ajouté ici, sinon ces parseurs cessent de voir ses
    /// valeurs — sans erreur ni build rouge.</summary>
    public const string MarkChars = "\u0001\u0002\u0008\u000E";

    /// <summary>
    /// Remplace chaque plage <c>a...b...c</c> de la description par <c>progression[v][rank]</c>,
    /// où <c>v</c> est la variable dont les ancres (rangs 0/12/15) valent a/b/c. Rang null,
    /// pas de progression, ou plage non appariée → laissés inchangés (plage verte).
    /// <paramref name="fluxBoosted"/> = le rang inclut un bonus de flux → valeurs marquées
    /// distinctement (toute la description scale sur le même attribut, donc marquage uniforme).
    /// <paramref name="substituted"/> = le rang est celui d'une AUTRE caractéristique, imposée par
    /// une compétence active (lot 5) → valeurs marquées en rose, priorité sur le flux.
    /// <paramref name="bonusColumn"/> / <paramref name="bonus"/> (lot 6a) = un effet actif relève de
    /// <paramref name="bonus"/> la valeur de cette colonne de progression : la PREMIÈRE occurrence de
    /// la colonne dans le texte porte la valeur relevée, marquée <see cref="MarkEffect"/> ; les
    /// suivantes restent à la valeur de base (le second paquet d'un Coup brutal est conditionnel,
    /// décision § 6.6 du plan). Fonctionne à l'identique sur le texte EN et le texte FR : les deux
    /// résolvent depuis la MÊME colonne.
    /// </summary>
    public static string Resolve(string description, string[][]? progression, int? rank, bool fluxBoosted = false, bool frAnchors = false, bool substituted = false,
                                 int bonusColumn = -1, int bonus = 0)
    {
        if (string.IsNullOrEmpty(description) || progression is null || progression.Length == 0 || rank is null)
            return description;

        int r = rank.Value;
        // La substitution prime sur le flux : c'est l'information neuve, et le rang substitue
        // n'est plus celui que le flux a releve.
        char mark = substituted ? MarkSubst : fluxBoosted ? MarkFlux : Mark;
        var boosted = bonus != 0 && bonusColumn >= 0 && bonusColumn < progression.Length
            ? progression[bonusColumn] : null;
        bool bumped = false;
        return RangeRegex.Replace(description, m =>
        {
            var parts = m.Value.Split("...");
            var v = MatchVariable(progression, parts, frAnchors);
            if (v is null || v.Length == 0) return m.Value;
            int idx = Math.Clamp(r, 0, v.Length - 1);
            if (!bumped && ReferenceEquals(v, boosted) && int.TryParse(v[idx], out int b))
            {
                bumped = true;
                return $"{MarkEffect}{b + bonus}{MarkEffect}";
            }
            return $"{mark}{v[idx]}{mark}";
        });
    }

    /// <summary>Index de la colonne de progression dont les ancres apparient la plage
    /// <paramref name="range"/> (« 5...17...20 »), ou −1. Même appariement que
    /// <see cref="Resolve"/>, donc la colonne rendue est bien celle qui résoudra cette plage —
    /// c'est ce qui permet de désigner le paquet à relever (lot 6a) sans jamais deviner un index.</summary>
    public static int ColumnOf(string[][]? progression, string? range, bool frAnchors = false)
    {
        if (progression is null || string.IsNullOrEmpty(range)) return -1;
        var v = MatchVariable(progression, range.Split("..."), frAnchors);
        return v is null ? -1 : Array.IndexOf(progression, v);
    }

    // Variable dont les ancres correspondent aux parts de la plage. Wiki EN : rang 0 =
    // part[0], 12 = part[1], 15 = part[2]. FR (gwiki, frAnchors) : plages à 2 valeurs aux
    // rangs 0 et 15 — le clamp de At couvre aussi les pistes de titre (tables courtes).
    // Comparaison de chaînes → aucun parsing numérique.
    private static string[]? MatchVariable(string[][] progression, string[] parts, bool frAnchors)
    {
        foreach (var v in progression)
        {
            if (frAnchors)
            {
                if (parts.Length == 2 && At(v, 0) == parts[0] && At(v, 15) == parts[1])
                    return v;
                continue;
            }
            if (At(v, 0) == parts[0]
                && (parts.Length < 2 || At(v, 12) == parts[1])
                && (parts.Length < 3 || At(v, 15) == parts[2]))
                return v;
        }
        return null;
    }

    // Valeur au rang i. Au-delà de la dernière → clampée à la dernière valeur : les tables de rang
    // de titre s'arrêtent au rang 10, et la concise y ancre sa borne haute (parts[1] d'une plage
    // "min...max") sur la valeur de plateau. Sans effet sur les attributs (indices 0/12/15 toujours
    // dans une table 0–21). i < 0 → sentinelle non-numérique (ne matche aucune part chiffrée).
    private static string At(string[] v, int i)
        => v.Length == 0 || i < 0 ? "x" : v[Math.Min(i, v.Length - 1)];

    /// <summary>Valeur entière d'une colonne de progression au rang donné (clampée au dernier
    /// indice, comme <see cref="At"/>). Null = colonne absente/vide ou valeur non numérique.
    /// Utile pour lire directement un unique paramètre chiffré (Blessed Aura %, Tranquility %…)
    /// sans repasser par la substitution de plages.</summary>
    public static int? IntAt(string[]? column, int rank)
        => column is { Length: > 0 } && int.TryParse(At(column, Math.Max(0, rank)), out int n) ? n : null;
}
