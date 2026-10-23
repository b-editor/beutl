using System.Globalization;

using Avalonia;

using Beutl.Utilities;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The checks the new project and new scene dialogs share: a folder name that can still be
/// created where it goes, a frame that has a size, positive rates, and the first free default name.
/// </summary>
internal static class NewDocumentValidation
{
    public static string? ValidateName(string? name, string location)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
        {
            return MessageStrings.InvalidString;
        }
        else if (Directory.Exists(Path.Combine(location, name)))
        {
            return MessageStrings.AlreadyExists;
        }
        else
        {
            return null;
        }
    }

    public static string? ValidateSize(PixelSize size)
        => size.Width <= 0 || size.Height <= 0
            ? MessageStrings.ValueLessThanOrEqualToZero
            : null;

    public static string? ValidatePositive(int value)
        => value <= 0 ? MessageStrings.ValueLessThanOrEqualToZero : null;

    public static string? ValidateFrameRate(string? text)
        => ParseFrameRate(text) switch
        {
            null => MessageStrings.InvalidString,
            { Numerator: > 0, Denominator: > 0 } => null,
            _ => MessageStrings.ValueLessThanOrEqualToZero,
        };

    // A whole number, a fraction such as 30000/1001, or a decimal; a decimal near an NTSC rate (29.97) is that rate.
    public static Rational? ParseFrameRate(string? text)
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

    // Whether the folder can be created and the frame has a size. Nothing can be created
    // without a name and a location.
    public static bool CanCreateAt(string? name, string? location, PixelSize size)
        => location != null
            && name != null
            && !Directory.Exists(Path.Combine(location, name))
            && size.Width > 0
            && size.Height > 0;

    // The first of baseName1, baseName2, ... that is not a folder in location yet.
    public static string UniqueName(string location, string baseName)
    {
        int n = 1;

        while (Directory.Exists(Path.Combine(location, baseName + n)))
        {
            n++;
        }

        return baseName + n;
    }
}
