using ZCodex.Core.Data;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ZCodex.App.Behaviors;

/// <summary>
/// Propriétés attachées qui remplissent les Inlines d'un TextBlock à partir d'un texte de
/// description GW1 (<see cref="TextProperty"/>, surligné) suivi d'un fragment gris clair non
/// surligné (<see cref="SuffixProperty"/>), en mettant en gras la valeur : vert (comme le wiki) pour une plage de
/// variables (ex: "5...13...15") ou une valeur résolue (<see cref="SkillProgression.Mark"/>),
/// couleur de flux pour une valeur résolue à un rang relevé par un flux (<see cref="SkillProgression.MarkFlux"/>).
/// </summary>
public static class SkillMarkup
{
    // Gras : valeur résolue normale (groupe 1, vert), valeur résolue boostée par un flux
    // (groupe 2, couleur flux), valeur relevée par la Puissance de l'invocation (groupe 3, bleu
    // clair), valeur modifiée par un rituel de la nature (groupe 4, couleur rituel), bonus de
    // compétence équipée active (groupe 5, violet), attribut FIXÉ par une compétence override
    // (groupe 6, ambre), OU une plage "n...n[...n]" (vert).
    private static readonly Regex HighlightRegex = new(
        $@"{SkillProgression.Mark}([^{SkillProgression.Mark}]*){SkillProgression.Mark}"
        + $@"|{SkillProgression.MarkFlux}([^{SkillProgression.MarkFlux}]*){SkillProgression.MarkFlux}"
        + $@"|{SkillProgression.MarkSummon}([^{SkillProgression.MarkSummon}]*){SkillProgression.MarkSummon}"
        + $@"|{SkillProgression.MarkRitual}([^{SkillProgression.MarkRitual}]*){SkillProgression.MarkRitual}"
        + $@"|{SkillProgression.MarkSkillBoost}([^{SkillProgression.MarkSkillBoost}]*){SkillProgression.MarkSkillBoost}"
        + $@"|{SkillProgression.MarkOverride}([^{SkillProgression.MarkOverride}]*){SkillProgression.MarkOverride}"
        + @"|\d+(?:\.\.\.\d+)+",
        RegexOptions.Compiled);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(SkillMarkup),
            new PropertyMetadata(string.Empty, OnInputChanged));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    /// <summary>
    /// Fragment ajouté À LA SUITE du texte surligné, en gris clair et SANS passer par le
    /// surlignage (mention de caractéristique de l'infobulle). Le contournement du surlignage est
    /// le point clé : sa plage « 0...12...15 » matcherait la regex et virerait au vert gras.
    /// </summary>
    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.RegisterAttached(
            "Suffix", typeof(string), typeof(SkillMarkup),
            new PropertyMetadata(string.Empty, OnInputChanged));

    public static string GetSuffix(DependencyObject obj) => (string)obj.GetValue(SuffixProperty);
    public static void SetSuffix(DependencyObject obj, string value) => obj.SetValue(SuffixProperty, value);

    // Text et Suffix alimentent les MÊMES Inlines : les deux repassent par une reconstruction
    // complète, sinon celui posé en second effacerait l'autre (ordre non garanti en XAML).
    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) Rebuild(tb);
    }

    private static void Rebuild(TextBlock tb)
    {
        tb.Inlines.Clear();
        var text = GetText(tb) ?? string.Empty;
        if (text.Length > 0)
        {
            // '\n' = saut de ligne explicite (footer multi-lignes) → LineBreak entre segments.
            var lines = text.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                if (li > 0) tb.Inlines.Add(new LineBreak());
                AppendHighlighted(tb, lines[li]);
            }
        }

        var suffix = GetSuffix(tb) ?? string.Empty;
        if (suffix.Length == 0) return;
        // Collé à la phrase (espace insécable exclu : la mention DOIT pouvoir passer à la ligne).
        var run = new Run(text.Length > 0 ? " " + suffix : suffix);
        run.SetResourceReference(TextElement.ForegroundProperty, "TextFaintBrush");
        tb.Inlines.Add(run);
    }

    private static void AppendHighlighted(TextBlock tb, string text)
    {
        int last = 0;
        foreach (Match m in HighlightRegex.Matches(text))
        {
            if (m.Index > last)
                tb.Inlines.Add(new Run(text.Substring(last, m.Index - last)));

            // Groupe 1 = valeur résolue normale (vert) ; groupe 2 = valeur boostée par un flux
            // (couleur flux) ; groupe 3 = valeur relevée par l'invocation (bleu clair) ; groupe 4 =
            // valeur modifiée par un rituel de la nature (couleur rituel) ; groupe 5 = bonus de
            // compétence équipée active (violet) ; groupe 6 = attribut FIXÉ par une compétence
            // override (ambre, même couleur que le rituel) ; sinon = plage telle quelle (vert).
            string shown, brush;
            if (m.Groups[1].Success)      { shown = m.Groups[1].Value; brush = "SkillVariableBrush"; }
            else if (m.Groups[2].Success) { shown = m.Groups[2].Value; brush = "FluxVariableBrush"; }
            else if (m.Groups[3].Success) { shown = m.Groups[3].Value; brush = "SummonVariableBrush"; }
            else if (m.Groups[4].Success) { shown = m.Groups[4].Value; brush = "RitualVariableBrush"; }
            else if (m.Groups[5].Success) { shown = m.Groups[5].Value; brush = "SkillBoostVariableBrush"; }
            else if (m.Groups[6].Success) { shown = m.Groups[6].Value; brush = "RitualVariableBrush"; }
            else                          { shown = m.Value;           brush = "SkillVariableBrush"; }
            var run = new Run(shown) { FontWeight = FontWeights.Bold };
            run.SetResourceReference(TextElement.ForegroundProperty, brush);
            tb.Inlines.Add(run);
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            tb.Inlines.Add(new Run(text.Substring(last)));
    }
}
