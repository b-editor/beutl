using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Beutl.Utilities;

namespace Beutl.Animation;

// https://github.com/AvaloniaUI/Avalonia/blob/d3b21f589389b8bedfa75ed06d546658745e2089/src/Avalonia.Animation/KeySpline.cs#L20
public class KeySpline
{
    // コントロールポイント
    private float _controlPointX1;
    private float _controlPointY1;
    private float _controlPointX2;
    private float _controlPointY2;
    private bool _isSpecified;
    private bool _isDirty;

    // The parameter that corresponds to the most recent time
    private float _parameter;

    // Cached coefficients
    private float _bx;        // 3*points[0].X
    private float _cx;        // 3*points[1].X
    private float _cx_bx;     // 2*(Cx - Bx)
    private float _three_Cx;  // 3 - Cx

    private float _by;        // 3*points[0].Y
    private float _cy;        // 3*points[1].Y

    // constants
    private const float Accuracy = 0.001f;   // 1/3 the desired accuracy in X
    private const float Fuzz = 0.000001f;    // computational zero

    public KeySpline()
    {
        _controlPointX1 = 0.0f;
        _controlPointY1 = 0.0f;
        _controlPointX2 = 1.0f;
        _controlPointY2 = 1.0f;
        _isDirty = true;
    }

    public KeySpline(float x1, float y1, float x2, float y2)
    {
        _controlPointX1 = x1;
        _controlPointY1 = y1;
        _controlPointX2 = x2;
        _controlPointY2 = y2;
        _isDirty = true;
    }

    public static bool TryParse(string s, [NotNullWhen(true)] out KeySpline? keySpline, IFormatProvider? provider = null)
    {
        return TryParse(s.AsSpan(), out keySpline, provider);
    }

    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out KeySpline? keySpline, IFormatProvider? provider = null)
    {
        try
        {
            keySpline = Parse(s, provider);
            return true;
        }
        catch
        {
            keySpline = default;
            return false;
        }
    }

    public static KeySpline Parse(string value, IFormatProvider? provider = null)
    {
        return Parse(value.AsSpan(), provider);
    }

    public static KeySpline Parse(ReadOnlySpan<char> value, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.InvariantCulture;

        using var tokenizer = new RefStringTokenizer(value, provider, exceptionMessage: $"Invalid KeySpline string: \"{value}\".");

        return new KeySpline(tokenizer.ReadSingle(), tokenizer.ReadSingle(), tokenizer.ReadSingle(), tokenizer.ReadSingle());
    }

    public float ControlPointX1
    {
        get => _controlPointX1;
        set
        {
            if (IsValidXValue(value))
            {
                _controlPointX1 = value;
                _isDirty = true;
            }
            else
            {
                throw new ArgumentException("Invalid KeySpline X1 value. Must be >= 0.0 and <= 1.0.");
            }
        }
    }

    public float ControlPointY1
    {
        get => _controlPointY1;
        set
        {
            _controlPointY1 = value;
            _isDirty = true;
        }
    }

    public float ControlPointX2
    {
        get => _controlPointX2;
        set
        {
            if (IsValidXValue(value))
            {
                _controlPointX2 = value;
                _isDirty = true;
            }
            else
            {
                throw new ArgumentException("Invalid KeySpline X2 value. Must be >= 0.0 and <= 1.0.");
            }
        }
    }

    public float ControlPointY2
    {
        get => _controlPointY2;
        set
        {
            _controlPointY2 = value;
            _isDirty = true;
        }
    }

    public float GetSplineProgress(float linearProgress)
    {
        if (_isDirty)
        {
            Build();
        }

        if (!_isSpecified)
        {
            return linearProgress;
        }
        else
        {
            SetParameterFromX(linearProgress);

            return GetBezierValue(_by, _cy, _parameter);
        }
    }

    public bool IsValid()
    {
        return IsValidXValue(_controlPointX1) && IsValidXValue(_controlPointX2);
    }

    private static bool IsValidXValue(float value)
    {
        return value >= 0.0f && value <= 1.0f;
    }

    private void Build()
    {
        if (_controlPointX1 == 0 && _controlPointY1 == 0 && _controlPointX2 == 1 && _controlPointY2 == 1)
        {
            // This KeySpline would have no effect on the progress.
            _isSpecified = false;
        }
        else
        {
            _isSpecified = true;

            _parameter = 0;

            // X coefficients
            _bx = 3 * _controlPointX1;
            _cx = 3 * _controlPointX2;
            _cx_bx = 2 * (_cx - _bx);
            _three_Cx = 3 - _cx;

            // Y coefficients
            _by = 3 * _controlPointY1;
            _cy = 3 * _controlPointY2;
        }

        _isDirty = false;
    }

    private static float GetBezierValue(float b, float c, float t)
    {
        float s = 1.0f - t;
        float t2 = t * t;

        return b * t * s * s + c * t2 * s + t2 * t;
    }

    private void GetXAndDx(float t, out float x, out float dx)
    {
        float s = 1.0f - t;
        float t2 = t * t;
        float s2 = s * s;

        x = _bx * t * s2 + _cx * t2 * s + t2 * t;
        dx = _bx * s2 + _cx_bx * s * t + _three_Cx * t2;
    }

    private void SetParameterFromX(float time)
    {
        // Dynamic search interval to clamp with
        float bottom = 0;
        float top = 1;

        if (time == 0)
        {
            _parameter = 0;
        }
        else if (time == 1)
        {
            _parameter = 1;
        }
        else
        {
            // Loop while improving the guess
            while (top - bottom > Fuzz)
            {
                // Get x and dx/dt at the current parameter
                GetXAndDx(_parameter, out float x, out float dx);
                float absdx = MathF.Abs(dx);

                // Clamp down the search interval, relying on the monotonicity of X(t)
                if (x > time)
                {
                    top = _parameter;      // because parameter > solution
                }
                else
                {
                    bottom = _parameter;  // because parameter < solution
                }

                // The desired accuracy is in ultimately in y, not in x, so the
                // accuracy needs to be multiplied by dx/dy = (dx/dt) / (dy/dt).
                // But dy/dt <=3, so we omit that
                if (MathF.Abs(x - time) < Accuracy * absdx)
                {
                    break; // We're there
                }

                if (absdx > Fuzz)
                {
                    // Nonzero derivative, use Newton-Raphson to obtain the next guess
                    float next = _parameter - (x - time) / dx;

                    // If next guess is out of the search interval then clamp it in
                    if (next >= top)
                    {
                        _parameter = (_parameter + top) / 2;
                    }
                    else if (next <= bottom)
                    {
                        _parameter = (_parameter + bottom) / 2;
                    }
                    else
                    {
                        // Next guess is inside the search interval, accept it
                        _parameter = next;
                    }
                }
                else    // Zero derivative, halve the search interval
                {
                    _parameter = (bottom + top) / 2;
                }
            }
        }
    }
}
