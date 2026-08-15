using Beutl.Editor.Components.Helpers;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class FrameNumberHelperTests
{
    // A theme extension may declare these resources with any numeric type, and FindResource boxes
    // the value as declared — so every numeric representation must resolve, not fall back.
    [TestCase(25d, 25d)]
    [TestCase(25, 25d)]
    [TestCase(25f, 25d)]
    [TestCase(25L, 25d)]
    [TestCase((short)25, 25d)]
    [TestCase((byte)25, 25d)]
    public void ToDouble_NumericResource_ResolvesInsteadOfFallingBack(object resource, double expected)
    {
        Assert.That(FrameNumberHelper.ToDouble(resource, fallback: -1d), Is.EqualTo(expected));
    }

    [Test]
    public void ToDouble_DecimalResource_Resolves()
    {
        Assert.That(FrameNumberHelper.ToDouble(25m, fallback: -1d), Is.EqualTo(25d));
    }

    [TestCaseSource(nameof(NonNumericResources))]
    public void ToDouble_NonNumericResource_UsesFallback(object? resource)
    {
        Assert.That(FrameNumberHelper.ToDouble(resource, fallback: 42d), Is.EqualTo(42d));
    }

    private static IEnumerable<object?> NonNumericResources()
    {
        yield return null;
        yield return "25";
        yield return true;
        yield return 'x';
        yield return new object();
    }
}
