using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Beutl.Controls.PropertyEditors;

internal class Vector2Editor<TElement> : Vector2Editor
    where TElement : INumber<TElement>
{
    public static readonly DirectProperty<Vector2Editor<TElement>, TElement> FirstValueProperty =
        Vector4Editor<TElement>.FirstValueProperty.AddOwner<Vector2Editor<TElement>>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<Vector2Editor<TElement>, TElement> SecondValueProperty =
        Vector4Editor<TElement>.SecondValueProperty.AddOwner<Vector2Editor<TElement>>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly StyledProperty<TElement> LargeChangeProperty =
        Vector4Editor<TElement>.LargeChangeProperty.AddOwner<Vector2Editor<TElement>>();

    public static readonly StyledProperty<TElement> SmallChangeProperty =
        Vector4Editor<TElement>.SmallChangeProperty.AddOwner<Vector2Editor<TElement>>();

    private readonly NumberVectorValues<TElement> _values;

    public Vector2Editor()
    {
        Values = _values = new NumberVectorValues<TElement>(
            this,
            [FirstValueProperty, SecondValueProperty],
            static (n, o, routedEvent) => new PropertyEditorValueChangedEventArgs<(TElement, TElement)>(
                (n[0], n[1]), (o[0], o[1]), routedEvent));
        FirstHeader = "0";
        SecondHeader = "0";
    }

    public TElement FirstValue
    {
        get => _values[0];
        set => _values.Set(0, value);
    }

    public TElement SecondValue
    {
        get => _values[1];
        set => _values.Set(1, value);
    }

    public TElement LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    public TElement SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }
}

[PseudoClasses(
    ":focus-any-textbox", ":focus-1st-textbox", ":focus-2nd-textbox",
    ":border-pointerover", ":uniform")]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_HeaderFirstTextBlock", typeof(TextBlock))]
[TemplatePart("PART_HeaderSecondTextBlock", typeof(TextBlock))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
internal class Vector2Editor : VectorEditorBase
{
    public static readonly DirectProperty<Vector2Editor, string> FirstTextProperty =
        Vector4Editor.FirstTextProperty.AddOwner<Vector2Editor>(
            o => o.FirstText,
            (o, v) => o.FirstText = v);

    public static readonly DirectProperty<Vector2Editor, string> SecondTextProperty =
        Vector4Editor.SecondTextProperty.AddOwner<Vector2Editor>(
            o => o.SecondText,
            (o, v) => o.SecondText = v);

    public static readonly StyledProperty<string?> FirstHeaderProperty =
        Vector4Editor.FirstHeaderProperty.AddOwner<Vector2Editor>();

    public static readonly StyledProperty<string?> SecondHeaderProperty =
        Vector4Editor.SecondHeaderProperty.AddOwner<Vector2Editor>();

    public static readonly StyledProperty<bool> IsUniformProperty =
        Vector4Editor.IsUniformProperty.AddOwner<Vector2Editor>();

    public static readonly StyledProperty<string?> NumberFormatProperty =
        Vector4Editor.NumberFormatProperty.AddOwner<Vector2Editor>();

    public Vector2Editor()
        : base([FirstTextProperty, SecondTextProperty], IsUniformProperty, componentHeaders: true)
    {
    }

    public string FirstText
    {
        get => GetText(0);
        set => SetText(0, value);
    }

    public string SecondText
    {
        get => GetText(1);
        set => SetText(1, value);
    }

    public string? FirstHeader
    {
        get => GetValue(FirstHeaderProperty);
        set => SetValue(FirstHeaderProperty, value);
    }

    public string? SecondHeader
    {
        get => GetValue(SecondHeaderProperty);
        set => SetValue(SecondHeaderProperty, value);
    }

    public bool IsUniform
    {
        get => GetValue(IsUniformProperty);
        set => SetValue(IsUniformProperty, value);
    }

    public string? NumberFormat
    {
        get => GetValue(NumberFormatProperty);
        set => SetValue(NumberFormatProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Vector2Editor);
}
