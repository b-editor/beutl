namespace Beutl.UnitTests.Core;

public class ReferenceTests
{
    private sealed class TestObject : CoreObject;

    [Test]
    public void DefaultReference_IsNull()
    {
        Reference<TestObject> reference = default;
        Assert.That(reference.IsNull, Is.True);
        Assert.That(reference.Value, Is.Null);
        Assert.That(reference.Id, Is.EqualTo(Guid.Empty));
    }

    [Test]
    public void Constructor_WithGuid_StoresId()
    {
        var id = Guid.NewGuid();
        var reference = new Reference<TestObject>(id);
        Assert.That(reference.Id, Is.EqualTo(id));
        Assert.That(reference.Value, Is.Null);
        Assert.That(reference.IsNull, Is.False);
    }

    [Test]
    public void Constructor_WithObject_PullsIdFromObject()
    {
        var obj = new TestObject();
        var reference = new Reference<TestObject>(obj);
        Assert.That(reference.Id, Is.EqualTo(obj.Id));
        Assert.That(reference.Value, Is.SameAs(obj));
    }

    [Test]
    public void Resolved_AttachesObject_KeepsIdConsistent()
    {
        var obj = new TestObject();
        var reference = new Reference<TestObject>(obj.Id);

        Reference<TestObject> resolved = reference.Resolved(obj);

        Assert.That(resolved.Value, Is.SameAs(obj));
        Assert.That(resolved.Id, Is.EqualTo(obj.Id));
    }

    [Test]
    public void IReferenceResolved_DelegatesToTyped()
    {
        var obj = new TestObject();
        IReference reference = new Reference<TestObject>(obj.Id);
        IReference resolved = reference.Resolved(obj);
        Assert.That(resolved.Value, Is.SameAs(obj));
        Assert.That(resolved.ObjectType, Is.EqualTo(typeof(TestObject)));
    }

    [Test]
    public void Equals_TwoReferencesWithSameIdAndValue_AreEqual()
    {
        var obj = new TestObject();
        var a = new Reference<TestObject>(obj);
        var b = new Reference<TestObject>(obj);
        Assert.That(a == b, Is.True);
        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Equals_DifferentIds_AreNotEqual()
    {
        var a = new Reference<TestObject>(Guid.NewGuid());
        var b = new Reference<TestObject>(Guid.NewGuid());
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void Deconstruct_ExposesIdAndValue()
    {
        var obj = new TestObject();
        var reference = new Reference<TestObject>(obj);
        var (id, value) = reference;
        Assert.That(id, Is.EqualTo(obj.Id));
        Assert.That(value, Is.SameAs(obj));
    }

    [Test]
    public void ImplicitConversion_FromGuid_AndToGuid_AreSymmetric()
    {
        var id = Guid.NewGuid();
        Reference<TestObject> reference = id;
        Guid back = reference;
        Assert.That(back, Is.EqualTo(id));
    }

    [Test]
    public void ImplicitConversion_FromObject_PreservesId()
    {
        var obj = new TestObject();
        Reference<TestObject> reference = obj;
        TestObject? back = reference;
        Assert.That(back, Is.SameAs(obj));
    }
}
