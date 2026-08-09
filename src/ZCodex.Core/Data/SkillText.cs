namespace ZCodex.Core.Data;

/// <summary>Helpers de texte de compétence partagés (UI + résolution).</summary>
public static class SkillText
{
    /// <summary>
    /// Corps de la description sans la 1re phrase de type (ex: "Elite Hex Spell.") déjà
    /// affichée sur sa propre ligne. SkillType non vide ⇒ la 1re phrase EST le type.
    /// </summary>
    public static string ConciseBody(string description, string skillType)
    {
        var desc = description ?? string.Empty;
        if (!string.IsNullOrEmpty(skillType))
        {
            var dot = desc.IndexOf('.');
            if (dot >= 0 && dot < desc.Length - 1)
                desc = desc[(dot + 1)..];
        }
        return desc.Trim();
    }
}
