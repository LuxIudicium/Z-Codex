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

    /// <summary>Les deux marqueurs de valeur résolue (normal + flux), à placer dans une classe
    /// regex <c>[…]</c> par tout parseur de description résolue (SkillDamage, WeaponStrike…) pour
    /// détecter une valeur quel que soit son marquage.</summary>
    public const string MarkChars = "";

    /// <summary>
    /// Remplace chaque plage <c>a...b...c</c> de la description par <c>progression[v][rank]</c>,
    /// où <c>v</c> est la variable dont les ancres (rangs 0/12/15) valent a/b/c. Rang null,
    /// pas de progression, ou plage non appariée → laissés inchangés (plage verte).
    /// <paramref name="fluxBoosted"/> = le rang inclut un bonus de flux → valeurs marquées
    /// distinctement (toute la description scale sur le même attribut, donc marquage uniforme).
    /// </summary>
    public static string Resolve(string description, string[][]? progression, int? rank, bool fluxBoosted = false, bool frAnchors = false)
    {
        if (string.IsNullOrEmpty(description) || progression is null || progression.Length == 0 || rank is null)
            return description;

        int r = rank.Value;
        char mark = fluxBoosted ? MarkFlux : Mark;
        return RangeRegex.Replace(description, m =>
        {
            var parts = m.Value.Split("...");
            var v = MatchVariable(progression, parts, frAnchors);
            if (v is null || v.Length == 0) return m.Value;
            int idx = Math.Clamp(r, 0, v.Length - 1);
            return $"{mark}{v[idx]}{mark}";
        });
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
