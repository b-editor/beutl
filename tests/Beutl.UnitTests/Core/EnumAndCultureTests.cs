using System;

namespace Beutl.UnitTests.Core;

public class EnumAndCultureTests
{
    [Flags]
    private enum ByteFlags : byte { A=1, B=2, C=4 }
    [Flags]
    private enum ShortFlags : short { A=1, B=2, C=4 }
    [Flags]
    private enum IntFlags : int { A=1, B=2, C=4 }
    [Flags]
    private enum LongFlags : long { A=1, B=2, C=4 }

    [Test]
    public void EnumExtensions_HasAllAndAny()
    {
        Assert.That((ByteFlags.A | ByteFlags.B).HasAllFlags(ByteFlags.A | ByteFlags.B), Is.True);
        Assert.That((ShortFlags.A | ShortFlags.C).HasAnyFlag(ShortFlags.B | ShortFlags.C), Is.True);
        Assert.That(IntFlags.A.HasAnyFlag(IntFlags.B), Is.False);
        Assert.That((LongFlags.A | LongFlags.B | LongFlags.C).HasAllFlags(LongFlags.A | LongFlags.C), Is.True);
    }

    [Test]
    public void CultureNameValidation_Works()
    {
        Assert.That(CultureNameValidation.IsValid("en-US"), Is.True);
        Assert.That(CultureNameValidation.IsValid("xx-YY"), Is.False);
    }
}

