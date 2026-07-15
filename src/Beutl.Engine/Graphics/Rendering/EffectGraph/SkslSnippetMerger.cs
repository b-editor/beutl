using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Merges a run of adjacent coordinate-invariant SKSL snippets (<c>half4 apply(half4 c)</c>) into one generated
/// whole-source program. Each snippet contributes a prefixed apply function; the generated main samples the source
/// child once and chains the calls in node order.
/// </summary>
internal static class SkslSnippetMerger
{
    private static readonly ConditionalWeakTable<SkslSource, SourceBindingCache> s_bindingCaches = new();

    /// <summary>The child-shader name the generated <c>main</c> samples as the fused group's input.</summary>
    public const string SourceChildName = "src";

    public static string Merge(IReadOnlyList<SkslSource> snippets)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        if (snippets.Count == 0)
            throw new ArgumentException("At least one snippet is required.", nameof(snippets));

        var sb = new StringBuilder();
        sb.Append("uniform shader ").Append(SourceChildName).Append(";\n");
        var prefixes = new string[snippets.Count];

        for (int i = 0; i < snippets.Count; i++)
        {
            SkslSource snippet = snippets[i];
            if (snippet.Kind != SkslSourceKind.Snippet)
                throw new ArgumentException("Only snippet sources can be merged.", nameof(snippets));

            string prefix = GetPrefix(snippet, i);
            prefixes[i] = prefix;
            sb.Append(Prefix(snippet.Source, prefix)).Append('\n');
        }

        sb.Append("half4 main(float2 coord) {\n");
        sb.Append("    half4 _fused = ").Append(SourceChildName).Append(".eval(coord);\n");
        for (int i = 0; i < snippets.Count; i++)
        {
            sb.Append("    _fused = ").Append(prefixes[i]).Append("apply(_fused);\n");
        }

