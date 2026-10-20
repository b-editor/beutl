using System.Diagnostics.CodeAnalysis;

namespace Beutl.Controls.PropertyEditors;

// The C# keywords of the built-in types, for type names shown to users who write expressions.
public static class CSharpTypeKeywords
{
    private static readonly Dictionary<Type, string> s_keywords = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(object)] = "object",
    };

    public static bool TryGetCSharpKeyword(Type type, [NotNullWhen(true)] out string? keyword)
    {
        return s_keywords.TryGetValue(type, out keyword);
    }
}
