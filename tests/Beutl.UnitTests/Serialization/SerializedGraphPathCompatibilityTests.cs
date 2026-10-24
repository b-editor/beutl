using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

public class SerializedGraphPathCompatibilityTests
{
    [TestCase(false, "index:0")]
    [TestCase(true, "key:7")]
    public void ContainerKeysKeepTheirMainShape(bool dictionary, string suffix)
    {
        var transform = new RotationTransform();
        var holder = new ContainerHolder();
        holder.Content.Value = dictionary
            ? new Dictionary<int, Transform> { [7] = transform }
            : new List<Optional<Transform>> { new(transform) };
        var element = new Element();
        element.AddObject(holder);

        SerializedGraphPath path = SerializedGraphPaths.EnumerateSerializedGraphDescendantPaths(element)
            .Single(item => ReferenceEquals(item.Object, transform)).Path;

        Assert.Multiple(() =>
        {
            Assert.That(path.Stable, Is.EqualTo($"$/property:Objects/id:{holder.Id.Value:D}#0/property:Content/{suffix}"));
            Assert.That(path.Positional, Is.EqualTo($"$/property:Objects/index:0/property:Content/{suffix}"));
        });
    }

    [Test]
    public void VisitingARecoveredSceneCycleDoesNotRebuildItsRecoveryMetadata()
    {
        var scene = new Scene { Uri = new Uri("file:///visitor/main.scene") };
        var element = new Element { Uri = new Uri("file:///visitor/element.belm") };
        element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri);
        element.AddObject(new SceneLink { Scene = scene });
        scene.Children.Add(element);

        Assert.That(SerializedGraphTraversal.Enumerate(scene).OfType<Scene>(), Is.EqualTo(new[] { scene }));
    }

    [SuppressResourceClassGeneration]
    private sealed class SceneLink : EngineObject
    {
        public Scene? Scene { get; init; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Scene), Scene);
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class ContainerHolder : EngineObject
    {
        public ContainerHolder() => ScanProperties<ContainerHolder>();

        public IExpressionProperty<object?> Content { get; } = Property.CreateExpression<object?>();
    }
}
