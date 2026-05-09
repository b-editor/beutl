using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;

namespace Beutl.Engine.Expressions;

public class ExpressionGlobals
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> s_getValueMethods = new();
    private readonly ExpressionContext _context;

    public ExpressionGlobals(ExpressionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        EngineObject? obj = _context.CurrentProperty.GetOwnerObject();
        Start = obj?.TimeRange.Start.TotalSeconds ?? 0;
        Duration = obj?.TimeRange.Duration.TotalSeconds ?? 0;
        Time = _context.Time.TotalSeconds - Start;
    }

    public double Time { get; private set; }

    public double Start { get; }

    public double Duration { get; }

    public double Progress => Duration > 0 ? (double)Time / Duration : 0;

    public double PI => Math.PI;

    public T GetProperty<T>(string path)
    {
        if (_context.TryGetPropertyValue<T>(path, out var value))
        {
            return value!;
        }
        return default!;
    }
    public T GetProperty<T>(Guid objectId, string propertyName)
    {
        string path = $"{objectId}.{propertyName}";
        if (_context.TryGetPropertyValue<T>(path, out var value))
        {
            return value!;
        }
        return default!;
    }

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

    public double Wiggle(double freq, double amp) => Wiggle(freq, amp, 1, 0.5, Time);

    public double Wiggle(double freq, double amp, int octaves) => Wiggle(freq, amp, octaves, 0.5, Time);

    public double Wiggle(double freq, double amp, int octaves, double ampMult) => Wiggle(freq, amp, octaves, ampMult, Time);

    public double Wiggle(double freq, double amp, int octaves, double ampMult, double t)
        => WiggleSample(freq, amp, octaves, ampMult, t, 0);

    public Vector2 Wiggle(Vector2 baseValue, double freq, double amp)
        => Wiggle(baseValue, freq, amp, 1, 0.5, Time);

    public Vector2 Wiggle(Vector2 baseValue, double freq, double amp, int octaves, double ampMult, double t)
    {
        return new Vector2(
            baseValue.X + (float)WiggleSample(freq, amp, octaves, ampMult, t, 0),
            baseValue.Y + (float)WiggleSample(freq, amp, octaves, ampMult, t, 1));
    }

    public Vector3 Wiggle(Vector3 baseValue, double freq, double amp)
        => Wiggle(baseValue, freq, amp, 1, 0.5, Time);

    public Vector3 Wiggle(Vector3 baseValue, double freq, double amp, int octaves, double ampMult, double t)
    {
        return new Vector3(
            baseValue.X + (float)WiggleSample(freq, amp, octaves, ampMult, t, 0),
            baseValue.Y + (float)WiggleSample(freq, amp, octaves, ampMult, t, 1),
            baseValue.Z + (float)WiggleSample(freq, amp, octaves, ampMult, t, 2));
    }

    public Vector4 Wiggle(Vector4 baseValue, double freq, double amp)
        => Wiggle(baseValue, freq, amp, 1, 0.5, Time);

    public Vector4 Wiggle(Vector4 baseValue, double freq, double amp, int octaves, double ampMult, double t)
    {
        return new Vector4(
            baseValue.X + (float)WiggleSample(freq, amp, octaves, ampMult, t, 0),
            baseValue.Y + (float)WiggleSample(freq, amp, octaves, ampMult, t, 1),
            baseValue.Z + (float)WiggleSample(freq, amp, octaves, ampMult, t, 2),
            baseValue.W + (float)WiggleSample(freq, amp, octaves, ampMult, t, 3));
    }

    private static double WiggleSample(double freq, double amp, int octaves, double ampMult, double t, int seedOffset)
    {
        if (octaves < 1) octaves = 1;
        double sum = 0.0;
        double currentAmp = 1.0;
        double currentFreq = 1.0;
        double normalizer = 0.0;
        for (int i = 0; i < octaves; i++)
        {
            double seed = (i + 1) * 12.9898 + seedOffset * 78.233;
            double phase = Math.Sin(seed) * 43758.5453;
            phase -= Math.Floor(phase);
            sum += currentAmp * Math.Sin((t * freq * currentFreq + phase) * 2.0 * Math.PI);
            normalizer += currentAmp;
            currentAmp *= ampMult;
            currentFreq *= 2.0;
        }
        if (normalizer <= 0) normalizer = 1.0;
        return (sum / normalizer) * amp;
    }

    public double LoopOut(double t, double period) => LoopOut(t, period, "cycle");

    public double LoopOut(double t, double period, string type)
    {
        if (period <= 0) return t;
        if (type == null) return Mod(t, period);
        return type.ToLowerInvariant() switch
        {
            "cycle" => Mod(t, period),
            "pingpong" => PingPong(t, period),
            "offset" => throw new NotSupportedException(
                "LoopOut 'offset' mode requires keyframe access and is not supported in this expression engine."),
            "continue" => throw new NotSupportedException(
                "LoopOut 'continue' mode requires keyframe access and is not supported in this expression engine."),
            _ => throw new ArgumentException(
                $"Unknown LoopOut type '{type}'. Valid values are 'cycle' or 'pingpong'.", nameof(type)),
        };
    }

    public double LoopIn(double t, double period) => LoopIn(t, period, "cycle");

    public double LoopIn(double t, double period, string type) => LoopOut(t, period, type);

    private static double PingPong(double t, double period)
    {
        if (period <= 0) return 0.0;
        double m = t - 2.0 * period * Math.Floor(t / (2.0 * period));
        if (m > period) m = 2.0 * period - m;
        return m;
    }

    public T ValueAtTime<T>(double t)
    {
        if (_context.CurrentProperty is not IProperty<T> typedProperty)
        {
            return default!;
        }
        TimeSpan saved = _context.Time;
        try
        {
            _context.Time = TimeSpan.FromSeconds(t + Start);
            return typedProperty.GetValue(_context);
        }
        finally
        {
            _context.Time = saved;
        }
    }

    public T ValueAtTime<T>(Guid objectId, string propertyName, double t)
    {
        TimeSpan savedTime = _context.Time;
        IProperty savedProperty = _context.CurrentProperty;
        try
        {
            _context.Time = TimeSpan.FromSeconds(t + Start);
            if (FindProperty(objectId, propertyName) is IProperty<T> typed)
            {
                _context.CurrentProperty = typed;
                return typed.GetValue(_context);
            }
            // CoreProperty fallback (no expressions, CurrentProperty unchanged).
            if (_context.TryGetPropertyValue<T>(objectId, propertyName, out var value))
            {
                return value!;
            }
            return default!;
        }
        finally
        {
            _context.Time = savedTime;
            _context.CurrentProperty = savedProperty;
        }
    }

    public double ValueAtTime(double t) => ConvertToDouble(ValueAtTimeAsObject(t));

    public double ValueAtTime(Guid objectId, string propertyName, double t)
        => ConvertToDouble(ValueAtTimeAsObject(objectId, propertyName, t));

    public Vector2 ValueAtTimeAsVector2(double t) => ValueAtTime<Vector2>(t);
    public Vector2 ValueAtTimeAsVector2(Guid objectId, string propertyName, double t)
        => ValueAtTime<Vector2>(objectId, propertyName, t);

    public Vector3 ValueAtTimeAsVector3(double t) => ValueAtTime<Vector3>(t);
    public Vector3 ValueAtTimeAsVector3(Guid objectId, string propertyName, double t)
        => ValueAtTime<Vector3>(objectId, propertyName, t);

    public Vector4 ValueAtTimeAsVector4(double t) => ValueAtTime<Vector4>(t);
    public Vector4 ValueAtTimeAsVector4(Guid objectId, string propertyName, double t)
        => ValueAtTime<Vector4>(objectId, propertyName, t);

    public object? ValueAtTimeAsObject(double t)
    {
        IProperty current = _context.CurrentProperty;
        TimeSpan saved = _context.Time;
        try
        {
            _context.Time = TimeSpan.FromSeconds(t + Start);
            return InvokeGetValue(current, _context);
        }
        finally
        {
            _context.Time = saved;
        }
    }

    public object? ValueAtTimeAsObject(Guid objectId, string propertyName, double t)
    {
        TimeSpan savedTime = _context.Time;
        IProperty savedProperty = _context.CurrentProperty;
        try
        {
            _context.Time = TimeSpan.FromSeconds(t + Start);
            if (_context.PropertyLookup.FindById(objectId) is not CoreObject coreObject)
            {
                return null;
            }
            if (coreObject is EngineObject engineObject)
            {
                IProperty? property = engineObject.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                if (property is not null)
                {
                    _context.CurrentProperty = property;
                    return InvokeGetValue(property, _context);
                }
            }
            // CoreProperty fallback for non-engine objects (or engine objects
            // exposing only registered CoreProperties).
            CoreProperty? coreProperty = PropertyRegistry.GetRegistered(coreObject.GetType())
                .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            if (coreProperty is not null)
            {
                return coreObject.GetValue(coreProperty);
            }
            return null;
        }
        finally
        {
            _context.Time = savedTime;
            _context.CurrentProperty = savedProperty;
        }
    }

    private IProperty? FindProperty(Guid objectId, string propertyName)
    {
        if (_context.PropertyLookup.FindById(objectId) is not EngineObject engineObject)
        {
            return null;
        }
        return engineObject.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static object? InvokeGetValue(IProperty property, ExpressionContext context)
    {
        MethodInfo? method = s_getValueMethods.GetOrAdd(property.GetType(), static t =>
        {
            Type? iface = t.GetInterfaces().FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IProperty<>));
            return iface?.GetMethod("GetValue", new[] { typeof(Beutl.Composition.CompositionContext) });
        });
        return method?.Invoke(property, [context]);
    }

    private static double ConvertToDouble(object? value)
    {
        if (value == null) return 0.0;
        try
        {
            return Convert.ToDouble(value);
        }
        catch (InvalidCastException)
        {
            return 0.0;
        }
        catch (FormatException)
        {
            return 0.0;
        }
    }

    // AE-compatible side-effecting form: quantizes Time so that subsequent
    // helpers (Wiggle, etc.) read the posterized value through `Time`.
    public double PosterizeTime(double fps)
    {
        if (fps <= 0) return Time;
        Time = Math.Floor(Time * fps) / fps;
        return Time;
    }

    // Pure form: quantize an arbitrary time value without touching `Time`.
    public double PosterizeTime(double fps, double t)
    {
        if (fps <= 0) return t;
        return Math.Floor(t * fps) / fps;
    }

    public double Linear(double t, double tMin, double tMax, double valueMin, double valueMax)
    {
        if (tMax == tMin) return valueMin;
        if (t <= tMin) return valueMin;
        if (t >= tMax) return valueMax;
        double n = (t - tMin) / (tMax - tMin);
        return valueMin + n * (valueMax - valueMin);
    }

    public double Linear(double t, double valueMin, double valueMax)
    {
        return Linear(t, 0.0, 1.0, valueMin, valueMax);
    }

    public double Ease(double t, double tMin, double tMax, double valueMin, double valueMax)
    {
        if (tMax == tMin) return valueMin;
        if (t <= tMin) return valueMin;
        if (t >= tMax) return valueMax;
        double n = (t - tMin) / (tMax - tMin);
        double eased = n * n * (3.0 - 2.0 * n);
        return valueMin + eased * (valueMax - valueMin);
    }

    public double Ease(double t, double valueMin, double valueMax)
    {
        return Ease(t, 0.0, 1.0, valueMin, valueMax);
    }

    public double EaseIn(double t, double tMin, double tMax, double valueMin, double valueMax)
    {
        if (tMax == tMin) return valueMin;
        if (t <= tMin) return valueMin;
        if (t >= tMax) return valueMax;
        double n = (t - tMin) / (tMax - tMin);
        double eased = n * n;
        return valueMin + eased * (valueMax - valueMin);
    }

    public double EaseIn(double t, double valueMin, double valueMax)
    {
        return EaseIn(t, 0.0, 1.0, valueMin, valueMax);
    }

    public double EaseOut(double t, double tMin, double tMax, double valueMin, double valueMax)
    {
        if (tMax == tMin) return valueMin;
        if (t <= tMin) return valueMin;
        if (t >= tMax) return valueMax;
        double n = (t - tMin) / (tMax - tMin);
        double eased = 1.0 - (1.0 - n) * (1.0 - n);
        return valueMin + eased * (valueMax - valueMin);
    }

    public double EaseOut(double t, double valueMin, double valueMax)
    {
        return EaseOut(t, 0.0, 1.0, valueMin, valueMax);
    }

    public double Length(double v) => Math.Abs(v);

    public double Length(Vector2 v) => v.Length();

    public double Length(Vector3 v) => v.Length();

    public double Length(Vector4 v) => v.Length();

    public double Length(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

    public double Length(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

    public double Length(Vector4 a, Vector4 b) => Vector4.Distance(a, b);

    public double Length(double a, double b) => Math.Abs(b - a);

    public double DegToRad(double x) => x * Math.PI / 180.0;

    public double RadToDeg(double x) => x * 180.0 / Math.PI;

    public double wiggle(double freq, double amp) => Wiggle(freq, amp);
    public double wiggle(double freq, double amp, int octaves) => Wiggle(freq, amp, octaves);
    public double wiggle(double freq, double amp, int octaves, double ampMult) => Wiggle(freq, amp, octaves, ampMult);
    public double wiggle(double freq, double amp, int octaves, double ampMult, double t) => Wiggle(freq, amp, octaves, ampMult, t);
    public Vector2 wiggle(Vector2 baseValue, double freq, double amp) => Wiggle(baseValue, freq, amp);
    public Vector2 wiggle(Vector2 baseValue, double freq, double amp, int octaves, double ampMult, double t) => Wiggle(baseValue, freq, amp, octaves, ampMult, t);
    public Vector3 wiggle(Vector3 baseValue, double freq, double amp) => Wiggle(baseValue, freq, amp);
    public Vector3 wiggle(Vector3 baseValue, double freq, double amp, int octaves, double ampMult, double t) => Wiggle(baseValue, freq, amp, octaves, ampMult, t);
    public Vector4 wiggle(Vector4 baseValue, double freq, double amp) => Wiggle(baseValue, freq, amp);
    public Vector4 wiggle(Vector4 baseValue, double freq, double amp, int octaves, double ampMult, double t) => Wiggle(baseValue, freq, amp, octaves, ampMult, t);

    public double loopOut(double t, double period) => LoopOut(t, period);
    public double loopOut(double t, double period, string type) => LoopOut(t, period, type);

    public double loopIn(double t, double period) => LoopIn(t, period);
    public double loopIn(double t, double period, string type) => LoopIn(t, period, type);

    public T valueAtTime<T>(double t) => ValueAtTime<T>(t);
    public T valueAtTime<T>(Guid objectId, string propertyName, double t) => ValueAtTime<T>(objectId, propertyName, t);
    public double valueAtTime(double t) => ValueAtTime(t);
    public double valueAtTime(Guid objectId, string propertyName, double t) => ValueAtTime(objectId, propertyName, t);
    public Vector2 valueAtTimeAsVector2(double t) => ValueAtTimeAsVector2(t);
    public Vector2 valueAtTimeAsVector2(Guid objectId, string propertyName, double t) => ValueAtTimeAsVector2(objectId, propertyName, t);
    public Vector3 valueAtTimeAsVector3(double t) => ValueAtTimeAsVector3(t);
    public Vector3 valueAtTimeAsVector3(Guid objectId, string propertyName, double t) => ValueAtTimeAsVector3(objectId, propertyName, t);
    public Vector4 valueAtTimeAsVector4(double t) => ValueAtTimeAsVector4(t);
    public Vector4 valueAtTimeAsVector4(Guid objectId, string propertyName, double t) => ValueAtTimeAsVector4(objectId, propertyName, t);
    public object? valueAtTimeAsObject(double t) => ValueAtTimeAsObject(t);
    public object? valueAtTimeAsObject(Guid objectId, string propertyName, double t) => ValueAtTimeAsObject(objectId, propertyName, t);

    public double posterizeTime(double fps) => PosterizeTime(fps);
    public double posterizeTime(double fps, double t) => PosterizeTime(fps, t);

    public double linear(double t, double tMin, double tMax, double valueMin, double valueMax) => Linear(t, tMin, tMax, valueMin, valueMax);
    public double linear(double t, double valueMin, double valueMax) => Linear(t, valueMin, valueMax);

    public double ease(double t, double tMin, double tMax, double valueMin, double valueMax) => Ease(t, tMin, tMax, valueMin, valueMax);
    public double ease(double t, double valueMin, double valueMax) => Ease(t, valueMin, valueMax);

    public double easeIn(double t, double tMin, double tMax, double valueMin, double valueMax) => EaseIn(t, tMin, tMax, valueMin, valueMax);
    public double easeIn(double t, double valueMin, double valueMax) => EaseIn(t, valueMin, valueMax);

    public double easeOut(double t, double tMin, double tMax, double valueMin, double valueMax) => EaseOut(t, tMin, tMax, valueMin, valueMax);
    public double easeOut(double t, double valueMin, double valueMax) => EaseOut(t, valueMin, valueMax);

    public double length(double v) => Length(v);
    public double length(Vector2 v) => Length(v);
    public double length(Vector3 v) => Length(v);
    public double length(Vector4 v) => Length(v);
    public double length(Vector2 a, Vector2 b) => Length(a, b);
    public double length(Vector3 a, Vector3 b) => Length(a, b);
    public double length(Vector4 a, Vector4 b) => Length(a, b);
    public double length(double a, double b) => Length(a, b);

    public double clamp(double v, double min, double max) => Clamp(v, min, max);

    public double mod(double a, double b) => Mod(a, b);

    public double degToRad(double x) => DegToRad(x);

    public double radToDeg(double x) => RadToDeg(x);
}
