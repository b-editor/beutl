using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class KeyFrameNavigationHelperTests
{
    #region EnumerateKeyFrameAnimations

    [Test]
    public void EnumerateKeyFrameAnimations_WithNoAnimations_ReturnsEmpty()
    {
        var element = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(5) };
        element.AddObject(new TestAnimatableObject());

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnumerateKeyFrameAnimations_EngineProperty_LocalClock_ReturnsAnimationWithElementOffset()
    {
        var element = new Element { Start = MediaTime.FromSeconds(3), Length = MediaTime.FromSeconds(5) };
        var obj = new TestAnimatableObject();
        element.AddObject(obj);

        var animation = CreateAnimation(useGlobalClock: false,
            MediaTime.FromSeconds(0), MediaTime.FromSeconds(1));
        obj.FloatValue.SetKeyFrameAnimation(animation);

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Animation, Is.SameAs(animation));
            Assert.That(result[0].Offset, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void EnumerateKeyFrameAnimations_EngineProperty_GlobalClock_ReturnsZeroOffset()
    {
        var element = new Element { Start = MediaTime.FromSeconds(3), Length = MediaTime.FromSeconds(5) };
        var obj = new TestAnimatableObject();
        element.AddObject(obj);

        var animation = CreateAnimation(useGlobalClock: true,
            MediaTime.FromSeconds(0), MediaTime.FromSeconds(1));
        obj.FloatValue.SetKeyFrameAnimation(animation);

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Animation, Is.SameAs(animation));
            Assert.That(result[0].Offset, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void EnumerateKeyFrameAnimations_MultipleProperties_ReturnsAll()
    {
        var element = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(5) };
        var obj = new TestMultiPropertyObject();
        element.AddObject(obj);

        var anim1 = CreateAnimation(useGlobalClock: false, MediaTime.Zero);
        var anim2 = CreateAnimation(useGlobalClock: false, MediaTime.FromSeconds(1));
        obj.FloatA.SetKeyFrameAnimation(anim1);
        obj.FloatB.SetKeyFrameAnimation(anim2);

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    #endregion

    #region EnumerateKeyFrameAnimations - NodeGraph members

    [Test]
    public void EnumerateKeyFrameAnimations_NodeGraphMember_LocalClock_ReturnsAnimationWithGraphNodeOffset()
    {
        (Element element, KeyFrameNavTestGraphNode node) = CreateNodeGraphElement(
            new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(5)));

        var animation = CreateAnimation(useGlobalClock: false,
            MediaTime.FromSeconds(0), MediaTime.FromSeconds(1));
        node.Items.Add(new TestNodeMember<float>("Value", animation));

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Animation, Is.SameAs(animation));
            Assert.That(result[0].Offset, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void EnumerateKeyFrameAnimations_NodeGraphMember_GlobalClock_ReturnsZeroOffset()
    {
        (Element element, KeyFrameNavTestGraphNode node) = CreateNodeGraphElement(
            new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(5)));

        var animation = CreateAnimation(useGlobalClock: true,
            MediaTime.FromSeconds(0), MediaTime.FromSeconds(1));
        node.Items.Add(new TestNodeMember<float>("Value", animation));

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Animation, Is.SameAs(animation));
            Assert.That(result[0].Offset, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void EnumerateKeyFrameAnimations_EnginePropertyBackedInputPort_IsExcluded()
    {
        (Element element, KeyFrameNavTestGraphNode node) = CreateNodeGraphElement(
            new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(5)));

        // The engine-property pass already covers EngineObject-backed properties, so the node-graph
        // pass skips IEnginePropertyBackedInputPort to avoid double-counting. The backing object is
        // intentionally kept out of the searched tree, so the excluded port is the only path that
        // could otherwise surface its animation.
        var backing = new TestAnimatableObject();
        backing.FloatValue.SetKeyFrameAnimation(CreateAnimation(useGlobalClock: false,
            MediaTime.Zero, MediaTime.FromSeconds(1)));
        node.Items.Add(new EnginePropertyBackedInputPort<float>(backing, backing.FloatValue));

        var result = KeyFrameNavigationHelper.EnumerateKeyFrameAnimations(element).ToList();

        Assert.That(result, Is.Empty);
    }

    #endregion

    #region FindAdjacentKeyFrame

    [TestCase(2.0, true, 3.0)]
    [TestCase(4.0, false, 3.0)]
    [TestCase(3.0, true, 5.0)]
    [TestCase(3.0, false, 1.0)]
    public void FindAdjacentKeyFrame_ReturnsNearestOtherKeyFrameInDirection(
        double currentSeconds,
        bool forward,
        double expectedSeconds)
    {
        var element = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(10) };
        var obj = new TestAnimatableObject();
        element.AddObject(obj);

        var animation = CreateAnimation(useGlobalClock: true,
            MediaTime.FromSeconds(1), MediaTime.FromSeconds(3), MediaTime.FromSeconds(5));
        obj.FloatValue.SetKeyFrameAnimation(animation);

        MediaTime? result = KeyFrameNavigationHelper.FindAdjacentKeyFrame(
            [element], MediaTime.FromSeconds(currentSeconds), forward);

        Assert.That(result, Is.EqualTo(MediaTime.FromSeconds(expectedSeconds)));
    }

    [Test]
    public void FindAdjacentKeyFrame_NoKeyFrameInDirection_ReturnsNull()
    {
        var element = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(10) };
        var obj = new TestAnimatableObject();
        element.AddObject(obj);

        var animation = CreateAnimation(useGlobalClock: true,
            MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        obj.FloatValue.SetKeyFrameAnimation(animation);

        MediaTime? result = KeyFrameNavigationHelper.FindAdjacentKeyFrame(
            [element], MediaTime.FromSeconds(5), forward: true);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindAdjacentKeyFrame_MultipleElements_SearchesAll()
    {
        var el1 = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(10) };
        var obj1 = new TestAnimatableObject();
        el1.AddObject(obj1);
        obj1.FloatValue.SetKeyFrameAnimation(CreateAnimation(useGlobalClock: true,
            MediaTime.FromSeconds(5)));

        var el2 = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(10) };
        var obj2 = new TestAnimatableObject();
        el2.AddObject(obj2);
        obj2.FloatValue.SetKeyFrameAnimation(CreateAnimation(useGlobalClock: true,
            MediaTime.FromSeconds(2)));

        MediaTime? result = KeyFrameNavigationHelper.FindAdjacentKeyFrame(
            [el1, el2], MediaTime.FromSeconds(1), forward: true);

        Assert.That(result, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void FindAdjacentKeyFrame_WithOffset_AdjustsKeyFrameTime()
    {
        var element = new Element { Start = MediaTime.FromSeconds(10), Length = MediaTime.FromSeconds(5) };
        var obj = new TestAnimatableObject();
        element.AddObject(obj);

        var animation = CreateAnimation(useGlobalClock: false,
            MediaTime.FromSeconds(0), MediaTime.FromSeconds(2));
        obj.FloatValue.SetKeyFrameAnimation(animation);

        MediaTime? result = KeyFrameNavigationHelper.FindAdjacentKeyFrame(
            [element], MediaTime.FromSeconds(9), forward: true);

        Assert.That(result, Is.EqualTo(MediaTime.FromSeconds(10)));
    }

    [Test]
    public void FindAdjacentKeyFrame_EmptyRoots_ReturnsNull()
    {
        MediaTime? result = KeyFrameNavigationHelper.FindAdjacentKeyFrame(
            [], MediaTime.FromSeconds(1), forward: true);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region Helpers

    private static KeyFrameAnimation<float> CreateAnimation(bool useGlobalClock, params MediaTime[] keyTimes)
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = useGlobalClock };
        for (int i = 0; i < keyTimes.Length; i++)
        {
            animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = keyTimes[i],
                Value = i * 10f,
                Easing = new LinearEasing()
            });
        }

        return animation;
    }

    [SuppressResourceClassGeneration]
    private class TestAnimatableObject : EngineObject
    {
        public TestAnimatableObject()
        {
            FloatValue = Property.CreateAnimatable(0f);
            ScanProperties<TestAnimatableObject>();
        }

        public IProperty<float> FloatValue { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestMultiPropertyObject : EngineObject
    {
        public TestMultiPropertyObject()
        {
            FloatA = Property.CreateAnimatable(0f);
            FloatB = Property.CreateAnimatable(0f);
            ScanProperties<TestMultiPropertyObject>();
        }

        public IProperty<float> FloatA { get; }

        public IProperty<float> FloatB { get; }
    }

    private static (Element Element, KeyFrameNavTestGraphNode Node) CreateNodeGraphElement(TimeRange nodeTimeRange)
    {
        var element = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(10) };
        var drawable = new NodeGraphDrawable();
        element.AddObject(drawable);

        GraphModel model = drawable.Model.Value!;
        var node = new KeyFrameNavTestGraphNode { TimeRange = nodeTimeRange };
        model.Nodes.Add(node);

        return (element, node);
    }

    private sealed class TestNodeMember<T> : NodeMember<T>
    {
        public TestNodeMember(string name, KeyFrameAnimation<T>? animation)
        {
            Property = new NodePropertyAdapter<T>(name, default, animation);
        }
    }

    #endregion
}

// Top-level (not nested) so the EngineObject Resource source generator can reference it.
internal sealed partial class KeyFrameNavTestGraphNode : GraphNode
{
}
