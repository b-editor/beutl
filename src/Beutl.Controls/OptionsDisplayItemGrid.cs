using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.VisualTree;

#nullable enable

namespace Beutl.Controls;

// Layout for the icon, text, action and chevron in the OptionsDisplayItem template.
public sealed class OptionsDisplayItemGrid : Grid
{
    private const double MinimumTextWidth = 200;
    private bool _isStacked;
    private Control? _actionControl;
    private IDisposable? _minimumWidthConstraint;
    private double? _constrainedMinimumWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 4 || Children[2] is not ContentPresenter action)
            return base.MeasureOverride(availableSize);

        Control icon = Children[0];
        Control text = Children[1];
        Control chevron = Children[3];
        if (!ReferenceEquals(_actionControl, action.Content))
        {
            ReleaseActionControl();
            _actionControl = action.Content as Control;
            if (_actionControl is not null)
                _actionControl.DetachedFromVisualTree += OnActionDetached;
        }
        double naturalMinimumWidth = _actionControl?.GetBaseValue(MinWidthProperty).GetValueOrDefault() ?? 0;
        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (Control child in Children)
            child.Measure(unconstrained);

        // Use the action's natural width, independent of its current row. Small toggles
        // can stay beside the text; wide inputs move below it before the text is squeezed.
        double actionWidth = Math.Max(naturalMinimumWidth,
            action.DesiredSize.Width - action.Margin.Left - action.Margin.Right);
        double inlineWidth = icon.DesiredSize.Width + Math.Min(text.DesiredSize.Width, MinimumTextWidth)
            + actionWidth + 16 + chevron.DesiredSize.Width;
        bool stacked = action.Content is not null && action.IsVisible && availableSize.Width < inlineWidth;
        if (_isStacked != stacked)
        {
            _isStacked = stacked;
            SetColumnSpan(text, stacked ? 2 : 1);
            SetRow(action, stacked ? 1 : 0);
            SetColumn(action, stacked ? 0 : 2);
            SetColumnSpan(action, stacked ? 4 : 1);
            action.Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(8, 4);
            action.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }

        double availableActionWidth = Math.Max(0, availableSize.Width
            - action.Margin.Left - action.Margin.Right
            - action.Padding.Left - action.Padding.Right
            - (_actionControl?.Margin.Left ?? 0) - (_actionControl?.Margin.Right ?? 0));
        if (stacked && _actionControl is { } control && availableActionWidth < naturalMinimumWidth)
        {
            if (_constrainedMinimumWidth != availableActionWidth)
            {
                ClearMinimumWidthConstraint();
                // A temporary high-priority value keeps the original local value or binding intact.
                _minimumWidthConstraint = control.SetValue(MinWidthProperty, availableActionWidth, BindingPriority.Animation);
                _constrainedMinimumWidth = availableActionWidth;
            }
        }
        else
        {
            ClearMinimumWidthConstraint();
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseActionControl();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActionDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ReleaseActionControl();
        InvalidateMeasure();
    }

    private void ReleaseActionControl()
    {
        if (_actionControl is not null)
            _actionControl.DetachedFromVisualTree -= OnActionDetached;
        ClearMinimumWidthConstraint();
        _actionControl = null;
    }

    private void ClearMinimumWidthConstraint()
    {
        _minimumWidthConstraint?.Dispose();
        _minimumWidthConstraint = null;
        _constrainedMinimumWidth = null;
    }
}
