using System.Collections.Specialized;
using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class CoreDictionaryTests
{
    [Test]
    public void Add_RaisesAddCollectionChanged_AndCountProperty()
    {
        var dict = new CoreDictionary<string, int>();
        var actions = new List<NotifyCollectionChangedAction>();
        var properties = new List<string?>();

        dict.CollectionChanged += (_, e) => actions.Add(e.Action);
        dict.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        dict.Add("a", 1);

        Assert.Multiple(() =>
        {
            Assert.That(dict.Count, Is.EqualTo(1));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Add }));
            Assert.That(properties, Does.Contain("Count"));
            Assert.That(properties, Does.Contain("Item[a]"));
        });
    }

    [Test]
    public void Indexer_NewKey_AddsAndFiresAdd()
    {
        var dict = new CoreDictionary<string, int>();
        var actions = new List<NotifyCollectionChangedAction>();
        dict.CollectionChanged += (_, e) => actions.Add(e.Action);

        dict["foo"] = 1;

        Assert.Multiple(() =>
        {
            Assert.That(dict["foo"], Is.EqualTo(1));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Add }));
        });
    }

    [Test]
    public void Indexer_ExistingKey_FiresReplace()
    {
        var dict = new CoreDictionary<string, int> { ["foo"] = 1 };
        var args = new List<NotifyCollectionChangedEventArgs>();
        dict.CollectionChanged += (_, e) => args.Add(e);

        dict["foo"] = 99;

        Assert.Multiple(() =>
        {
            Assert.That(dict["foo"], Is.EqualTo(99));
            Assert.That(args, Has.Count.EqualTo(1));
            Assert.That(args[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Replace));
        });
    }

    [Test]
    public void Remove_ReturnsTrueAndRaisesRemove()
    {
        var dict = new CoreDictionary<string, int> { ["foo"] = 1, ["bar"] = 2 };
        var actions = new List<NotifyCollectionChangedAction>();
        dict.CollectionChanged += (_, e) => actions.Add(e.Action);

        Assert.Multiple(() =>
        {
            Assert.That(dict.Remove("foo"), Is.True);
            Assert.That(dict.Remove("missing"), Is.False);
            Assert.That(dict.Count, Is.EqualTo(1));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Remove }));
        });
    }

    [Test]
    public void Clear_FiresRemoveAndPropertyChanges()
    {
        var dict = new CoreDictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var actions = new List<NotifyCollectionChangedAction>();
        var properties = new List<string?>();
        dict.CollectionChanged += (_, e) => actions.Add(e.Action);
        dict.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        dict.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(dict.Count, Is.EqualTo(0));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Remove }));
            Assert.That(properties, Does.Contain("Count"));
            Assert.That(properties, Does.Contain("Item"));
        });
    }

    [Test]
    public void TryGetValue_AndContainsKey()
    {
        var dict = new CoreDictionary<string, int> { ["foo"] = 1 };

        Assert.Multiple(() =>
        {
            Assert.That(dict.TryGetValue("foo", out int value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(dict.TryGetValue("bar", out _), Is.False);
            Assert.That(dict.ContainsKey("foo"), Is.True);
            Assert.That(dict.ContainsKey("bar"), Is.False);
        });
    }

    [Test]
    public void Enumerator_ReturnsAllPairs()
    {
        var dict = new CoreDictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var actual = dict.OrderBy(kv => kv.Key).ToArray();

        Assert.That(
            actual,
            Is.EqualTo(
                new[]
                {
                    new KeyValuePair<string, int>("a", 1),
                    new KeyValuePair<string, int>("b", 2),
                }
            )
        );
    }
}
