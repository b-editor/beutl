using System.Reactive;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.NodeGraph;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class NodeMemberOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void SetUp()
    {
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Helper Classes

    [SuppressResourceClassGeneration]
    private sealed partial class TestNodeMember<T> : NodeMember<T>
    {
        public TestNodeMember(string name, T? value = default, IAnimation<T>? animation = null)
        {
            var adapter = new NodePropertyAdapter<T>(name, value, animation);
            Property = adapter;
        }

        public void SetPropertyValue(T? value)
        {
            Property?.SetValue(value);
        }

        public void SetAnimation(IAnimation<T>? animation)
        {
            if (Property is NodePropertyAdapter<T> adapter)
            {
                adapter.Animation = animation;
            }
        }
    }

    private sealed class TestCoreObject : CoreObject
    {
        public static readonly CoreProperty<string?> NameValueProperty;
        private string? _nameValue;

        static TestCoreObject()
        {
            NameValueProperty = ConfigureProperty<string?, TestCoreObject>(nameof(NameValue))
                .Accessor(o => o.NameValue, (o, v) => o.NameValue = v)
                .Register();
        }

        public string? NameValue
        {
            get => _nameValue;
            set => SetAndRaise(NameValueProperty, ref _nameValue, value);
        }
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldCreateChildObserver_WhenPropertyValueIsCoreObject()
    {
        var coreObject = new TestCoreObject { NameValue = "Initial" };
        var nodeMember = new TestNodeMember<TestCoreObject>("TestProperty", coreObject);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        coreObject.NameValue = "Changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldCreateCollectionObserver_WhenPropertyValueIsList()
    {
        var list = new CoreList<TestCoreObject>();
        var nodeMember = new TestNodeMember<CoreList<TestCoreObject>>("TestProperty", list);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        list.Add(new TestCoreObject());

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldUsePropertyPath_WhenProvided()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "Parent");

        nodeMember.SetPropertyValue(20);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.PropertyPath, Is.EqualTo("Parent.Property"));
    }

    #endregion

    #region Value Change Tests

    [Test]
    public void OnChanged_ShouldPublishOperation_WhenPropertyValueChanges()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        nodeMember.SetPropertyValue(20);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NewValue, Is.EqualTo(20));
        Assert.That(operation.OldValue, Is.EqualTo(10));
        Assert.That(operation.PropertyPath, Is.EqualTo("Property"));
        Assert.That(operation.NodeMember, Is.SameAs(nodeMember));
    }

    [Test]
    public void OnChanged_ShouldNotPublish_WhenPublishingIsSuppressed()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            nodeMember.SetPropertyValue(20);
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnChanged_ShouldRecreateChildObserver_WhenCoreObjectValueChanges()
    {
        var initialObject = new TestCoreObject { NameValue = "Initial" };
        var nodeMember = new TestNodeMember<TestCoreObject>("TestProperty", initialObject);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        var newObject = new TestCoreObject { NameValue = "New" };
        nodeMember.SetPropertyValue(newObject);

        receivedOperations.Clear();

        initialObject.NameValue = "Old Changed";

        Assert.That(receivedOperations, Is.Empty);

        newObject.NameValue = "New Changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnChanged_ShouldDisposeOldChildObserver_WhenValueChanges()
    {
        var initialObject = new TestCoreObject { NameValue = "Initial" };
        var nodeMember = new TestNodeMember<TestCoreObject>("TestProperty", initialObject);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        nodeMember.SetPropertyValue(null);

        receivedOperations.Clear();

        initialObject.NameValue = "Changed";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnChanged_ShouldHandleNullToValueTransition()
    {
        var nodeMember = new TestNodeMember<TestCoreObject>("TestProperty");
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        var newObject = new TestCoreObject { NameValue = "New" };
        nodeMember.SetPropertyValue(newObject);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));

        receivedOperations.Clear();

        newObject.NameValue = "Changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region Animation Change Tests

    [Test]
    public void OnAnimationChanged_ShouldPublishOperation_WhenAnimationChanges()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        var newAnimation = new KeyFrameAnimation<float>();
        nodeMember.SetAnimation(newAnimation);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NewValue, Is.SameAs(newAnimation));
        Assert.That(operation.OldValue, Is.SameAs(animation));
        Assert.That(operation.PropertyPath, Is.EqualTo("Animation"));
    }

    [Test]
    public void OnAnimationChanged_ShouldNotPublish_WhenPublishingIsSuppressed()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            var newAnimation = new KeyFrameAnimation<float>();
            nodeMember.SetAnimation(newAnimation);
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnAnimationChanged_ShouldRecreateAnimationObserver()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        var newAnimation = new KeyFrameAnimation<float>();
        nodeMember.SetAnimation(newAnimation);

        receivedOperations.Clear();

        newAnimation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0f });

        Assert.That(receivedOperations, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void OnAnimationChanged_ShouldSetAnimationToNull()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        nodeMember.SetAnimation(null);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NewValue, Is.Null);
        Assert.That(operation.OldValue, Is.SameAs(animation));
    }

    [Test]
    public void OnAnimationChanged_ShouldUsePropertyPath_WhenProvided()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "Parent");

        var newAnimation = new KeyFrameAnimation<float>();
        nodeMember.SetAnimation(newAnimation);

        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation!.PropertyPath, Is.EqualTo("Parent.Animation"));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldCompleteObservable()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var completed = false;

        var sut = new NodeMemberOperationObserver(null, nodeMember, _sequenceGenerator);

        using var subscription = sut.Operations.Subscribe(
            _ => { },
            _ => { },
            () => completed = true);

        sut.Dispose();

        Assert.That(completed, Is.True);
    }

    [Test]
    public void Dispose_ShouldUnsubscribeFromPropertyChanges()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);
        sut.Dispose();

        nodeMember.SetPropertyValue(20);

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldUnsubscribeFromAnimationChanges()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);
        sut.Dispose();

        nodeMember.SetAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldDisposeChildObserver()
    {
        var coreObject = new TestCoreObject { NameValue = "Initial" };
        var nodeMember = new TestNodeMember<TestCoreObject>("TestProperty", coreObject);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);
        sut.Dispose();

        coreObject.NameValue = "Changed";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldDisposeAnimationObserver()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);
        sut.Dispose();

        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0f });

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldDisposeCollectionObserver()
    {
        var list = new CoreList<TestCoreObject>();
        var nodeMember = new TestNodeMember<CoreList<TestCoreObject>>("TestProperty", list);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);
        sut.Dispose();

        list.Add(new TestCoreObject());

        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region PropertyPathsToTrack Tests

    [Test]
    public void Constructor_ShouldNotTrackProperty_WhenPropertyPathsToTrackExcludesIt()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var pathsToTrack = new HashSet<string> { "Animation" };

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "", pathsToTrack);

        nodeMember.SetPropertyValue(20);

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Constructor_ShouldNotTrackAnimation_WhenPropertyPathsToTrackExcludesIt()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var pathsToTrack = new HashSet<string> { "Property" };

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "", pathsToTrack);

        nodeMember.SetAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Constructor_ShouldTrackBoth_WhenPropertyPathsToTrackIncludesBothOrIsNull(bool listBoth)
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        HashSet<string>? pathsToTrack = listBoth ? new HashSet<string> { "Property", "Animation" } : null;

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "", pathsToTrack);

        nodeMember.SetPropertyValue(2.0f);
        nodeMember.SetAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    #endregion

    #region Sequence Number Tests

    [Test]
    public void Operations_ShouldHaveIncrementingSequenceNumbers()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        nodeMember.SetPropertyValue(20);
        nodeMember.SetPropertyValue(30);
        nodeMember.SetPropertyValue(40);

        Assert.That(receivedOperations[0].SequenceNumber, Is.LessThan(receivedOperations[1].SequenceNumber));
        Assert.That(receivedOperations[1].SequenceNumber, Is.LessThan(receivedOperations[2].SequenceNumber));
    }

    [Test]
    public void SequenceNumber_ShouldBeUsedAcrossPropertyAndAnimation()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        nodeMember.SetPropertyValue(2.0f);
        nodeMember.SetAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations[0].SequenceNumber, Is.LessThan(receivedOperations[1].SequenceNumber));
    }

    #endregion

    #region Operations Property Tests

    [Test]
    public void Operations_ShouldSupportMultipleSubscribers()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOps1 = new List<ChangeOperation>();
        var receivedOps2 = new List<ChangeOperation>();

        using var sut = new NodeMemberOperationObserver(null, nodeMember, _sequenceGenerator);

        using var sub1 = sut.Operations.Subscribe(op => receivedOps1.Add(op));
        using var sub2 = sut.Operations.Subscribe(op => receivedOps2.Add(op));

        nodeMember.SetPropertyValue(20);

        Assert.That(receivedOps1, Has.Count.EqualTo(1));
        Assert.That(receivedOps2, Has.Count.EqualTo(1));
    }

    #endregion

    #region Property Null Tests

    [Test]
    public void Constructor_ShouldHandleNullProperty()
    {
        var nodeMember = new DefaultNodeMember<int>();

        Assert.DoesNotThrow(() =>
        {
            using var sut = new NodeMemberOperationObserver(null, nodeMember, _sequenceGenerator);
        });
    }

    #endregion
}
