using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Beutl.Utilities;

namespace Beutl.Animation;

// https://github.com/AvaloniaUI/Avalonia/blob/d3b21f589389b8bedfa75ed06d546658745e2089/src/Avalonia.Animation/KeySpline.cs#L20
public class KeySpline
{
    // Evaluations use one immutable set of control points and coefficients.
    private readonly Lock _updateLock = new();
    private SplineState _state;

    // constants
    private const float Accuracy = 0.001f;   // 1/3 the desired accuracy in X
    private const float Fuzz = 0.000001f;    // computational zero

    public KeySpline() : this(0, 0, 1, 1)
    {
    }

    public KeySpline(float x1, float y1, float x2, float y2)
    {
        _state = new SplineState(x1, y1, x2, y2);
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
        get => Volatile.Read(ref _state).ControlPointX1;
        set
        {
            if (!IsValidXValue(value))
            {
                throw new ArgumentException("Invalid KeySpline X1 value. Must be >= 0.0 and <= 1.0.");
            }

            lock (_updateLock)
            {
                SplineState state = _state;
                Volatile.Write(ref _state,
                    new SplineState(value, state.ControlPointY1, state.ControlPointX2, state.ControlPointY2));
            }
        }
    }

    public float ControlPointY1
    {
        get => Volatile.Read(ref _state).ControlPointY1;
        set
        {
            lock (_updateLock)
            {
                SplineState state = _state;
                Volatile.Write(ref _state,
                    new SplineState(state.ControlPointX1, value, state.ControlPointX2, state.ControlPointY2));
            }
        }
    }

    public float ControlPointX2
    {
        get => Volatile.Read(ref _state).ControlPointX2;
        set
        {
            if (!IsValidXValue(value))
            {
                throw new ArgumentException("Invalid KeySpline X2 value. Must be >= 0.0 and <= 1.0.");
            }

            lock (_updateLock)
            {
                SplineState state = _state;
                Volatile.Write(ref _state,
                    new SplineState(state.ControlPointX1, state.ControlPointY1, value, state.ControlPointY2));
            }
        }
    }

    public float ControlPointY2
    {
        get => Volatile.Read(ref _state).ControlPointY2;
        set
        {
            lock (_updateLock)
            {
                SplineState state = _state;
                Volatile.Write(ref _state,
                    new SplineState(state.ControlPointX1, state.ControlPointY1, state.ControlPointX2, value));
            }
        }
    }

    public float GetSplineProgress(float linearProgress)
    {
        SplineState state = Volatile.Read(ref _state);
        if (!state.IsSpecified)
        {
            return linearProgress;
        }
        else
        {
            float parameter = GetParameterFromX(state, linearProgress);
            return GetBezierValue(state.By, state.Cy, parameter);
        }
    }

    public bool IsValid()
    {
        SplineState state = Volatile.Read(ref _state);
        return IsValidXValue(state.ControlPointX1) && IsValidXValue(state.ControlPointX2);
    }

    private static bool IsValidXValue(float value)
    {
        return value >= 0.0f && value <= 1.0f;
    }

    private static float GetBezierValue(float b, float c, float t)
    {
        float s = 1.0f - t;
        float t2 = t * t;

        return b * t * s * s + c * t2 * s + t2 * t;
    }

    private static void GetXAndDx(SplineState state, float t, out float x, out float dx)
    {
        float s = 1.0f - t;
        float t2 = t * t;
        float s2 = s * s;

        x = state.Bx * t * s2 + state.Cx * t2 * s + t2 * t;
        dx = state.Bx * s2 + state.CxBx * s * t + state.ThreeCx * t2;
    }

    private static float GetParameterFromX(SplineState state, float time)
    {
        // Dynamic search interval to clamp with
        float bottom = 0;
        float top = 1;
        float parameter = 0;

        if (time == 0)
        {
            return 0;
        }
        else if (time == 1)
        {
            return 1;
        }
        else
        {
            // Loop while improving the guess
            while (top - bottom > Fuzz)
            {
                // Get x and dx/dt at the current parameter
                GetXAndDx(state, parameter, out float x, out float dx);
                float absdx = MathF.Abs(dx);

                // Clamp down the search interval, relying on the monotonicity of X(t)
                if (x > time)
                {
                    top = parameter;      // because parameter > solution
                }
                else
                {
                    bottom = parameter;  // because parameter < solution
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
                    float next = parameter - (x - time) / dx;

                    // If next guess is out of the search interval then clamp it in
                    if (next >= top)
                    {
                        parameter = (parameter + top) / 2;
                    }
                    else if (next <= bottom)
                    {
                        parameter = (parameter + bottom) / 2;
                    }
                    else
                    {
                        // Next guess is inside the search interval, accept it
                        parameter = next;
                    }
                }
                else    // Zero derivative, halve the search interval
                {
                    parameter = (bottom + top) / 2;
                }
            }
        }

        return parameter;
    }

    private sealed class SplineState
    {
        public SplineState(float x1, float y1, float x2, float y2)
        {
            ControlPointX1 = x1;
            ControlPointY1 = y1;
            ControlPointX2 = x2;
            ControlPointY2 = y2;
            IsSpecified = x1 != 0 || y1 != 0 || x2 != 1 || y2 != 1;

            // Cache the coefficients together with the control points.
            Bx = 3 * x1;
            Cx = 3 * x2;
            CxBx = 2 * (Cx - Bx);
            ThreeCx = 3 - Cx;
            By = 3 * y1;
            Cy = 3 * y2;
        }

        public float ControlPointX1 { get; }
        public float ControlPointY1 { get; }
        public float ControlPointX2 { get; }
        public float ControlPointY2 { get; }
        public bool IsSpecified { get; }
        public float Bx { get; }
        public float Cx { get; }
        public float CxBx { get; }
        public float ThreeCx { get; }
        public float By { get; }
        public float Cy { get; }
    }
}
