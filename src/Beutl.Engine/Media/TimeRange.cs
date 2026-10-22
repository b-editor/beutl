namespace Beutl.Media;

public readonly struct TimeRange : IEquatable<TimeRange>
{
    public static readonly TimeRange Empty = new();

    public TimeRange()
    {
        Start = MediaTime.Zero;
        Duration = MediaTime.Zero;
    }

    public TimeRange(MediaTime start, MediaTime duration)
    {
        Start = start;
        Duration = duration;
    }

    public TimeRange(MediaTime duration)
    {
        Start = MediaTime.Zero;
        Duration = duration;
    }

    public MediaTime Start { get; }

    public MediaTime Duration { get; }

    public MediaTime End => Start + Duration;

    public bool IsEmpty => Duration <= MediaTime.Zero;

    public static TimeRange FromSeconds(double start, double duration)
    {
        return new TimeRange(MediaTime.FromSeconds(start), MediaTime.FromSeconds(duration));
    }

    public static TimeRange FromSeconds(double duration)
    {
        return new TimeRange(MediaTime.FromSeconds(duration));
    }

    public static TimeRange FromRange(MediaTime start, MediaTime end)
    {
        return new TimeRange(start, end - start);
    }

    public override string ToString()
    {
        return $"{Start.ToTimeSpan():hh\\:mm\\:ss\\.ff}, {Duration.ToTimeSpan():hh\\:mm\\:ss\\.ff}";
    }

    public bool Contains(MediaTime time)
    {
        return time >= Start && time < End;
    }

    public bool Contains(TimeRange time)
    {
        return Contains(time.Start) && Contains(time.End);
    }

    public TimeRange Intersect(TimeRange time)
    {
        MediaTime newStart = time.Start > Start ? time.Start : Start;
        MediaTime newEnd = time.End < End ? time.End : End;

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
        if (IsEmpty || time.IsEmpty)
        {
            return false;
        }

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
            MediaTime st = time.Start < Start ? time.Start : Start;
            MediaTime ed = time.End > End ? time.End : End;

            return new TimeRange(st, ed - st);
        }
    }

    public TimeRange WithStart(MediaTime start)
    {
        return new TimeRange(start, Duration);
    }

    public TimeRange WithDuration(MediaTime duration)
    {
        return new TimeRange(Start, duration);
    }

    public TimeRange AddStart(MediaTime ts)
    {
        return new TimeRange(Start + ts, Duration);
    }

    public TimeRange SubtractStart(MediaTime ts)
    {
        return new TimeRange(Start - ts, Duration);
    }

    public override bool Equals(object? obj) => obj is TimeRange range && Equals(range);

    public bool Equals(TimeRange other) => Start.Equals(other.Start) && Duration.Equals(other.Duration);

    public override int GetHashCode() => HashCode.Combine(Start, Duration);

    public static bool operator ==(TimeRange left, TimeRange right) => left.Equals(right);

    public static bool operator !=(TimeRange left, TimeRange right) => !(left == right);
}
