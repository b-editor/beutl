namespace Beutl.UnitTests.Core;

public class PropertyRegistryTests
{
    private sealed class FixtureBase : CoreObject
    {
        public static readonly CoreProperty<int> BaseValueProperty;

        static FixtureBase()
        {
            BaseValueProperty = ConfigureProperty<int, FixtureBase>(nameof(BaseValue))
                .DefaultValue(0)
                .Register();
        }

        public int BaseValue
        {
            get => GetValue(BaseValueProperty);
            set => SetValue(BaseValueProperty, value);
        }
    }

    private sealed class FixtureLeaf : CoreObject
    {
        public static readonly CoreProperty<string> LabelProperty;

        static FixtureLeaf()
        {
            LabelProperty = ConfigureProperty<string, FixtureLeaf>(nameof(Label))
                .DefaultValue(string.Empty)
                .Register();
        }

        public string Label
        {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }
    }

    private sealed class FixtureUnregistered : CoreObject
    {
    }

    [Test]
    public void GetRegistered_ReturnsRegisteredProperty()
    {
        IReadOnlyList<CoreProperty> registered = PropertyRegistry.GetRegistered(typeof(FixtureBase));
        Assert.That(registered, Does.Contain(FixtureBase.BaseValueProperty));
    }

    [Test]
    public void GetRegistered_IncludesBaseTypeProperties()
    {
        IReadOnlyList<CoreProperty> registered = PropertyRegistry.GetRegistered(typeof(FixtureLeaf));

        Assert.Multiple(() =>
        {
            Assert.That(registered, Does.Contain(FixtureLeaf.LabelProperty));
            // CoreObject から継承した Id / Name もリストに含まれる
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
        CoreProperty? property = PropertyRegistry.FindRegistered(typeof(FixtureLeaf), "Label");
        Assert.That(property, Is.SameAs(FixtureLeaf.LabelProperty));
    }

    [Test]
    public void FindRegistered_ByName_OnUnknownName_ReturnsNull()
    {
        CoreProperty? property = PropertyRegistry.FindRegistered(typeof(FixtureLeaf), "NoSuchProperty");
        Assert.That(property, Is.Null);
    }

    [Test]
    public void FindRegistered_AttachedSyntax_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PropertyRegistry.FindRegistered(typeof(FixtureLeaf), "Some.Attached"));
    }

    [Test]
    public void FindRegistered_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PropertyRegistry.FindRegistered((Type)null!, "x"));
        Assert.Throws<ArgumentNullException>(() => PropertyRegistry.FindRegistered(typeof(FixtureLeaf), null!));
    }

    [Test]
    public void FindRegistered_ByObject_FindsProperty()
    {
        var leaf = new FixtureLeaf();
        CoreProperty? property = PropertyRegistry.FindRegistered(leaf, "Label");
        Assert.That(property, Is.SameAs(FixtureLeaf.LabelProperty));
    }

    [Test]
    public void FindRegistered_ByObject_NullName_Throws()
    {
        var leaf = new FixtureLeaf();
        Assert.Throws<ArgumentException>(() => PropertyRegistry.FindRegistered(leaf, ""));
        Assert.Throws<ArgumentException>(() => PropertyRegistry.FindRegistered(leaf, null!));
    }

    [Test]
    public void FindRegistered_ByObject_NullObject_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PropertyRegistry.FindRegistered((ICoreObject)null!, "Label"));
    }

    [Test]
    public void FindRegistered_ById_ReturnsProperty()
    {
        CoreProperty target = FixtureBase.BaseValueProperty;
        CoreProperty? property = PropertyRegistry.FindRegistered(target.Id);
        Assert.That(property, Is.SameAs(target));
    }

    [Test]
    public void FindRegistered_ById_OutOfRange_ReturnsNull()
    {
        CoreProperty? property = PropertyRegistry.FindRegistered(int.MaxValue);
        Assert.That(property, Is.Null);
    }

    [Test]
    public void IsRegistered_RegisteredType_ReturnsTrue()
    {
        Assert.That(PropertyRegistry.IsRegistered(typeof(FixtureLeaf), FixtureLeaf.LabelProperty), Is.True);
    }

    [Test]
    public void IsRegistered_UnrelatedType_ReturnsFalse()
    {
        Assert.That(
            PropertyRegistry.IsRegistered(typeof(FixtureUnregistered), FixtureLeaf.LabelProperty),
            Is.False);
    }

    [Test]
    public void IsRegistered_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PropertyRegistry.IsRegistered((Type)null!, FixtureLeaf.LabelProperty));
        Assert.Throws<ArgumentNullException>(() =>
            PropertyRegistry.IsRegistered(typeof(FixtureLeaf), null!));
    }

    [Test]
    public void IsRegistered_ByObject_Works()
    {
        var leaf = new FixtureLeaf();
        Assert.Multiple(() =>
        {
            Assert.That(PropertyRegistry.IsRegistered(leaf, FixtureLeaf.LabelProperty), Is.True);
            Assert.Throws<ArgumentNullException>(() =>
                PropertyRegistry.IsRegistered((object)null!, FixtureLeaf.LabelProperty));
            Assert.Throws<ArgumentNullException>(() =>
                PropertyRegistry.IsRegistered(leaf, null!));
        });
    }
}
