using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Beutl.JsonDiscriminator;

namespace Beutl
{
    internal static class TypeFormat
    {
        public static Type? ToType(string fullName)
        {
            List<Token> tokens = new TypeNameTokenizer(fullName).Tokenize();
            return new TypeNameParser(tokens).Parse();
        }

        public static string ToString(Type type)
        {
            return new TypeNameFormatter(type).Format();
        }
    }

    namespace JsonDiscriminator
    {
        internal class Token
        {
            public Token(TokenType type, string? text = null)
            {
                Type = type;
                Text = text;

                if (text == null && type != TokenType.Part)
                {
                    Text = type switch
                    {
                        TokenType.BeginAssembly => "[",
                        TokenType.EndAssembly => "]",
                        TokenType.Colon => ":",
                        TokenType.Period => ".",
                        TokenType.BeginGenericArguments => "<",
                        TokenType.EndGenericArguments => ">",
                        _ => null,
                    };
                }
            }

            public TokenType Type { get; }

            public string? Text { get; }
        }

        /*
         * 名前空間とアセンブリ名が同じ場合
         * [Beutl.Graphics]:Point
         * 
         * 型がグローバル空間にある場合
         * [Beutl.Graphics]global::Point
         * 
         * 名前空間とアセンブリ名が途中まで同じ場合
         * [Beutl.Graphics].Shapes:Ellipse
         * 
         * 名前空間とアセンブリ名が一致しない場合
         * [Beutl.Graphics]Beutl.Audio:Sound
         * 
         * ジェネリック引数がある場合
         * [System.Collections].Generic:List<[System.Runtime]System:Int32>
         * 
         * 入れ子になったクラス
         * [System.Net.Mail]System.Net.Mime:MediaTypeNames:Application
         */
        internal enum TokenType
        {
            BeginAssembly,

            EndAssembly,

            // Colon
            Colon,

            Period,

            Comma,

            BeginGenericArguments,

            EndGenericArguments,

            Part,
        }

        internal class TypeNameParser
        {
            private readonly List<Token> _tokens;
            private readonly Func<string, Assembly?> _assemblyResolver;
            private string? _assemblyName;
            private Assembly? _assembly;
            private string? _namespace;

            public TypeNameParser(List<Token> tokens, Func<string, Assembly?>? assemblyResolver = null)
            {
                Assembly? DefaultAssemblyResolver(string s)
                {
#if !DEBUG
#warning TypeFormatの互換性コードが残っている
#endif
                    if (s is "Beutl.Graphics")
                    {
                        s = "Beutl.Engine";
                    }
                    else if (s is "Beutl.Framework")
                    {
                        s = "Beutl.Extensibility";
                    }


                    //AssemblyLoadContext.Default.Assemblies
                    return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == s);
                }

                _tokens = tokens;
                _assemblyResolver = assemblyResolver ?? DefaultAssemblyResolver;
            }

            public Type? Parse()
            {
                Token[] asmTokens = TakeAssemblyTokens(_tokens).ToArray();
                _assemblyName = string.Concat(asmTokens
                    .Where(x => x.Type is not (TokenType.BeginAssembly or TokenType.EndAssembly))
                    .Select(x => x.Text));
                _assembly = _assemblyResolver(_assemblyName);

                Token[] nsTokens = TakeNamespaceTokens(_tokens.Skip(asmTokens.Length)).ToArray();
                _namespace = ParseNamespace(nsTokens);

                Token[] typeTokens = _tokens.Skip(asmTokens.Length + nsTokens.Length).ToArray();
                return ParseNestedType(typeTokens);
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

            private static Type[] ParseGenericTypes(Span<Token> tokens)
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
                            var parser = new TypeNameParser(new List<Token>(genericType.ToArray()));
                            list.Add(parser.Parse());
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

            private Type? ParseNestedType(Span<Token> tokens)
            {
                string typeName = TakeTypeNameTokens(tokens, out Span<Token> genericTokens, out Span<Token> parents);
                Type[] genericArgs = ParseGenericTypes(genericTokens);
                string suffix = genericArgs.Length > 0 ? $"`{genericArgs.Length}" : "";

                Type? type;
                if (parents.Length != 0)
                {
                    Type? parent = ParseNestedType(parents);
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    type = parent?.GetNestedType($"{typeName}{suffix}", flags)!;
                }
                else
                {
                    type = _assembly?.GetType($"{_namespace ?? ""}.{typeName}{suffix}")!;
                }

                if (genericArgs.Length > 0)
                {
                    type = type.MakeGenericType(genericArgs);
                }

                return type;
            }
        }

