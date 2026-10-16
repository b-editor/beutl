using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Beutl.Controls.PropertyEditors;

internal class Vector3Editor<TElement> : Vector3Editor
    where TElement : INumber<TElement>
{
    public static readonly DirectProperty<Vector3Editor<TElement>, TElement> FirstValueProperty =
        Vector4Editor<TElement>.FirstValueProperty.AddOwner<Vector3Editor<TElement>>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<Vector3Editor<TElement>, TElement> SecondValueProperty =
        Vector4Editor<TElement>.SecondValueProperty.AddOwner<Vector3Editor<TElement>>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly DirectProperty<Vector3Editor<TElement>, TElement> ThirdValueProperty =
        Vector4Editor<TElement>.ThirdValueProperty.AddOwner<Vector3Editor<TElement>>(
            o => o.ThirdValue,
            (o, v) => o.ThirdValue = v);

    public static readonly StyledProperty<TElement> LargeChangeProperty =
        Vector4Editor<TElement>.LargeChangeProperty.AddOwner<Vector3Editor<TElement>>();

    public static readonly StyledProperty<TElement> SmallChangeProperty =
        Vector4Editor<TElement>.SmallChangeProperty.AddOwner<Vector3Editor<TElement>>();

    private readonly NumberVectorValues<TElement> _values;

    public Vector3Editor()
    {
        Values = _values = new NumberVectorValues<TElement>(
            this,
            [FirstValueProperty, SecondValueProperty, ThirdValueProperty],
            static (n, o, routedEvent) => new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement)>(
                (n[0], n[1], n[2]), (o[0], o[1], o[2]), routedEvent));
        FirstHeader = "0";
        SecondHeader = "0";
        ThirdHeader = "0";
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

    public TElement ThirdValue
    {
        get => _values[2];
        set => _values.Set(2, value);
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
    ":focus-any-textbox", ":focus-1st-textbox", ":focus-2nd-textbox", ":focus-3rd-textbox",
    ":border-pointerover", ":uniform")]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerThirdTextBox", typeof(TextBox))]
[TemplatePart("PART_HeaderFirstTextBlock", typeof(TextBlock))]
[TemplatePart("PART_HeaderSecondTextBlock", typeof(TextBlock))]
[TemplatePart("PART_HeaderThirdTextBlock", typeof(TextBlock))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
internal class Vector3Editor : VectorEditorBase
{
    public static readonly DirectProperty<Vector3Editor, string> FirstTextProperty =
        Vector4Editor.FirstTextProperty.AddOwner<Vector3Editor>(
            o => o.FirstText,
            (o, v) => o.FirstText = v);

    public static readonly DirectProperty<Vector3Editor, string> SecondTextProperty =
        Vector4Editor.SecondTextProperty.AddOwner<Vector3Editor>(
            o => o.SecondText,
            (o, v) => o.SecondText = v);

    public static readonly DirectProperty<Vector3Editor, string> ThirdTextProperty =
        Vector4Editor.ThirdTextProperty.AddOwner<Vector3Editor>(
            o => o.ThirdText,
            (o, v) => o.ThirdText = v);

    public static readonly StyledProperty<string?> FirstHeaderProperty =
        Vector4Editor.FirstHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string?> SecondHeaderProperty =
        Vector4Editor.SecondHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string?> ThirdHeaderProperty =
        Vector4Editor.ThirdHeaderProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<bool> IsUniformProperty =
        Vector4Editor.IsUniformProperty.AddOwner<Vector3Editor>();

    public static readonly StyledProperty<string?> NumberFormatProperty =
        Vector4Editor.NumberFormatProperty.AddOwner<Vector3Editor>();

    public Vector3Editor()
        : base([FirstTextProperty, SecondTextProperty, ThirdTextProperty], IsUniformProperty, componentHeaders: true)
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

    public string ThirdText
    {
        get => GetText(2);
        set => SetText(2, value);
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

    public string? ThirdHeader
    {
        get => GetValue(ThirdHeaderProperty);
        set => SetValue(ThirdHeaderProperty, value);
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

    protected override Type StyleKeyOverride => typeof(Vector3Editor);
}
