using System.Globalization;

using Avalonia;
using Avalonia.Data;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

public class RationalEditor : ValueStringEditor<Rational>
{
    public static readonly DirectProperty<RationalEditor, Rational> ValueProperty =
        AvaloniaProperty.RegisterDirect<RationalEditor, Rational>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public RationalEditor()
        : base(default, canScrub: true)
    {
        Text = "0/1";
    }

    private protected override DirectPropertyBase<Rational> ValuePropertyCore => ValueProperty;

    private protected override string Format(Rational value)
    {
        return $"{value.Numerator}/{value.Denominator}";
    }

    private protected override bool Differs(Rational x, Rational y) => x != y;

    private protected override bool TryParse(string? text, out Rational value)
    {
        return Rational.TryParse(text, CultureInfo.CurrentCulture, out value);
    }

    // Steps by whole numbers, without the decimal rounding of NumberEditor.
    private protected override Rational StepByWheel(Rational value, PointerWheelEventArgs e)
    {
        var delta = new Rational(10);
        double wheelDelta = e.Delta.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            delta = Rational.One;
            wheelDelta = -e.Delta.X;
        }

        return wheelDelta switch
        {
            < 0 => value - delta,
            > 0 => value + delta,
            _ => value
        };
    }

    private protected override Rational Scrub(Rational value, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
        return value + new Rational(scrub.NextDelta<int>(header, e), 1);
    }
}
