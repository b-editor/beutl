using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

using Avalonia.Input;

namespace Beutl;

public static class NumberEditorHelper
{
    // Alt or Command makes a drag fine, a tenth as fast.
    internal static bool IsFineModifier(KeyModifiers modifiers)
        => modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Meta);

    // The value rounded to a multiple of step, with no more decimals than the step has; a step of zero or less leaves
    // it as it is. A small negative value that rounds to zero gives 0, since a box would show -0 as "-0".
    internal static double RoundToStep(double value, double step)
    {
        if (step > 0 && double.IsFinite(step))
        {
            value = Math.Round(value / step) * step;
            value = Math.Round(value, Math.Min((int)((decimal)step).Scale, 15));
        }

        return value == 0 ? 0 : value;
    }

    private static double GetScrubModifierCoefficient(KeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool fine = IsFineModifier(modifiers);
        if (shift && fine)
        {
            return 1.0;
        }
        if (shift)
        {
            return 10.0;
        }
        if (fine)
        {
            return 0.1;
        }
        return 1.0;
    }

    public static double ApplyScrubModifier(double move, KeyModifiers modifiers)
    {
        return move * GetScrubModifierCoefficient(modifiers);
    }

    // 整数型 TValue では小数移動量が CreateTruncating で 0 に切り捨てられ、
    // fine modifier (係数 0.1) ではドラッグがほぼ反応しなくなる。
    // ここでフレーム間の小数残差を accumulator に持ち越し、整数化できた分のみ返す。
    public static TValue ConsumeScrubAccumulator<TValue>(ref double accumulator, double scaledMove)
        where TValue : INumber<TValue>
    {
        accumulator += scaledMove;
        TValue truncated = TValue.CreateTruncating(accumulator);
        accumulator -= double.CreateTruncating(truncated);
        return truncated;
    }

    public static TValue AddPreservingScale<TValue>(TValue original, TValue delta)
        where TValue : INumber<TValue>
    {
        try
        {
            // The sum keeps the larger number of decimal places of the two values.
            var originalDecimal = decimal.CreateTruncating(original);
            var deltaDecimal = decimal.CreateTruncating(delta);
            int scale = Math.Max(GetScale(originalDecimal), GetScale(deltaDecimal));
            decimal result = originalDecimal + deltaDecimal;

            return TValue.CreateTruncating(decimal.Round(result, scale));
        }
        catch (OverflowException)
        {
            // decimal に収まらない場合はスケール保持を諦めて単純加算にフォールバック
            return original + delta;
        }
    }

    private static int GetScale(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0xFF;
    }

    // Reads a number or simple arithmetic expression typed into an editor in the current culture or with a period as
    // the decimal point. Where the decimal separator is a comma, the culture's own parse took the period for a group
    // separator and read "1.5" as 15, so group separators are allowed only for text neither decimal point reads, such
    // as "1.000,5". An integer takes no decimal point on a plain number, so "1.000" stays rejected rather than
    // becoming 1, while arithmetic expressions such as "1920 / 2" or "100 * 1.5" evaluate and round to the integer type.
    internal static bool TryParse<TValue>(ReadOnlySpan<char> text, [MaybeNullWhen(false)] out TValue value)
        where TValue : INumberBase<TValue>
    {
        return TryParseNumber(text, out value)
            || NumericExpressionEvaluator.TryEvaluate(text, out value);
    }

    internal static bool TryParseNumber<TValue>(ReadOnlySpan<char> text, [MaybeNullWhen(false)] out TValue value)
        where TValue : INumberBase<TValue>
    {
        NumberStyles style = DecimalStyle<TValue>.Value;
        return TValue.TryParse(text, style, CultureInfo.CurrentCulture, out value)
            || TValue.TryParse(text, style, CultureInfo.InvariantCulture, out value)
            || TValue.TryParse(text, CultureInfo.CurrentCulture, out value);
    }

    internal static bool IsFloatingPoint<TValue>()
        where TValue : INumberBase<TValue>
        => DecimalStyle<TValue>.Value == NumberStyles.Float;

    private static class DecimalStyle<TValue>
        where TValue : INumberBase<TValue>
    {
        public static readonly NumberStyles Value =
            TValue.TryParse("0.5", NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? NumberStyles.Float
                : NumberStyles.Integer;
    }

    // A uniform editor can apply the same value to the other components even when this one stayed the same.
    internal static bool TryParseEdit<TValue>(
        string? newText,
        string? oldText,
        [NotNullWhen(true)] out TValue? newValue,
        [NotNullWhen(true)] out TValue? oldValue,
        bool allowSameValue = false)
        where TValue : INumber<TValue>
    {
        if (TryParse(newText, out newValue)
            && newValue is not null)
        {
            if (!TryParse(oldText, out oldValue) || oldValue is null)
            {
                oldValue = newValue;
                return true;
            }

            return allowSameValue || newValue != oldValue;
        }

        oldValue = default;
        return false;
    }

    internal static TValue StepByWheel<TValue>(TValue value, PointerWheelEventArgs e, TValue largeChange, TValue smallChange)
        where TValue : INumber<TValue>
    {
        TValue delta = largeChange;
        double wheelDelta = e.Delta.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            wheelDelta = -e.Delta.X;
            delta = smallChange;
        }

        return wheelDelta switch
        {
            < 0 => AddPreservingScale(value, -delta),
            > 0 => AddPreservingScale(value, delta),
            _ => value
        };
    }

    // .NET writes negative zero as "-0", which an expression such as Round(-0.2) or a typed "-0" gives. Abs drops the
    // sign and keeps the places of a decimal zero such as 0.00, which AddPreservingScale keeps.
    internal static string Format<TValue>(TValue value, string? numberFormat)
        where TValue : INumber<TValue>
    {
        if (TValue.IsZero(value))
            value = TValue.Abs(value);

        return value.ToString(numberFormat ?? "G", CultureInfo.CurrentCulture);
    }

    // Zero and the property's default are reference points, including fast scrubs past either.
    // Staying at a reference (e.g. a clamped lower bound) does not repeat the feedback.
    internal static bool CrossedHapticReference<TValue>(TValue oldValue, TValue newValue, TValue reference)
        where TValue : INumber<TValue>
        => CrossedReference(oldValue, newValue, TValue.Zero)
            || (reference != TValue.Zero && CrossedReference(oldValue, newValue, reference));

    private static bool CrossedReference<TValue>(TValue oldValue, TValue newValue, TValue reference)
        where TValue : INumber<TValue>
        => (oldValue < reference && newValue >= reference)
            || (oldValue > reference && newValue <= reference);
}
