using System.Reactive;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.NodeGraph;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class NodeMemberOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void SetUp()
    {
        _sequenceGenerator = new OperationSequenceGenerator();
        Log.LoggerFactory = LoggerFactory.Create(b => { });
    }

    [TearDown]
    public void TearDown()
    {
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

        public NodePropertyAdapter<T>? GetProperty() => Property as NodePropertyAdapter<T>;

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
    public void Constructor_ShouldStoreNodeMemberAndSequenceGenerator()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 42);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        Assert.That(sut.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_ShouldSubscribeToObserver_WhenObserverIsNotNull()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        // Change property value to trigger an operation
        nodeMember.SetPropertyValue(20);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldNotThrow_WhenObserverIsNull()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 42);

        Assert.DoesNotThrow(() =>
        {
            using var sut = new NodeMemberOperationObserver(null, nodeMember, _sequenceGenerator);
        });
    }

    [Test]
    public void Constructor_ShouldCreateChildObserver_WhenPropertyValueIsCoreObject()
    {
        var coreObject = new TestCoreObject { NameValue = "Initial" };
        var nodeMember = new TestNodeMember<TestCoreObject>("TestProperty", coreObject);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        // Change the nested object's property - should be tracked by child observer
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

        // Add item to list - should be tracked by collection observer
        list.Add(new TestCoreObject());

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldSubscribeToAnimationChanges_WhenPropertyIsAnimatable()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        // Change animation
        var newAnimation = new KeyFrameAnimation<float>();
        nodeMember.SetAnimation(newAnimation);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.PropertyPath, Is.EqualTo("Animation"));
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

        // Change on old object should NOT trigger
        initialObject.NameValue = "Old Changed";

        Assert.That(receivedOperations, Is.Empty);

        // Change on new object SHOULD trigger
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

        // Changes on old object should not trigger operations
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

        // New object's changes should now be tracked
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

        // Changes to the new animation should be tracked
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

        // Subscribe directly to Operations to receive completion
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

        // Changes after dispose should not be tracked
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

        // Animation changes after dispose should not be tracked
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

        // Child object changes after dispose should not be tracked
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

        // Animation internal changes after dispose should not be tracked
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

        // Collection changes after dispose should not be tracked
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

    [Test]
    public void Constructor_ShouldTrackBoth_WhenPropertyPathsToTrackIncludesBoth()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var pathsToTrack = new HashSet<string> { "Property", "Animation" };

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "", pathsToTrack);

        nodeMember.SetPropertyValue(2.0f);
        nodeMember.SetAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void Constructor_ShouldTrackAll_WhenPropertyPathsToTrackIsNull()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator, "", null);

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
    public void Operations_ShouldReturnObservable()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        using var sut = new NodeMemberOperationObserver(null, nodeMember, _sequenceGenerator);

        Assert.That(sut.Operations, Is.Not.Null);
        Assert.That(sut.Operations, Is.InstanceOf<IObservable<ChangeOperation>>());
    }

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

    #region NodeMember Reference Tests

    [Test]
    public void UpdateNodeMemberOperation_ShouldContainNodeMemberReference()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var receivedOperations = new List<ChangeOperation>();
        var observer = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var sut = new NodeMemberOperationObserver(observer, nodeMember, _sequenceGenerator);

        nodeMember.SetPropertyValue(20);

        var operation = receivedOperations[0] as UpdateNodeMemberOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NodeMember, Is.SameAs(nodeMember));
    }

    #endregion

    #region Property Null Tests

    [Test]
    public void Constructor_ShouldHandleNullProperty()
    {
        var nodeMember = new DefaultNodeMember<int>();
        // Property is null by default

        Assert.DoesNotThrow(() =>
        {
            using var sut = new NodeMemberOperationObserver(null, nodeMember, _sequenceGenerator);
        });
    }

    #endregion
}