        internal class TypeNameFormatter(Type type)
        {
            private void WriteNamespace(StringBuilder sb)
            {
                string? asmName = type.Assembly.GetName().Name;
                string? ns = type.Namespace;
                if (ns != null)
                {
                    if (asmName != null)
                    {
                        if (ns.StartsWith(asmName))
                        {
                            ns = ns.Substring(asmName.Length);
                        }
                    }
                }
                else
                {
                    ns = "global:";
                }

                sb.Append(ns);
            }

            public string Format()
            {
                var sb = new StringBuilder();
                string? asmName = type.Assembly.GetName().Name;
                if (asmName != null)
                {
                    sb.Append('[');
                    sb.Append(asmName);
                    sb.Append(']');
                }

                WriteNamespace(sb);

                WriteTypeName(sb, type);

                return sb.ToString();
            }

            private static void WriteGenericArguments(StringBuilder sb, Type type)
            {
                sb.Append('<');
                Type[] array = type.GetGenericArguments();
                for (int i = 0; i < array.Length;)
                {
                    Type? item = array[i];
                    var formatter = new TypeNameFormatter(item);
                    sb.Append(formatter.Format());
                    if (++i < array.Length)
                    {
                        sb.Append(", ");
                    }
                }
                sb.Append('>');
            }

            private static void WriteTypeName(StringBuilder sb, Type type)
            {
                Type? declaringType = type.DeclaringType;
                if (type.IsNested && declaringType != null)
                {
                    WriteTypeName(sb, declaringType);
                }

                sb.Append(':');
                sb.Append(TrimTypeName(type.Name));
                if (type.IsGenericType)
                {
                    WriteGenericArguments(sb, type);
                }
            }

            private static string TrimTypeName(string typeName)
            {
                int idx = typeName.IndexOf('`');
                if (idx < 0)
                {
                    return typeName;
                }
                else
                {
                    return typeName[..idx];
                }
            }
        }

        internal class TypeNameTokenizer(string s)
        {
            public List<Token> Tokenize()
            {
                var list = new List<Token>();
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (IsKigou(c))
                    {
                        switch (c)
                        {
                            case '[':
                                list.Add(new(TokenType.BeginAssembly));
                                break;
                            case ']':
                                list.Add(new(TokenType.EndAssembly));
                                break;
                            case ':':
                                list.Add(new(TokenType.Colon));
                                break;
                            case '<':
                                list.Add(new(TokenType.BeginGenericArguments));
                                break;
                            case '>':
                                list.Add(new(TokenType.EndGenericArguments));
                                break;
                            case '.':
                                list.Add(new(TokenType.Period));
                                break;
                            case ',':
                                list.Add(new(TokenType.Comma));
                                break;
                        }
                    }
                    else
                    {
                        int start = i;
                        while (true)
                        {
                            c = s[i];

                            if (IsKigou(c))
                            {
                                list.Add(new(TokenType.Part, s.Substring(start, i - start)));
                                i--;
                                break;
                            }

                            i++;
                            if (i >= s.Length)
                            {
                                list.Add(new(TokenType.Part, s.Substring(start, i - start)));
                                break;
                            }
                        }
                    }
                }

                return list;
            }

            private static bool IsKigou(char c)
            {
                return c switch
                {
                    '[' or ']' or ':' or '<' or '>' or '.' or ',' => true,
                    _ => false,
                };
            }
        }
    }
}
