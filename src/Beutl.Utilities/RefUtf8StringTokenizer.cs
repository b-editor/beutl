using System.Buffers;
using System.Globalization;
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
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
                if (category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
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
    {
        if (!TryReadString(out ReadOnlySpan<byte> stringResult, separator))
        {
            result = default;
            return false;
        }
        else
        {
            if (IsMax(stringResult))
            {
                result = int.MaxValue;
                return true;
            }
            else if (IsMin(stringResult))
            {
                result = int.MinValue;
                return true;
            }
            else if (int.TryParse(stringResult, NumberStyles.Integer, _formatProvider, out result))
            {
                return true;
            }
        }

        return false;
    }

    public int ReadInt32(char? separator = null)
    {
        if (!TryReadInt32(out int result, separator))
        {
            throw GetFormatException();
        }

        return result;
    }

    public bool TryReadDouble(out double result, char? separator = null)
    {
        if (!TryReadString(out ReadOnlySpan<byte> stringResult, separator))
        {
            result = default;
            return false;
        }
        else
        {
            if (IsMax(stringResult))
            {
                result = double.MaxValue;
                return true;
            }
            else if (IsMin(stringResult))
            {
                result = double.MinValue;
                return true;
            }
            else if (double.TryParse(stringResult, NumberStyles.Float, _formatProvider, out result))
            {
                return true;
            }
        }

        return false;
    }

    public double ReadDouble(char? separator = null)
    {
        if (!TryReadDouble(out double result, separator))
        {
            throw GetFormatException();
        }

        return result;
    }

    public bool TryReadSingle(out float result, char? separator = null)
    {
        if (!TryReadString(out ReadOnlySpan<byte> stringResult, separator))
        {
            result = default;
            return false;
        }
        else
        {
            if (IsMax(stringResult))
            {
                result = float.MaxValue;
                return true;
            }
            else if (IsMin(stringResult))
            {
                result = float.MinValue;
                return true;
            }
            else if (float.TryParse(stringResult, NumberStyles.Float, _formatProvider, out result))
            {
                return true;
            }
        }

        return false;
    }

    public float ReadSingle(char? separator = null)
    {
        if (!TryReadSingle(out float result, separator))
        {
            throw GetFormatException();
        }

        return result;
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
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
                if (category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    || rune == separatorRune)
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
        if (_index < _length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(_s.Slice(_index), out Rune rune, out int bytesConsumed);
            if (status == OperationStatus.Done)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
                if (!(category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    || rune == separatorRune))
                {
                    throw GetFormatException();
                }

                int length = 0;

                while (_index < _length)
                {
                    status = Rune.DecodeFromUtf8(_s.Slice(_index), out rune, out bytesConsumed);
                    if (status == OperationStatus.Done)
                    {
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
                            category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
                            if (!(category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
                            {
                                break;
                            }

                            _index += bytesConsumed;
                        }
                    }
                    else
                    {
                        throw GetFormatException();
                    }
                }

                if (length > 0 && _index >= _length)
                {
                    throw GetFormatException();
                }
            }
            else
            {
                throw GetFormatException();
            }
        }
    }

    private FormatException GetFormatException() =>
        _exceptionMessage != null ? new FormatException(_exceptionMessage) : new FormatException();
}