        sb.Append("    return _fused;\n}\n");
        return sb.ToString();
    }

    // Chooses a deterministic stage prefix that no source identifier already starts with. This prevents an author name
    // such as fe0_x from colliding with the generated name for x. The executor uses the same function when binding.
    internal static string GetPrefix(SkslSource snippet, int index)
        => GetStageBindingCache(snippet, index).Prefix;

    internal static string GetPrefixedName(SkslSource snippet, int index, string name)
    {
        StageBindingCache cache = GetStageBindingCache(snippet, index);
        return cache.Names.GetOrAdd(name, static (value, prefix) => prefix + value, cache.Prefix);
    }

    private static StageBindingCache GetStageBindingCache(SkslSource snippet, int index)
    {
        SourceBindingCache sourceCache = s_bindingCaches.GetValue(snippet, static _ => new());
        return sourceCache.Stages.GetOrAdd(
            index, static (stageIndex, source) => new(ComputePrefix(source, stageIndex)), snippet);
    }

    private static string ComputePrefix(SkslSource snippet, int index)
    {
        string prefix = $"fe{index}_";
        List<SkslToken> tokens = SkslLexer.Tokenize(snippet.Source);
        while (tokens.Any(token => token.IsIdentifier && token.Text.StartsWith(prefix, StringComparison.Ordinal)))
            prefix += "_";
        return prefix;
    }

    private sealed class SourceBindingCache
    {
        public ConcurrentDictionary<int, StageBindingCache> Stages { get; } = new();
    }

    private sealed class StageBindingCache(string prefix)
    {
        public string Prefix { get; } = prefix;

        public ConcurrentDictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    }

    // Alpha-renames the original token stream in one pass. Looking at the preceding significant token (comments and
    // whitespace are absent from the token list) keeps both c.r and c . r member/swizzle accesses unchanged. Because
    // replacements are emitted from the original source rather than fed into another replacement, generated names can
    // never be renamed a second time.
    private static string Prefix(string source, string prefix)
    {
        List<SkslToken> tokens = SkslLexer.Tokenize(source);
        HashSet<string> names = CollectTopLevelNames(tokens);
        HashSet<int> layoutQualifierIdentifiers = CollectLayoutQualifierIdentifiers(tokens);
        var result = new StringBuilder(source.Length + (names.Count * prefix.Length));
        int copiedThrough = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            SkslToken token = tokens[i];
            if (!token.IsIdentifier
                || !names.Contains(token.Text)
                || layoutQualifierIdentifiers.Contains(i)
                || (i > 0 && tokens[i - 1].Text == "."))
            {
                continue;
            }

            result.Append(source, copiedThrough, token.Start - copiedThrough);
            result.Append(prefix).Append(token.Text);
            copiedThrough = token.Start + token.Length;
        }

        result.Append(source, copiedThrough, source.Length - copiedThrough);
        return result.ToString();
    }

    // `layout(color) uniform half4 color;` contains two equal identifier tokens with different roles: the first is
    // fixed layout metadata and the second is the uniform declarator. Only the declarator and its value references
    // participate in alpha-renaming. Record identifier indexes inside every top-level layout(...) group so Prefix can
    // leave the metadata intact without weakening ordinary renaming inside function bodies.
    private static HashSet<int> CollectLayoutQualifierIdentifiers(IReadOnlyList<SkslToken> tokens)
    {
        var result = new HashSet<int>();
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i] is not { IsIdentifier: true, Text: "layout", Depth: 0 }
                || tokens[i + 1].Text != "(")
            {
                continue;
            }

            int groupDepth = 0;
            for (int j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "(")
                {
                    groupDepth++;
                }
                else if (tokens[j].Text == ")")
                {
                    groupDepth--;
                    if (groupDepth == 0)
                    {
                        i = j;
                        break;
                    }
                }
                else if (groupDepth > 0 && tokens[j].IsIdentifier)
                {
                    result.Add(j);
                }
            }
        }

        return result;
    }

    // At brace depth 0, `uniform`/`const` [PRECISION] TYPE NAME declares NAME; IDENT IDENT `(` is a function
    // definition; and IDENT IDENT followed by `=`/`;`/`[` is a mutable global. Multi-declarator mutable globals are
    // collected through the terminating semicolon; uniform/const multi-declarators are rejected by SkslSource.
    private static HashSet<string> CollectTopLevelNames(IReadOnlyList<SkslToken> tokens)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int t = 0; t + 1 < tokens.Count; t++)
        {
            if (!tokens[t].IsIdentifier || tokens[t].Depth != 0)
                continue;

            if (tokens[t].Text is "uniform" or "const")
            {
                int type = t + 1;
                if (type < tokens.Count && tokens[type].Text is "lowp" or "mediump" or "highp")
                    type++;

                int name = SkipArrayDimensions(tokens, type + 1);
                if (name < tokens.Count && tokens[type].IsIdentifier && tokens[name].IsIdentifier)
                    names.Add(tokens[name].Text);
                t = name;
            }
            else if (tokens[t + 1].IsIdentifier && t + 2 < tokens.Count && tokens[t + 2].Text == "(")
            {
                names.Add(tokens[t + 1].Text);
                t++;
            }
            else if (tokens[t + 1].IsIdentifier && t + 2 < tokens.Count && tokens[t + 2].Text is "=" or ";" or "[" or ",")
            {
                names.Add(tokens[t + 1].Text);
                int groupDepth = 0;
                int u = t + 2;
                for (; u < tokens.Count; u++)
                {
                    string text = tokens[u].Text;
                    if (text is "(" or "[" or "{")
                        groupDepth++;
                    else if (text is ")" or "]" or "}")
                        groupDepth = Math.Max(0, groupDepth - 1);
                    else if (groupDepth == 0 && text == ";")
                        break;
                    else if (groupDepth == 0 && text == "," && u + 1 < tokens.Count && tokens[u + 1].IsIdentifier)
                        names.Add(tokens[u + 1].Text);
                }

                t = u;
            }
        }

        return names;
    }

    private static int SkipArrayDimensions(IReadOnlyList<SkslToken> tokens, int index)
    {
        while (index < tokens.Count && tokens[index].Text == "[")
        {
            int depth = 1;
            index++;
            while (index < tokens.Count && depth > 0)
            {
                if (tokens[index].Text == "[")
                    depth++;
                else if (tokens[index].Text == "]")
                    depth--;
                index++;
            }
        }

        return index;
    }

}
