using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Serialization;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Beutl.UnitTests.Core;

// A value-only property is its own type, so code and editors can tell from the type that it takes no expression.
[TestFixture]
public class ValueOnlyPropertyTests
{
    private sealed class Owner : EngineObject
    {
        public Owner() => ScanProperties<Owner>();

        [Range(0, 10)]
        public IProperty<int> Count { get; } = Property.CreateValue(1);
    }

    [Test]
    public void CreateValue_MakesAPropertyThatIsNotAnExpressionProperty()
    {
        IProperty<int> property = Property.CreateValue(5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property, Is.InstanceOf<ValueOnlyProperty<int>>());
            Assert.That(property, Is.Not.InstanceOf<IExpressionProperty>());
            Assert.That(property.CurrentValue, Is.EqualTo(5));
            Assert.That(property.HasLocalValue, Is.False);
        }
    }

    [Test]
    public void SettingTheValue_ValidatesAndNotifiesOnce()
    {
        var owner = new Owner();
        int changed = 0;
        owner.Count.ValueChanged += (_, _) => changed++;

        owner.Count.CurrentValue = 20;
        owner.Count.CurrentValue = 10;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.Count.CurrentValue, Is.EqualTo(10), "An out-of-range value is coerced to the maximum.");
            Assert.That(owner.Count.HasLocalValue, Is.True);
            Assert.That(changed, Is.EqualTo(1));
        }
    }

    [Test]
    public void ACompositionContext_ReadsTheStoredValue()
    {
        var owner = new Owner();
        owner.Count.CurrentValue = 3;

        Assert.That(new CompositionContext(TimeSpan.FromSeconds(1)).Get(owner.Count), Is.EqualTo(3));
    }

    [Test]
    public void TheValue_IsSavedUnderItsNameAndRead()
    {
        var owner = new Owner();
        owner.Count.CurrentValue = 7;

        var json = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (Owner)CoreSerializer.DeserializeFromJsonObject(json, typeof(Owner));

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int?)json[nameof(Owner.Count)], Is.EqualTo(7));
            Assert.That(((JsonObject)json["Expressions"]!).ContainsKey(nameof(Owner.Count)), Is.False);
            Assert.That(restored.Count.CurrentValue, Is.EqualTo(7));
        }
    }
}
