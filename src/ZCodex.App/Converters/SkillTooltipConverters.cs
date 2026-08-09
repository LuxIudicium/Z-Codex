using ZCodex.Core.Models;
using System.Globalization;
using System.Windows.Data;

namespace ZCodex.App.Converters;

/// <summary>
/// Ligne "type de compétence (campagne)" de l'infobulle.
/// Ex : "Elite Hex Spell (Factions)". Core est omis (n'apporte rien).
/// </summary>
public class SkillTypeLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Skill skill ? Build(skill) : string.Empty;

    // Logique partagée avec SkillTooltipControl (qui la recalcule dans une DependencyProperty à
    // chaque ouverture, pour suivre la langue courante — le binding direct sur le POCO Skill, lui,
    // ne serait jamais réévalué au switch de langue).
    public static string Build(Skill skill)
    {
        // FR : type gwiki + suffixe « élite » (« Sort élite ») ; EN : préfixe wiki (« Elite Spell »).
        var line = AppLanguage.IsFr && skill.TypeFr.Length > 0
            ? (skill.IsElite ? $"{skill.TypeFr} élite" : skill.TypeFr)
            : ((skill.IsElite ? "Elite " : string.Empty) + skill.SkillType).Trim();
        // Core n'apporte rien à l'affichage ; on ne montre que les campagnes spécifiques
        // (noms propres identiques dans les deux langues).
        if (!string.IsNullOrWhiteSpace(skill.Campaign) &&
            !skill.Campaign.Equals("Core", StringComparison.OrdinalIgnoreCase))
            line = $"{line} ({skill.Campaign})";
        return line;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Description de l'infobulle : retire la phrase de type initiale dupliquée
/// (ex: "Elite Hex Spell.") déjà affichée sur sa propre ligne.
/// TODO(ui): ajouter dans le menu View un toggle description complète / concise.
///   La description stockée est la CONCISE ; la FULL devra être scrapée du wiki
///   (page individuelle de chaque skill) — voir TODO(skilldata) du chantier données.
/// </summary>
public class SkillDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Corps concis dans la langue affichée (FR gwiki tel quel, sinon EN sans phrase de type).
        return value is Skill skill ? skill.DisplayDescriptionBody : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
