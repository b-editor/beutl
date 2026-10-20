namespace Beutl.UnitTests.Core;

public class PropertyRegistryTests
{
    private sealed class FixtureA : CoreObject
    {
        public static readonly CoreProperty<int> BaseValueProperty;

        static FixtureA()
        {
            BaseValueProperty = ConfigureProperty<int, FixtureA>(nameof(BaseValue))
                .DefaultValue(0)
                .Register();
        }

        public int BaseValue
        {
            get => GetValue(BaseValueProperty);
            set => SetValue(BaseValueProperty, value);
        }
    }

    private sealed class FixtureB : CoreObject
    {
        public static readonly CoreProperty<string> LabelProperty;

        static FixtureB()
        {
            LabelProperty = ConfigureProperty<string, FixtureB>(nameof(Label))
                .DefaultValue(string.Empty)
                .Register();
        }

        public string Label
        {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }
    }

    [Test]
    public void GetRegistered_ReturnsRegisteredProperty()
    {
        IReadOnlyList<CoreProperty> registered = PropertyRegistry.GetRegistered(typeof(FixtureA));
        Assert.That(registered, Does.Contain(FixtureA.BaseValueProperty));
    }

    [Test]
    public void GetRegistered_IncludesBaseTypeProperties()
    {
        IReadOnlyList<CoreProperty> registered = PropertyRegistry.GetRegistered(typeof(FixtureB));

        Assert.Multiple(() =>
        {
            Assert.That(registered, Does.Contain(FixtureB.LabelProperty));
            Assert.That(registered, Does.Contain(CoreObject.IdProperty));
            Assert.That(registered, Does.Contain(CoreObject.NameProperty));
        });
    }

    [Test]
    public void GetRegistered_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PropertyRegistry.GetRegistered(null!));
    }

    [Test]
    public void FindRegistered_ByName_ReturnsProperty()
    {
        CoreProperty? property = PropertyRegistry.FindRegistered(typeof(FixtureB), "Label");
        Assert.That(property, Is.SameAs(FixtureB.LabelProperty));
    }

    [Test]
    public void FindRegistered_ByName_OnUnknownName_ReturnsNull()
    {
        CoreProperty? property = PropertyRegistry.FindRegistered(typeof(FixtureB), "NoSuchProperty");
        Assert.That(property, Is.Null);
    }

    [Test]
    public void FindRegistered_AttachedSyntax_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PropertyRegistry.FindRegistered(typeof(FixtureB), "Some.Attached"));
    }

    [Test]
    public void FindRegistered_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PropertyRegistry.FindRegistered((Type)null!, "x"));
        Assert.Throws<ArgumentNullException>(() => PropertyRegistry.FindRegistered(typeof(FixtureB), null!));
    }

    [Test]
    public void FindRegistered_ByObject_FindsProperty()
    {
        var leaf = new FixtureB();
        CoreProperty? property = PropertyRegistry.FindRegistered(leaf, "Label");
        Assert.That(property, Is.SameAs(FixtureB.LabelProperty));
    }

    [Test]
    public void FindRegistered_ByObject_NullName_Throws()
    {
        var leaf = new FixtureB();
        Assert.Throws<ArgumentException>(() => PropertyRegistry.FindRegistered(leaf, ""));
        Assert.Throws<ArgumentException>(() => PropertyRegistry.FindRegistered(leaf, null!));
    }

    [Test]
    public void FindRegistered_ByObject_NullObject_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PropertyRegistry.FindRegistered((ICoreObject)null!, "Label"));
    }
}
