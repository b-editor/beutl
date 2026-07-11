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
internal static partial class SkslSnippetMerger
{
    /// <summary>The child-shader name the generated <c>main</c> samples as the fused group's input.</summary>
    public const string SourceChildName = "src";

    // The optional bracket group matches SKSL fixed-size array uniforms (`uniform float lut[4];`).
    [GeneratedRegex(@"uniform\s+[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[\s*\d+\s*\])?\s*;")]
    private static partial Regex UniformDeclarationRegex();

    // A file-scope const (`const float3 LUMA = ...`), tolerating leading whitespace. Applied only to lines that
    // start at brace depth 0 (see <see cref="EnumerateTopLevelLines"/>), so function-local consts are left alone.
    [GeneratedRegex(@"^[ \t]*const\s+[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ConstDeclarationRegex();

    // A top-level function definition (`float3 apply(...)`, `int modInt(...)`), tolerating leading whitespace.
    // Applied only to lines starting at brace depth 0, so a body call/control statement (`return foo(c);`, `if (...)`)
    // — which lives inside the function body at depth > 0 — is never mistaken for a definition.
    [GeneratedRegex(@"^[ \t]*[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex FunctionDefinitionRegex();

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
        var names = new HashSet<string>(StringComparer.Ordinal);

        // Uniforms only ever appear at file scope, so an unanchored match is safe regardless of indentation.
        foreach (Match match in UniformDeclarationRegex().Matches(source))
            names.Add(match.Groups[1].Value);

        // Const and function definitions are file-scope only; classify by the brace depth at each line's start so
        // the leading-whitespace-tolerant matchers see an indented signature/const but never a body statement.
        foreach (string line in EnumerateTopLevelLines(source))
        {
            Match constMatch = ConstDeclarationRegex().Match(line);
            if (constMatch.Success)
                names.Add(constMatch.Groups[1].Value);

            Match functionMatch = FunctionDefinitionRegex().Match(line);
            if (functionMatch.Success)
                names.Add(functionMatch.Groups[1].Value);
        }

        string result = source;
        foreach (string name in names)
        {
            // `(?<!\.)` skips a dot-preceded occurrence: a top-level declaration is never `x.name`, so `.name` is
            // a member/swizzle access (`c.r` when a uniform is named `r`) that must not be renamed. `\b` alone
            // matches after `.`, so without the lookbehind `c.r` would corrupt to `c.fe0_r`.
            result = Regex.Replace(result, $@"(?<!\.)\b{Regex.Escape(name)}\b", prefix + name);
        }

        return result;
    }

    // Yields each line whose start is at brace depth 0 (file scope). A function signature line opens its body brace
    // at or after the yield, so it is classified at depth 0; the body statements it encloses start at depth > 0 and
    // are skipped. Line (`//`) and block (`/* */`) comments are ignored when counting braces.
    private static IEnumerable<string> EnumerateTopLevelLines(string source)
    {
        int depth = 0;
        bool inBlockComment = false;
        foreach (string line in source.Split('\n'))
        {
            bool topLevel = depth == 0 && !inBlockComment;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inBlockComment)
                {
                    if (ch == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    break;

                if (ch == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (ch == '{')
                    depth++;
                else if (ch == '}' && depth > 0)
                    depth--;
            }

            if (topLevel)
                yield return line;
        }
    }
}
