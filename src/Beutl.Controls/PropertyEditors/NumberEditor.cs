using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

using Avalonia;
using Avalonia.Data;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

public class NumberEditor<TValue> : ValueStringEditor<TValue>
    where TValue : INumber<TValue>
{
    public static readonly DirectProperty<NumberEditor<TValue>, TValue> ValueProperty =
        AvaloniaProperty.RegisterDirect<NumberEditor<TValue>, TValue>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TValue> LargeChangeProperty =
        AvaloniaProperty.Register<NumberEditor<TValue>, TValue>(
            nameof(LargeChange),
            defaultValue: TValue.CreateTruncating(10));

    public static readonly StyledProperty<TValue> SmallChangeProperty =
        AvaloniaProperty.Register<NumberEditor<TValue>, TValue>(
            nameof(SmallChange),
            defaultValue: TValue.One);

    public static readonly StyledProperty<string?> NumberFormatProperty =
        AvaloniaProperty.Register<NumberEditor<TValue>, string?>(
            nameof(NumberFormat),
            defaultValue: null);

    public NumberEditor()
        : base(TValue.Zero, canScrub: true)
    {
        Text = "0";
    }

    public TValue LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    public TValue SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public string? NumberFormat
    {
        get => GetValue(NumberFormatProperty);
        set => SetValue(NumberFormatProperty, value);
    }

    internal TValue HapticReferenceValue { get; set; } = TValue.Zero;

    private protected override void OnScrubbed(TValue oldValue, ulong timestamp)
    {
        // ValueChanged has already applied the property's coercion to Value.
        if (NumberEditorHelper.CrossedHapticReference(oldValue, Value, HapticReferenceValue))
            NativeHapticFeedback.PerformAlignment(this, timestamp);
    }

    private protected override DirectPropertyBase<TValue> ValuePropertyCore => ValueProperty;

    private protected override string Format(TValue value)
    {
        return NumberEditorHelper.Format(value, NumberFormat);
    }

    private protected override bool Differs(TValue x, TValue y) => x != y;

    private protected override bool TryParse(string? text, [MaybeNullWhen(false)] out TValue value)
    {
        return TValue.TryParse(text, CultureInfo.CurrentCulture, out value) && value is not null;
    }

    private protected override TValue StepByWheel(TValue value, PointerWheelEventArgs e)
    {
        return NumberEditorHelper.StepByWheel(value, e, LargeChange, SmallChange);
    }

    private protected override TValue Scrub(TValue value, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
        return NumberEditorHelper.AddPreservingScale(value, scrub.NextDelta<TValue>(header, e) * SmallChange);
    }
}
