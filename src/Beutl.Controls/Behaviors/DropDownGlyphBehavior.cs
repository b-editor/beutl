#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Beutl.Controls.Behaviors;

/// <summary>
/// Adjusts the right inset of the dropdown glyph ("DropDownGlyph") inside FluentAvalonia's
/// ComboBox / DropDownButton templates.
/// </summary>
/// <remarks>
/// The templates set the glyph's Margin inline, which Avalonia applies as a local value that no
/// style (any priority) can override. This attached property re-applies the desired right inset
/// as a local value each time the template is applied.
/// </remarks>
public static class DropDownGlyphBehavior
{
    public static readonly AttachedProperty<double> RightInsetProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, double>(
            "RightInset", typeof(DropDownGlyphBehavior), double.NaN);

    static DropDownGlyphBehavior()
    {
        RightInsetProperty.Changed.AddClassHandler<TemplatedControl>(OnRightInsetChanged);
    }

    public static double GetRightInset(TemplatedControl control)
    {
        return control.GetValue(RightInsetProperty);
    }

    public static void SetRightInset(TemplatedControl control, double value)
    {
        control.SetValue(RightInsetProperty, value);
    }

    private static void OnRightInsetChanged(TemplatedControl control, AvaloniaPropertyChangedEventArgs e)
    {
        control.TemplateApplied -= OnTemplateApplied;
        if (!double.IsNaN(control.GetValue(RightInsetProperty)))
        {
            control.TemplateApplied += OnTemplateApplied;
            Apply(control);
        }
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        var control = (TemplatedControl)sender!;
        if (e.NameScope.Find<Layoutable>("DropDownGlyph") is { } glyph)
        {
            Apply(control, glyph);
        }
    }

    private static void Apply(TemplatedControl control)
    {
        Layoutable? glyph = control.GetVisualDescendants()
            .OfType<Layoutable>()
            .FirstOrDefault(v => v.Name == "DropDownGlyph");
        if (glyph is not null)
        {
            Apply(control, glyph);
        }
    }

    private static void Apply(TemplatedControl control, Layoutable glyph)
    {
        double inset = control.GetValue(RightInsetProperty);
        if (double.IsNaN(inset)) return;

        Thickness margin = glyph.Margin;
        glyph.Margin = new Thickness(margin.Left, margin.Top, inset, margin.Bottom);
    }
}
