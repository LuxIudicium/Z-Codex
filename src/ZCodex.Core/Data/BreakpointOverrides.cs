namespace ZCodex.Core.Data;

/// <summary>
/// Override manuel, par ID de skill, de la variable utilisée pour les breakpoints
/// (quand l'heuristique « la plus en escalier » de <see cref="SkillBreakpoints"/> ne choisit
/// pas la variable voulue).
///
/// Vide aujourd'hui : l'analyse de la DB complète n'a trouvé AUCUN cas où le choix de variable
/// change le saut de niveau (toutes les variables les plus grossières donnent les mêmes
/// breakpoints). Point d'extension propre : ajouter une entrée { skillId, variableIndex }.
/// </summary>
public static class BreakpointOverrides
{
    private static readonly IReadOnlyDictionary<int, int> _bySkillId = new Dictionary<int, int>
    {
        // ex: { 1782, 1 } forcerait "The Power Is Yours!" sur la variable d'indice 1.
    };

    public static int? For(int skillId) => _bySkillId.TryGetValue(skillId, out var v) ? v : null;
}
