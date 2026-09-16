#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Layout;
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
    private const double LayoutTolerance = .1;
    private Control? _scope;
    private ColumnDefinition? _alignedHeader;
    private GridLength _originalHeaderWidth;
    private double _rightInset = double.NaN;
    private double _precedingWidth;
    private double _minimumValueSpace;
    private bool _canAlign;
    private bool _settingWidth;
    private bool _reclampPending;

    static PropertyEditorGrid()
    {
        AffectsMeasure<PropertyEditorGrid>(ValueColumnProperty);
        // Listen at the attached property so enabling a scope also reaches rows
        // that have no active scope subscription, without retaining detached rows.
        IsAlignmentScopeProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            foreach (var grid in control.GetVisualDescendants().OfType<PropertyEditorGrid>())
            {
                if (grid.IsAttachedToVisualTree())
                    grid.RefreshAlignmentScope();
            }
        });
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
        RefreshAlignmentScope();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseAlignmentScope();
        base.OnDetachedFromVisualTree(e);
    }

    private void ReleaseAlignmentScope()
    {
        CancelPendingReclamp();
        if (_scope != null)
            _scope.PropertyChanged -= OnScopePropertyChanged;
        _scope = null;
        _canAlign = false;
        _rightInset = double.NaN;
        RestoreHeaderWidth();
    }

    private void RefreshAlignmentScope()
    {
        Control? scope = this.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetIsAlignmentScope);
        if (_scope != scope)
        {
            ReleaseAlignmentScope();
            _scope = scope;
            if (_scope != null)
                _scope.PropertyChanged += OnScopePropertyChanged;
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        CancelPendingReclamp();
        _canAlign = _scope is { Bounds.Width: >= MinimumScopeWidth }
            && double.IsFinite(availableSize.Width)
            && HasInlineValue();
        if (_canAlign && _scope != null)
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
            _minimumValueSpace = MeasureMinimumValueSpace(availableSize.Height);

            double bandWidth = _scope.Bounds.Width * (1 - GetValueColumnRatio(_scope))
                - (double.IsNaN(_rightInset) ? 0 : _rightInset);
            double labelWidth = availableSize.Width - bandWidth - _precedingWidth - InputInset;
            if (labelWidth >= MinimumHeaderWidth - LayoutTolerance && bandWidth >= _minimumValueSpace - LayoutTolerance)
            {
                if (_alignedHeader != ColumnDefinitions[0])
                {
                    RestoreHeaderWidth();
                    _alignedHeader = ColumnDefinitions[0];
                    _originalHeaderWidth = _alignedHeader.Width;
                    _alignedHeader.PropertyChanged += OnHeaderPropertyChanged;
                }
                SetHeaderWidth(new GridLength(Math.Max(labelWidth, MinimumHeaderWidth)));
                PseudoClasses.Set(":aligned", true);
            }
            else
            {
                RestoreHeaderWidth();
                // Bounds still describe the previous layout during measure/arrange.
                // Clamp once the new row position and scope width are both final.
                _reclampPending = true;
                LayoutUpdated += ReclampAfterLayout;
            }
        }
        else
        {
            RestoreHeaderWidth();
        }

        return base.MeasureOverride(availableSize);
    }

    private bool HasInlineValue()
        => ValueColumn > 0 && ValueColumn < ColumnDefinitions.Count
            && Children.Any(child => child.IsVisible && GetColumn(child) == ValueColumn && GetRow(child) == 0);

    private double MeasureMinimumValueSpace(double availableHeight)
    {
        double result = 0;
        for (int column = ValueColumn; column < ColumnDefinitions.Count; column++)
        {
            ColumnDefinition definition = ColumnDefinitions[column];
            double width = Math.Max(definition.MinWidth, definition.Width.IsAbsolute ? definition.Width.Value : 0);
            foreach (Control child in Children.Where(child => child.IsVisible && GetRow(child) == 0
                         && GetColumn(child) == column && GetColumnSpan(child) == 1))
            {
                child.Measure(new Size(double.PositiveInfinity, availableHeight));
                double required = child.DesiredSize.Width;
                // Editable values can scroll/truncate their contents. Respect their
                // declared minimum instead of reserving the entire current text.
                if (column == ValueColumn && (child.MinWidth > 0 || double.IsFinite(child.Width) || child is TextBox))
                {
                    double minimum = Math.Max(child.MinWidth, double.IsFinite(child.Width) ? child.Width : 0);
                    if (child is TextBox textBox)
                        minimum = Math.Max(minimum, textBox.Padding.Left + textBox.Padding.Right
                            + textBox.BorderThickness.Left + textBox.BorderThickness.Right);
                    Thickness margin = child.Margin;
                    if (child.UseLayoutRounding)
                    {
                        double scale = LayoutHelper.GetLayoutScale(child);
                        minimum = LayoutHelper.RoundLayoutValueUp(minimum, scale);
                        margin = LayoutHelper.RoundLayoutThickness(margin, scale);
                    }
                    required = minimum + margin.Left + margin.Right;
                }
                width = Math.Max(width, required);
            }
            result += width;
        }
        // The shared position is the input's edge, after its leading inset.
        return Math.Max(0, result - InputInset);
    }

    private void CancelPendingReclamp()
    {
        if (!_reclampPending) return;
        LayoutUpdated -= ReclampAfterLayout;
        _reclampPending = false;
    }

    private void ReclampAfterLayout(object? sender, EventArgs e)
    {
        CancelPendingReclamp();
        if (_scope is { Bounds.Width: >= MinimumScopeWidth } && _canAlign && IsEffectivelyVisible)
        {
            double position = GetValueColumnRatio(_scope) * _scope.Bounds.Width;
            double clamped = ClampSharedPosition(position);
            if (Math.Abs(clamped - position) > LayoutTolerance)
                SetValueColumnRatio(_scope, clamped / _scope.Bounds.Width);
        }
    }

    private double ClampSharedPosition(double position)
    {
        if (_scope == null) return position;
        double minimum = 0;
        double maximum = _scope.Bounds.Width;
        foreach (var grid in _scope.GetVisualDescendants().OfType<PropertyEditorGrid>())
        {
            if (grid._scope == _scope && grid._canAlign && grid.IsEffectivelyVisible && grid.Bounds.Width > 0
                && grid.TranslatePoint(default, _scope) is { } origin)
            {
                minimum = Math.Max(minimum, origin.X + MinimumHeaderWidth + grid._precedingWidth + InputInset);
                maximum = Math.Min(maximum, origin.X + grid.Bounds.Width - grid._minimumValueSpace);
            }
        }
        // Incompatible rows keep their proportional fallback; do not alternate
        // between a minimum and maximum that cannot both be satisfied.
        return minimum <= maximum
            ? Math.Clamp(position, minimum, maximum)
            : GetValueColumnRatio(_scope) * _scope.Bounds.Width;
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
            if (double.IsNaN(_rightInset) || Math.Abs(_rightInset - inset) > LayoutTolerance)
            {
                _rightInset = inset;
                InvalidateMeasure();
            }
        }
        return result;
    }

    private void OnScopePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty || e.Property == ValueColumnRatioProperty)
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
            SetValueColumnRatio(_scope, ClampSharedPosition(position) / _scope.Bounds.Width);
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
