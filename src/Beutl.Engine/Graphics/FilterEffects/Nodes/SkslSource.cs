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

        return new SkslSource(source, SkslSourceKind.Snippet);
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
