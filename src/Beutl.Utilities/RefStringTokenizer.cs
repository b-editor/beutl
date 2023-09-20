using System.Globalization;

namespace Beutl.Utilities;

public ref struct RefStringTokenizer
{
    private readonly ReadOnlySpan<char> _s;
    private readonly int _length;
    private readonly char _separator;
    private readonly string _exceptionMessage;
    private readonly IFormatProvider _formatProvider;
    private int _index;
    private int _tokenIndex;
    private int _tokenLength;

    public RefStringTokenizer(ReadOnlySpan<char> s, IFormatProvider formatProvider, string exceptionMessage = "")
        : this(s, TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider), exceptionMessage)
    {
        _formatProvider = formatProvider;
    }

    public RefStringTokenizer(ReadOnlySpan<char> s, char separator = TokenizerHelper.DefaultSeparatorChar, string exceptionMessage = "")
    {
        _s = s;
        _length = s.Length;
        _separator = separator;
        _exceptionMessage = exceptionMessage;
        _formatProvider = CultureInfo.InvariantCulture;
        _index = 0;
        _tokenIndex = -1;
        _tokenLength = 0;

        while (_index < _length && char.IsWhiteSpace(_s[_index]))
        {
            _index++;
        }
    }

    public readonly ReadOnlySpan<char> CurrentToken => _tokenIndex < 0 ? default : _s.Slice(_tokenIndex, _tokenLength);

    public readonly void Dispose()
    {
        if (_index != _length)
        {
            throw GetFormatException();
        }
    }

    public bool TryReadInt32(out int result, char? separator = null)
    {
        if (!TryReadString(out ReadOnlySpan<char> stringResult, separator))
        {
            result = default;
            return false;
        }
        else
        {
            if (stringResult.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                result = int.MaxValue;
                return true;
            }
            else if (stringResult.Equals("min", StringComparison.OrdinalIgnoreCase))
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
        if (!TryReadString(out ReadOnlySpan<char> stringResult, separator))
        {
            result = default;
            return false;
        }
        else
        {
            if (stringResult.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                result = double.MaxValue;
                return true;
            }
            else if (stringResult.Equals("min", StringComparison.OrdinalIgnoreCase))
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
        if (!TryReadString(out ReadOnlySpan<char> stringResult, separator))
        {
            result = default;
            return false;
        }
        else
        {
            if (stringResult.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                result = float.MaxValue;
                return true;
            }
            else if (stringResult.Equals("min", StringComparison.OrdinalIgnoreCase))
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

    public bool TryReadString(out ReadOnlySpan<char> result, char? separator = null)
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

    public ReadOnlySpan<char> ReadString(char? separator = null)
    {
        if (!TryReadString(out ReadOnlySpan<char> result, separator))
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

        while (_index < _length)
        {
            char c = _s[_index];

            if (char.IsWhiteSpace(c) || c == separator)
            {
                break;
            }

            _index++;
            length++;
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
        if (_index < _length)
        {
            char c = _s[_index];

            if (c != separator && !char.IsWhiteSpace(c))
            {
                throw GetFormatException();
            }

            int length = 0;

            while (_index < _length)
            {
                c = _s[_index];

                if (c == separator)
                {
                    length++;
                    _index++;

                    if (length > 1)
                    {
                        throw GetFormatException();
                    }
                }
                else
                {
                    if (!char.IsWhiteSpace(c))
                    {
                        break;
                    }

                    _index++;
                }
            }

            if (length > 0 && _index >= _length)
            {
                throw GetFormatException();
            }
        }
    }

    private readonly FormatException GetFormatException() =>
        _exceptionMessage != null ? new FormatException(_exceptionMessage) : new FormatException();
}
