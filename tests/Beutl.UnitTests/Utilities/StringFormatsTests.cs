using System.Globalization;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class StringFormatsTests
{
    [Test]
    [TestCase(1024, ExpectedResult = "1024.00 B")]
    [TestCase(1048576, ExpectedResult = "1024.00 KB")]
    [TestCase(1073741824, ExpectedResult = "1024.00 MB")]
    [TestCase(500, ExpectedResult = "500.00 B")]
    [TestCase(1536, ExpectedResult = "1.50 KB")]
    public string ToHumanReadableSize_ShouldReturnCorrectFormat(double size)
    {
        return StringFormats.ToHumanReadableSize(
            size,
            formatProvider: CultureInfo.InvariantCulture
        );
    }

    [Test]
    public void ToHumanReadableSize_LargerThanGB_StaysAtGB()
    {
        // Once the recursive call reaches the GB scale (last unit), it stops
        // even if the size is still larger than the standard.
        double bytes = 5d * 1024 * 1024 * 1024;
        Assert.That(
            StringFormats.ToHumanReadableSize(bytes, formatProvider: CultureInfo.InvariantCulture),
            Is.EqualTo("5.00 GB")
        );
    }

    [Test]
    public void ToHumanReadableSize_CustomStandard_RespectsBase1000()
    {
        // With a base-1000 standard, 1500 -> 1.50 KB
        Assert.That(
            StringFormats.ToHumanReadableSize(
                1500,
                standard: 1000,
                formatProvider: CultureInfo.InvariantCulture
            ),
            Is.EqualTo("1.50 KB")
        );
    }

    [Test]
    public void ToHumanReadableSize_StartingScale_ChoosesProvidedUnit()
    {
        // Starting at scale=2 (MB), 4 stays at "4.00 MB" since 4 <= 1024.
        Assert.That(
            StringFormats.ToHumanReadableSize(
                4,
                scale: 2,
                formatProvider: CultureInfo.InvariantCulture
            ),
            Is.EqualTo("4.00 MB")
        );
    }

    [Test]
    public void ToHumanReadableSize_NoFormatProvider_UsesCurrent()
    {
        // Sanity check: doesn't throw and returns a non-empty string.
        Assert.That(StringFormats.ToHumanReadableSize(1024), Is.Not.Empty);
    }
}
