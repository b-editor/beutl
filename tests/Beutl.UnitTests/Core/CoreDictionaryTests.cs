using System.Collections;
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

    [TestCase(false)]
    [TestCase(true)]
    public void NonGenericIndexer_RaisesMatchingCollectionAndPropertyChanges(bool replace)
    {
        var dict = new CoreDictionary<string, int>();
        if (replace) dict.Add("key", 1);
        var changes = new List<NotifyCollectionChangedEventArgs>();
        var properties = new List<string?>();
        dict.CollectionChanged += (_, e) => changes.Add(e);
        dict.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        ((IDictionary)dict)["key"] = 2;

        Assert.Multiple(() =>
        {
            Assert.That(dict["key"], Is.EqualTo(2));
            Assert.That(changes, Has.Count.EqualTo(1));
            if (changes.Count == 1)
            {
                Assert.That(changes[0].Action,
                    Is.EqualTo(replace ? NotifyCollectionChangedAction.Replace : NotifyCollectionChangedAction.Add));
                Assert.That(changes[0].NewItems!.Cast<KeyValuePair<string, int>>(),
                    Is.EqualTo(new[] { new KeyValuePair<string, int>("key", 2) }));
                if (replace)
                    Assert.That(changes[0].OldItems!.Cast<KeyValuePair<string, int>>(),
                        Is.EqualTo(new[] { new KeyValuePair<string, int>("key", 1) }));
            }

            Assert.That(properties, Is.EqualTo(replace ? new[] { "Item[key]" } : ["Count", "Item[key]"]));
        });
    }

    [TestCase("missing", 2, false)]
    [TestCase("key", 2, false)]
    [TestCase("key", 1, true)]
    public void RemovePair_RequiresBothKeyAndValue(string key, int value, bool removed)
    {
        var dict = new CoreDictionary<string, int> { ["key"] = 1 };
        var changes = new List<NotifyCollectionChangedEventArgs>();
        var properties = new List<string?>();
        dict.CollectionChanged += (_, e) => changes.Add(e);
        dict.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        bool result = ((ICollection<KeyValuePair<string, int>>)dict).Remove(new KeyValuePair<string, int>(key, value));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(removed));
            Assert.That(dict.ContainsKey("key"), Is.EqualTo(!removed));
            Assert.That(changes, Has.Count.EqualTo(removed ? 1 : 0));
            Assert.That(properties, Is.EqualTo(removed ? new[] { "Count", "Item[key]" } : Array.Empty<string>()));
        });
    }

    [TestCase(null, 1, typeof(ArgumentNullException))]
    [TestCase(1, 1, typeof(ArgumentException))]
    [TestCase("key", "wrong", typeof(ArgumentException))]
    [TestCase("key", null, typeof(ArgumentNullException))]
    public void NonGenericIndexer_InvalidAssignment_PreservesDictionaryExceptions(object? key, object? value, Type exceptionType)
    {
        IDictionary dict = new CoreDictionary<string, int>();

        Assert.Throws(exceptionType, () => dict[key!] = value);
        Assert.That(dict.Count, Is.Zero);
    }

    [Test]
    public void NonGenericIndexer_NullReferenceValue_IsAllowedAndNotified()
    {
        var dict = new CoreDictionary<string, string?>();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        dict.CollectionChanged += (_, e) => changes.Add(e);

        ((IDictionary)dict)["key"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dict.ContainsKey("key"), Is.True);
            Assert.That(dict["key"], Is.Null);
            Assert.That(changes, Has.Count.EqualTo(1));
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

        Assert.That(actual, Is.EqualTo(new[]
        {
            new KeyValuePair<string, int>("a", 1),
            new KeyValuePair<string, int>("b", 2),
        }));
    }
}
