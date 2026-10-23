using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using Beutl.JsonConverters;

namespace Beutl;

// A time or a duration as a whole number of flicks (1/705,600,000 s). Frames at the common rates (23.976 to
// 60 fps) and samples at 44.1, 48 and 96 kHz are whole numbers of flicks, so their conversions are exact.
[JsonConverter(typeof(MediaTimeJsonConverter))]
public readonly struct MediaTime(long flicks)
    : IEquatable<MediaTime>,
      IComparable<MediaTime>,
      IComparable,
      IComparisonOperators<MediaTime, MediaTime, bool>,
      IAdditionOperators<MediaTime, MediaTime, MediaTime>,
      ISubtractionOperators<MediaTime, MediaTime, MediaTime>,
      IUnaryNegationOperators<MediaTime, MediaTime>
{
    public const long FlicksPerSecond = 705_600_000;

    // A TimeSpan tick is 70.56 flicks.
    private const long FlicksPerTickNumerator = 1764;
    private const long FlicksPerTickDenominator = 25;

    public static MediaTime Zero => default;

    public static MediaTime MinValue => new(long.MinValue);

    public static MediaTime MaxValue => new(long.MaxValue);

    public long Flicks { get; } = flicks;

    // Exact when a frame is a whole number of flicks; otherwise the nearest flick.
    public static MediaTime FromFrames(long frame, Rational rate)
    {
        (Int128 numerator, Int128 denominator) = GetPositiveRate(rate);
        return new MediaTime(checked((long)UnitsToFlicks(frame, numerator, denominator)));
    }

    public long ToFrame(Rational rate, MidpointRounding rounding)
    {
        (Int128 numerator, Int128 denominator) = GetPositiveRate(rate);
        return ToUnits(numerator, denominator, rounding);
    }

    // The time of the frame that ToFrame picks.
    public MediaTime RoundToFrame(Rational rate, MidpointRounding rounding) => FromFrames(ToFrame(rate, rounding), rate);

    public static MediaTime FromSamples(long sample, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return new MediaTime(checked((long)UnitsToFlicks(sample, sampleRate, 1)));
    }

    public long ToSamples(int sampleRate, MidpointRounding rounding)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return ToUnits(sampleRate, 1, rounding);
    }

    public static MediaTime FromTimeSpan(TimeSpan value)
    {
        Int128 flicks = RoundDivide(
            (Int128)value.Ticks * FlicksPerTickNumerator, FlicksPerTickDenominator, MidpointRounding.AwayFromZero);
        return new MediaTime(checked((long)flicks));
    }

    public TimeSpan ToTimeSpan()
    {
        Int128 ticks = RoundDivide(
            (Int128)Flicks * FlicksPerTickDenominator, FlicksPerTickNumerator, MidpointRounding.AwayFromZero);
        return TimeSpan.FromTicks((long)ticks);
    }

    // Directed conversions for TimeSpan strings, of earlier files and agent documents; a tick outside the range of
    // MediaTime is clamped.
    internal static MediaTime FromTicks(long ticks, MidpointRounding rounding)
    {
        Int128 flicks = RoundDivide((Int128)ticks * FlicksPerTickNumerator, FlicksPerTickDenominator, rounding);
        return new MediaTime((long)Int128.Clamp(flicks, long.MinValue, long.MaxValue));
    }

    internal long ToTicks(MidpointRounding rounding)
        => (long)RoundDivide((Int128)Flicks * FlicksPerTickDenominator, FlicksPerTickNumerator, rounding);

    public static MediaTime FromSeconds(double seconds)
    {
        if (double.IsNaN(seconds))
            throw new ArgumentException("The value is not a number.", nameof(seconds));

        double flicks = Math.Round(seconds * FlicksPerSecond, MidpointRounding.AwayFromZero);
        // 2^63, the first double past long.MaxValue.
        if (flicks < long.MinValue || flicks >= 9_223_372_036_854_775_808d)
            throw new OverflowException("The value is outside the range of MediaTime.");

        return new MediaTime((long)flicks);
    }

    public double ToSeconds() => (double)Flicks / FlicksPerSecond;

    public static MediaTime operator +(MediaTime left, MediaTime right) => new(checked(left.Flicks + right.Flicks));

    public static MediaTime operator -(MediaTime left, MediaTime right) => new(checked(left.Flicks - right.Flicks));

    public static MediaTime operator -(MediaTime value) => new(checked(-value.Flicks));

    public static bool operator ==(MediaTime left, MediaTime right) => left.Flicks == right.Flicks;

    public static bool operator !=(MediaTime left, MediaTime right) => left.Flicks != right.Flicks;

    public static bool operator <(MediaTime left, MediaTime right) => left.Flicks < right.Flicks;

    public static bool operator >(MediaTime left, MediaTime right) => left.Flicks > right.Flicks;

    public static bool operator <=(MediaTime left, MediaTime right) => left.Flicks <= right.Flicks;

    public static bool operator >=(MediaTime left, MediaTime right) => left.Flicks >= right.Flicks;

    public bool Equals(MediaTime other) => Flicks == other.Flicks;

    public override bool Equals(object? obj) => obj is MediaTime other && Equals(other);

    public override int GetHashCode() => Flicks.GetHashCode();

    public int CompareTo(MediaTime other) => Flicks.CompareTo(other.Flicks);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        MediaTime other => CompareTo(other),
        _ => throw new ArgumentException($"The object is not a {nameof(MediaTime)}.", nameof(obj)),
    };

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Flicks} flicks");

    private static (Int128 Numerator, Int128 Denominator) GetPositiveRate(Rational rate)
    {
        Int128 numerator = rate.Numerator;
        Int128 denominator = rate.Denominator;
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        if (numerator <= 0 || denominator == 0)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The rate must be positive and finite.");

        return (numerator, denominator);
    }

    // A unit is a frame or a sample, and numerator / denominator of them make a second.
    private static Int128 UnitsToFlicks(Int128 units, Int128 numerator, Int128 denominator)
        => RoundDivide(checked(units * denominator * FlicksPerSecond), numerator, MidpointRounding.AwayFromZero);

    private long ToUnits(Int128 numerator, Int128 denominator, MidpointRounding rounding)
    {
        Int128 dividend = (Int128)Flicks * numerator;
        Int128 divisor = checked(denominator * FlicksPerSecond);
        // A unit's own time maps back to that unit under every rounding, also when it was rounded to a flick.
        Int128 nearest = RoundDivide(dividend, divisor, MidpointRounding.AwayFromZero);
        if (UnitsToFlicks(nearest, numerator, denominator) == Flicks)
        {
            ValidateRounding(rounding);
            return checked((long)nearest);
        }

        return checked((long)RoundDivide(dividend, divisor, rounding));
    }

    private static Int128 RoundDivide(Int128 dividend, Int128 divisor, MidpointRounding rounding)
    {
        ValidateRounding(rounding);
        (Int128 quotient, Int128 remainder) = Int128.DivRem(dividend, divisor);
        if (remainder == 0)
            return quotient;

        // The remainder has the dividend's sign, and the divisor is positive.
        Int128 step = remainder < 0 ? -1 : 1;
        Int128 twice = Int128.Abs(remainder) * 2;
        return rounding switch
        {
            MidpointRounding.ToZero => quotient,
            MidpointRounding.ToNegativeInfinity => step < 0 ? quotient - 1 : quotient,
            MidpointRounding.ToPositiveInfinity => step > 0 ? quotient + 1 : quotient,
            _ when twice > divisor => quotient + step,
            _ when twice < divisor => quotient,
            MidpointRounding.AwayFromZero => quotient + step,
            _ => Int128.IsEvenInteger(quotient) ? quotient : quotient + step,
        };
    }

    private static void ValidateRounding(MidpointRounding rounding)
    {
        if ((uint)rounding > (uint)MidpointRounding.ToPositiveInfinity)
            throw new ArgumentOutOfRangeException(nameof(rounding), rounding, null);
    }
}
