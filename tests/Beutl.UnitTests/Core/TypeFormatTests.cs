namespace Beutl.UnitTests.Core;

public class TypeFormatTests
{
    private class MyClass;

    [Test]
    public void ToType_ReturnsCorrectType_ForValidFullName()
    {
        var type = TypeFormat.ToType("[Beutl.UnitTests].Core:TypeFormatTests:MyClass");
        Assert.That(type, Is.EqualTo(typeof(MyClass)));
    }

    [Test]
    public void ToType_ReturnsNull_ForInvalidFullName()
    {
        Assert.Catch(() => TypeFormat.ToType("[InvalidNamespace]InvalidType"));
    }

    [Test]
    public void ToString_ReturnsCorrectString_ForValidType()
    {
        string typeString = TypeFormat.ToString(typeof(MyClass));
        Assert.That(typeString, Is.EqualTo("[Beutl.UnitTests].Core:TypeFormatTests:MyClass"));
    }
}
