namespace Beutl.UnitTests.Core;

public class CoreObjectExtensionsTests
{
    [Test]
    public void GetObservable_NullProperty_Throws()
    {
        Assert.That(
            () => CoreObjectExtensions.GetObservable<string>(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetObservable_EmitsOncePerChange()
    {
        var obj = new TestCoreObject { Label = { Value = "init" } };
        var values = new List<string?>();

        using IDisposable sub = obj.Label.GetObservable().Subscribe(values.Add);
        obj.Label.Value = "next";

        Assert.That(values, Is.EqualTo(new[] { "init", "next" }));
    }

    [Test]
    public void GetObservable_LeavesNoHandlerBehindAfterUnsubscribing()
    {
        var obj = new TestCoreObject { Label = { Value = "init" } };
        IObservable<string> observable = obj.Label.GetObservable();

        for (int i = 0; i < 3; i++)
        {
            observable.Subscribe(_ => { }).Dispose();
        }

        var values = new List<string?>();
        using IDisposable sub = observable.Subscribe(values.Add);
        obj.Label.Value = "next";

        Assert.That(values, Is.EqualTo(new[] { "init", "next" }));
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

        ICoreObject? result = obj.FindById(obj.Id.Value);

        Assert.That(result, Is.SameAs(obj));
    }

    [Test]
    public void FindById_NotMatchingId_ReturnsNull()
    {
        var obj = new TestCoreObject();

        ICoreObject? result = obj.FindById(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    private sealed class TestCoreObject : CoreObject
    {
        public TestCoreObject()
        {
            ScanProperties<TestCoreObject>();
        }

        public IProperty<string> Label { get; } = new ValueOnlyProperty<string>(string.Empty);
    }
}
