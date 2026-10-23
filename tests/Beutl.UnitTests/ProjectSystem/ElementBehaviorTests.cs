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
            Start = { Value = MediaTime.FromSeconds(1) },
            Length = { Value = MediaTime.FromSeconds(2) }
        };

        Assert.That(element.Range.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
        Assert.That(element.Range.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Start_ChangePropagatesToObjectsAndRaisesEditedWithRanges()
    {
        var element = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(2) } };
        var obj = new TestObject();
        element.AddObject(obj);
        ElementEditedEventArgs? args = null;
        element.Edited += (_, e) => args = e as ElementEditedEventArgs;

        element.Start.Value = MediaTime.FromSeconds(5);

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.AffectedRange, Is.Not.Empty);
        Assert.That(obj.TimeRange.Value.Start, Is.EqualTo(MediaTime.FromSeconds(5)));
        Assert.That(obj.TimeRange.Value.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Length_ChangePropagatesToObjects()
    {
        var element = new Element { Start = { Value = MediaTime.FromSeconds(1) } };
        var obj = new TestObject();
        element.AddObject(obj);

        element.Length.Value = MediaTime.FromSeconds(7);

        Assert.That(obj.TimeRange.Value.Duration, Is.EqualTo(MediaTime.FromSeconds(7)));
    }

    [Test]
    public void ZIndex_ChangePropagatesToObjects()
    {
        var element = new Element();
        var obj = new TestObject();
        element.AddObject(obj);

        element.ZIndex.Value = 4;

        Assert.That(obj.ZIndex.Value, Is.EqualTo(4));
    }

    [Test]
    public void IsEnabled_DefaultsToTrue()
    {
        var element = new Element();

        Assert.That(element.IsEnabled.Value, Is.True);
    }

    [Test]
    public void IsEnabled_ChangeRaisesEdited()
    {
        var element = new Element();
        int count = 0;
        element.Edited += (_, _) => count++;

        element.IsEnabled.Value = false;

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void IsEnabled_SetSameValueDoesNotRaiseEdited()
    {
        var element = new Element { IsEnabled = { Value = true } };
        int invocationCount = 0;
        element.Edited += (_, _) => invocationCount++;

        element.IsEnabled.Value = true;

        Assert.That(invocationCount, Is.EqualTo(0));
    }

    [Test]
    public void TimelineLayer_CompositionalChangeRaisesSceneEdited()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        var layer = new TimelineLayer { ZIndex = { Value = 2 } };
        scene.Layers.Add(layer);
        int count = 0;
        scene.Edited += (_, _) => count++;

        layer.IsVideoMuted.Value = true;
        layer.IsAudioMuted.Value = true;
        layer.IsSolo.Value = true;
        layer.ZIndex.Value = 3;

        Assert.That(count, Is.EqualTo(4));
    }

    [Test]
    public void TimelineLayer_EditorOnlyChangeDoesNotRaiseSceneEdited()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        var layer = new TimelineLayer { ZIndex = { Value = 2 } };
        scene.Layers.Add(layer);
        int count = 0;
        scene.Edited += (_, _) => count++;

        layer.IsLocked.Value = true;
        layer.Color.Value = Beutl.Media.Colors.Red;
        layer.Name.Value = "renamed";

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void Layers_AddOrRemoveDefaultModel_DoesNotRaiseSceneEdited()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        int count = 0;
        scene.Edited += (_, _) => count++;

        var lockOnly = new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } };
        scene.Layers.Add(lockOnly);
        scene.Layers.Remove(lockOnly);

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void Layers_AddOrRemoveCompositionalModel_RaisesSceneEdited()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        int count = 0;
        scene.Edited += (_, _) => count++;

        var soloed = new TimelineLayer { ZIndex = { Value = 1 }, IsSolo = { Value = true } };
        scene.Layers.Add(soloed);
        scene.Layers.Remove(soloed);

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void AddChild_AutoPlacement_SkipsLockedLayer()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"beutl_addchild_{Guid.NewGuid():N}");
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene"))
        };
        var occupying = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(5) },
            ZIndex = { Value = 0 },
            Uri = new Uri(Path.Combine(basePath, "a.belm")),
        };
        scene.Children.Add(occupying);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        var added = new Element
        {
            Start = { Value = MediaTime.FromSeconds(1) },
            Length = { Value = MediaTime.FromSeconds(2) },
            ZIndex = { Value = 0 },
            Uri = new Uri(Path.Combine(basePath, "b.belm")),
        };

        scene.AddChild(added);

        Assert.That(added.ZIndex.Value, Is.EqualTo(2));
    }

    [Test]
    public void IsLayerLocked_ReflectsLayerModel()
    {
        var scene = new Scene(1920, 1080, string.Empty);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });

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
        var onLockedLayer = new Element { ZIndex = { Value = 2 } };
        var selfLocked = new Element { ZIndex = { Value = 0 }, IsLocked = { Value = true } };
        var free = new Element { ZIndex = { Value = 0 } };
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });

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
        Assert.That(ts, Is.EqualTo(default(MediaTime)));
    }

    [Test]
    public void HasOriginalDuration_WithProvider_ReturnsTrueAndDuration()
    {
        var element = new Element();
        element.AddObject(new TestDurationObject(MediaTime.FromSeconds(8)));

        Assert.That(element.HasOriginalDuration(), Is.True);
        Assert.That(element.TryGetOriginalDuration(out var ts), Is.True);
        Assert.That(ts, Is.EqualTo(MediaTime.FromSeconds(8)));
    }

    [Test]
    public void NotifySplitted_DispatchesToSplittables()
    {
        var element = new Element();
        var splittable = new TestSplittable();
        element.AddObject(splittable);

        element.NotifySplitted(backward: true, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        Assert.That(splittable.LastBackward, Is.True);
        Assert.That(splittable.LastStartDelta, Is.EqualTo(MediaTime.FromSeconds(1)));
        Assert.That(splittable.LastDurationDelta, Is.EqualTo(MediaTime.FromSeconds(2)));
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
        private readonly MediaTime _duration;

        public TestDurationObject(MediaTime duration)
        {
            _duration = duration;
        }

        public bool HasOriginalDuration() => true;

        public bool TryGetOriginalDuration(out MediaTime timeSpan)
        {
            timeSpan = _duration;
            return true;
        }
    }

    [SuppressResourceClassGeneration]
    private class TestSplittable : EngineObject, ISplittable
    {
        public bool LastBackward { get; private set; }
        public MediaTime LastStartDelta { get; private set; }
        public MediaTime LastDurationDelta { get; private set; }

        public void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
        {
            LastBackward = backward;
            LastStartDelta = startDelta;
            LastDurationDelta = durationDelta;
        }
    }
}
