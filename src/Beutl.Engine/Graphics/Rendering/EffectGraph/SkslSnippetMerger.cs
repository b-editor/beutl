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
/// so the merger never needs the binding list. Uniform renaming is whole-word; a snippet whose uniform name
/// collides with the <c>apply</c> parameter name is an authoring foot-gun (documented) — built-in snippets and
/// the tests use distinct names.
/// </remarks>
internal static partial class SkslSnippetMerger
{
    /// <summary>The child-shader name the generated <c>main</c> samples as the fused group's input.</summary>
    public const string SourceChildName = "src";

    [GeneratedRegex(@"uniform\s+[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*;")]
    private static partial Regex UniformDeclarationRegex();

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

    // Prefixes uniform declarations/references with feN_ and renames apply -> feN_apply for one snippet.
    private static string Prefix(string source, int index)
    {
        string prefix = $"fe{index}_";
        string result = source;

        foreach (Match match in UniformDeclarationRegex().Matches(source))
        {
            string name = match.Groups[1].Value;
            result = Regex.Replace(result, $@"\b{Regex.Escape(name)}\b", prefix + name);
        }

        return Regex.Replace(result, @"\bapply\b", prefix + "apply");
    }
}
