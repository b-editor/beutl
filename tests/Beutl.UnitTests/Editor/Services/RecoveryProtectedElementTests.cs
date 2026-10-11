using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// When part of an element's file cannot be loaded (usually a type from a plugin that is not installed),
// recovery keeps the file as it is until the element is repaired, so saving never writes the element's
// edits. The editor must therefore refuse those edits, the way it refuses edits to a locked clip,
// instead of accepting them and losing them on save. Removing the element, and removing or replacing
// what could not be loaded, stay allowed.
[TestFixture]
public class RecoveryProtectedElementTests
{
    private const string MissingShapeType = "[Missing.Plugin]Missing.Namespace:MissingShape";

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "recovery-protected-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void MovingAProtectedElement_IsRefusedInsteadOfBeingLostOnSave()
    {
        (Uri sceneUri, string elementPath) = CreateSceneWithMissingType();
        byte[] original = File.ReadAllBytes(elementPath);
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        TimeSpan start = element.Start;
        using var harness = new HistoryHarness(scene);
        var resize = new ElementResizeService(harness.History);

        resize.Resize(scene,
            [new ElementResizeRequest(element, start + TimeSpan.FromSeconds(1), element.Length, element.ZIndex)],
            ripple: false);
        CoreSerializer.StoreToUri(scene, sceneUri);
        Element reopened = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(element.SuppressedStorageSource, Is.Not.Null, "The missing type must keep the file protected.");
            Assert.That(scene.IsElementLocked(element), Is.True);
            Assert.That(ElementEditability.IsEditable(element), Is.False);
            Assert.That(element.Start, Is.EqualTo(start), "The move must be refused, not accepted and then dropped.");
            Assert.That(harness.History.CanUndo, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(original));
            Assert.That(reopened.Start, Is.EqualTo(start));
        });
    }

    [Test]
    public void RemovingTheUnavailableObject_RepairsTheElementAndLaterEditsAreSaved()
    {
        (Uri sceneUri, string elementPath) = CreateSceneWithMissingType();
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        using var harness = new HistoryHarness(scene);
        var objects = new ElementObjectService(harness.History);
        var resize = new ElementResizeService(harness.History);
        EngineObject unavailable = element.Objects.Single();
        Assert.That(unavailable, Is.InstanceOf<IFallback>());

        bool removed = objects.Remove(element, unavailable);
        TimeSpan moved = element.Start + TimeSpan.FromSeconds(1);
        resize.Resize(scene, [new ElementResizeRequest(element, moved, element.Length, element.ZIndex)], ripple: false);
        CoreSerializer.StoreToUri(scene, sceneUri);
        Element reopened = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(element.SuppressedStorageSource, Is.Null);
            Assert.That(ElementEditability.IsEditable(element), Is.True);
            Assert.That(reopened.Start, Is.EqualTo(moved));
            Assert.That(reopened.Objects, Is.Empty);
        });
    }

    [Test]
    public void ReplacingTheUnavailableObject_RepairsTheElement()
    {
        (Uri sceneUri, _) = CreateSceneWithMissingType();
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        using var harness = new HistoryHarness(scene);
        var objects = new ElementObjectService(harness.History);

        ObjectPasteOutcome outcome = objects.PasteOver(element, 0, CoreSerializer.SerializeToJsonString(new RectShape()));

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ObjectPasteOutcome.Pasted));
            Assert.That(element.Objects.Single(), Is.InstanceOf<RectShape>());
            Assert.That(element.SuppressedStorageSource, Is.Null);
        });
    }

    [Test]
    public void ReplacingTheUnavailableObject_WithContentThatCannotBeLoadedEither_IsRefused()
    {
        (Uri sceneUri, _) = CreateSceneWithMissingType();
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        EngineObject unavailable = element.Objects.Single();
        using var harness = new HistoryHarness(scene);
        var objects = new ElementObjectService(harness.History);
        // A known top-level type whose effect comes from a plugin that is not installed.
        JsonObject json = CoreSerializer.SerializeToJsonObject(new RectShape { FilterEffect = { CurrentValue = new Blur() } });
        json[nameof(RectShape.FilterEffect)]!.AsObject()["$type"] = "[Missing.Plugin]Missing.Namespace:MissingEffect";

        ObjectPasteOutcome outcome = objects.PasteOver(element, 0, json.ToJsonString());

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ObjectPasteOutcome.NotEditable));
            Assert.That(element.Objects.Single(), Is.SameAs(unavailable));
            Assert.That(element.SuppressedStorageSource, Is.Not.Null);
            Assert.That(harness.History.CanUndo, Is.False);
        });
    }

    [Test]
    public void ProtectedElement_RefusesOrdinaryObjectEdits()
    {
        (Uri sceneUri, _) = CreateSceneWithMissingType(addHealthyObject: true);
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        EngineObject unavailable = element.Objects[0];
        EngineObject healthy = element.Objects[1];
        Assert.That(unavailable, Is.InstanceOf<IFallback>());
        using var harness = new HistoryHarness(scene);
        var objects = new ElementObjectService(harness.History);
        var attributes = new ElementAttributeService(harness.History);
        string name = element.Name;
        string json = CoreSerializer.SerializeToJsonString(new RectShape());

        objects.Add(element, new RectShape());
        objects.InsertAt(element, 0, new RectShape());
        bool movedObject = objects.Move(element, 0, 1);
        bool toggledUnavailable = objects.SetEnabled(unavailable, !unavailable.IsEnabled);
        bool toggledHealthy = objects.SetEnabled(healthy, !healthy.IsEnabled);
        bool removedHealthy = objects.Remove(element, healthy);
        ObjectPasteOutcome pastedOverHealthy = objects.PasteOver(element, 1, json);
        attributes.SetName(element, "renamed");

        Assert.Multiple(() =>
        {
            Assert.That(element.Objects, Is.EqualTo(new[] { unavailable, healthy }));
            Assert.That(movedObject || toggledUnavailable || toggledHealthy || removedHealthy, Is.False);
            Assert.That(pastedOverHealthy, Is.EqualTo(ObjectPasteOutcome.NotEditable));
            Assert.That(element.Name, Is.EqualTo(name));
            Assert.That(element.SuppressedStorageSource, Is.Not.Null);
            Assert.That(harness.History.CanUndo, Is.False);
        });
    }

    [Test]
    public void DeletingOrExcludingAProtectedElement_IsAllowedAndKeepsItsFile()
    {
        (Uri sceneUri, string[] elementPaths) = CreateSceneWithMissingTypes(2);
        byte[][] originals = elementPaths.Select(File.ReadAllBytes).ToArray();
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element deleted = scene.Children.Single(e => e.Uri!.LocalPath == elementPaths[0]);
        Element excluded = scene.Children.Single(e => e.Uri!.LocalPath == elementPaths[1]);
        using var harness = new HistoryHarness(scene);
        var structure = new ElementStructureService(harness.History);

        structure.Delete(scene, [deleted]);
        structure.Exclude(scene, [excluded]);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Is.Empty);
            Assert.That(File.ReadAllBytes(elementPaths[0]), Is.EqualTo(originals[0]));
            Assert.That(File.ReadAllBytes(elementPaths[1]), Is.EqualTo(originals[1]));
        });
    }

    // A lock added to a protected clip would not be saved, since the retained file wins, and it would
    // block the repair and the delete. Unlocking a clip that is already locked stays allowed.
    [Test]
    public void LockingAProtectedElement_IsRefused_ButUnlockingStaysAllowed()
    {
        (Uri sceneUri, _) = CreateSceneWithMissingType();
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        using var harness = new HistoryHarness(scene);
        var attributes = new ElementAttributeService(harness.History);

        attributes.SetLocked(element, true);
        bool locked = element.IsLocked;
        bool recorded = harness.History.CanUndo;
        element.IsLocked = true;
        harness.History.Commit("locked in the retained file");
        attributes.SetLocked(element, false);

        Assert.Multiple(() =>
        {
            Assert.That(locked, Is.False);
            Assert.That(recorded, Is.False);
            Assert.That(element.IsLocked, Is.False, "Unlocking makes the repair and the delete possible again.");
            Assert.That(element.SuppressedStorageSource, Is.Not.Null);
        });
    }

    [Test]
    public void UserLock_StillStopsRemovingAProtectedElement()
    {
        (Uri sceneUri, _) = CreateSceneWithMissingType();
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element element = scene.Children.Single();
        element.IsLocked = true;
        using var harness = new HistoryHarness(scene);
        var structure = new ElementStructureService(harness.History);
        var objects = new ElementObjectService(harness.History);

        structure.Delete(scene, [element]);
        bool removedUnavailable = objects.Remove(element, element.Objects.Single());

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Does.Contain(element));
            Assert.That(removedUnavailable, Is.False);
            Assert.That(element.SuppressedStorageSource, Is.Not.Null);
        });
    }

    [Test]
    public void Observe_FollowsRepairUndoAndRedo()
    {
        using var harness = new SceneHistoryHarness("beutl_recovery_observe");
        Element element = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var fallback = new FallbackEngineObject();
        element.AddObject(fallback);
        element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri!);
        harness.History.Commit("Setup");
        var objects = new ElementObjectService(harness.History);
        var values = new List<bool>();
        using IDisposable subscription = ElementEditability.Observe(element, harness.Scene).Subscribe(values.Add);

        Assert.That(values, Is.EqualTo(new[] { false }));
        Assert.That(objects.Remove(element, fallback), Is.True);
        Assert.That(values, Is.EqualTo(new[] { false, true }));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(values, Is.EqualTo(new[] { false, true, false }), "Undo puts the protection back.");
        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(values, Is.EqualTo(new[] { false, true, false, true }));
    }

    [Test]
    public void CanRepair_AllowsOnlyContentThatHoldsWhatCouldNotBeLoaded()
    {
        using var harness = new SceneHistoryHarness("beutl_recovery_repair");
        Element element = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var holder = new RectShape();
        var unavailable = new FallbackEngineObject();
        element.AddObject(unavailable);
        element.AddObject(holder);
        element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(ElementEditability.CanRepair(element, unavailable), Is.True);
            Assert.That(ElementEditability.CanRepair(element, holder), Is.False);
            Assert.That(ElementEditability.CanRepair(element, null), Is.False);
        });

        element.IsLocked = true;
        Assert.That(ElementEditability.CanRepair(element, unavailable), Is.False, "A lock refuses repairs too.");
    }

    [Test]
    public void CanRepair_WhenNothingIsLeftToRepair_AllowsReplacingAnyContent()
    {
        // The unreadable value was dropped while loading, so no object holds it any more. Replacing
        // content is then the only way to repair the element, as it was before it became read-only.
        using var harness = new SceneHistoryHarness("beutl_recovery_dropped");
        Element element = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var shape = new RectShape();
        element.AddObject(shape);
        element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(ElementEditability.IsEditable(element), Is.False);
            Assert.That(ElementEditability.CanRepair(element, shape), Is.True);
            Assert.That(ElementEditability.CanRepair(element, null), Is.True);
        });
    }

    [Test]
    public void ProtectedFollower_IsARippleAnchor()
    {
        using var harness = new SceneHistoryHarness("beutl_recovery_ripple", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        Element removed = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element follower = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), zIndex: 0);
        Element grown = harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), zIndex: 1);
        Element secondFollower = harness.AddElement(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(2), zIndex: 1);
        follower.SuppressedStorageSource = new SuppressedStorageSource([], follower.Uri!);
        secondFollower.SuppressedStorageSource = new SuppressedStorageSource([], secondFollower.Uri!);

        new ElementStructureService(harness.History).Delete(harness.Scene, [removed], ripple: true);
        // Growing the right edge from 12 to 15 would overlap the protected follower at [12,14].
        new ElementResizeService(harness.History).Resize(harness.Scene,
            [new ElementResizeRequest(grown, grown.Start, TimeSpan.FromSeconds(5), grown.ZIndex)],
            ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(follower.Start, Is.EqualTo(TimeSpan.FromSeconds(4)), "Shifting it would not be saved.");
            Assert.That(secondFollower.Start, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(grown.Range.End, Is.EqualTo(TimeSpan.FromSeconds(12)), "The grow stops at the protected follower.");
        });
    }

    [Test]
    public void CloseAllGaps_DoesNotMoveAProtectedElement()
    {
        using var harness = new SceneHistoryHarness("beutl_recovery_gaps", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element protectedElement = harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));
        protectedElement.SuppressedStorageSource = new SuppressedStorageSource([], protectedElement.Uri!);

        harness.Scene.CloseAllGaps();

        Assert.That(protectedElement.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public void Notice_NamesTheProtectedElementsOfTheScene()
    {
        (Uri sceneUri, string[] elementPaths) = CreateSceneWithMissingTypes(2);
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element unnamed = scene.Children.Single(e => e.Uri!.LocalPath == elementPaths[1]);
        unnamed.Name = string.Empty;
        var healthy = new Element { Name = "Healthy", Uri = new Uri(Path.Combine(_root, "healthy.belm")) };
        scene.Children.Add(healthy);
        using var cancellation = new CancellationTokenSource();

        Notification? notice = RecoveryProtectionNotice.Create(scene, cancellation.Token);

        Assert.That(notice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(notice!.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notice.Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
            Assert.That(notice.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(notice.Message, Does.Contain(scene.Name));
            Assert.That(notice.Message, Does.Contain("element0"));
            Assert.That(notice.Message, Does.Contain("element1"), "An unnamed element is named by its file.");
            Assert.That(notice.Message, Does.Not.Contain("Healthy"));
        });
    }

    [Test]
    public void Notice_IsNotShownWithoutProtectedElements_AndListsAtMostFiveNames()
    {
        var scene = new Scene(64, 64, "Plain");
        scene.Children.Add(new Element { Name = "Healthy" });
        Assert.That(RecoveryProtectionNotice.Create(scene), Is.Null);

        for (int i = 0; i < 7; i++)
        {
            var element = new Element
            {
                Name = $"Protected{i}",
                Start = TimeSpan.FromSeconds(i),
                Uri = new Uri(Path.Combine(_root, $"protected{i}.belm")),
            };
            element.SuppressedStorageSource = new SuppressedStorageSource([], element.Uri);
            scene.Children.Add(element);
        }

        string message = RecoveryProtectionNotice.Create(scene)!.Message;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Protected0, Protected1, Protected2, Protected3, Protected4, …"));
            Assert.That(message, Does.Not.Contain("Protected5"));
        });
    }

    private (Uri SceneUri, string ElementPath) CreateSceneWithMissingType(bool addHealthyObject = false)
    {
        (Uri sceneUri, string[] elementPaths) = CreateSceneWithMissingTypes(1, addHealthyObject);
        return (sceneUri, elementPaths[0]);
    }

    // Saves a scene whose elements each hold a rectangle, then rewrites the rectangle's type in every
    // element file to one from an assembly that is not loaded, as when the plugin that provides it is
    // not installed.
    private (Uri SceneUri, string[] ElementPaths) CreateSceneWithMissingTypes(int count, bool addHealthyObject = false)
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        string[] elementPaths = new string[count];
        for (int i = 0; i < count; i++)
        {
            elementPaths[i] = Path.Combine(_root, $"element{i}.belm");
            var element = new Element
            {
                Name = $"element{i}",
                Start = TimeSpan.FromSeconds(i * 2),
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPaths[i]),
            };
            element.AddObject(new RectShape());
            if (addHealthyObject) element.AddObject(new EllipseShape());
            scene.Children.Add(element);
        }

        CoreSerializer.StoreToUri(scene, sceneUri);
        foreach (string elementPath in elementPaths)
        {
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject()["$type"] = MissingShapeType;
            File.WriteAllText(elementPath, json.ToJsonString());
        }

        return (sceneUri, elementPaths);
    }
}
