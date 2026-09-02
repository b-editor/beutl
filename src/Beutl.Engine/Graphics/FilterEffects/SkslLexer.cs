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
                i = SkipBlockComment(source, i);
                continue;
            }

            if (char.IsWhiteSpace(c))
                continue;

            if (char.IsDigit(c) || c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                int start = i;
                if (c == '0'
                    && i + 2 < source.Length
                    && source[i + 1] is 'x' or 'X'
                    && Uri.IsHexDigit(source[i + 2]))
                {
                    i += 2;
                    while (i + 1 < source.Length && Uri.IsHexDigit(source[i + 1]))
                        i++;
                }
                else if (c == '.')
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

                if (i + 1 < source.Length && source[i + 1] is 'f' or 'F' or 'h' or 'H' or 'u' or 'U')
                    i++;

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
                i = SkipBlockComment(source, i);
                result.Append(' ');
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns the index of the comment's closing slash, or the end of the source when it is never closed.
    /// </summary>
    /// <remarks>
    /// An unterminated block comment is a syntax error, and reporting it belongs to the shading-language
    /// compiler, which sees the source with its comments intact and can name a line. This scanner also runs
    /// on the render path while an effect resource compiles its script, where throwing would fail the frame
    /// rather than surface a message on the effect, so it consumes the rest of the source instead.
    /// </remarks>
    private static int SkipBlockComment(string source, int start)
    {
        int i = start + 2;
        while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
            i++;

        return i + 1 >= source.Length ? source.Length : i + 1;
    }
}
