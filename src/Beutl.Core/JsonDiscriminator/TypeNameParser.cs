using System.Reflection;
using System.Text;

namespace Beutl.JsonDiscriminator;

internal class TypeNameParser
{
    private readonly List<Token> _tokens;
    private readonly Func<string, string, Type?> _aliasResolver;
    private string? _assemblyName;
    private Assembly? _assembly;
    private string? _namespace;

    // aliasResolver takes the assembly name and the full name (in Type.FullName form) of a type definition that
    // does not resolve, and returns the type that gives it as a former name.
    public TypeNameParser(List<Token> tokens, Func<string, string, Type?>? aliasResolver = null)
    {
        _tokens = tokens;
        _aliasResolver = aliasResolver ?? TypeFormat.FindAlias;
    }

    // Whether Parse resolved part of the name, such as a type argument, through a former name. Such a result changes
    // once a type with that name loads, which then takes its place.
    public bool UsedFormerName { get; private set; }

    private static Assembly? DefaultAssemblyResolver(string s)
    {
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == s);
    }

    public Type? Parse()
    {
        Token[] asmTokens = TakeAssemblyTokens(_tokens).ToArray();
        _assemblyName = string.Concat(asmTokens
            .Where(x => x.Type is not (TokenType.BeginAssembly or TokenType.EndAssembly))
            .Select(x => x.Text));
        _assembly = DefaultAssemblyResolver(_assemblyName);

        Token[] nsTokens = TakeNamespaceTokens(_tokens.Skip(asmTokens.Length)).ToArray();
        _namespace = ParseNamespace(nsTokens);

        Token[] typeTokens = _tokens.Skip(asmTokens.Length + nsTokens.Length).ToArray();
        return ParseNestedType(typeTokens, out _);
    }

    private static string ConcatTokens(ReadOnlySpan<Token> tokens)
    {
        var sb = new StringBuilder();
        foreach (Token item in tokens)
        {
            sb.Append(item.Text);
        }

        return sb.ToString();
    }

    private static IEnumerable<Token> TakeAssemblyTokens(IEnumerable<Token> tokens)
    {
        foreach (Token item in tokens)
        {
            if (item.Type is TokenType.BeginAssembly or TokenType.EndAssembly or TokenType.Part or TokenType.Period)
            {
                yield return item;
                if (item.Type is TokenType.EndAssembly)
                {
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<Token> TakeNamespaceTokens(IEnumerable<Token> tokens)
    {
        int colonCount = 0;
        foreach (Token item in tokens)
        {
            if (item.Type is TokenType.Period or TokenType.Part or TokenType.Colon)
            {
                if (item.Type is TokenType.Colon)
                {
                    colonCount++;
                    if (colonCount == 1)
                    {
                        continue;
                    }
                    else if (colonCount == 2)
                    {
                        // コロンが二連続 -> "global::XXX"
                        yield return item;
                        yield break;
                    }
                }

                if (colonCount == 1)
                {
                    yield break;
                }

                yield return item;
            }
        }
    }

    private string? ParseNamespace(Token[] tokens)
    {
        if (tokens.Length == 0)
        {
            return _assemblyName;
        }

        if (tokens.Length >= 2)
        {
            if (tokens[^1].Type == TokenType.Colon
                && tokens[^2] is { Text: "global" })
            {
                if (tokens.Length > 2)
                    throw new InvalidOperationException($"Invalid Tokens: {ConcatTokens(tokens)}");

                return null;
            }
            else if (tokens[0].Type == TokenType.Period)
            {
                return $"{_assemblyName}{ConcatTokens(tokens)}";
            }
        }

        return ConcatTokens(tokens);
    }

    private static void TakeGenericArguments(Span<Token> tokens, out Span<Token> genericArgs)
    {
        // リストパターンにするとNestedTypeの親にGeneric引数がある場合、壊れる
        if (tokens[^1].Type == TokenType.EndGenericArguments)
        {
            // 解析中の型はジェネリック型
            var genericRange = new Range(Index.End, tokens.Length);
            int innerGenericCount = 0;

            for (int i = tokens.Length - 2; i >= 0; i--)
            {
                TokenType type = tokens[i].Type;
                if (type == TokenType.EndGenericArguments)
                {
                    innerGenericCount++;
                }
                else if (type == TokenType.BeginGenericArguments)
                {
                    if (innerGenericCount == 0)
                    {
                        // 内側にジェネリック引数が無くて、'<'に到達した
                        genericRange = new Range(i, genericRange.End);
                        break;
                    }
                    else
                    {
                        innerGenericCount--;
                    }
                }
            }

            if (genericRange.Start.IsFromEnd)
                throw new InvalidOperationException($"Invalid Tokens: {ConcatTokens(tokens)}");

            genericArgs = tokens[genericRange];
        }
        else
        {
            genericArgs = default;
        }
    }

    private Type[] ParseGenericTypes(Span<Token> tokens)
    {
        if (tokens.Length == 0)
            return [];

        var list = new List<Type?>();

        if (tokens is [{ Type: TokenType.BeginGenericArguments }, .. var generics, { Type: TokenType.EndGenericArguments }])
        {
            int start = 0;
            int innerGenericCount = 0;
            for (int i = 0; i < generics.Length;)
            {
                Token item = generics[i];
                TokenType type = item.Type;
                if (type == TokenType.BeginGenericArguments)
                {
                    innerGenericCount++;
                }
                else if (type == TokenType.EndGenericArguments)
                {
                    innerGenericCount--;
                }

                if (++i == generics.Length || (innerGenericCount == 0 && item.Type is TokenType.Comma))
                {
                    Span<Token> genericType = generics[start..i];
                    var parser = new TypeNameParser([.. genericType]);
                    list.Add(parser.Parse());
                    UsedFormerName |= parser.UsedFormerName;
                }
            }
        }

        if (list.Any(x => x == null))
            throw new InvalidOperationException($"Invalid Tokens: {ConcatTokens(tokens)}");

        return list.ToArray()!;
    }

    // ":List<[System.Runtime]:Int32>"を解析
    private static string TakeTypeNameTokens(Span<Token> tokens, out Span<Token> genericTokens, out Span<Token> parents)
    {
        TakeGenericArguments(tokens, out genericTokens);

        Span<Token> nokoriToken = tokens.Slice(0, tokens.Length - genericTokens.Length);

        if (nokoriToken is [.. var parentTokens, { Type: TokenType.Colon }, { Type: TokenType.Part, Text: string typeName }])
        {
            parents = parentTokens;
            return typeName;
        }
        else
        {
            throw new InvalidOperationException($"Invalid Tokens: {ConcatTokens(tokens)}");
        }
    }

    // definitionName is the written type definition in Type.FullName form, by which the alias table is keyed.
    private Type? ParseNestedType(Span<Token> tokens, out string definitionName)
    {
        string typeName = TakeTypeNameTokens(tokens, out Span<Token> genericTokens, out Span<Token> parents);
        Type[] genericArgs = ParseGenericTypes(genericTokens);
        string suffix = genericArgs.Length > 0 ? $"`{genericArgs.Length}" : "";

        Type? type;
        if (parents.Length != 0)
        {
            Type? parent = ParseNestedType(parents, out string parentName);
            definitionName = $"{parentName}+{typeName}{suffix}";
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            type = parent?.GetNestedType($"{typeName}{suffix}", flags)!;
        }
        else
        {
            definitionName = _namespace == null ? $"{typeName}{suffix}" : $"{_namespace}.{typeName}{suffix}";
            type = _assembly?.GetType($"{_namespace ?? ""}.{typeName}{suffix}")!;
        }

        // A former name (FormerPersistedNameAttribute) is read only when no type has the name now.
        if (type == null)
        {
            type = _aliasResolver(_assemblyName ?? "", definitionName);
            UsedFormerName |= type != null;
        }

        if (type == null)
        {
            return null;
        }

        if (genericArgs.Length > 0)
        {
            type = type.MakeGenericType(genericArgs);
        }

        return type;
    }

    // Reads a FormerPersistedNameAttribute name of a type: "[Assembly]Namespace:Type", with a nested type after its
    // declaring type, and without type arguments. aritySuffix is the "`n" a generic type's name ends with. Returns
    // null for a name that is not written that way.
    internal static FormerTypeName? ParseFormerName(string name, string aritySuffix)
    {
        try
        {
            List<Token> tokens = new TypeNameTokenizer(name).Tokenize();
            var parser = new TypeNameParser(tokens, static (_, _) => null);
            return parser.ParseFormerName(aritySuffix);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException
                                       or ArgumentOutOfRangeException
                                       or ArgumentException
                                       or InvalidOperationException)
        {
            return null;
        }
    }

    private FormerTypeName? ParseFormerName(string aritySuffix)
    {
        // The assembly comes first, and only names and periods follow: no type arguments.
        int endAssembly = _tokens.FindIndex(token => token.Type == TokenType.EndAssembly);
        if (_tokens is not [{ Type: TokenType.BeginAssembly }, ..]
            || endAssembly < 2
            || _tokens.Take(endAssembly).Skip(1).Any(token => token.Type is not (TokenType.Part or TokenType.Period))
            || _tokens.Skip(endAssembly + 1)
                .Any(token => token.Type is not (TokenType.Part or TokenType.Period or TokenType.Colon)))
        {
            return null;
        }

        _assemblyName = string.Concat(_tokens.Take(endAssembly).Skip(1).Select(token => token.Text));
        Token[] nsTokens = TakeNamespaceTokens(_tokens.Skip(endAssembly + 1)).ToArray();
        _namespace = ParseNamespace(nsTokens);

        var names = new List<string>();
        if (!TakeNestedNames(_tokens.Skip(endAssembly + 1 + nsTokens.Length).ToArray(), names))
        {
            return null;
        }

        names[^1] += aritySuffix;
        string path = string.Join('+', names);
        return new FormerTypeName(
            _assemblyName,
            _namespace,
            names[^1],
            _namespace == null ? path : $"{_namespace}.{path}");
    }

    // ":Outer:Inner" -> [Outer, Inner]
    private static bool TakeNestedNames(Span<Token> tokens, List<string> names)
    {
        if (tokens is not [.. var parents, { Type: TokenType.Colon }, { Type: TokenType.Part, Text: string name }])
        {
            return false;
        }

        if (parents.Length != 0 && !TakeNestedNames(parents, names))
        {
            return false;
        }

        names.Add(name);
        return true;
    }
}

/// <summary>A former name of a type, as <see cref="Serialization.FormerPersistedNameAttribute"/> gives it.</summary>
/// <param name="AssemblyName">The assembly the name gives.</param>
/// <param name="Namespace">The namespace, or <see langword="null"/> for the global namespace.</param>
/// <param name="Name">The name of the type itself, as <see cref="System.Reflection.MemberInfo.Name"/> gives it.</param>
/// <param name="FullName">The namespace, the declaring types and the name, as <see cref="Type.FullName"/> gives them.</param>
internal sealed record FormerTypeName(string AssemblyName, string? Namespace, string Name, string FullName);
