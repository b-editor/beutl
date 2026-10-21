using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine;

// A value-only property hidden from the panels stands for a CoreProperty field (#2884 step 7).
[TestFixture]
public class BaseFieldTests
{
    [SuppressResourceClassGeneration]
    private sealed class Owner : EngineObject
    {
        public Owner()
        {
            ScanProperties<Owner>();
            HideProperty(CountProperty);
        }

        public IProperty<int> CountProperty { get; } = Property.CreateValue(0);

        public IProperty<int> ShownProperty { get; } = Property.CreateValue(0);
    }

    [Test]
    public void ABaseField_IsSavedBeforeTheCoreObjectMembers_AndReadBack()
    {
        var owner = new Owner();
        owner.CountProperty.Value = 3;
        owner.ShownProperty.Value = 4;

        JsonObject json = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (Owner)CoreSerializer.DeserializeFromJsonObject(json, typeof(Owner));

        string[] keys = [.. json.Select(pair => pair.Key)];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Array.IndexOf(keys, "Count"), Is.LessThan(Array.IndexOf(keys, "Id")));
            Assert.That(Array.IndexOf(keys, "Shown"), Is.GreaterThan(Array.IndexOf(keys, "Expressions")));
            Assert.That(restored.CountProperty.Value, Is.EqualTo(3));
            Assert.That(restored.ShownProperty.Value, Is.EqualTo(4));
        }
    }

    [Test]
    public void EngineObjectsOwnFields_AreBaseFields()
    {
        var owner = new Owner();
        IProperty[] fields = [owner.IsTimeAnchorProperty, owner.IsEnabledProperty, owner.ZIndexProperty, owner.TimeRangeProperty];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields.Select(field => field.Name), Is.EqualTo(new[] { "IsTimeAnchor", "IsEnabled", "ZIndex", "TimeRange" }));
            Assert.That(fields.All(owner.IsBaseField), Is.True);
            Assert.That(owner.GetDisplayProperties(), Is.EqualTo(new IProperty[] { owner.ShownProperty }));
        }
    }
}
