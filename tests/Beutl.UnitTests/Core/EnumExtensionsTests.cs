namespace Beutl.UnitTests.Core;

public class EnumExtensionsTests
{
    [Flags]
    private enum ByteFlags : byte
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    [Flags]
    private enum ShortFlags : short
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    [Flags]
    private enum IntFlags : int
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        C = 1 << 2,
    }

    [Flags]
    private enum LongFlags : long
    {
        None = 0,
        A = 1L,
        B = 1L << 33,
        C = 1L << 50,
    }

    [Test]
    public void HasAllFlags_Byte_DetectsExpectedSubset()
    {
        ByteFlags value = ByteFlags.A | ByteFlags.B;
        Assert.That(value.HasAllFlags(ByteFlags.A), Is.True);
        Assert.That(value.HasAllFlags(ByteFlags.A | ByteFlags.B), Is.True);
        Assert.That(value.HasAllFlags(ByteFlags.C), Is.False);
        Assert.That(value.HasAllFlags(ByteFlags.A | ByteFlags.C), Is.False);
    }

    [Test]
    public void HasAnyFlag_Byte_DetectsAtLeastOneOverlap()
    {
        ByteFlags value = ByteFlags.A;
        Assert.That(value.HasAnyFlag(ByteFlags.A | ByteFlags.B), Is.True);
        Assert.That(value.HasAnyFlag(ByteFlags.B | ByteFlags.C), Is.False);
    }

    [Test]
    public void HasAllFlags_Short_DetectsExpectedSubset()
    {
        ShortFlags value = ShortFlags.A | ShortFlags.C;
        Assert.That(value.HasAllFlags(ShortFlags.A | ShortFlags.C), Is.True);
        Assert.That(value.HasAllFlags(ShortFlags.A | ShortFlags.B), Is.False);
    }

    [Test]
    public void HasAnyFlag_Short_DetectsOverlap()
    {
        ShortFlags value = ShortFlags.B;
        Assert.That(value.HasAnyFlag(ShortFlags.A | ShortFlags.B), Is.True);
        Assert.That(value.HasAnyFlag(ShortFlags.A | ShortFlags.C), Is.False);
    }

    [Test]
    public void HasAllFlags_Int_DetectsExpectedSubset()
    {
        IntFlags value = IntFlags.A | IntFlags.B;
        Assert.That(value.HasAllFlags(IntFlags.A), Is.True);
        Assert.That(value.HasAllFlags(IntFlags.C), Is.False);
    }

    [Test]
    public void HasAnyFlag_Int_DetectsOverlap()
    {
        IntFlags value = IntFlags.A;
        Assert.That(value.HasAnyFlag(IntFlags.A | IntFlags.B), Is.True);
        Assert.That(value.HasAnyFlag(IntFlags.B | IntFlags.C), Is.False);
    }

    [Test]
    public void HasAllFlags_Long_HandlesHighBits()
    {
        LongFlags value = LongFlags.B | LongFlags.C;
        Assert.That(value.HasAllFlags(LongFlags.B), Is.True);
        Assert.That(value.HasAllFlags(LongFlags.C), Is.True);
        Assert.That(value.HasAllFlags(LongFlags.B | LongFlags.C), Is.True);
        Assert.That(value.HasAllFlags(LongFlags.A | LongFlags.B), Is.False);
    }

    [Test]
    public void HasAnyFlag_Long_HandlesHighBits()
    {
        LongFlags value = LongFlags.C;
        Assert.That(value.HasAnyFlag(LongFlags.B | LongFlags.C), Is.True);
        Assert.That(value.HasAnyFlag(LongFlags.A | LongFlags.B), Is.False);
    }

    [Test]
    public void HasAllFlags_None_AlwaysTrue()
    {
        Assert.That(ByteFlags.A.HasAllFlags(ByteFlags.None), Is.True);
        Assert.That(IntFlags.None.HasAllFlags(IntFlags.None), Is.True);
    }

    [Test]
    public void HasAnyFlag_None_AlwaysFalse()
    {
        Assert.That(ByteFlags.A.HasAnyFlag(ByteFlags.None), Is.False);
        Assert.That(LongFlags.A.HasAnyFlag(LongFlags.None), Is.False);
    }
}
