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
        return StringFormats.ToHumanReadableSize(size, formatProvider: CultureInfo.InvariantCulture);
    }
}
