using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class ListPropertyTests
{
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

        Assert.That(property.IsAnimatable, Is.False);
        Assert.That(property.SupportsExpression, Is.False);
        Assert.That(property.HasLocalValue, Is.True);
        Assert.That(property.HasExpression, Is.False);
        Assert.That(property.HasValidator, Is.False);
        Assert.That(property.ElementType, Is.EqualTo(typeof(int)));
        Assert.That(
            property.ValueType.GetGenericTypeDefinition(),
            Is.EqualTo(typeof(Beutl.Collections.ICoreList<>))
        );
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
        var originalRef = property.CurrentValue;
        property.AddRange([1, 2, 3]);

        property.Replace([10, 20]);

        Assert.That(property.CurrentValue, Is.SameAs(originalRef));
        Assert.That(property, Is.EqualTo(new[] { 10, 20 }));
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
    public void Animation_AlwaysNullAndAcceptsAnyAssignment()
    {
        var property = Make<int>();

        Assert.That(property.Animation, Is.Null);
        Assert.DoesNotThrow(() => property.Animation = null);
    }

    [Test]
    public void Expression_AlwaysNullAndSilentSet()
    {
        var property = Make<int>();

        Assert.That(property.Expression, Is.Null);
        Assert.DoesNotThrow(() => property.Expression = null);
    }

    [Test]
    public void GetValue_ReturnsCurrentValueInstance()
    {
        var property = Make<int>();
        property.Add(7);

        var value = property.GetValue(CompositionContext.Default);

        Assert.That(value, Is.SameAs(property.CurrentValue));
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

    [Test]
    public void SerializeExpression_IsNull()
    {
        var property = Make<int>();

        Assert.That(property.SerializeExpression(), Is.Null);
    }
}
