namespace Beutl.Graphics.Shaders;

public sealed partial class SkslSource
{
    // 0 until analyzed, then 1 when the program reads its input only where it draws, and 2 otherwise.
    private int _sourceReadState;

    /// <summary>
    /// Whether a whole-source program reads its upstream input only at the point it computes: every
    /// <c>src.eval</c> in it takes the entry point's own coordinate, which nothing changes.
    /// </summary>
    /// <remarks>
    /// Such a program needs of its input only the part of its output a request needs, so the input need not be drawn
    /// whole. The check reads significant tokens and refuses whatever it cannot follow: a sample at any other
    /// coordinate or outside <c>main</c>, <c>src</c> used any other way, the coordinate assigned to or declared again
    /// in <c>main</c>, and an <c>out</c> or <c>inout</c> parameter, through which a call could change it.
    /// </remarks>
    internal bool ReadsSourceOnlyAtEntryCoordinate
    {
        get
        {
            int state = Volatile.Read(ref _sourceReadState);
            if (state == 0)
            {
                state = Kind == ShaderDescriptionKind.WholeSource && ReadsSourceOnlyAtEntryCoordinateCore(Text) ? 1 : 2;
                Volatile.Write(ref _sourceReadState, state);
            }

            return state == 1;
        }
    }

    private static bool ReadsSourceOnlyAtEntryCoordinateCore(string text)
    {
        List<SkslToken> tokens = SkslLexer.Tokenize(text);
        if (!TryFindWholeSourceEntry(tokens, out string coordinate, out int bodyStart, out int bodyEnd))
            return false;

        for (int index = 0; index < tokens.Count; index++)
        {
            SkslToken token = tokens[index];
            if (!token.IsIdentifier)
                continue;

            if (token.Text is "out" or "inout")
                return false;

            bool insideMain = index > bodyStart && index < bodyEnd;
            if (token.Text == "src" && !IsMemberName(tokens, index))
            {
                if (IsSourceDeclaration(tokens, index))
                    continue;
                if (!insideMain || !IsSampleAt(tokens, index, coordinate))
                    return false;

                // Past `src . eval ( coordinate )`.
                index += 5;
                continue;
            }

            if (insideMain
                && token.Text == coordinate
                && !IsMemberName(tokens, index)
                && !IsReadOnlyUse(tokens, index, tokens[bodyStart].ParenthesisDepth))
            {
                return false;
            }
        }

        return true;
    }

    // Finds `half4 main ( float2 coordinate ) {` and the brace that closes its body.
    private static bool TryFindWholeSourceEntry(
        List<SkslToken> tokens,
        out string coordinate,
        out int bodyStart,
        out int bodyEnd)
    {
        coordinate = "";
        bodyStart = -1;
        bodyEnd = -1;
        for (int index = 0; index + 6 < tokens.Count; index++)
        {
            if (tokens[index].Depth != 0
                || tokens[index].Text != "half4"
                || tokens[index + 1].Text != "main"
                || tokens[index + 2].Text != "("
                || tokens[index + 3].Text != "float2"
                || !tokens[index + 4].IsIdentifier
                || tokens[index + 5].Text != ")"
                || tokens[index + 6].Text != "{")
            {
                continue;
            }

            if (bodyStart >= 0)
                return false;

            coordinate = tokens[index + 4].Text;
            bodyStart = index + 6;
        }

        if (bodyStart < 0)
            return false;

        int closingDepth = tokens[bodyStart].BraceDepth - 1;
        for (int index = bodyStart + 1; index < tokens.Count; index++)
        {
            if (tokens[index].Text == "}" && tokens[index].BraceDepth == closingDepth)
            {
                bodyEnd = index;
                return true;
            }
        }

        return false;
    }

    private static bool IsMemberName(List<SkslToken> tokens, int index)
        => index > 0 && tokens[index - 1].Text == ".";

    private static bool IsSourceDeclaration(List<SkslToken> tokens, int index)
        => index >= 2
           && tokens[index - 2].Text == "uniform"
           && tokens[index - 1].Text == "shader"
           && index + 1 < tokens.Count
           && tokens[index + 1].Text == ";";

    private static bool IsSampleAt(List<SkslToken> tokens, int index, string coordinate)
        => index + 5 < tokens.Count
           && tokens[index + 1].Text == "."
           && tokens[index + 2].Text == "eval"
           && tokens[index + 3].Text == "("
           && tokens[index + 4].Text == coordinate
           && tokens[index + 5].Text == ")";

    // Whether a use of the coordinate inside main leaves it as it is: neither a declaration that shadows it nor an
    // assignment, compound assignment, increment or decrement of it or of one of its components.
    private static bool IsReadOnlyUse(List<SkslToken> tokens, int index, int statementParenthesisDepth)
    {
        if (index > 0)
        {
            SkslToken previous = tokens[index - 1];
            if (previous.IsIdentifier && previous.Text != "return")
                return false;
            if (previous.Text == "," && tokens[index].ParenthesisDepth == statementParenthesisDepth)
                return false;
            if (index > 1 && previous.Text is "+" or "-" && tokens[index - 2].Text == previous.Text)
                return false;
        }

        int next = index + 1;
        while (next < tokens.Count)
        {
            if (tokens[next].Text == "." && next + 1 < tokens.Count && tokens[next + 1].IsIdentifier)
            {
                next += 2;
            }
            else if (tokens[next].Text == "[")
            {
                int nesting = 0;
                for (; next < tokens.Count; next++)
                {
                    if (tokens[next].Text == "[")
                        nesting++;
                    else if (tokens[next].Text == "]" && --nesting == 0)
                        break;
                }

                next++;
            }
            else
            {
                break;
            }
        }

        if (next >= tokens.Count)
            return true;

        string first = tokens[next].Text;
        string second = next + 1 < tokens.Count ? tokens[next + 1].Text : "";
        string third = next + 2 < tokens.Count ? tokens[next + 2].Text : "";
        if (first == "=")
            return second == "=";
        if (first is "+" or "-" or "*" or "/" or "%" or "&" or "|" or "^" && second == "=")
            return false;
        if (first is "+" or "-" && second == first)
            return false;
        if (first is "<" or ">" && second == first && third == "=")
            return false;

        return true;
    }
}
