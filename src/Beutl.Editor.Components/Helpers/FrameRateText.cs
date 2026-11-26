using System.Globalization;
using Beutl.Utilities;

namespace Beutl.Editor.Components.Helpers;

internal static class FrameRateText
{
    public static IReadOnlyList<string> Presets { get; } = ["23.976", "24", "25", "29.97", "30", "50", "59.94", "60"];

    public static string Format(Rational rate)
    {
        if (rate is not { Numerator: > 0, Denominator: > 0 })
            return "30";

        return rate.Denominator == 1
            ? rate.Numerator.ToString(CultureInfo.InvariantCulture)
            : rate.ToString();
    }

    public static string? Validate(string? text)
        => Parse(text) switch
        {
            null => MessageStrings.InvalidString,
            { Numerator: > 0, Denominator: > 0 } => null,
            _ => MessageStrings.ValueLessThanOrEqualToZero,
        };

    // A whole number, a fraction such as 30000/1001, or a decimal; a decimal near an NTSC rate (29.97) is that rate.
    public static Rational? Parse(string? text)
    {
        text = text?.Trim();
        if (!Rational.TryParse(text, CultureInfo.InvariantCulture, out Rational rate))
        {
            if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
                || value.Scale > 6
                || Math.Abs(value) > 1_000_000)
            {
                return null;
            }

            decimal ntsc = Math.Round(value * 1.001m);
            long scale = (long)Math.Pow(10, value.Scale);
            rate = value != decimal.Truncate(value) && Math.Abs((ntsc / 1.001m) - value) < 0.005m
                ? new Rational((long)ntsc * 1000, 1001)
                : new Rational((long)(value * scale), scale);
        }

        // Reduced, since a whole rate such as 60/2 is written as its numerator.
        long divisor = Math.Max(1, MathUtilities.GreatestCommonDivisor(Math.Abs(rate.Numerator), Math.Abs(rate.Denominator)));
        return new Rational(rate.Numerator / divisor, rate.Denominator / divisor);
    }
}
