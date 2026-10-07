using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Beutl.Utilities;

public ref struct RefUtf8StringTokenizer
{
    private readonly ReadOnlySpan<byte> _s;
    private readonly int _length;
    private readonly char _separator;
    private readonly string _exceptionMessage;
    private readonly IFormatProvider _formatProvider;
    private int _index;
    private int _tokenIndex;
    private int _tokenLength;

    public RefUtf8StringTokenizer(ReadOnlySpan<byte> s, IFormatProvider formatProvider, string exceptionMessage = "")
        : this(s, TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider), exceptionMessage)
    {
        _formatProvider = formatProvider;
    }

    public RefUtf8StringTokenizer(ReadOnlySpan<byte> s, char separator = TokenizerHelper.DefaultSeparatorChar, string exceptionMessage = "")
    {
        _s = s;
        _length = s.Length;
        _separator = separator;
        _exceptionMessage = exceptionMessage;
        _formatProvider = CultureInfo.InvariantCulture;
        _index = 0;
        _tokenIndex = -1;
        _tokenLength = 0;

        int index = 0;
        while (index < _length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(_s.Slice(index), out Rune rune, out var bytesConsumed);
            index += bytesConsumed;
            if (status == OperationStatus.Done)
            {
                if (IsWhitespace(rune))
                {
                    _index = index;
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }
    }

    public ReadOnlySpan<byte> CurrentToken => _tokenIndex < 0 ? default : _s.Slice(_tokenIndex, _tokenLength);

    public void Dispose()
    {
        if (_index != _length)
        {
            throw GetFormatException();
        }
    }

    private static bool IsMax(ReadOnlySpan<byte> s)
    {
        return s.Length == 3
            && s[0] is 0x4d or 0x6d
            && s[1] is 0x41 or 0x61
            && s[2] is 0x58 or 0x78;
    }

    private static bool IsMin(ReadOnlySpan<byte> s)
    {
        return s.Length == 3
            && s[0] is 0x4d or 0x6d
            && s[1] is 0x49 or 0x69
            && s[2] is 0x4E or 0x6E;
    }

    public bool TryReadInt32(out int result, char? separator = null)
        => TryReadNumber(out result, NumberStyles.Integer, separator);

    public int ReadInt32(char? separator = null)
    {
        if (!TryReadInt32(out int result, separator))
        {
            throw GetFormatException();
        }

        return result;
    }

    public bool TryReadDouble(out double result, char? separator = null)
        => TryReadNumber(out result, NumberStyles.Float, separator);

    public double ReadDouble(char? separator = null)
    {
        if (!TryReadDouble(out double result, separator))
        {
            throw GetFormatException();
        }

        return result;
    }

    public bool TryReadSingle(out float result, char? separator = null)
        => TryReadNumber(out result, NumberStyles.Float, separator);

    public float ReadSingle(char? separator = null)
    {
        if (!TryReadSingle(out float result, separator))
        {
            throw GetFormatException();
        }

        return result;
    }

    private bool TryReadNumber<T>(out T result, NumberStyles style, char? separator)
        where T : struct, INumberBase<T>, IMinMaxValue<T>
    {
        if (!TryReadString(out ReadOnlySpan<byte> stringResult, separator))
        {
            result = default;
            return false;
        }

        if (IsMax(stringResult))
        {
            result = T.MaxValue;
            return true;
        }

        if (IsMin(stringResult))
        {
            result = T.MinValue;
            return true;
        }

        return T.TryParse(stringResult, style, _formatProvider, out result);
    }

    public bool TryReadString(out ReadOnlySpan<byte> result, char? separator = null)
    {
        bool success = TryReadToken(separator ?? _separator);

        if (success)
        {
            result = _s.Slice(_tokenIndex, _tokenLength);
        }
        else
        {
            result = default;
        }

        return success;
    }

    public ReadOnlySpan<byte> ReadString(char? separator = null)
    {
        if (!TryReadString(out ReadOnlySpan<byte> result, separator))
        {
            throw GetFormatException();
        }

        return result;
    }

    private bool TryReadToken(char separator)
    {
        _tokenIndex = -1;

        if (_index >= _length)
        {
            return false;
        }

        int index = _index;
        int length = 0;
        Rune separatorRune = new(separator);

        while (_index < _length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(_s.Slice(_index), out Rune rune, out int bytesConsumed);
            if (status == OperationStatus.Done)
            {
                if (IsWhitespace(rune) || rune == separatorRune)
                {
                    break;
                }
            }

            _index += bytesConsumed;
            length += bytesConsumed;
        }

        SkipToNextToken(separator);

        _tokenIndex = index;
        _tokenLength = length;

        if (_tokenLength < 1)
        {
            throw GetFormatException();
        }

        return true;
    }

    private void SkipToNextToken(char separator)
    {
        Rune separatorRune = new(separator);
        if (_index >= _length)
        {
            return;
        }

        OperationStatus status = Rune.DecodeFromUtf8(_s.Slice(_index), out Rune rune, out int bytesConsumed);
        if (status != OperationStatus.Done)
        {
            throw GetFormatException();
        }

        if (!(IsWhitespace(rune) || rune == separatorRune))
        {
            throw GetFormatException();
        }

        int length = 0;

        while (_index < _length)
        {
            status = Rune.DecodeFromUtf8(_s.Slice(_index), out rune, out bytesConsumed);
            if (status != OperationStatus.Done)
            {
                throw GetFormatException();
            }

            if (rune == separatorRune)
            {
                length += bytesConsumed;
                _index += bytesConsumed;

                if (length > 1)
                {
                    throw GetFormatException();
                }
            }
            else
            {
                if (!IsWhitespace(rune))
                {
                    break;
                }

                _index += bytesConsumed;
            }
        }

        if (length > 0 && _index >= _length)
        {
            throw GetFormatException();
        }
    }

    private static bool IsWhitespace(Rune rune)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
        return category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }

    private FormatException GetFormatException() =>
        _exceptionMessage != null ? new FormatException(_exceptionMessage) : new FormatException();
}
