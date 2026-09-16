#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.VisualTree;

namespace Beutl.Controls.PropertyEditors;

/// <summary>
/// Aligns inline inputs to a shared position in a wide property inspector, including
/// nested rows. Grid still handles the children, validation and responsive row placement.
/// </summary>
[PseudoClasses(":aligned")]
public sealed class PropertyEditorGrid : Grid
{
    public static readonly AttachedProperty<bool> IsAlignmentScopeProperty =
        AvaloniaProperty.RegisterAttached<PropertyEditorGrid, Control, bool>("IsAlignmentScope");

    public static readonly AttachedProperty<double> ValueColumnRatioProperty =
        AvaloniaProperty.RegisterAttached<PropertyEditorGrid, Control, double>("ValueColumnRatio", .5,
            validate: value => double.IsFinite(value) && value is >= 0 and <= 1);

    public static readonly StyledProperty<int> ValueColumnProperty =
        AvaloniaProperty.Register<PropertyEditorGrid, int>(nameof(ValueColumn), 2);

    private const double MinimumScopeWidth = 640;
    private const double MinimumHeaderWidth = 80;
    private const double InputInset = 4;
    private Control? _scope;
    private ColumnDefinition? _alignedHeader;
    private GridLength _originalHeaderWidth;
    private double _rightInset = double.NaN;
    private double _precedingWidth;
    private bool _settingWidth;

    static PropertyEditorGrid()
    {
        AffectsMeasure<PropertyEditorGrid>(ValueColumnProperty);
    }

    public static bool GetIsAlignmentScope(Control control) => control.GetValue(IsAlignmentScopeProperty);
    public static void SetIsAlignmentScope(Control control, bool value) => control.SetValue(IsAlignmentScopeProperty, value);
    public static double GetValueColumnRatio(Control control) => control.GetValue(ValueColumnRatioProperty);
    public static void SetValueColumnRatio(Control control, double value) => control.SetValue(ValueColumnRatioProperty, value);

    public int ValueColumn
    {
        get => GetValue(ValueColumnProperty);
        set => SetValue(ValueColumnProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scope = this.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetIsAlignmentScope);
        if (_scope != null)
            _scope.PropertyChanged += OnScopePropertyChanged;
        InvalidateMeasure();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseAlignmentScope();
        base.OnDetachedFromVisualTree(e);
    }

    private void ReleaseAlignmentScope()
    {
        if (_scope != null)
            _scope.PropertyChanged -= OnScopePropertyChanged;
        _scope = null;
        _rightInset = double.NaN;
        RestoreHeaderWidth();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_scope is { Bounds.Width: >= MinimumScopeWidth }
            && double.IsFinite(availableSize.Width)
            && ValueColumn > 0 && ValueColumn < ColumnDefinitions.Count
            && Children.Any(child => child.IsVisible && GetColumn(child) == ValueColumn && GetRow(child) == 0))
        {
            // Only the splitter columns lie between the label and value. Measure their
            // margins as well so different templates share the actual Box edge.
            _precedingWidth = 0;
            for (int column = 1; column < ValueColumn; column++)
            {
                double width = 0;
                foreach (Control child in Children.Where(child => GetColumn(child) == column && GetColumnSpan(child) == 1))
                {
                    child.Measure(availableSize);
                    width = Math.Max(width, child.DesiredSize.Width);
                }
                _precedingWidth += width;
            }

            double bandWidth = _scope.Bounds.Width * (1 - GetValueColumnRatio(_scope))
                - (double.IsNaN(_rightInset) ? 0 : _rightInset);
            double labelWidth = availableSize.Width - bandWidth - _precedingWidth - InputInset;
            if (labelWidth >= MinimumHeaderWidth)
            {
                if (_alignedHeader != ColumnDefinitions[0])
                {
                    RestoreHeaderWidth();
                    _alignedHeader = ColumnDefinitions[0];
                    _originalHeaderWidth = _alignedHeader.Width;
                    _alignedHeader.PropertyChanged += OnHeaderPropertyChanged;
                }
                SetHeaderWidth(new GridLength(labelWidth));
                PseudoClasses.Set(":aligned", true);
            }
            else
            {
                RestoreHeaderWidth();
            }
        }
        else
        {
            RestoreHeaderWidth();
        }

        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        if (_scope is { Bounds.Width: >= MinimumScopeWidth }
            && this.TranslatePoint(new Point(finalSize.Width, 0), _scope) is { } right)
        {
            // The row's right edge accounts for every ancestor's padding and border,
            // unlike counting TreeLineDecorator indentation levels.
            double inset = _scope.Bounds.Width - right.X;
            if (double.IsNaN(_rightInset) || Math.Abs(_rightInset - inset) > .1)
            {
                _rightInset = inset;
                InvalidateMeasure();
            }
        }
        return result;
    }

    private void OnScopePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsAlignmentScopeProperty && _scope != null && !GetIsAlignmentScope(_scope))
        {
            ReleaseAlignmentScope();
            InvalidateMeasure();
        }
        else if (e.Property == BoundsProperty || e.Property == ValueColumnRatioProperty)
            InvalidateMeasure();
    }

    private void OnHeaderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_settingWidth && e.Property == ColumnDefinition.WidthProperty
            && _scope is { Bounds.Width: > 0 } && _alignedHeader is { Width.IsAbsolute: true }
            && !double.IsNaN(_rightInset))
        {
            // Keep the existing splitter usable: dragging any row moves the shared
            // input position, rather than having the next measure undo the drag.
            double left = _scope.Bounds.Width - _rightInset - Bounds.Width;
            double position = left + _alignedHeader.Width.Value + _precedingWidth + InputInset;
            // The deepest participating row determines how far left the shared
            // splitter can move without dropping any row out of alignment.
            foreach (var grid in _scope.GetVisualDescendants().OfType<PropertyEditorGrid>())
            {
                if (grid._scope == _scope && grid._alignedHeader != null && grid.IsEffectivelyVisible
                    && !double.IsNaN(grid._rightInset))
                {
                    double gridLeft = _scope.Bounds.Width - grid._rightInset - grid.Bounds.Width;
                    position = Math.Max(position, gridLeft + MinimumHeaderWidth + grid._precedingWidth + InputInset);
                }
            }
            SetValueColumnRatio(_scope, Math.Clamp(position / _scope.Bounds.Width, 0, 1));
        }
    }

    private void SetHeaderWidth(GridLength width)
    {
        if (_alignedHeader == null || _alignedHeader.Width == width) return;
        _settingWidth = true;
        try { _alignedHeader.Width = width; }
        finally { _settingWidth = false; }
    }

    private void RestoreHeaderWidth()
    {
        PseudoClasses.Set(":aligned", false);
        if (_alignedHeader == null) return;
        _alignedHeader.PropertyChanged -= OnHeaderPropertyChanged;
        SetHeaderWidth(_originalHeaderWidth);
        _alignedHeader = null;
    }
}
