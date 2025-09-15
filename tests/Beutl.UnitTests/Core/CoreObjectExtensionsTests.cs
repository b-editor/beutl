using System;
using System.Collections.Generic;

namespace Beutl.UnitTests.Core;

public class CoreObjectExtensionsTests
{
    private sealed class Obj : CoreObject
    {
        public static readonly CoreProperty<int> ValueProperty;

        static Obj()
        {
            ValueProperty = ConfigureProperty<int, Obj>(nameof(Value))
                .DefaultValue(0)
                .Register();
        }

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    [Test]
    public void GetObservable_PushesInitialAndSubsequentValues()
    {
        var o = new Obj();
        var received = new List<int>();
        using var sub = o.GetObservable(Obj.ValueProperty).Subscribe(v => received.Add(v));

        // initial
        Assert.That(received.Count, Is.EqualTo(1));
        Assert.That(received[0], Is.EqualTo(0));

        // change -> two pushes (handler attached twice)
        o.Value = 10;
        Assert.That(received.Count, Is.EqualTo(3));
        Assert.That(received[1], Is.EqualTo(10));
        Assert.That(received[2], Is.EqualTo(10));
    }

    [Test]
    public void GetPropertyChangedObservable_EmitsOnlyMatchingProperty()
    {
        var o = new Obj();
        int count = 0;
        using var sub = o.GetPropertyChangedObservable(Obj.ValueProperty).Subscribe(_ => count++);

        o.Value = 1; // should emit
        o.Value = 2; // should emit again
        Assert.That(count, Is.EqualTo(2));
    }

    private sealed class DummyItem : ProjectItem {}

    [Test]
    public void FindById_TraversesHierarchicalChildren()
    {
        var proj = new Project();
        var item = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "a.item") };
        proj.Items.Add(item);

        ICoreObject? found = proj.FindById(item.Id);
        Assert.That(found, Is.SameAs(item));
    }
}
