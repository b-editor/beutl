namespace Beutl.Media;

public readonly struct TimeRange : IEquatable<TimeRange>
{
    public static readonly TimeRange Empty = new();

    public TimeRange()
    {
        Start = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
    }

    public TimeRange(TimeSpan start, TimeSpan duration)
    {
        Start = start;
        Duration = duration;
    }

    public TimeRange(TimeSpan duration)
    {
        Start = TimeSpan.Zero;
        Duration = duration;
    }

    public TimeSpan Start { get; }

    public TimeSpan Duration { get; }

    public TimeSpan End => Start + Duration;

    public bool IsEmpty => Start > End;

    public static TimeRange FromSeconds(double start, double duration)
    {
        return new TimeRange(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(duration));
    }

    public static TimeRange FromSeconds(double duration)
    {
        return new TimeRange(TimeSpan.FromSeconds(duration));
    }

    public override string ToString()
    {
        return $"{Start:hh\\:mm\\:ss\\.ff}, {Duration:hh\\:mm\\:ss\\.ff}";
    }

    public bool Contains(TimeSpan time)
    {
        return time >= Start && time < End;
    }

    public bool Contains(TimeRange time)
    {
        return Contains(time.Start) && Contains(time.End);
    }

    public TimeRange Intersect(TimeRange time)
    {
        TimeSpan newStart = time.Start > Start ? time.Start : Start;
        TimeSpan newEnd = time.Duration > Duration ? time.End : End;

        if (newStart < newEnd)
        {
            return new TimeRange(newStart, newEnd - newStart);
        }
        else
        {
            return Empty;
        }
    }

    public bool Intersects(TimeRange time)
    {
        return time.Start < End && Start < time.End;
    }

    public TimeRange Union(TimeRange time)
    {
        if (IsEmpty)
        {
            return time;
        }
        else if (time.IsEmpty)
        {
            return this;
        }
        else
        {
            long st = Math.Min(Start.Ticks, time.Start.Ticks);
            long ed = Math.Max(End.Ticks, time.End.Ticks);

            return new TimeRange(TimeSpan.FromTicks(st), TimeSpan.FromTicks(ed - st));
        }
    }

    public TimeRange WithStart(TimeSpan start)
    {
        return new TimeRange(start, Duration);
    }

    public TimeRange WithDuration(TimeSpan duration)
    {
        return new TimeRange(Start, duration);
    }

    public TimeRange AddStart(TimeSpan ts)
    {
        return new TimeRange(Start + ts, Duration);
    }
    
    public TimeRange SubtractStart(TimeSpan ts)
    {
        return new TimeRange(Start - ts, Duration);
    }

    public override bool Equals(object? obj) => obj is TimeRange range && Equals(range);

    public bool Equals(TimeRange other) => Start.Equals(other.Start) && Duration.Equals(other.Duration);

    public override int GetHashCode() => HashCode.Combine(Start, Duration);

    public static bool operator ==(TimeRange left, TimeRange right) => left.Equals(right);

    public static bool operator !=(TimeRange left, TimeRange right) => !(left == right);
}
