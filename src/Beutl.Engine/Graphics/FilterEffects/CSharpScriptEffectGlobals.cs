using System.Numerics;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The script globals a <see cref="CSharpScriptEffect"/> script runs against (feature 004, contract A6,
/// contracts/breaking-changes.md). A script authors the declarative effect graph exactly like a compiled effect
/// author: it appends node descriptors through <see cref="Builder"/> (an <see cref="EffectGraphBuilder"/>). The
/// convenience vocabulary (<c>Blur</c>, <c>DropShadow</c>, <c>Saturate</c>, <c>ColorMatrix</c>, <c>Transform</c>,
/// …) mirrors the removed imperative context's method names, so legacy scripts migrate near-mechanically
/// (<c>Context.Blur(…)</c> → <c>Builder.Blur(…)</c>); custom canvas drawing stays available through
/// <see cref="EffectGraphBuilder.Geometry(System.Action{GeometrySession}, System.Nullable{BoundsContract}, object, bool)"/>.
/// Legacy <c>Context</c>/<c>Session</c> identifiers are diagnosed by <see cref="CSharpScriptEffect"/>'s Roslyn
/// validation without retaining public compatibility members on this globals type.
/// </summary>
public class CSharpScriptEffectGlobals
{
    public CSharpScriptEffectGlobals(EffectGraphBuilder builder, float progress, float duration, float time)
    {
        Builder = builder ?? throw new ArgumentNullException(nameof(builder));
        Progress = progress;
        Duration = duration;
        Time = time;
    }

    /// <summary>
    /// The declarative recording surface the script appends node descriptors to — the same
    /// <see cref="EffectGraphBuilder"/> a compiled effect author receives in <c>Describe</c>. Its convenience
    /// vocabulary and <see cref="EffectGraphBuilder.Geometry(System.Action{GeometrySession}, System.Nullable{BoundsContract}, object, bool)"/>
    /// escape hatch cover blur/shadow/color filtering and arbitrary canvas drawing.
    /// </summary>
    public EffectGraphBuilder Builder { get; }

    public float Time { get; }

    public float Duration { get; }

    public float Progress { get; }

    public double PI => Math.PI;

    public T Sin<T>(T x) where T : ITrigonometricFunctions<T> => T.Sin(x);
    public T Cos<T>(T x) where T : ITrigonometricFunctions<T> => T.Cos(x);
    public T Tan<T>(T x) where T : ITrigonometricFunctions<T> => T.Tan(x);
    public T Asin<T>(T x) where T : ITrigonometricFunctions<T> => T.Asin(x);
    public T Acos<T>(T x) where T : ITrigonometricFunctions<T> => T.Acos(x);
    public T Atan<T>(T x) where T : ITrigonometricFunctions<T> => T.Atan(x);
    public T Atan2<T>(T y, T x) where T : IFloatingPointIeee754<T> => T.Atan2(y, x);
    public T Sinh<T>(T x) where T : IHyperbolicFunctions<T> => T.Sinh(x);
    public T Cosh<T>(T x) where T : IHyperbolicFunctions<T> => T.Cosh(x);
    public T Tanh<T>(T x) where T : IHyperbolicFunctions<T> => T.Tanh(x);
    public T Sqrt<T>(T x) where T : IRootFunctions<T> => T.Sqrt(x);
    public T Pow<T>(T x, T y) where T : IPowerFunctions<T> => T.Pow(x, y);
    public T Exp<T>(T x) where T : IExponentialFunctions<T> => T.Exp(x);
    public T Log<T>(T x) where T : ILogarithmicFunctions<T> => T.Log(x);
    public T Log10<T>(T x) where T : ILogarithmicFunctions<T> => T.Log10(x);
    public T Log2<T>(T x) where T : ILogarithmicFunctions<T> => T.Log2(x);
    public T Abs<T>(T x) where T : INumberBase<T> => T.Abs(x);
    public T Floor<T>(T x) where T : IFloatingPoint<T> => T.Floor(x);
    public T Ceil<T>(T x) where T : IFloatingPoint<T> => T.Ceiling(x);
    public T Round<T>(T x) where T : IFloatingPoint<T> => T.Round(x);
    public T Round<T>(T x, int digits) where T : IFloatingPoint<T> => T.Round(x, digits);
    public T Min<T>(T x, T y) where T : INumber<T> => T.Min(x, y);
    public T Max<T>(T x, T y) where T : INumber<T> => T.Max(x, y);
    public T Clamp<T>(T value, T min, T max) where T : INumber<T> => T.Clamp(value, min, max);
    public int Sign<T>(T x) where T : INumber<T> => T.Sign(x);
    public T Truncate<T>(T x) where T : IFloatingPointIeee754<T> => T.Truncate(x);

    public T Lerp<T>(T a, T b, T t) where T : IFloatingPointIeee754<T> => T.Lerp(a, b, t);

    public T InverseLerp<T>(T a, T b, T value) where T : IFloatingPointIeee754<T> => (value - a) / (b - a);

    public T Remap<T>(T value, T fromMin, T fromMax, T toMin, T toMax) where T : IFloatingPointIeee754<T>
    {
        T t = InverseLerp(fromMin, fromMax, value);
        return Lerp(toMin, toMax, t);
    }

    public T Smoothstep<T>(T edge0, T edge1, T x) where T : IFloatingPointIeee754<T>, INumber<T>
    {
        T t = Clamp((x - edge0) / (edge1 - edge0), T.Zero, T.One);
        return t * t * (T.CreateChecked(3) - T.CreateChecked(2) * t);
    }

    public T Radians<T>(T degrees) where T : IFloatingPointIeee754<T> => degrees * T.CreateChecked(Math.PI) / T.CreateChecked(180);

    public T Degrees<T>(T radians) where T : IFloatingPointIeee754<T> => radians * T.CreateChecked(180) / T.CreateChecked(Math.PI);

    public T Mod<T>(T x, T y) where T : IFloatingPointIeee754<T> => x - y * T.Floor(x / y);

    public T Frac<T>(T x) where T : IFloatingPointIeee754<T> => x - T.Floor(x);

    public double Random(int seed)
    {
        var rng = new Random(seed);
        return rng.NextDouble();
    }

    public double Random(int seed, double min, double max)
    {
        var rng = new Random(seed);
        return min + rng.NextDouble() * (max - min);
    }

    public double FrameRandom() => Random((int)(Time * 1000));

    public double FrameRandom(double min, double max) => Random((int)(Time * 1000), min, max);
}
