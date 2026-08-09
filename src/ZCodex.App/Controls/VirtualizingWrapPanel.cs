using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ZCodex.App.Controls;

/// <summary>
/// Panneau virtualisant "vue Liste explorateur" : remplit une colonne de haut en bas,
/// puis passe à la colonne suivante à droite, avec défilement HORIZONTAL.
/// Contrairement à un WrapPanel natif (qui casse la virtualisation et gèle l'app sur
/// ~1300 skills), il n'instancie que les colonnes visibles via l'IItemContainerGenerator
/// et implémente IScrollInfo (défilement logique en pixels, molette mappée à l'horizontale).
/// Items = données réelles (skills) → sélection / drag / menu contextuel inchangés.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    // Taille fixe d'une cellule (la ligne dense icône + nom + mécaniques). Réglable en XAML.
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(340.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    // Nombre de lignes par colonne calculé au dernier Measure (dépend de la hauteur du viewport).
    private int _rowsPerColumn = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Touche InternalChildren pour forcer l'init de l'ItemContainerGenerator.
        _ = InternalChildren;
        var generator = ItemContainerGenerator;

        var owner = ItemsControl.GetItemsOwner(this);
        int itemCount = owner?.Items.Count ?? 0;

        double viewportHeight = double.IsInfinity(availableSize.Height) ? ItemHeight : availableSize.Height;
        double viewportWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;

        _rowsPerColumn = Math.Max(1, (int)Math.Floor(viewportHeight / ItemHeight));
        int totalColumns = itemCount == 0 ? 0 : (int)Math.Ceiling((double)itemCount / _rowsPerColumn);

        // Fenêtre de colonnes visibles (+1 tampon de chaque bord pour un scroll fluide).
        double effectiveWidth = viewportWidth > 0 ? viewportWidth : totalColumns * ItemWidth;
        int firstCol = Math.Max(0, (int)Math.Floor(_offset.X / ItemWidth));
        int visibleCols = Math.Max(1, (int)Math.Ceiling(effectiveWidth / ItemWidth)) + 1;
        int lastCol = Math.Min(Math.Max(0, totalColumns - 1), firstCol + visibleCols);

        int firstIndex = itemCount == 0 ? 0 : firstCol * _rowsPerColumn;
        int lastIndex = itemCount == 0 ? -1 : Math.Min(itemCount - 1, (lastCol + 1) * _rowsPerColumn - 1);

        RealizeRange(generator, firstIndex, lastIndex);
        CleanupRange(generator, firstIndex, lastIndex);

        UpdateScrollInfo(new Size(effectiveWidth, viewportHeight), totalColumns);

        // On occupe exactement le viewport (défilement géré par IScrollInfo, pas par la taille).
        return new Size(
            double.IsInfinity(availableSize.Width) ? totalColumns * ItemWidth : availableSize.Width,
            viewportHeight);
    }

    private void RealizeRange(IItemContainerGenerator gen, int firstIndex, int lastIndex)
    {
        if (lastIndex < firstIndex) return;
        var startPos = gen.GeneratorPositionFromIndex(firstIndex);
        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (gen.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                var child = (UIElement)gen.GenerateNext(out bool newlyRealized);
                if (child == null) break;
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);
                    gen.PrepareItemContainer(child);
                }
                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }
    }

    private void CleanupRange(IItemContainerGenerator gen, int minIndex, int maxIndex)
    {
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            int itemIndex = gen.IndexFromGeneratorPosition(pos);
            if (itemIndex < minIndex || itemIndex > maxIndex)
            {
                gen.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        int rows = Math.Max(1, (int)Math.Floor(finalSize.Height / ItemHeight));
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0) continue;
            int col = itemIndex / rows;
            int row = itemIndex % rows;
            double x = col * ItemWidth - _offset.X;
            double y = row * ItemHeight;
            child.Arrange(new Rect(x, y, ItemWidth, ItemHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                // Filtre/ tri changé : on repart à gauche pour éviter un offset hors bornes.
                SetHorizontalOffset(0);
                break;
        }
        InvalidateMeasure();
    }

    // ── IScrollInfo (horizontal seulement) ────────────────────────────────────
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private const double LineStep = 24; // pas clavier/bouton
    private const double WheelStep = 72; // pas molette (≈ 3 lignes)

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => 0;

    private void UpdateScrollInfo(Size viewport, int totalColumns)
    {
        var extent = new Size(totalColumns * ItemWidth, viewport.Height);
        bool changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;

        double maxOffset = Math.Max(0, _extent.Width - _viewport.Width);
        if (_offset.X > maxOffset) _offset.X = maxOffset;
        if (_offset.X < 0) _offset.X = 0;

        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetHorizontalOffset(double offset)
    {
        double maxOffset = Math.Max(0, _extent.Width - _viewport.Width);
        offset = Math.Max(0, Math.Min(offset, maxOffset));
        if (Math.Abs(offset - _offset.X) < 0.5) return;
        _offset.X = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset) { }

    public void LineLeft() => SetHorizontalOffset(_offset.X - LineStep);
    public void LineRight() => SetHorizontalOffset(_offset.X + LineStep);
    public void LineUp() { }
    public void LineDown() { }

    // Molette : on la mappe à l'axe horizontal (vue par colonnes).
    public void MouseWheelUp() => SetHorizontalOffset(_offset.X - WheelStep);
    public void MouseWheelDown() => SetHorizontalOffset(_offset.X + WheelStep);
    public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - WheelStep);
    public void MouseWheelRight() => SetHorizontalOffset(_offset.X + WheelStep);

    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);
    public void PageUp() { }
    public void PageDown() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        // Amène la colonne du container ciblé dans le viewport (drag/sélection clavier).
        var child = visual as UIElement;
        if (child == null) return rectangle;
        int index = -1;
        for (int i = 0; i < InternalChildren.Count; i++)
            if (ReferenceEquals(InternalChildren[i], child)) { index = i; break; }
        if (index < 0) return rectangle;

        int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));
        if (itemIndex < 0) return rectangle;
        int col = itemIndex / Math.Max(1, _rowsPerColumn);
        double left = col * ItemWidth;
        double right = left + ItemWidth;
        if (left < _offset.X) SetHorizontalOffset(left);
        else if (right > _offset.X + _viewport.Width) SetHorizontalOffset(right - _viewport.Width);
        return rectangle;
    }
}
