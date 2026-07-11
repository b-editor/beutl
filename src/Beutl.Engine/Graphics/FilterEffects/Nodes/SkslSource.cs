using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.Graphics.Effects;

/// <summary>Whether an <see cref="SkslSource"/> is a fusable per-pixel snippet or a standalone whole-source shader.</summary>
public enum SkslSourceKind
{
    /// <summary>A <c>half4 apply(half4 c)</c> snippet: coordinate-invariant, mergeable into a fused program.</summary>
    Snippet,

    /// <summary>A whole shader defining <c>half4 main(float2 coord)</c> with a <c>src</c> child (today's <see cref="SKSLShader"/> convention).</summary>
    WholeSource,
}

/// <summary>
/// An identity-hashable SKSL source string (feature 004, data-model §1). The <see cref="IdentityHash"/> is a
/// stable content hash used as the structural-key contribution and (in a later step) as the program-cache key,
/// so two effects sharing a source string share a compiled program. The source is treated as <em>structure</em>:
/// authors must never bake parameter values into it (A4) or the program cache and structural key are defeated.
/// </summary>
public sealed partial record SkslSource
{
    // A uniform declaration whose declarator list continues past the first name (`uniform float a, b;`). The snippet
    // merger prefixes uniforms by name (feN_) one declarator at a time, so a multi-declarator list would leave the
    // trailing names unprefixed — silently binding them wrong in a fused program (A2). Rejected at snippet construction.
    [GeneratedRegex(@"uniform\s+[A-Za-z_][A-Za-z0-9_]*\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:\[\s*\d+\s*\])?\s*,")]
    private static partial Regex MultiDeclaratorUniformRegex();

    private SkslSource(string source, SkslSourceKind kind)
    {
        Source = source;
        Kind = kind;
        IdentityHash = ComputeHash(source);
    }

    /// <summary>The SKSL text.</summary>
    public string Source { get; }

    /// <summary>Whether this is a fusable snippet or a standalone whole-source shader.</summary>
    public SkslSourceKind Kind { get; }

    /// <summary>A stable 64-bit content hash (hex) of <see cref="Source"/>. Equal sources hash equal on every run and machine.</summary>
    public string IdentityHash { get; }

    /// <summary>
    /// Wraps a <c>half4 apply(half4 c)</c> snippet source. Must be non-empty. Each uniform must be declared in its
    /// own statement (single declarator): a comma-separated list is rejected because it would escape the fused
    /// snippet merger's per-name prefixing (A2).
    /// </summary>
    public static SkslSource Snippet(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (MultiDeclaratorUniformRegex().IsMatch(source))
        {
            throw new ArgumentException(
                "A fusable snippet must declare each uniform in its own statement (e.g. 'uniform float a; "
                + "uniform float b;'), not as a comma-separated list ('uniform float a, b;'): the snippet merger "
                + "prefixes uniforms by name (feN_) and a multi-declarator list escapes prefixing, silently "
                + "binding the trailing uniforms wrong in a fused program (contract A2).",
                nameof(source));
        }

        if (HasTopLevelMultiDeclaratorConst(source))
        {
            throw new ArgumentException(
                "A fusable snippet must declare each top-level const in its own statement (e.g. 'const float A = "
                + "1.0; const float B = 2.0;'), not as a comma-separated list ('const float A = 1.0, B = 2.0;'): "
                + "the snippet merger prefixes top-level consts by name (feN_) one declarator at a time, so a "
                + "multi-declarator list leaves the trailing consts unprefixed, silently colliding them in a fused "
                + "program (contract A2). Function-local consts are unaffected.",
                nameof(source));
        }

        if (HasTopLevelStruct(source))
        {
            throw new ArgumentException(
                "A fusable snippet must not declare a top-level struct: the snippet merger prefixes uniforms and "
                + "consts by name (feN_) but does NOT rename a struct TYPE, so two fused snippets each declaring a "
                + "struct of the same name would collide in the merged program (contract A2). Function-local structs "
                + "are unaffected; a whole-source shader is exempt (it is never merged).",
                nameof(source));
        }

        return new SkslSource(source, SkslSourceKind.Snippet);
    }

    // A comma at declarator level (paren/brace depth 0) inside a file-scope (brace depth 0) `const` statement marks
    // a multi-declarator list (`const float A = 1.0, B = 2.0;`). Commas inside an initializer's parens/brackets/
    // braces (`float3(1, 2, 3)`, `{1, 2}`) are not separators; function-local consts (brace depth > 0) are
    // block-scoped and left alone. Line/block comments are skipped.
    private static bool HasTopLevelMultiDeclaratorConst(string source)
    {
        int braceDepth = 0;
        int groupDepth = 0;
        bool inConst = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];

            if (inLineComment)
            {
                if (c == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (!inConst)
            {
                if (c == '{')
                    braceDepth++;
                else if (c == '}' && braceDepth > 0)
                    braceDepth--;
                else if (braceDepth == 0 && IsKeywordAt(source, i, "const"))
                {
                    inConst = true;
                    groupDepth = 0;
                    i += 4;
                }

                continue;
            }

            if (c is '(' or '[' or '{')
            {
                groupDepth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (groupDepth > 0)
                    groupDepth--;
            }
            else if (c == ';')
            {
                inConst = false;
            }
            else if (c == ',' && groupDepth == 0)
            {
                return true;
            }
        }

        return false;
    }

    // A `struct` declared at file scope (brace depth 0) in a snippet. The merger prefixes uniform/const names by
    // (feN_) but does NOT rename a struct TYPE, so two fused snippets each declaring a top-level struct of the same
    // name collide in the merged program (A2). A function-local struct (brace depth > 0) is block-scoped and left
    // alone. Line/block comments are skipped.
    private static bool HasTopLevelStruct(string source)
    {
        int braceDepth = 0;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];

            if (inLineComment)
            {
                if (c == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '{')
                braceDepth++;
            else if (c == '}' && braceDepth > 0)
                braceDepth--;
            else if (braceDepth == 0 && IsKeywordAt(source, i, "struct"))
                return true;
        }

        return false;
    }

    private static bool IsKeywordAt(string s, int i, string keyword)
    {
        if (i + keyword.Length > s.Length || !s.AsSpan(i, keyword.Length).SequenceEqual(keyword))
            return false;
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_'))
            return false;
        int after = i + keyword.Length;
        return after >= s.Length || (!char.IsLetterOrDigit(s[after]) && s[after] != '_');
    }

    /// <summary>Wraps a whole-source shader defining <c>half4 main(float2 coord)</c> with a <c>src</c> child. Must be non-empty.</summary>
    public static SkslSource WholeSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new SkslSource(source, SkslSourceKind.WholeSource);
    }

    // FNV-1a over UTF-8 bytes: deterministic across runs/machines (unlike string.GetHashCode's randomized seed).
    private static string ComputeHash(string source)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte b in Encoding.UTF8.GetBytes(source))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash.ToString("x16");
    }
}
