using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZCodex.App.Behaviors;

// Infobulle « texte complet » qui ne s'ouvre QUE si le TextBlock est réellement tronqué par son ellipse
// (demande de Philippe du 28/09/2026 : nom de build trop long pour la colonne de 110 px de la carte). Le
// ToolTip lui-même se pose dans le XAML (ToolTip="{Binding Text, RelativeSource={RelativeSource Self}}") ;
// ce comportement ne fait qu'annuler son ouverture quand le texte tient en entier.
public static class TrimmedToolTip
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(TrimmedToolTip),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.ToolTipOpening -= OnToolTipOpening;
        if ((bool)e.NewValue) tb.ToolTipOpening += OnToolTipOpening;
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is TextBlock tb && !IsTrimmed(tb)) e.Handled = true;
    }

    /// <summary>Largeur naturelle du texte (mêmes police, taille, graisse) supérieure à la place réellement
    /// allouée : c'est exactement le cas où TextTrimming a remplacé la fin par « … ».</summary>
    public static bool IsTrimmed(TextBlock tb)
    {
        if (string.IsNullOrEmpty(tb.Text)) return false;
        var ft = new FormattedText(tb.Text, CultureInfo.CurrentCulture, tb.FlowDirection,
            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
            tb.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(tb).PixelsPerDip);
        double available = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        return ft.WidthIncludingTrailingWhitespace > available + 0.5;
    }
}
