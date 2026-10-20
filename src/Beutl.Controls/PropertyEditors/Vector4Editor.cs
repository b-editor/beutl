using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Beutl.Controls.PropertyEditors;

public class Vector4Editor<TElement> : Vector4Editor
    where TElement : INumber<TElement>
{
    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> FirstValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(FirstValue),
            o => o.FirstValue,
            (o, v) => o.FirstValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> SecondValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(SecondValue),
            o => o.SecondValue,
            (o, v) => o.SecondValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> ThirdValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(ThirdValue),
            o => o.ThirdValue,
            (o, v) => o.ThirdValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor<TElement>, TElement> FourthValueProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor<TElement>, TElement>(
            nameof(FourthValue),
            o => o.FourthValue,
            (o, v) => o.FourthValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TElement> LargeChangeProperty =
        AvaloniaProperty.Register<Vector4Editor<TElement>, TElement>(
            nameof(LargeChange),
            defaultValue: TElement.CreateTruncating(10));

    public static readonly StyledProperty<TElement> SmallChangeProperty =
        AvaloniaProperty.Register<Vector4Editor<TElement>, TElement>(
            nameof(SmallChange),
            defaultValue: TElement.One);

    private readonly NumberVectorValues<TElement> _values;

    public Vector4Editor()
    {
        Values = _values = new NumberVectorValues<TElement>(
            this,
            [FirstValueProperty, SecondValueProperty, ThirdValueProperty, FourthValueProperty],
            static (n, o, routedEvent) => new PropertyEditorValueChangedEventArgs<(TElement, TElement, TElement, TElement)>(
                (n[0], n[1], n[2], n[3]), (o[0], o[1], o[2], o[3]), routedEvent));
        FirstHeader = "0";
        SecondHeader = "0";
        ThirdHeader = "0";
        FourthHeader = "0";
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

    public TElement FourthValue
    {
        get => _values[3];
        set => _values.Set(3, value);
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
    ":focus-any-textbox",
    ":focus-1st-textbox", ":focus-2nd-textbox", ":focus-3rd-textbox", ":focus-4th-textbox",
    ":border-pointerover", ":uniform")]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerThirdTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerFourthTextBox", typeof(TextBox))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
public class Vector4Editor : VectorEditorBase
{
    public static readonly DirectProperty<Vector4Editor, string> FirstTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(FirstText),
            o => o.FirstText,
            (o, v) => o.FirstText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor, string> SecondTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(SecondText),
            o => o.SecondText,
            (o, v) => o.SecondText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor, string> ThirdTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(ThirdText),
            o => o.ThirdText,
            (o, v) => o.ThirdText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Vector4Editor, string> FourthTextProperty =
        AvaloniaProperty.RegisterDirect<Vector4Editor, string>(
            nameof(FourthText),
            o => o.FourthText,
            (o, v) => o.FourthText = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> FirstHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(FirstHeader));

    public static readonly StyledProperty<string?> SecondHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(SecondHeader));

    public static readonly StyledProperty<string?> ThirdHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(ThirdHeader));

    public static readonly StyledProperty<string?> FourthHeaderProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(nameof(FourthHeader));

    public static readonly StyledProperty<bool> IsUniformProperty =
        AvaloniaProperty.Register<Vector4Editor, bool>(nameof(IsUniform));

    public static readonly StyledProperty<string?> NumberFormatProperty =
        AvaloniaProperty.Register<Vector4Editor, string?>(
            nameof(NumberFormat),
            defaultValue: null);

    public Vector4Editor()
        : base(
            [FirstTextProperty, SecondTextProperty, ThirdTextProperty, FourthTextProperty],
            IsUniformProperty,
            componentHeaders: false)
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

    public string FourthText
    {
        get => GetText(3);
        set => SetText(3, value);
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

    public string? FourthHeader
    {
        get => GetValue(FourthHeaderProperty);
        set => SetValue(FourthHeaderProperty, value);
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

    protected override Type StyleKeyOverride => typeof(Vector4Editor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateFocusState();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        UpdateAutoCompact(availableSize);
        return measured;
    }
}
