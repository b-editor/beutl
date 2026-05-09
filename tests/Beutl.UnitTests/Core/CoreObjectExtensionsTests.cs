namespace Beutl.UnitTests.Core;

public class CoreObjectExtensionsTests
{
    [Test]
    public void GetPropertyChangedObservable_NullObj_Throws()
    {
        Assert.That(
            () => CoreObjectExtensions.GetPropertyChangedObservable<string>(null!, CoreObject.NameProperty),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetPropertyChangedObservable_NullProperty_Throws()
    {
        var obj = new TestCoreObject();

        Assert.That(
            () => obj.GetPropertyChangedObservable<string>(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetPropertyChangedObservable_FiresOnPropertyChange()
    {
        var obj = new TestCoreObject();
        var values = new List<string?>();

        using IDisposable sub = obj.GetPropertyChangedObservable(CoreObject.NameProperty)
            .Subscribe(args => values.Add(args.NewValue));

        obj.Name = "alpha";
        obj.Name = "beta";

        Assert.That(values, Is.EqualTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public void GetObservable_NullObj_Throws()
    {
        Assert.That(
            () => CoreObjectExtensions.GetObservable<string>(null!, CoreObject.NameProperty),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetObservable_NullProperty_Throws()
    {
        var obj = new TestCoreObject();

        Assert.That(
            () => obj.GetObservable<string>(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetObservable_EmitsCurrentValueImmediately()
    {
        var obj = new TestCoreObject { Name = "init" };
        string? captured = null;

        using IDisposable sub = obj.GetObservable(CoreObject.NameProperty).Subscribe(v => captured = v);

        Assert.That(captured, Is.EqualTo("init"));

        obj.Name = "next";
        Assert.That(captured, Is.EqualTo("next"));
    }

    [Test]
    public void Find_PredicateNull_Throws()
    {
        var obj = new TestCoreObject();

        Assert.That(() => obj.Find(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Find_IncludeSelfTrue_ReturnsObjectIfMatches()
    {
        var obj = new TestCoreObject();

        object? result = obj.Find(o => ReferenceEquals(o, obj));

        Assert.That(result, Is.SameAs(obj));
    }

    [Test]
    public void Find_IncludeSelfFalse_DoesNotReturnSelf()
    {
        var obj = new TestCoreObject();

        object? result = obj.Find(o => ReferenceEquals(o, obj), includeSelf: false);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindById_FindsSelf()
    {
        var obj = new TestCoreObject();

        ICoreObject? result = obj.FindById(obj.Id);

        Assert.That(result, Is.SameAs(obj));
    }

    [Test]
    public void FindById_NotMatchingId_ReturnsNull()
    {
        var obj = new TestCoreObject();

        ICoreObject? result = obj.FindById(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    private sealed class TestCoreObject : CoreObject;
}
