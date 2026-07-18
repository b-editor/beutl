using System.Buffers;
using System.Runtime.CompilerServices;
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
/// stable content hash used to select a program-cache bucket; cache and structural equality also compare the full
/// source text and <see cref="Kind"/>, so a 64-bit collision cannot alias programs. The source is treated as
/// <em>structure</em>: authors must never bake parameter values into it (A4) or the caches are defeated.
/// </summary>
public sealed partial record SkslSource
{
    // A uniform declaration whose declarator list continues past the first name (`uniform float a, b;`). The snippet
    // merger prefixes uniforms by name (feN_) one declarator at a time, so a multi-declarator list would leave the
    // trailing names unprefixed — silently binding them wrong in a fused program (A2). Rejected at snippet construction.
    [GeneratedRegex(@"uniform\s+(?:(?:lowp|mediump|highp)\s+)?[A-Za-z_][A-Za-z0-9_]*"
        + @"(?:\s*\[[^\]]*\])*\s+"
        + @"[A-Za-z_][A-Za-z0-9_]*\s*(?:\s*\[[^\]]*\])*\s*,")]
    private static partial Regex MultiDeclaratorUniformRegex();

    [GeneratedRegex(@"\buniform\s+(?:(?:lowp|mediump|highp)\s+)?"
        + @"(?<type>[A-Za-z_][A-Za-z0-9_]*)"
        + @"(?<array>\s*\[\s*(?<extent>[^\]]*)\s*\])*\s+"
        + @"[A-Za-z_][A-Za-z0-9_]*"
        + @"(?<array>\s*\[\s*(?<extent>[^\]]*)\s*\])*\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex UniformDeclarationRegex();

    private static readonly ConditionalWeakTable<string, SkslSource> s_snippets = new();
    private static readonly ConditionalWeakTable<string, SkslSource> s_wholeSources = new();
    private static readonly ConditionalWeakTable<SkslSource, UniformMetadata> s_uniformMetadata = new();

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

    /// <summary>
    /// A stable 64-bit content hash (hex) of <see cref="Source"/>. Equal sources hash equal on every run and machine;
    /// consumers must still compare <see cref="Source"/> and <see cref="Kind"/> before treating a match as identity.
    /// </summary>
    public string IdentityHash { get; }

    /// <summary>Number of four-component uniform vectors consumed by this source.</summary>
    internal int UniformVectorCount
        => s_uniformMetadata.GetValue(this, static source => new(ComputeUniformVectorCount(source.Source))).Count;

    /// <summary>
    /// Wraps a <c>half4 apply(half4 c)</c> snippet source. Must be non-empty. Each uniform must be declared in its
    /// own statement (single declarator): a comma-separated list is rejected because it would escape the fused
    /// snippet merger's per-name prefixing (A2).
    /// </summary>
    public static SkslSource Snippet(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return s_snippets.GetValue(source, static text => CreateSnippet(text));
    }

    private static SkslSource CreateSnippet(string source)
    {
        // Comments may legally mention a rejected form ('// uniform float a, b; is not allowed'), so every
        // structural check runs over a comment-stripped copy.
        string stripped = SkslLexer.StripComments(source);
        List<SkslToken> tokens = SkslLexer.Tokenize(source);
        if (MultiDeclaratorUniformRegex().IsMatch(stripped))
        {
            throw new ArgumentException(
                "A fusable snippet must declare each uniform in its own statement (e.g. 'uniform float a; "
                + "uniform float b;'), not as a comma-separated list ('uniform float a, b;'): the snippet merger "
                + "prefixes uniforms by name (feN_) and a multi-declarator list escapes prefixing, silently "
                + "binding the trailing uniforms wrong in a fused program (contract A2).",
                nameof(source));
        }

        if (HasTopLevelMultiDeclaratorConst(tokens))
        {
            throw new ArgumentException(
                "A fusable snippet must declare each top-level const in its own statement (e.g. 'const float A = "
                + "1.0; const float B = 2.0;'), not as a comma-separated list ('const float A = 1.0, B = 2.0;'): "
                + "the snippet merger prefixes top-level consts by name (feN_) one declarator at a time, so a "
                + "multi-declarator list leaves the trailing consts unprefixed, silently colliding them in a fused "
                + "program (contract A2). Function-local consts are unaffected.",
                nameof(source));
        }

        if (HasTopLevelStruct(tokens))
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
    // block-scoped and left alone, and a `const`-qualified function PARAMETER (paren depth > 0) is not a
    // declaration — its parameter-list comma must not read as a declarator list. Expects comment-stripped input.
    private static bool HasTopLevelMultiDeclaratorConst(IReadOnlyList<SkslToken> tokens)
    {
        int groupDepth = 0;
        bool inConst = false;

        foreach (SkslToken token in tokens)
        {
            if (!inConst)
            {
                if (token is { IsIdentifier: true, Text: "const", Depth: 0 })
                {
                    inConst = true;
                    groupDepth = 0;
                }

                continue;
            }

            if (token.Text is "(" or "[" or "{")
            {
                groupDepth++;
            }
            else if (token.Text is ")" or "]" or "}")
            {
                if (groupDepth > 0)
                    groupDepth--;
            }
            else if (token.Text == ";" && groupDepth == 0)
            {
                inConst = false;
            }
            else if (token.Text == "," && groupDepth == 0)
            {
                return true;
            }
        }

        return false;
    }

    // A `struct` declared at file scope (brace depth 0) in a snippet. The merger prefixes uniform/const names by
    // (feN_) but does NOT rename a struct TYPE, so two fused snippets each declaring a top-level struct of the same
    // name collide in the merged program (A2). A function-local struct (brace depth > 0) is block-scoped and left
    // alone. Expects comment-stripped input.
    private static bool HasTopLevelStruct(IReadOnlyList<SkslToken> tokens)
        => tokens.Any(static token => token is { IsIdentifier: true, Text: "struct", Depth: 0 });

    /// <summary>Wraps a whole-source shader defining <c>half4 main(float2 coord)</c> with a <c>src</c> child. Must be non-empty.</summary>
    public static SkslSource WholeSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return s_wholeSources.GetValue(
            source, static text => new SkslSource(text, SkslSourceKind.WholeSource));
    }

    private static int ComputeUniformVectorCount(string source)
    {
        string stripped = SkslLexer.StripComments(source);
        long total = 0;
        foreach (Match match in UniformDeclarationRegex().Matches(stripped))
        {
            string type = match.Groups["type"].Value;
            if (type is "shader" or "colorFilter" or "blender")
                continue;

            long vectors = UniformTypeVectors(type);
            foreach (Capture extent in match.Groups["extent"].Captures)
            {
                if (!int.TryParse(extent.Value.Trim(), out int count))
                    return int.MaxValue;

                vectors *= count;
                if (vectors >= int.MaxValue)
                    return int.MaxValue;
            }

            total += vectors;
            if (total >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)total;
    }

    private static int UniformTypeVectors(string type)
    {
        if (type is "mat2" or "mat3" or "mat4")
            return type[^1] - '0';

        Match matrix = Regex.Match(
            type, @"^(?:half|float)(?<columns>[2-4])x[2-4]$", RegexOptions.CultureInvariant);
        return matrix.Success && int.TryParse(matrix.Groups["columns"].Value, out int columns)
            ? columns
            : 1;
    }

    private sealed record UniformMetadata(int Count);

    // FNV-1a over UTF-8 bytes: deterministic across runs/machines (unlike string.GetHashCode's randomized seed).
    private static string ComputeHash(string source)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        const int StackBufferSize = 512;
        int byteCount = Encoding.UTF8.GetByteCount(source);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= StackBufferSize
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));

        ulong hash = offset;
        try
        {
            int written = Encoding.UTF8.GetBytes(source, bytes);
            foreach (byte b in bytes[..written])
            {
                hash ^= b;
                hash *= prime;
            }
        }
        finally
        {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }

        return hash.ToString("x16");
    }
}
