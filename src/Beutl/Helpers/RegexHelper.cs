using System.Text.RegularExpressions;

namespace Beutl;

internal static partial class RegexHelper
{
    public static Regex[] CreateRegexes(string pattern)
    {
        return pattern.ToUpperInvariant()
            .Split(' ')
            .Select(i => i.Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => MyRegex().Replace(i, m =>
            {
                string s = m.Value;
                if (s.Equals("?"))
                {
                    return ".";
                }
                else if (s.Equals("*"))
                {
                    return ".*";
                }
                else
                {
                    return Regex.Escape(s);
                }
            }))
            .Select(i => new Regex(i))
            .ToArray();
    }

    public static bool IsMatch(Regex[] regices, string str)
    {
        string upper = str.ToUpperInvariant();
        bool result = false;

        foreach (Regex item in regices)
        {
            result |= item.IsMatch(upper);
        }

        return result;
    }

    [GeneratedRegex(".")]
    private static partial Regex MyRegex();
}
