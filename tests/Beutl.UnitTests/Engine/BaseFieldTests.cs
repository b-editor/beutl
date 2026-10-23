using System.Text.Json.Nodes;
using Beutl.Collections;
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
            HideProperty(Count);
        }

        public IProperty<int> Count { get; } = Property.CreateValue(0);

        public IProperty<int> Shown { get; } = Property.CreateValue(0);
    }

    [SuppressResourceClassGeneration]
    private sealed class ListOwner : EngineObject
    {
        public ListOwner()
        {
            ScanProperties<ListOwner>();
            HideProperty(Numbers);
        }

        public IProperty<CoreList<int>> Numbers { get; } = Property.CreateValue<CoreList<int>>([]);
    }

    [Test]
    public void ABaseField_IsSavedBeforeTheCoreObjectMembers_AndReadBack()
    {
        var owner = new Owner();
        owner.Count.Value = 3;
        owner.Shown.Value = 4;

        JsonObject json = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (Owner)CoreSerializer.DeserializeFromJsonObject(json, typeof(Owner));

        string[] keys = [.. json.Select(pair => pair.Key)];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Array.IndexOf(keys, "Count"), Is.LessThan(Array.IndexOf(keys, "Id")));
            Assert.That(Array.IndexOf(keys, "Shown"), Is.GreaterThan(Array.IndexOf(keys, "Expressions")));
            Assert.That(restored.Count.Value, Is.EqualTo(3));
            Assert.That(restored.Shown.Value, Is.EqualTo(4));
        }
    }

    // Only a list property saved as a base field is left to its owner to read; a hidden value-only list is read back.
    [Test]
    public void AHiddenListThatItsOwnerDoesNotRead_IsReadBack()
    {
        var owner = new ListOwner();
        owner.Numbers.Value = [1, 2];

        JsonObject json = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (ListOwner)CoreSerializer.DeserializeFromJsonObject(json, typeof(ListOwner));

        Assert.That(restored.Numbers.Value, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void EngineObjectsOwnFields_AreBaseFields()
    {
        var owner = new Owner();
        IProperty[] fields = [owner.IsTimeAnchor, owner.IsEnabled, owner.ZIndex, owner.TimeRange];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields.Select(field => field.Name), Is.EqualTo(new[] { "IsTimeAnchor", "IsEnabled", "ZIndex", "TimeRange" }));
            Assert.That(fields.All(owner.IsBaseField), Is.True);
            Assert.That(owner.GetDisplayProperties(), Is.EqualTo(new IProperty[] { owner.Shown }));
        }
    }

    // A list an engine object hides, like GraphModel.Nodes, is a base field, as its value-only form was.
    [Test]
    public void AHiddenListProperty_IsABaseField_AndAShownOneIsNot()
    {
        var graph = new Beutl.NodeGraph.GraphModel();
        var group = new Beutl.Graphics.DrawableGroup();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.IsBaseField(graph.Nodes), Is.True);
            Assert.That(graph.IsBaseField(graph.AllConnections), Is.True);
            Assert.That(group.IsBaseField(group.Children), Is.False);
        }
    }
}
