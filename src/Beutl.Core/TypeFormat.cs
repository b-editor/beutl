using Beutl.JsonDiscriminator;

namespace Beutl;

internal static class TypeFormat
{
    public static Type? ToType(string fullName)
    {
        fullName = fullName.Replace("Beutl.Embedding.FFmpeg", "Beutl.Extensions.FFmpeg");
        try
        {
            List<Token> tokens = new TypeNameTokenizer(fullName).Tokenize();
            return new TypeNameParser(tokens).Parse();
        }
        // The tokenizer/parser index freely and throw on ill-formed names (e.g. "x" from a
        // hand-edited file); every caller already treats null as "unknown type".
        catch (Exception ex) when (ex is IndexOutOfRangeException
                                       or ArgumentOutOfRangeException
                                       or ArgumentException
                                       or InvalidOperationException)
        {
            return null;
        }
    }

    public static string ToString(Type type)
    {
        return new TypeNameFormatter(type).Format();
    }
}
