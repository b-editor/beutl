namespace Beutl.UnitTests.Core;

public class HierarchyExceptionTests
{
    [Test]
    public void Default_HasNoMessage()
    {
        var ex = new HierarchyException();
        Assert.That(ex.InnerException, Is.Null);
    }

    [Test]
    public void WithMessage_PreservesMessage()
    {
        var ex = new HierarchyException("hierarchy mismatch");
        Assert.That(ex.Message, Is.EqualTo("hierarchy mismatch"));
        Assert.That(ex.InnerException, Is.Null);
    }

    [Test]
    public void WithInner_PreservesBoth()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new HierarchyException("outer", inner);
        Assert.That(ex.Message, Is.EqualTo("outer"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }
}
