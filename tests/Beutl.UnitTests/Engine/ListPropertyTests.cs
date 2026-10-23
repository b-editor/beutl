using Beutl.Animation;
using Beutl.Collections;
using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class ListPropertyTests
{
    [SuppressResourceClassGeneration]
    private sealed class Node : EngineObject
    {
        public Node() => ScanProperties<Node>();

        public IListProperty<Node> Children { get; } = Property.CreateList<Node>();
    }

    private static ListProperty<T> Make<T>(string name = "Items")
    {
        var property = new ListProperty<T>();
        property.SetAttributes(name, []);
        return property;
    }

    [Test]
    public void Defaults_AreReportedCorrectly()
    {
        var property = Make<int>();

        Assert.That(property, Is.Not.InstanceOf<IExpressionProperty>());
        Assert.That(property.IsValueSet, Is.True);
        Assert.That(property.HasValidator, Is.False);
        Assert.That(property.GetValidator(), Is.Null);
        Assert.That(property.ElementType, Is.EqualTo(typeof(int)));
        Assert.That(property.ValueType.GetGenericTypeDefinition(), Is.EqualTo(typeof(Beutl.Collections.ICoreList<>)));
        Assert.That(property.DefaultValue, Is.Null);
    }

    [Test]
    public void Add_RaisesEdited()
    {
        var property = Make<int>();
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.Add(1);
        property.Add(2);

        Assert.That(property.Count, Is.EqualTo(2));
        Assert.That(edited, Is.EqualTo(2));
    }

    [Test]
    public void Remove_AffectsCount()
    {
        var property = Make<int>();
        property.AddRange([1, 2, 3]);

        bool removed = property.Remove(2);

        Assert.That(removed, Is.True);
        Assert.That(property, Is.EqualTo(new[] { 1, 3 }));
    }

    [Test]
    public void Replace_KeepsSameInstance()
    {
        var property = Make<int>();
        var originalRef = property.Value;
        property.AddRange([1, 2, 3]);

        property.Replace([10, 20]);

        Assert.That(property.Value, Is.SameAs(originalRef));
        Assert.That(property, Is.EqualTo(new[] { 10, 20 }));
    }

    [Test]
    public void SetValueEvenIfEqual_ThroughBaseContract_ReplacesContents()
    {
        var property = Make<int>();
        property.AddRange([1, 2, 3]);
        IProperty baseProperty = property;

        baseProperty.SetValueEvenIfEqual(new CoreList<int>([4, 5]));

        Assert.That(property, Is.EqualTo(new[] { 4, 5 }));
    }

    [Test]
    public void Indexer_AssignmentReplacesValue()
    {
        var property = Make<int>();
        property.AddRange([1, 2, 3]);

        property[1] = 99;

        Assert.That(property[1], Is.EqualTo(99));
    }

    [Test]
    public void Insert_PlacesValueAtIndex()
    {
        var property = Make<int>();
        property.AddRange([1, 3]);

        property.Insert(1, 2);

        Assert.That(property, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Animation_AlwaysNullAndAcceptsRemoval()
    {
        var property = Make<int>();

        Assert.That(property.GetKeyFrameAnimation(), Is.Null);
        Assert.DoesNotThrow(() => property.SetKeyFrameAnimation(null));
    }

    [Test]
    public void Get_ReturnsCurrentValueInstance()
    {
        var property = Make<int>();
        property.Add(7);

        var value = CompositionContext.Default.Get(property);

        Assert.That(value, Is.SameAs(property.Value));
    }

    [Test]
    public void Name_BeforeInitialization_Throws()
    {
        var property = new ListProperty<int>();

        Assert.Throws<InvalidOperationException>(() => _ = property.Name);
    }

    [Test]
    public void GetEnumerator_AndContains_WorkOnUnderlyingList()
    {
        var property = Make<string>();
        property.AddRange(["a", "b"]);

        Assert.That(property.Contains("a"), Is.True);
        Assert.That(property.IndexOf("b"), Is.EqualTo(1));

        var seen = new List<string>();
        foreach (var item in property)
        {
            seen.Add(item);
        }

        Assert.That(seen, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void CompoundAssign_ReplacesContents()
    {
        var property = Make<int>();
        property.AddRange([1, 2]);

        property <<= new Beutl.Collections.CoreList<int>([9, 8, 7]);

        Assert.That(property, Is.EqualTo(new[] { 9, 8, 7 }));
    }

    [Test]
    public void Clear_RemovesAll()
    {
        var property = Make<int>();
        property.AddRange([1, 2, 3]);

        ((Beutl.Collections.ICoreList<int>)property).Clear();

        Assert.That(property.Count, Is.Zero);
    }

    // An item that holds the list's owner would become its own ancestor, as in a HierarchicalList.
    [Test]
    public void Adding_the_owner_or_its_ancestor_throws_and_keeps_the_list()
    {
        var root = new Node();
        var child = new Node();
        root.Children.Add(child);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidOperationException>(() => child.Children.Add(root));
            Assert.Throws<InvalidOperationException>(() => child.Children.Add(child));
            Assert.That(child.Children, Is.Empty);
            Assert.That(child.HierarchicalParent, Is.SameAs(root));
        }
    }

    // A list whose owner reports its changes, like GraphModel.Nodes, raises no Edited for a change or an item's edit.
    [TestCase(true, 3)]
    [TestCase(false, 0)]
    public void Edited_follows_AffectsRender(bool affectsRender, int expected)
    {
        var property = new ListProperty<Node> { AffectsRender = affectsRender };
        property.SetAttributes("Items", []);
        int edited = 0;
        property.Edited += (_, _) => edited++;
        var item = new Node();

        property.Add(item);
        item.Children.Add(new Node());
        property.Remove(item);

        Assert.That(edited, Is.EqualTo(expected));
    }
}
