using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

using Avalonia.Input;

namespace Beutl;

public static class NumberEditorHelper
{
    private static double GetScrubModifierCoefficient(KeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool fine = modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Meta);
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

    // Reads a number typed into an editor in the current culture or with a period as the decimal point. Where the
    // decimal separator is a comma, the culture's own parse took the period for a group separator and read "1.5" as
    // 15, so group separators are allowed only for text neither decimal point reads, such as "1.000,5".
    // An integer takes no decimal point, so "1.000" stays rejected rather than becoming 1.
    internal static bool TryParse<TValue>(ReadOnlySpan<char> text, [MaybeNullWhen(false)] out TValue value)
        where TValue : INumberBase<TValue>
    {
        NumberStyles style = DecimalStyle<TValue>.Value;
        return TValue.TryParse(text, style, CultureInfo.CurrentCulture, out value)
            || TValue.TryParse(text, style, CultureInfo.InvariantCulture, out value)
            || TValue.TryParse(text, CultureInfo.CurrentCulture, out value);
    }

    private static class DecimalStyle<TValue>
        where TValue : INumberBase<TValue>
    {
        public static readonly NumberStyles Value =
            TValue.TryParse("0.5", NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? NumberStyles.Float
                : NumberStyles.Integer;
    }

    // An edit applies when the new text parses and either the old text did not parse or the value changed.
    internal static bool TryParseEdit<TValue>(
        string? newText,
        string? oldText,
        [NotNullWhen(true)] out TValue? newValue,
        [NotNullWhen(true)] out TValue? oldValue)
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

            return newValue != oldValue;
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

    internal static string Format<TValue>(TValue value, string? numberFormat)
        where TValue : INumber<TValue>
    {
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
