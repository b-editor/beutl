using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class ElementBehaviorTests
{
    [Test]
    public void Range_FollowsStartAndLength()
    {
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2)
        };

        Assert.That(element.Range.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(element.Range.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Start_ChangePropagatesToObjectsAndRaisesEditedWithRanges()
    {
        var element = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(2) };
        var obj = new TestObject();
        element.AddObject(obj);
        ElementEditedEventArgs? args = null;
        element.Edited += (_, e) => args = e as ElementEditedEventArgs;

        element.Start = TimeSpan.FromSeconds(5);

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.AffectedRange, Is.Not.Empty);
        Assert.That(obj.TimeRange.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(obj.TimeRange.Duration, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Length_ChangePropagatesToObjects()
    {
        var element = new Element { Start = TimeSpan.FromSeconds(1) };
        var obj = new TestObject();
        element.AddObject(obj);

        element.Length = TimeSpan.FromSeconds(7);

        Assert.That(obj.TimeRange.Duration, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void ZIndex_ChangePropagatesToObjects()
    {
        var element = new Element();
        var obj = new TestObject();
        element.AddObject(obj);

        element.ZIndex = 4;

        Assert.That(obj.ZIndex, Is.EqualTo(4));
    }

    [Test]
    public void IsEnabled_ChangeRaisesEdited()
    {
        var element = new Element();
        int count = 0;
        element.Edited += (_, _) => count++;

        element.IsEnabled = false;

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void TimelineLayer_ChangeRaisesSceneEdited()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        var layer = new TimelineLayer { ZIndex = 2 };
        scene.Layers.Add(layer);
        int count = 0;
        scene.Edited += (_, _) => count++;

        layer.IsVideoMuted = true;

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void IsLayerLocked_ReflectsLayerModel()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });

        Assert.Multiple(() =>
        {
            Assert.That(scene.IsLayerLocked(2), Is.True);
            Assert.That(scene.IsLayerLocked(0), Is.False);
        });
    }

    [Test]
    public void IsElementLocked_TrueWhenElementOrItsLayerIsLocked()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        var onLockedLayer = new Element { ZIndex = 2 };
        var selfLocked = new Element { ZIndex = 0, IsLocked = true };
        var free = new Element { ZIndex = 0 };
        scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });

        Assert.Multiple(() =>
        {
            Assert.That(scene.IsElementLocked(onLockedLayer), Is.True);
            Assert.That(scene.IsElementLocked(selfLocked), Is.True);
            Assert.That(scene.IsElementLocked(free), Is.False);
        });
    }

    [Test]
    public void AddObject_FlowOperator_AlsoAddsPortalBeforeIt()
    {
        var element = new Element();

        element.AddObject(new TestFlowOperator());

        Assert.That(element.Objects.Count, Is.EqualTo(2));
        Assert.That(element.Objects[0], Is.InstanceOf<PortalObject>());
        Assert.That(element.Objects[1], Is.InstanceOf<TestFlowOperator>());
    }

    [Test]
    public void AddObject_NormalObject_NoPortalAdded()
    {
        var element = new Element();

        element.AddObject(new TestObject());

        Assert.That(element.Objects.Count, Is.EqualTo(1));
        Assert.That(element.Objects[0], Is.InstanceOf<TestObject>());
    }

    [Test]
    public void InsertObject_FlowOperator_InsertsPortalBeforeIt()
    {
        var element = new Element();
        element.AddObject(new TestObject());

        element.InsertObject(0, new TestFlowOperator());

        Assert.That(element.Objects.Count, Is.EqualTo(3));
        Assert.That(element.Objects[0], Is.InstanceOf<PortalObject>());
        Assert.That(element.Objects[1], Is.InstanceOf<TestFlowOperator>());
        Assert.That(element.Objects[2], Is.InstanceOf<TestObject>());
    }

    [Test]
    public void InsertObject_NormalObject_InsertsAtIndex()
    {
        var element = new Element();
        element.AddObject(new TestObject());

        element.InsertObject(0, new TestObject { Tag = "First" });

        Assert.That(element.Objects.Count, Is.EqualTo(2));
        Assert.That(((TestObject)element.Objects[0]).Tag, Is.EqualTo("First"));
    }

    [Test]
    public void RemoveObject_RemovesFromCollection()
    {
        var element = new Element();
        var obj = new TestObject();
        element.AddObject(obj);

        element.RemoveObject(obj);

        Assert.That(element.Objects, Does.Not.Contain(obj));
    }

    [Test]
    public void HasOriginalDuration_NoProvider_ReturnsFalse()
    {
        var element = new Element();
        element.AddObject(new TestObject());

        Assert.That(element.HasOriginalDuration(), Is.False);
        Assert.That(element.TryGetOriginalDuration(out var ts), Is.False);
        Assert.That(ts, Is.EqualTo(default(TimeSpan)));
    }

    [Test]
    public void HasOriginalDuration_WithProvider_ReturnsTrueAndDuration()
    {
        var element = new Element();
        element.AddObject(new TestDurationObject(TimeSpan.FromSeconds(8)));

        Assert.That(element.HasOriginalDuration(), Is.True);
        Assert.That(element.TryGetOriginalDuration(out var ts), Is.True);
        Assert.That(ts, Is.EqualTo(TimeSpan.FromSeconds(8)));
    }

    [Test]
    public void NotifySplitted_DispatchesToSplittables()
    {
        var element = new Element();
        var splittable = new TestSplittable();
        element.AddObject(splittable);

        element.NotifySplitted(backward: true, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.That(splittable.LastBackward, Is.True);
        Assert.That(splittable.LastStartDelta, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(splittable.LastDurationDelta, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [SuppressResourceClassGeneration]
    private class TestObject : EngineObject
    {
        public string Tag { get; init; } = string.Empty;
    }

    [SuppressResourceClassGeneration]
    private class TestFlowOperator : EngineObject, IFlowOperator
    {
    }

    [SuppressResourceClassGeneration]
    private class TestDurationObject : EngineObject, IOriginalDurationProvider
    {
        private readonly TimeSpan _duration;

        public TestDurationObject(TimeSpan duration)
        {
            _duration = duration;
        }

        public bool HasOriginalDuration() => true;

        public bool TryGetOriginalDuration(out TimeSpan timeSpan)
        {
            timeSpan = _duration;
            return true;
        }
    }

    [SuppressResourceClassGeneration]
    private class TestSplittable : EngineObject, ISplittable
    {
        public bool LastBackward { get; private set; }
        public TimeSpan LastStartDelta { get; private set; }
        public TimeSpan LastDurationDelta { get; private set; }

        public void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta)
        {
            LastBackward = backward;
            LastStartDelta = startDelta;
            LastDurationDelta = durationDelta;
        }
    }
}
