using System.Buffers;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.Graphics.Shaders;

/// <summary>Provides normalized SkSL source that passed Beutl's description-level contract checks.</summary>
/// <remarks>
/// Instances are created by <see cref="ShaderDescription.CurrentPixel"/> and
/// <see cref="ShaderDescription.WholeSource"/>. The source model is immutable. These checks are not a complete SkSL
/// compiler; backend program validation may still reject a source during execution.
/// </remarks>
public sealed partial class SkslSource
{
    [GeneratedRegex(
        @"\buniform\s+(?:(?:lowp|mediump|highp)\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<array>\s*\[\s*(?<extent>[^\]]*)\s*\])?\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex UniformRegex();

    [GeneratedRegex(
        @"\buniform\s+(?:(?:lowp|mediump|highp)\s+)?[A-Za-z_][A-Za-z0-9_]*(?:\s*\[[^\]]*\])*\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*\[[^\]]*\])*\s*,",
        RegexOptions.CultureInvariant)]
    private static partial Regex MultiDeclaratorUniformRegex();

    [GeneratedRegex(
        @"\bhalf4\s+apply\s*\(\s*half4\s+(?<parameter>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentPixelEntryRegex();

    [GeneratedRegex(
        @"\bhalf4\s+main\s*\(\s*float2\s+(?<parameter>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex WholeSourceEntryRegex();

    private readonly IReadOnlyDictionary<string, SkslUniformDeclaration> _uniforms;
    private readonly IReadOnlySet<string>? _topLevelSymbols;

    /// <summary>Parses and validates SkSL for a current-pixel stage.</summary>
    /// <param name="source">
    /// Non-null SkSL defining exactly one <c>half4 apply(half4 color)</c> entry point.
    /// </param>
    /// <returns>An immutable parsed source that any number of descriptions can share.</returns>
    /// <remarks>
    /// Parsing once and reusing the result keeps a description's recording free of re-tokenization, which is
    /// what the engine's own effects do with their compile-time constant sources.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The source grammar, entry point, or declarations are invalid.
    /// </exception>
    public static SkslSource CurrentPixel(string source)
        => new(source, ShaderDescriptionKind.CurrentPixel);

    /// <summary>Parses and validates SkSL for a whole-source stage.</summary>
    /// <param name="source">
    /// Non-null SkSL defining exactly one <c>half4 main(float2 coord)</c> entry point and declaring the
    /// implicit upstream input as <c>uniform shader src;</c>.
    /// </param>
    /// <returns>An immutable parsed source that any number of descriptions can share.</returns>
    /// <inheritdoc cref="CurrentPixel(string)" path="/remarks|/exception"/>
    public static SkslSource WholeSource(string source)
        => new(source, ShaderDescriptionKind.WholeSource);

    internal SkslSource(string text, ShaderDescriptionKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalized = Normalize(text);
        List<SkslToken> tokens = SkslLexer.Tokenize(normalized);
        ValidateBalancedTokens(tokens);
        if (kind == ShaderDescriptionKind.CurrentPixel)
        {
            CurrentPixelValidationResult validation = new CurrentPixelValidator(tokens).Validate();
            _uniforms = validation.Uniforms;
            _topLevelSymbols = validation.TopLevelSymbols;
        }
        else
        {
            _uniforms = ParseUniforms(normalized);
            ValidateWholeSourceEntryPoint(normalized);
            ValidateWholeSourceReservedDeclarations(tokens);
        }

        Text = normalized;
        Kind = kind;
        IdentityHash = ComputeHash(normalized);
    }

    /// <summary>Gets the contract-checked source normalized to LF line endings with one trailing newline.</summary>
    public string Text { get; }

    /// <summary>Gets a deterministic, non-cryptographic hash of the normalized source.</summary>
    /// <remarks>
    /// The hash is a convenience value, not a unique identity. Do not use it as the sole equality key; the renderer's
    /// own reuse contract compares the complete normalized source and the remaining structural metadata.
    /// </remarks>
    public string IdentityHash { get; }

    /// <summary>Gets the entry-point and execution contract validated for this source.</summary>
    public ShaderDescriptionKind Kind { get; }

    internal IReadOnlyDictionary<string, SkslUniformDeclaration> Uniforms => _uniforms;

    internal IReadOnlySet<string> TopLevelSymbols
        => _topLevelSymbols
           ?? throw new InvalidOperationException(
               "Top-level symbol metadata is available only for CurrentPixel sources.");

    internal static bool HasCurrentPixelEntryPoint(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return CurrentPixelEntryRegex().IsMatch(SkslLexer.StripComments(source));
    }

    internal static bool HasUniformDeclaration(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string stripped = SkslLexer.StripComments(source);
        foreach (Match match in UniformRegex().Matches(stripped))
        {
            if (match.Groups["name"].Value == name)
                return true;
        }

        return false;
    }

    private static string Normalize(string source)
    {
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        return normalized + "\n";
    }

    private static void ValidateBalancedTokens(IReadOnlyList<SkslToken> tokens)
    {
        int braces = 0;
        int parentheses = 0;
        int brackets = 0;
        foreach (SkslToken token in tokens)
        {
            switch (token.Text)
            {
                case "{":
                    braces++;
                    break;
                case "}":
                    braces--;
                    break;
                case "(":
                    parentheses++;
                    break;
                case ")":
                    parentheses--;
                    break;
                case "[":
                    brackets++;
                    break;
                case "]":
                    brackets--;
                    break;
            }

            if (braces < 0 || parentheses < 0 || brackets < 0)
                throw new ArgumentException("The SkSL source has unbalanced delimiters.", "source");
        }

        if (braces != 0 || parentheses != 0 || brackets != 0)
            throw new ArgumentException("The SkSL source has unbalanced delimiters.", "source");
    }

    private static IReadOnlyDictionary<string, SkslUniformDeclaration> ParseUniforms(string source)
    {
        string stripped = SkslLexer.StripComments(source);
        if (MultiDeclaratorUniformRegex().IsMatch(stripped))
        {
            throw new ArgumentException(
                "Each shader uniform must use its own declaration so binding names can be rewritten safely.",
                nameof(source));
        }

        var result = new Dictionary<string, SkslUniformDeclaration>(StringComparer.Ordinal);
        foreach (Match match in UniformRegex().Matches(stripped))
        {
            string name = match.Groups["name"].Value;
            string type = match.Groups["type"].Value;
            int? extent = null;
            if (match.Groups["array"].Success)
            {
                string value = match.Groups["extent"].Value.Trim();
                if (!int.TryParse(value, out int parsed) || parsed <= 0)
                    throw new ArgumentException("Shader uniform arrays require a positive fixed extent.", nameof(source));
                extent = parsed;
            }

            if (SkslSnippetMerger.IsRendererGeneratedName(name) || IsFilterEffectBindingName(name))
            {
                throw new ArgumentException($"The shader binding name '{name}' is reserved by the renderer.", nameof(source));
            }

            if (!result.TryAdd(name, new SkslUniformDeclaration(type, extent)))
                throw new ArgumentException($"The shader declares duplicate binding '{name}'.", nameof(source));
        }

        return new ReadOnlyDictionary<string, SkslUniformDeclaration>(result);
    }

    private static void ValidateWholeSourceEntryPoint(string source)
    {
        string stripped = SkslLexer.StripComments(source);
        MatchCollection currentEntries = CurrentPixelEntryRegex().Matches(stripped);
        MatchCollection wholeEntries = WholeSourceEntryRegex().Matches(stripped);
        if (wholeEntries.Count != 1 || currentEntries.Count != 0)
        {
            throw new ArgumentException(
                "A WholeSource shader must define exactly one 'half4 main(float2 coord)' entry point and no apply entry point.",
                nameof(source));
        }
    }

    private static void ValidateWholeSourceReservedDeclarations(IReadOnlyList<SkslToken> tokens)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            SkslToken token = tokens[index];
            if (!token.IsIdentifier
                || token.Depth != 0
                || !SkslSnippetMerger.IsRendererGeneratedName(token.Text)
                || index > 0 && tokens[index - 1].Text == "."
                || index + 1 >= tokens.Count)
            {
                continue;
            }

            string next = tokens[index + 1].Text;
            if (!IsTopLevelDeclarationBoundary(next))
                continue;

            throw new ArgumentException(
                $"The top-level shader declaration name '{token.Text}' is reserved by the renderer.",
                "source");
        }
    }

    /// <summary>The child shader a standalone current-pixel program samples its input through.</summary>
    internal const string CurrentPixelInputName = "__beutl_src";

    /// <summary>
    /// Wraps a current-pixel source in the entry point that runs it on its own: <c>apply</c> is handed the
    /// fragment's sample of <see cref="CurrentPixelInputName"/>.
    /// </summary>
    /// <remarks>The text is part of the program cache key, so it must not change for an unchanged source.</remarks>
    internal static string CreateStandaloneCurrentPixelProgram(string text)
        => $"uniform shader {CurrentPixelInputName};\n{text}\n"
           + $"half4 main(float2 __beutl_coord) {{ return apply({CurrentPixelInputName}.eval(__beutl_coord)); }}\n";

    private static bool IsTopLevelDeclarationBoundary(string token)
        => token is "(" or "=" or "[" or ";" or "{" or ",";

    /// <summary>
    /// Reports whether <paramref name="name"/> has the shape the renderer reserves for filter-effect bindings:
    /// an <c>fe</c> prefix and an underscore.
    /// </summary>
    private static bool IsFilterEffectBindingName(string name)
        => name.StartsWith("fe", StringComparison.Ordinal) && name.Contains('_', StringComparison.Ordinal);

    private static string ComputeHash(string source)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        const int stackBufferSize = 512;
        int byteCount = Encoding.UTF8.GetByteCount(source);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= stackBufferSize
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));
        ulong hash = offset;
        try
        {
            int written = Encoding.UTF8.GetBytes(source, bytes);
            foreach (byte value in bytes[..written])
            {
                hash ^= value;
                hash *= prime;
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }

        return hash.ToString("x16");
    }
}
