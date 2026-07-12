using System.Text;
using System.Text.RegularExpressions;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Merges a run of adjacent coordinate-invariant SKSL snippets (<c>half4 apply(half4 c)</c>) into one generated
/// whole-source program (feature 004, T024, C1.3/D2). Each snippet <c>N</c> contributes a <c>feN_apply</c>
/// function with its uniforms prefixed <c>feN_</c>; the generated <c>main</c> samples the <c>src</c> child once
/// and chains the <c>apply</c> calls in node order. Merging is an <em>optimization</em> that reduces shader-stage
/// nesting depth and program count; it MUST NOT change ordering or results beyond floating-point tolerance.
/// </summary>
/// <remarks>
/// The executor binds stage <c>N</c>'s uniform <c>name</c> as <c>feN_name</c>, matching the prefix applied here,
/// so the merger never needs the binding list. Every top-level declaration of a snippet — its uniforms, its file-
/// scope <c>const</c>s, and its helper functions (including the <c>apply</c> entry) — is renamed whole-word so two
/// snippets that share a name (two effects with a <c>linearToSrgb</c> helper, a <c>LUMA</c> const, or the same
/// source appearing twice) merge without a redefinition. Function bodies keep their own local names, which are
/// block-scoped in the merged program and cannot collide.
/// </remarks>
internal static class SkslSnippetMerger
{
    /// <summary>The child-shader name the generated <c>main</c> samples as the fused group's input.</summary>
    public const string SourceChildName = "src";

    /// <summary>
    /// Builds the merged whole-source program for <paramref name="snippets"/> (all must be
    /// <see cref="SkslSourceKind.Snippet"/>). The result declares <c>uniform shader src;</c>, the prefixed
    /// snippet functions, and a <c>main</c> chaining them.
    /// </summary>
    public static string Merge(IReadOnlyList<SkslSource> snippets)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        if (snippets.Count == 0)
            throw new ArgumentException("At least one snippet is required.", nameof(snippets));

        var sb = new StringBuilder();
        sb.Append("uniform shader ").Append(SourceChildName).Append(";\n");

        for (int i = 0; i < snippets.Count; i++)
        {
            SkslSource snippet = snippets[i];
            if (snippet.Kind != SkslSourceKind.Snippet)
                throw new ArgumentException("Only snippet sources can be merged.", nameof(snippets));

            sb.Append(Prefix(snippet.Source, i)).Append('\n');
        }

        sb.Append("half4 main(float2 coord) {\n");
        sb.Append("    half4 _fused = ").Append(SourceChildName).Append(".eval(coord);\n");
        for (int i = 0; i < snippets.Count; i++)
        {
            sb.Append("    _fused = fe").Append(i).Append("_apply(_fused);\n");
        }

        sb.Append("    return _fused;\n}\n");
        return sb.ToString();
    }

    // Renames every top-level name a snippet introduces (uniforms, file-scope consts, helper functions incl.
    // apply) to feN_ so no two snippets in a merged program can redefine the same global. Whole-word replacement
    // keeps names that are prefixes of one another (lut vs lutSize, contrast vs contrastPivot) independent.
    private static string Prefix(string source, int index)
    {
        string prefix = $"fe{index}_";
        string result = source;
        foreach (string name in CollectTopLevelNames(source))
        {
            // `(?<!\.)` skips a dot-preceded occurrence: a top-level declaration is never `x.name`, so `.name` is
            // a member/swizzle access (`c.r` when a uniform is named `r`) that must not be renamed. `\b` alone
            // matches after `.`, so without the lookbehind `c.r` would corrupt to `c.fe0_r`. Struct bodies need no
            // exclusion: SkSL rejects top-level structs via SkslSource.Snippet and function-local struct statements
            // in its own front end (canary-pinned by SkslSnippetMergerTests), so no renamable snippet contains one.
            result = Regex.Replace(result, $@"(?<!\.)\b{Regex.Escape(name)}\b", prefix + name);
        }

        return result;
    }

    // Collects the names a snippet declares at file scope from a comment-skipping token scan rather than per-line
    // regexes: a declaration split across lines (`float3\nhelper(...)`) or a second definition after a same-line
    // body close (`} half4 tint(`) has no single line matching a signature pattern, and a missed name redefines in
    // the merged program. At brace depth 0, `uniform`/`const` TYPE NAME declares NAME; IDENT IDENT `(` is a
    // function definition; and IDENT IDENT followed by `=`/`;`/`[` is a MUTABLE global — SkSL accepts those, and an
    // unrenamed one shared by two snippets redefines in the merged program even though each compiles standalone
    // (statements only exist inside bodies at depth > 0, so calls/locals can't be mistaken for declarations).
    private static HashSet<string> CollectTopLevelNames(string source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new List<(string Text, bool IsIdent, int Depth)>();

        int depth = 0;
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

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i + 1 < source.Length && (char.IsLetterOrDigit(source[i + 1]) || source[i + 1] == '_'))
                    i++;
                tokens.Add((source[start..(i + 1)], true, depth));
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}' && depth > 0)
                depth--;
            tokens.Add((c.ToString(), false, depth));
        }

        for (int t = 0; t + 1 < tokens.Count; t++)
        {
            if (!tokens[t].IsIdent || tokens[t].Depth != 0)
                continue;

            if (tokens[t].Text is "uniform" or "const")
            {
                if (t + 2 < tokens.Count && tokens[t + 1].IsIdent && tokens[t + 2].IsIdent)
                    names.Add(tokens[t + 2].Text);
                t += 2;
            }
            else if (tokens[t + 1].IsIdent && t + 2 < tokens.Count && tokens[t + 2].Text is "(" or "=" or ";" or "[")
            {
                names.Add(tokens[t + 1].Text);
                t++;
            }
        }

        return names;
    }
}
