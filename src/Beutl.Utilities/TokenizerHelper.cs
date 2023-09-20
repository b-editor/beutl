using System.Globalization;

namespace Beutl.Utilities;

public static class TokenizerHelper
{
    public const char DefaultSeparatorChar = ',';

    public static char GetSeparatorFromFormatProvider(IFormatProvider? provider)
    {
        provider ??= CultureInfo.InvariantCulture;
        char c = DefaultSeparatorChar;

        var formatInfo = NumberFormatInfo.GetInstance(provider);
        if (formatInfo.NumberDecimalSeparator.Length > 0 && c == formatInfo.NumberDecimalSeparator[0])
        {
            c = ';';
        }

        return c;
    }
}
