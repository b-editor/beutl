using System.Numerics;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The script globals a <see cref="CSharpScriptEffect"/> script runs against (feature 004, contract A6,
/// contracts/breaking-changes.md). A script authors the declarative effect graph exactly like a compiled effect
/// author: it appends node descriptors through <see cref="Builder"/> (an <see cref="EffectGraphBuilder"/>). The
/// convenience vocabulary (<c>Blur</c>, <c>DropShadow</c>, <c>Saturate</c>, <c>ColorMatrix</c>, <c>Transform</c>,
/// …) mirrors the removed imperative context's method names, so legacy scripts migrate near-mechanically
/// (<c>Context.Blur(…)</c> → <c>Builder.Blur(…)</c>); custom canvas drawing stays available through
/// <see cref="EffectGraphBuilder.Geometry(System.Action{GeometrySession}, System.Nullable{BoundsContract}, object)"/>.
/// A legacy script that references the removed <c>Context</c> or <c>Session</c> globals fails to compile with a
/// diagnostic pointing at the migration guide — never silently wrong output.
/// </summary>
public class CSharpScriptEffectGlobals
{
    internal const string MigrationDiagnostic =
        "CSharpScriptEffect no longer exposes FilterEffectContext. Author the declarative effect graph through "
        + "'Builder' (EffectGraphBuilder): e.g. 'Context.Blur(...)' becomes 'Builder.Blur(...)'. See the migration "
        + "guide at docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md.";

    internal const string SessionMigrationDiagnostic =
        "The 'Session' (GeometrySession) global was replaced by 'Builder' (EffectGraphBuilder). Draw through "
        + "'Builder.Geometry(session => { ... })' and apply filters with 'Builder.Blur/DropShadow/Saturate/...'. "
        + "See the migration guide at docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md.";

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
    /// vocabulary and <see cref="EffectGraphBuilder.Geometry(System.Action{GeometrySession}, System.Nullable{BoundsContract}, object)"/>
    /// escape hatch cover blur/shadow/color filtering and arbitrary canvas drawing.
    /// </summary>
    public EffectGraphBuilder Builder { get; }

    /// <summary>
    /// Removed surface, typed <see cref="object"/> because <c>FilterEffectContext</c> itself is deleted. This is the
    /// FR-013 script diagnostic (an audit-sanctioned <c>[Obsolete]</c> member): referencing <c>Context</c> stays a
    /// compile-time error naming <c>Builder</c> and the migration guide; it never runs.
    /// </summary>
    [Obsolete(MigrationDiagnostic, error: true)]
    public object Context =>
        throw new NotSupportedException(MigrationDiagnostic);

    /// <summary>
    /// Removed surface: the <c>GeometrySession</c> globals only ever existed on the unreleased 004 branch and were
    /// replaced by <see cref="Builder"/> on that same branch. Kept as a one-line <c>[Obsolete]</c> diagnostic so a
    /// script written against the interim <c>Session</c> global fails to compile with a precise pointer to
    /// <c>Builder.Geometry(...)</c> instead of an opaque "does not exist" error; it never runs.
    /// </summary>
    [Obsolete(SessionMigrationDiagnostic, error: true)]
    public object Session =>
        throw new NotSupportedException(SessionMigrationDiagnostic);

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
