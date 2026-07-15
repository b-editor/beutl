using System.Text;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Shared significant-token scanner for the validation and alpha-renaming sides of the SKSL authoring contract.
/// Keeping comment handling, identifier boundaries, and scope depth here prevents accepted source from being
/// interpreted differently when snippets are merged.
/// </summary>
internal static class SkslLexer
{
    internal static List<SkslToken> Tokenize(string source)
    {
        var tokens = new List<SkslToken>();
        int braceDepth = 0;
        int parenthesisDepth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    i++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
                continue;

            if (char.IsDigit(c) || c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                int start = i;
                if (c == '.')
                {
                    while (i + 1 < source.Length && char.IsDigit(source[i + 1]))
                        i++;
                }
                else
                {
                    while (i + 1 < source.Length && char.IsDigit(source[i + 1]))
                        i++;
                    if (i + 1 < source.Length && source[i + 1] == '.')
                    {
                        i++;
                        while (i + 1 < source.Length && char.IsDigit(source[i + 1]))
                            i++;
                    }
                }

                if (i + 1 < source.Length && source[i + 1] is 'e' or 'E')
                {
                    int exponentEnd = i + 2;
                    if (exponentEnd < source.Length && source[exponentEnd] is '+' or '-')
                        exponentEnd++;
                    int exponentDigits = exponentEnd;
                    while (exponentEnd < source.Length && char.IsDigit(source[exponentEnd]))
                        exponentEnd++;
                    if (exponentEnd > exponentDigits)
                        i = exponentEnd - 1;
                }

                tokens.Add(new SkslToken(
                    source[start..(i + 1)], false, braceDepth, parenthesisDepth, start, i + 1 - start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i + 1 < source.Length && (char.IsLetterOrDigit(source[i + 1]) || source[i + 1] == '_'))
                    i++;
                tokens.Add(new SkslToken(
                    source[start..(i + 1)], true, braceDepth, parenthesisDepth, start, i + 1 - start));
                continue;
            }

            if (c == '{')
                braceDepth++;
            else if (c == '}' && braceDepth > 0)
                braceDepth--;
            else if (c == '(')
                parenthesisDepth++;
            else if (c == ')' && parenthesisDepth > 0)
                parenthesisDepth--;

            tokens.Add(new SkslToken(c.ToString(), false, braceDepth, parenthesisDepth, i, 1));
        }

        return tokens;
    }

    // Regex-based uniform metadata still needs a comment-free source. Preserve whitespace at comment boundaries so
    // removing a comment cannot join two identifiers into a token the significant-token scanner would never emit.
    internal static string StripComments(string source)
    {
        var result = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;
                if (i < source.Length)
                    result.Append('\n');
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    i++;
                i++;
                result.Append(' ');
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }
}

internal readonly record struct SkslToken(
    string Text,
    bool IsIdentifier,
    int BraceDepth,
    int ParenthesisDepth,
    int Start,
    int Length)
{
    public int Depth => BraceDepth + ParenthesisDepth;
}
