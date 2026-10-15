using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Beutl.Converters;

// Parses UTF-8 text with the type's UTF-16 parser, so each type has one parsing implementation.
internal static class Utf8TextParser
{
    // The parsed texts are short, so they are transcoded on the stack; longer ones use a pooled buffer.
    private const int StackBufferLength = 256;

    public static T Parse<T>(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
        where T : ISpanParsable<T>
    {
        char[]? rented = null;
        try
        {
            return T.Parse(Transcode(utf8Text, stackalloc char[StackBufferLength], ref rented), provider);
        }
        finally
        {
            Return(rented);
        }
    }

    public static bool TryParse<T>(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T>
    {
        char[]? rented = null;
        try
        {
            return T.TryParse(Transcode(utf8Text, stackalloc char[StackBufferLength], ref rented), provider, out result);
        }
        finally
        {
            Return(rented);
        }
    }

    private static ReadOnlySpan<char> Transcode(ReadOnlySpan<byte> utf8Text, Span<char> stackBuffer, ref char[]? rented)
    {
        int maxLength = Encoding.UTF8.GetMaxCharCount(utf8Text.Length);
        Span<char> buffer = maxLength <= stackBuffer.Length
            ? stackBuffer
            : (rented = ArrayPool<char>.Shared.Rent(maxLength));
        return buffer[..Encoding.UTF8.GetChars(utf8Text, buffer)];
    }

    private static void Return(char[]? rented)
    {
        if (rented != null)
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }
}
