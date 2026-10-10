namespace ZCodex.Core.Data;

/// <summary>Les cases « État du personnage » de l'onglet Armure : les situations que lisent les clauses
/// d'insignes (Sentry's, Blessed, Centurion's…) et les inscriptions de réduction plate.</summary>
[Flags]
public enum CharacterState
{
    None        = 0,
    Enchanted   = 1 << 0,
    Hexed       = 1 << 1,
    Stance      = 1 << 2,
    Attacking   = 1 << 3,
    HoldingItem = 1 << 4,
    Preparation = 1 << 5,
    PetAlive    = 1 << 6,
    Condition   = 1 << 7,
    Activating  = 1 << 8,
    WeaponSpell = 1 << 9,
    ShoutChant  = 1 << 10,
}

/// <summary>
/// Quel état du perso un effet coché dans « Sources externes » met en place (chantier « calculateur
/// d'armure : réductions de dégâts », lot 2 — décision Q4 de Philippe du 10/10/2026 : la case de l'état se
/// coche visiblement). Cocher Mantra of Earth, c'est être en posture : Sentry's doit compter sans qu'on
/// touche à « Pose de combat ».
///
/// La règle part du TYPE de la compétence tel que la base l'écrit, jamais d'une liste recopiée : un effet
/// ajouté plus tard à la table est relié d'office. Les exceptions sont écrites ci-dessous avec leur raison.
/// </summary>
public static class CharacterStateLinks
{
    private static readonly IReadOnlyDictionary<string, CharacterState> ByType =
        new Dictionary<string, CharacterState>(StringComparer.Ordinal)
        {
            ["Stance"]                  = CharacterState.Stance,
            ["Enchantment Spell"]       = CharacterState.Enchanted,
            ["Flash Enchantment Spell"] = CharacterState.Enchanted,
            ["Hex Spell"]               = CharacterState.Hexed,
            ["Weapon Spell"]            = CharacterState.WeaponSpell,
            // Un sort d'objet met son objet dans les mains du perso (Herald's : « while holding an item »).
            ["Item Spell"]              = CharacterState.HoldingItem,
            ["Preparation"]             = CharacterState.Preparation,
            // Centurion's : « while affected by a Shout, Echo, or Chant ».
            ["Shout"]                   = CharacterState.ShoutChant,
            ["Echo"]                    = CharacterState.ShoutChant,
            ["Chant"]                   = CharacterState.ShoutChant,
        };

    /// <summary>Exceptions à la règle du type, par nom anglais (clé de la ligne), chacune avec sa raison.</summary>
    public static readonly IReadOnlyDictionary<string, CharacterState> Exceptions =
        new Dictionary<string, CharacterState>(StringComparer.Ordinal)
        {
            // Le cri ne touche que le familier : le perso n'est pas « affecté par un cri ».
            ["Otyugh's Cry"]   = CharacterState.None,
            // −40 d'armure PENDANT l'activation : cocher la ligne, c'est être en train de l'activer, et la
            // préparation n'est pas encore en place.
            ["Barbed Arrows"]  = CharacterState.Activating,
            // −40 d'armure pendant l'utilisation du sceau.
            ["Healing Signet"] = CharacterState.Activating,
            // +10 d'armure « while conditioned » : cocher la ligne suppose la condition présente.
            ["Conviction"]     = CharacterState.Enchanted | CharacterState.Condition,
            // L'Armure brisée EST une condition.
            ["Cracked Armor"]  = CharacterState.Condition,
        };

    /// <summary>États mis en place par l'effet de nom anglais <paramref name="englishName"/> et de type
    /// <paramref name="skillType"/> (null pour un effet qui n'est pas une compétence).</summary>
    public static CharacterState Implied(string englishName, string? skillType)
        => Exceptions.TryGetValue(englishName, out var s) ? s
           : skillType is not null && ByType.TryGetValue(skillType, out var t) ? t
           : CharacterState.None;
}
