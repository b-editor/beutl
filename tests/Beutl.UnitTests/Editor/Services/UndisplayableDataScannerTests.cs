using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public sealed class UndisplayableDataScannerTests
{
    private const string Missing = "[Missing.Plugin]Missing.Namespace:MissingObject";
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "undisplayable-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(typeof(FallbackEngineObject))]
    [TestCase(typeof(FallbackDrawable))]
    [TestCase(typeof(FallbackSound))]
    [TestCase(typeof(FallbackFilterEffect))]
    [TestCase(typeof(FallbackTransform))]
    [TestCase(typeof(FallbackBrush))]
    [TestCase(typeof(FallbackGeometry))]
    [TestCase(typeof(FallbackPathSegment))]
    [TestCase(typeof(FallbackAudioEffect))]
    [TestCase(typeof(FallbackClipTransition))]
    [TestCase(typeof(FallbackGraphNode))]
    public void AnyFallbackIsReportedOnceWithoutItsKeptMembers(Type fallbackType)
    {
        var element = NewElement();
        var scene = NewScene();
        scene.Children.Add(element);
        var fallback = (EngineObject)Activator.CreateInstance(fallbackType)!;
        ((IFallback)fallback).Json = new JsonObject { ["$type"] = Missing, ["PluginValue"] = 7 };
        fallback.KeepUnknownMembers(new JsonObject { ["PluginValue"] = 7 });
        element.AddObject(fallback);

        UndisplayableDataEntry entry = UndisplayableDataScanner.Scan(scene).Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Kind, Is.EqualTo(UndisplayableDataKind.TypeNotFound));
            Assert.That(entry.TypeName, Is.EqualTo(Missing));
            Assert.That(entry.TargetObject, Is.SameAs(fallback));
            Assert.That(entry.Element, Is.SameAs(element));
            Assert.That(entry.SourceFilePath, Is.EqualTo(element.Uri!.LocalPath));
            Assert.That(entry.GetPreview(), Does.Contain("PluginValue"));
            Assert.That(entry.IsRecoveryProtected, Is.False);
            Assert.That(UndisplayableDataScanner.CountIssues(element)[entry.Kind], Is.EqualTo(1));
        });
    }

    [Test]
    public void ReportsTheOwningPropertyTransitionAndNestedGraph()
    {
        var scene = NewScene();
        var element = NewElement();
        scene.Children.Add(element);
        var effect = new FallbackFilterEffect { Json = new JsonObject { ["$type"] = Missing } };
        var brush = new FallbackBrush { Json = new JsonObject { ["$type"] = Missing } };
        var shape = new RectShape { Fill = { Value = brush }, FilterEffect = { Value = effect } };
        element.AddObject(shape);
        var transition = new FallbackClipTransition { Json = new JsonObject { ["$type"] = Missing } };
        element.ExitTransition.Value = transition;

        var root = new GraphModel();
        var group = new GroupNode { Name = { Value = "Outer group" } };
        var inner = new GroupNode();
        root.Nodes.Add(group);
        group.Group.Value.Nodes.Add(inner);
        var node = new FallbackGraphNode { Json = new JsonObject { ["$type"] = Missing }, Name = { Value = "Inner node" } };
        var sibling = new FallbackGraphNode { Json = new JsonObject { ["$type"] = Missing } };
        root.Nodes.Add(sibling);
        inner.Group.Value.Nodes.Add(node);
        var geometryNode = new GeometryShapeNode();
        var nodeBrush = new FallbackBrush { Json = new JsonObject { ["$type"] = Missing } };
        geometryNode.Fill.Property!.SetValue(nodeBrush);
        inner.Group.Value.Nodes.Add(geometryNode);
        element.AddObject(new NodeGraphDrawable { Model = { Value = root } });

        var entries = UndisplayableDataScanner.Scan(scene);
        var fillEntry = entries.Single(item => item.TargetObject == brush);
        var transitionEntry = entries.Single(item => item.TargetObject == transition);
        var nodeEntry = entries.Single(item => item.TargetObject == node);
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(6));
            Assert.That(fillEntry.PropertyOwner, Is.SameAs(shape));
            Assert.That(fillEntry.PropertyName, Is.EqualTo(nameof(Shape.Fill)));
            Assert.That(transitionEntry.TransitionEdge, Is.EqualTo(ElementEdge.End));
            Assert.That(nodeEntry.RootGraph, Is.SameAs(root));
            Assert.That(nodeEntry.ContainingGraph, Is.SameAs(inner.Group.Value));
            Assert.That(nodeEntry.GraphNode, Is.SameAs(node));
            Assert.That(nodeEntry.Breadcrumb, Does.Contain("Outer group").And.Contain("Inner node").And.Contain("Clip"));
            Assert.That(entries.Single(item => item.TargetObject == sibling).ContainingGraph, Is.SameAs(root));
            Assert.That(entries.Single(item => item.TargetObject == nodeBrush).GraphNode, Is.SameAs(geometryNode));
        });
        shape.Fill.Value = new SolidColorBrush();
        inner.Group.Value.Nodes.Remove(node);
        Assert.That(UndisplayableDataScanner.Scan(scene).Select(item => item.TargetObject), Does.Not.Contain(brush).And.Not.Contain(node));
    }

    [Test]
    public void ReportsUnknownGroupsOnKnownObjectsAndOrdersProjectScope()
    {
        var project = new Project { Uri = new Uri(Path.Combine(_directory, "project.bep")) };
        var other = new Scene(64, 64, "Other");
        var scene = NewScene();
        project.Items.Add(other);
        project.Items.Add(scene);
        var layer = new TimelineLayer { ZIndex = { Value = 1 }, Name = { Value = "Graphics" } };
        var marker = new SceneMarker(MediaTime.FromSeconds(2), "Cut");
        scene.Layers.Add(layer);
        scene.Markers.Add(marker);
        var element = NewElement();
        scene.Children.Add(element);

        var original = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 10 } });
        original.Width.SetKeyFrameAnimation(animation);
        JsonObject shapeJson = CoreSerializer.SerializeToJsonObject(original);
        shapeJson["FutureMember"] = "retained";
        shapeJson["Animations"]!["FutureAnimation"] = shapeJson["Animations"]![nameof(RectShape.Width)]!.DeepClone();
        shapeJson["Expressions"]!["FutureExpression"] = new JsonObject { ["Source"] = "1 + 2" };
        var shape = (RectShape)CoreSerializer.DeserializeFromJsonObject(shapeJson, typeof(EngineObject));
        shapeJson["$type"] = Missing;
        var fallback = (FallbackEngineObject)CoreSerializer.DeserializeFromJsonObject(shapeJson, typeof(EngineObject));
        element.AddObject(shape);
        element.AddObject(fallback);
        foreach (CoreObject obj in new CoreObject[] { project, scene, other, layer, marker, element })
            obj.KeepUnknownMembers(new JsonObject { ["FutureMember"] = true });

        var entries = UndisplayableDataScanner.Scan(project, scene);
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(10));
            Assert.That(entries.TakeWhile(item => item.Scene == scene).Count(), Is.EqualTo(8));
            Assert.That(entries[^2].Scene, Is.SameAs(other));
            Assert.That(entries[^1].TargetObject, Is.SameAs(project));
            Assert.That(entries[^1].SourceFilePath, Is.EqualTo(project.Uri!.LocalPath));
            Assert.That(entries[^1].Scene, Is.Null);
            Assert.That(entries.Single(item => item.TargetObject == layer).Layer, Is.SameAs(layer));
            Assert.That(entries.Single(item => item.TargetObject == marker).Marker, Is.SameAs(marker));
            Assert.That(entries.Single(item => item.Kind == UndisplayableDataKind.UnknownAnimations).UnknownKeys,
                Is.EqualTo(new[] { "FutureAnimation" }));
            Assert.That(entries.Single(item => item.Kind == UndisplayableDataKind.UnknownExpressions).UnknownKeys,
                Is.EqualTo(new[] { "FutureExpression" }));
            Assert.That(fallback.UnknownAnimations, Is.Not.Null);
            Assert.That(fallback.UnknownExpressions, Is.Not.Null);
            Assert.That(entries.Single(item => item.TargetObject == fallback).Kind, Is.EqualTo(UndisplayableDataKind.TypeNotFound));
            Assert.That(entries.Single(item => item.TargetObject == element).Breadcrumb, Does.Contain("Graphics"));
            Assert.That(UndisplayableDataScanner.Scan(scene), Has.Count.EqualTo(8));
        });
    }

    [Test]
    public void ReportsFailedDeserializationAndAnUnreadableFileWithItsOriginalText()
    {
        var scene = NewScene();
        var element = NewElement();
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        const string broken = """{"$type":"[Missing.Plugin].MissingElement", "broken":""";
        File.WriteAllText(element.Uri!.LocalPath, broken);
        scene = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        var failed = new FallbackEngineObject
        {
            Json = new JsonObject { ["$type"] = Missing },
            Reason = FallbackReason.DeserializationFailed,
            ErrorMessage = "Bad extension data",
        };
        var second = NewElement("failed");
        second.AddObject(failed);
        scene.Children.Add(second);

        var entries = UndisplayableDataScanner.Scan(scene);
        var fileEntry = entries.Single(item => item.IsUnreadableElement);
        var failedEntry = entries.Single(item => !item.IsUnreadableElement);
        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(item => item.Kind), Is.All.EqualTo(UndisplayableDataKind.DeserializationFailed));
            Assert.That(fileEntry.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(fileEntry.GetPreview(), Is.EqualTo(broken));
            Assert.That(fileEntry.SourceFilePath, Is.EqualTo(element.Uri.LocalPath));
            Assert.That(fileEntry.IsRecoveryProtected, Is.True);
            Assert.That(failedEntry.ErrorMessage, Is.EqualTo(failed.ErrorMessage));
            Assert.That(failedEntry.TypeName, Is.EqualTo(Missing));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DistinguishesBeutl1NodeTreesFromOperators(bool useNode)
    {
        var scene = NewScene();
        var element = NewElement();
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        var json = new JsonObject
        {
            [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
            [nameof(Element.Start)] = "00:00:02",
            [nameof(Element.Length)] = "00:00:01",
            ["UseNode"] = useNode,
            ["Operation"] = new JsonObject
            {
                ["Children"] = useNode ? new JsonArray() : new JsonArray(new JsonObject
                {
                    ["$type"] = Missing,
                    ["Value"] = new JsonObject { ["$type"] = Missing + "Value" },
                }),
            },
            ["NodeTree"] = new JsonObject { ["Nodes"] = new JsonArray(new JsonObject { ["Name"] = "Legacy" }) },
        };
        File.WriteAllText(element.Uri!.LocalPath, json.ToJsonString());
        scene = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);

        UndisplayableDataEntry entry = UndisplayableDataScanner.Scan(scene).Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Kind, Is.EqualTo(UndisplayableDataKind.UnmigratedLegacyContent));
            Assert.That(entry.IsLegacyNodeTree, Is.EqualTo(useNode));
            Assert.That(entry.TypeName, Is.EqualTo(useNode ? ElementMigration.LegacyNodeTreeTypeName : Missing));
            Assert.That(entry.IsRecoveryProtected, Is.True);
            if (!useNode) Assert.That(entry.GetPreview(), Does.Contain(Missing + "Value"));
        });
    }

    [Test]
    public void LocatesLossyEasingAndAKeyframeValueWithoutRepeatingRecoveryIncidents()
    {
        var scene = NewScene();
        var element = NewElement();
        var shape = new DroppingBrush();
        var animation = new KeyFrameAnimation<Brush?>();
        animation.KeyFrames.Add(new KeyFrame<Brush?> { Value = { Value = new SolidColorBrush() } });
        animation.KeyFrames.Add(new KeyFrame<Brush?>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = new SolidColorBrush() },
        });
        shape.Fill.SetKeyFrameAnimation(animation);
        element.AddObject(shape);
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Rewrite(element, json =>
        {
            var frames = json[nameof(Element.Objects)]![0]!["Animations"]![nameof(DroppingBrush.Fill)]!["KeyFrames"]!;
            frames[0]!["Easing"] = RecoveryBlockers.MissingEasingType;
            frames[0]!["Value"]!["$type"] = Missing;
            frames[1]!["Easing"] = RecoveryBlockers.MissingEasingType;
        });
        scene = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        element = scene.Children.Single();
        shape = (DroppingBrush)element.Objects.Single();
        animation = (KeyFrameAnimation<Brush?>)shape.Fill.GetKeyFrameAnimation()!;
        var entries = UndisplayableDataScanner.Scan(scene);
        var first = entries.Single(item => item.Kind == UndisplayableDataKind.LossyEasing && item.KeyTime == MediaTime.Zero);
        var value = entries.Single(item => item.Kind == UndisplayableDataKind.TypeNotFound);
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(3));
            Assert.That(first.Animation, Is.SameAs(animation));
            Assert.That(first.KeyFrame, Is.SameAs(animation.KeyFrames[0]));
            Assert.That(first.EngineObject, Is.SameAs(shape));
            Assert.That(first.PropertyName, Is.EqualTo(nameof(DroppingBrush.Fill)));
            Assert.That(first.Reason, Is.EqualTo(FallbackReason.TypeNotFound));
            Assert.That(first.Json!.GetValue<string>(), Is.EqualTo(RecoveryBlockers.MissingEasingType));
            Assert.That(first.ErrorMessage, Does.Contain(RecoveryBlockers.MissingEasingType));
            Assert.That(value.KeyFrame, Is.SameAs(first.KeyFrame));
            Assert.That(value.Animation, Is.SameAs(animation));
        });

        first.KeyFrame!.Easing.Value = new LinearEasing();
        Assert.That(element.IsRecoveryProtected, Is.True);
        Assert.That(UndisplayableDataScanner.Scan(scene).Count(item => item.Kind == UndisplayableDataKind.LossyEasing), Is.EqualTo(1));
        animation.KeyFrames.Clear();
        Assert.That(SceneRecovery.TryResumeElementPersistence(element), Is.Not.Null);
        Assert.That(UndisplayableDataScanner.CountIssues(element), Is.Empty);
    }

    [Test]
    public void ReportsTheDroppedOwnerAndRetiresItsIncidentAfterRepair()
    {
        var scene = NewScene();
        var element = NewElement();
        element.AddObject(new DroppingBrush());
        element.AddObject(RecoveryBlockers.CreateUnmigratedObject());
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Rewrite(element, json => json[nameof(Element.Objects)]![0]![nameof(DroppingBrush.Fill)] =
            new JsonObject { ["$type"] = Missing, ["Payload"] = 2 });
        scene = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        element = scene.Children.Single();
        var holder = (DroppingBrush)element.Objects[0];
        var entry = UndisplayableDataScanner.Scan(scene).Single(item => item.Kind == UndisplayableDataKind.DroppedValue);
        Assert.Multiple(() =>
        {
            Assert.That(entry.TargetObject, Is.SameAs(holder));
            Assert.That(entry.PropertyName, Is.EqualTo(nameof(DroppingBrush.Fill)));
            Assert.That(entry.TypeName, Is.EqualTo(Missing));
            Assert.That(entry.GetPreview(), Does.Contain("Payload"));
            Assert.That(entry.IsRecoveryProtected, Is.True);
        });

        // Repairing directly after Scan (without calling HeldDroppedValue first) must still retire the dropped entry.
        holder.Fill.Value = new SolidColorBrush();
        Assert.That(UndisplayableDataScanner.Scan(scene).Any(item => item.Kind == UndisplayableDataKind.DroppedValue), Is.False);
    }

    [Test]
    public void CrossSceneAndCrossElementReferencesDoNotLeakOrStealOwnership()
    {
        var project = new Project { Uri = new Uri(Path.Combine(_directory, "project.bep")) };
        var hostScene = new Scene(64, 64, "Host") { Uri = new Uri(Path.Combine(_directory, "host.scene")) };
        var referencedScene = new Scene(64, 64, "Referenced") { Uri = new Uri(Path.Combine(_directory, "ref.scene")) };
        referencedScene.KeepUnknownMembers(new JsonObject { ["FutureSceneSetting"] = 1 });
        project.Items.Add(hostScene);
        project.Items.Add(referencedScene);

        var refClip = NewElement("RefClip");
        var refFallback = new FallbackDrawable { Json = new JsonObject { ["$type"] = Missing } };
        refClip.AddObject(refFallback);
        referencedScene.Children.Add(refClip);

        var hostRefClip = NewElement("HostRefClip");
        hostRefClip.AddObject(new SceneDrawable { ReferencedScene = { Value = referencedScene } });
        hostScene.Children.Add(hostRefClip);

        // Cross-element reference: presenterClip appears before brushOwnerClip and references brushOwnerClip's FallbackBrush.
        var fallbackBrush = new FallbackBrush { Json = new JsonObject { ["$type"] = Missing } };
        var presenterClip = NewElement("PresenterClip");
        presenterClip.AddObject(new RectShape { Fill = { Value = new BrushPresenter { Target = { Value = fallbackBrush } } } });
        hostScene.Children.Add(presenterClip);

        var brushOwnerClip = NewElement("BrushOwnerClip");
        brushOwnerClip.AddObject(new RectShape { Fill = { Value = fallbackBrush } });
        hostScene.Children.Add(brushOwnerClip);

        var hostEntries = UndisplayableDataScanner.Scan(hostScene);
        var projectEntries = UndisplayableDataScanner.Scan(project, hostScene);
        var refSceneMemberEntry = projectEntries.Single(item => item.TargetObject == referencedScene);
        var refFallbackEntry = projectEntries.Single(item => item.TargetObject == refFallback);
        var brushEntry = projectEntries.Single(item => item.TargetObject == fallbackBrush);
        Assert.Multiple(() =>
        {
            Assert.That(UndisplayableDataScanner.CountIssues(hostRefClip), Is.Empty);
            Assert.That(UndisplayableDataScanner.CountIssues(presenterClip), Is.Empty);
            Assert.That(UndisplayableDataScanner.CountIssues(brushOwnerClip)[UndisplayableDataKind.TypeNotFound], Is.EqualTo(1));
            Assert.That(hostEntries, Has.Count.EqualTo(1));
            Assert.That(hostEntries[0].Element, Is.SameAs(brushOwnerClip));
            Assert.That(projectEntries, Has.Count.EqualTo(3));
            Assert.That(brushEntry.Scene, Is.SameAs(hostScene));
            Assert.That(brushEntry.Element, Is.SameAs(brushOwnerClip));
            Assert.That(refSceneMemberEntry.Scene, Is.SameAs(referencedScene));
            Assert.That(refSceneMemberEntry.Element, Is.Null);
            Assert.That(refFallbackEntry.Scene, Is.SameAs(referencedScene));
            Assert.That(refFallbackEntry.Element, Is.SameAs(refClip));
        });
    }

    [Test]
    public void UsesUnlocatedRecoveryIncidentsAsASafetyNet()
    {
        var scene = NewScene();
        var element = NewElement();
        element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri!,
            HasNonFallbackIncidents: true, RecoveryIncidents: [new("DeserializationFailed", Missing, "Recovery detail")]);
        scene.Children.Add(element);
        var entry = UndisplayableDataScanner.Scan(scene).Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Kind, Is.EqualTo(UndisplayableDataKind.DroppedValue));
            Assert.That(entry.TargetObject, Is.SameAs(element));
            Assert.That(entry.ErrorMessage, Is.EqualTo("Recovery detail"));
        });
    }

    private Scene NewScene() => new(64, 64, "Scene")
    {
        Uri = new Uri(Path.Combine(_directory, "scene.scene")),
        Duration = { Value = MediaTime.FromSeconds(10) },
    };

    private Element NewElement(string name = "Clip") => new()
    {
        Name = { Value = name },
        Uri = new Uri(Path.Combine(_directory, name + ".belm")),
        Start = { Value = MediaTime.FromSeconds(2) },
        Length = { Value = MediaTime.FromSeconds(2) },
        ZIndex = { Value = 1 },
    };

    private static void Rewrite(Element element, Action<JsonObject> edit)
    {
        string path = element.Uri!.LocalPath;
        JsonObject json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(json);
        File.WriteAllText(path, json.ToJsonString());
    }

    [SuppressResourceClassGeneration]
    public sealed class DroppingBrush : EngineObject
    {
        public DroppingBrush() => ScanProperties<DroppingBrush>();

        public IExpressionProperty<Brush?> Fill { get; } = Property.CreateAnimatable<Brush?>();

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            if (Fill.Value is IFallback) Fill.Value = null;
        }
    }
}
