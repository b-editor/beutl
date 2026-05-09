namespace Beutl.UnitTests.Core;

public class CultureNameValidationTests
{
    [Test]
    [TestCase("en-US")]
    [TestCase("ja-JP")]
    [TestCase("en")]
    [TestCase("")] // invariant culture
    public void IsValid_KnownCultureNames_ReturnsTrue(string name)
    {
        Assert.That(CultureNameValidation.IsValid(name), Is.True);
    }

    [Test]
    [TestCase("not-a-real-culture")]
    [TestCase("xx-XX-fake")]
    public void IsValid_UnknownCultureNames_ReturnsFalse(string name)
    {
        Assert.That(CultureNameValidation.IsValid(name), Is.False);
    }
}
