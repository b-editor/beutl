using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class TimeRangeExtraTests
{
    [Test]
    public void Empty_HasZeroStartAndDuration()
    {
        Assert.That(TimeRange.Empty.Start, Is.EqualTo(TimeSpan.Zero));
        Assert.That(TimeRange.Empty.Duration, Is.EqualTo(TimeSpan.Zero));
        Assert.That(TimeRange.Empty.End, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void DefaultConstructor_EqualsEmpty()
    {
        var range = new TimeRange();

        Assert.That(range, Is.EqualTo(TimeRange.Empty));
    }

    [Test]
    public void DurationOnlyConstructor_StartsAtZero()
    {
        var range = new TimeRange(TimeSpan.FromSeconds(3));

        Assert.That(range.Start, Is.EqualTo(TimeSpan.Zero));
        Assert.That(range.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void FromRange_DerivesDurationFromEndMinusStart()
    {
        var range = TimeRange.FromRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7));

        Assert.That(range.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(range.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(range.End, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void IsEmpty_TrueWhenDurationIsZeroOrNegative()
    {
        var zero = new TimeRange();
        var positive = TimeRange.FromSeconds(1, 2);
        var negative = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1));

        Assert.That(zero.IsEmpty, Is.True);
        Assert.That(positive.IsEmpty, Is.False);
        Assert.That(negative.IsEmpty, Is.True);
    }

    [Test]
    public void Contains_TimeSpan_ExcludesEnd()
    {
        var range = TimeRange.FromSeconds(2, 3);

        Assert.That(range.Contains(TimeSpan.FromSeconds(2)), Is.True);
        Assert.That(range.Contains(TimeSpan.FromSeconds(4)), Is.True);
        Assert.That(range.Contains(TimeSpan.FromSeconds(5)), Is.False);
        Assert.That(range.Contains(TimeSpan.FromSeconds(1.99)), Is.False);
    }

    [Test]
    public void WithStart_PreservesDuration()
    {
        var range = TimeRange.FromSeconds(2, 3);

        var modified = range.WithStart(TimeSpan.FromSeconds(10));

        Assert.That(modified.Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(modified.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void WithDuration_PreservesStart()
    {
        var range = TimeRange.FromSeconds(2, 3);

        var modified = range.WithDuration(TimeSpan.FromSeconds(10));

        Assert.That(modified.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(modified.Duration, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void AddStart_OffsetsStart()
    {
        var range = TimeRange.FromSeconds(2, 3);

        var shifted = range.AddStart(TimeSpan.FromSeconds(5));

        Assert.That(shifted.Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
        Assert.That(shifted.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void SubtractStart_RewindsStart()
    {
        var range = TimeRange.FromSeconds(5, 3);

        var shifted = range.SubtractStart(TimeSpan.FromSeconds(2));

        Assert.That(shifted.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(shifted.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void Union_WithIsEmptyOperand_ReturnsOther()
    {
        var range = TimeRange.FromSeconds(1, 2);
        var emptyByDef = new TimeRange();

        Assert.That(emptyByDef.IsEmpty, Is.True);
        Assert.That(emptyByDef.Union(range), Is.EqualTo(range));
        Assert.That(range.Union(emptyByDef), Is.EqualTo(range));
    }

    [Test]
    public void Intersect_NoOverlap_ReturnsEmpty()
    {
        var a = TimeRange.FromSeconds(0, 1);
        var b = TimeRange.FromSeconds(5, 1);

        Assert.That(a.Intersect(b), Is.EqualTo(TimeRange.Empty));
    }

    [Test]
    public void Intersects_EmptyRange_ReturnsFalse()
    {
        var zeroDuration = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.Zero);
        var negativeDuration = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1));
        var nonEmpty = TimeRange.FromSeconds(0, 10);

        Assert.That(zeroDuration.Intersects(nonEmpty), Is.False);
        Assert.That(nonEmpty.Intersects(zeroDuration), Is.False);
        Assert.That(negativeDuration.Intersects(nonEmpty), Is.False);
        Assert.That(nonEmpty.Intersects(negativeDuration), Is.False);
        Assert.That(TimeRange.Empty.Intersects(TimeRange.Empty), Is.False);
    }

    [Test]
    public void Equality_OnEqualRanges_IsTrueAndHashesMatch()
    {
        var a = TimeRange.FromSeconds(2, 3);
        var b = TimeRange.FromSeconds(2, 3);
        var c = TimeRange.FromSeconds(2, 4);

        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"foo"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void ToString_FormatsHoursMinutesSecondsAndHundredths()
    {
        var range = TimeRange.FromSeconds(3, 5);

        Assert.That(range.ToString(), Does.Match(@"\d{2}:\d{2}:\d{2}\.\d{2}"));
    }
}
