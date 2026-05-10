using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Graphics;

public class GraphicsExceptionTests
{
    [Test]
    public void DefaultConstructor_HasNonNullMessage()
    {
        var ex = new GraphicsException();
        Assert.That(ex.Message, Is.Not.Null);
    }

    [Test]
    public void MessageConstructor_StoresMessage()
    {
        var ex = new GraphicsException("oops");
        Assert.That(ex.Message, Is.EqualTo("oops"));
    }

    [Test]
    public void InnerExceptionConstructor_StoresInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new GraphicsException("outer", inner);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("outer"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        });
    }
}
