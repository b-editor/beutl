using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Animation;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class FullDocumentApplyTests
{
    [Test]
    public void Desired_document_plans_and_applies_property_set_and_child_insert()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        desired["Duration"] = TimeSpan.FromSeconds(10).ToString("c");

        var element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = 3
        };
        element.AddObject(new TextBlock { Text = { CurrentValue = "Title" } });

        JsonObject elementJson = CoreSerializer.SerializeToJsonObject(element);
        elementJson.Remove(nameof(CoreObject.Id));
        foreach (JsonObject obj in ((JsonArray)elementJson["Objects"]!).OfType<JsonObject>())
        {
            obj.Remove(nameof(CoreObject.Id));
        }

        ((JsonArray)desired["Elements"]!).Add(elementJson);

        ReconcilePlan plan = reconciler.PlanFromCurrent(session, _ => (desired, null));

        Assert.That(plan.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.SetProperty));
        Assert.That(plan.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.InsertChild));

        ReconcileResult result = reconciler.Apply(session, desired);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(scene.Children, Has.Count.EqualTo(1));
            Assert.That(scene.Children[0].Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(scene.Children[0].Objects, Has.Count.EqualTo(1));
            Assert.That(result.Plan.Changes, Is.Not.Empty);
        });
    }

    [Test]
    public void Full_document_that_omits_an_elements_objects_clears_them()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "first.belm")) };
        element.AddObject(new TextBlock { Text = { CurrentValue = "Title" } });
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!)[0]!.AsObject().Remove("Objects");

        reconciler.Apply(session, desired);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Has.Count.EqualTo(1));
            Assert.That(scene.Children[0].Objects, Is.Empty);
        });
    }

    [Test]
    public void New_element_inserted_without_an_objects_key_starts_empty()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);

        JsonObject elementJson = CoreSerializer.SerializeToJsonObject(new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2)
        });
        elementJson.Remove(nameof(CoreObject.Id));
        elementJson.Remove("Objects");
        ((JsonArray)desired["Elements"]!).Add(elementJson);

        ReconcileResult result = reconciler.Apply(session, desired);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Has.Count.EqualTo(1));
            Assert.That(scene.Children[0].Objects, Is.Empty);
            Assert.That(result.Plan.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.InsertChild));
        });
    }

    [Test]
    public void Full_document_that_omits_keyframes_clears_the_animation()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "first.belm")) };
        var text = new TextBlock { Text = { CurrentValue = "Title" } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.AddSorted(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0 });
        animation.KeyFrames.AddSorted(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 100 });
        text.Opacity.Animation = animation;
        element.AddObject(text);
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        var animationJson = (JsonObject)desired["Elements"]![0]!["Objects"]![0]!["Animations"]!["Opacity"]!;
        animationJson.Remove(nameof(KeyFrameAnimation.KeyFrames));

        reconciler.Apply(session, desired);

        Assert.That(((KeyFrameAnimation<float>)text.Opacity.Animation!).KeyFrames, Is.Empty);
    }

    [Test]
    public void Full_document_with_a_non_object_keyframe_entry_is_rejected_and_rolled_back()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "first.belm")) };
        var text = new TextBlock { Text = { CurrentValue = "Title" } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.AddSorted(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0 });
        animation.KeyFrames.AddSorted(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 100 });
        text.Opacity.Animation = animation;
        element.AddObject(text);
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        var animationJson = (JsonObject)desired["Elements"]![0]!["Objects"]![0]!["Animations"]!["Opacity"]!;
        // A primitive entry has no Id, so silently skipping it would leave the removal pass to delete
        // every existing keyframe. The apply must be rejected instead.
        animationJson[nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(JsonValue.Create(42));

        ReconcileException rejection = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired))!;

        Assert.Multiple(() =>
        {
            Assert.That(rejection.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejection.Error.Target, Is.EqualTo("KeyFrames[0]"));
            Assert.That(((KeyFrameAnimation<float>)text.Opacity.Animation!).KeyFrames, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Full_document_that_omits_groups_clears_the_existing_scene_groups()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var first = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "first.belm")) };
        var second = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "second.belm")) };
        scene.Children.Add(first);
        scene.Children.Add(second);
        scene.Groups.Add(ImmutableHashSet.Create(first.Id, second.Id));

        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        desired.Remove("Groups");

        reconciler.Apply(session, desired);

        Assert.That(scene.Groups, Is.Empty);
    }

    // Applying a document rebuilds Scene.Groups: it clears the list, then adds each desired group.
    // Scene.Groups is a plain CoreList, so the Clear is notified as a Reset.
    [Test]
    public void Undoing_an_agent_edit_restores_the_scene_groups_it_replaced()
    {
        Scene scene = CreateSceneWithGroup(out Element[] elements, out ImmutableHashSet<Guid> group);
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        desired["Groups"] = new JsonArray($"{elements[0].Id}:{elements[2].Id}");

        reconciler.Apply(session, desired);
        Assert.That(scene.Groups, Has.Count.EqualTo(1));
        Assert.That(scene.Groups[0], Is.EquivalentTo(new[] { elements[0].Id, elements[2].Id }));

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(scene.Groups, Has.Count.EqualTo(1));
        Assert.That(scene.Groups[0], Is.SameAs(group));

        Assert.That(session.History.Redo(), Is.True);
        Assert.That(scene.Groups, Has.Count.EqualTo(1));
        Assert.That(scene.Groups[0], Is.EquivalentTo(new[] { elements[0].Id, elements[2].Id }));
    }

    [Test]
    public void Undoing_an_agent_edit_restores_the_scene_groups_it_cleared()
    {
        Scene scene = CreateSceneWithGroup(out _, out ImmutableHashSet<Guid> group);
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        desired.Remove("Groups");

        reconciler.Apply(session, desired);
        Assert.That(scene.Groups, Is.Empty);

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(scene.Groups, Has.Count.EqualTo(1));
        Assert.That(scene.Groups[0], Is.SameAs(group));
    }

    private static Scene CreateSceneWithGroup(out Element[] elements, out ImmutableHashSet<Guid> group)
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        elements =
        [
            .. Enumerable.Range(0, 3).Select(i => new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(dir, $"element{i}.belm"))
            })
        ];
        foreach (Element element in elements)
        {
            scene.Children.Add(element);
        }

        group = ImmutableHashSet.Create(elements[0].Id, elements[1].Id);
        scene.Groups.Add(group);
        return scene;
    }

    [Test]
    public void Desired_document_with_unknown_id_returns_actionable_stale_handle()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c")
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.StaleHandle));
            Assert.That(ex.Error.Hint, Does.Contain("Omit Id to create"));
            Assert.That(ex.Error.Hint, Does.Contain("keep the parent Element Id"));
            Assert.That(ex.Error.Hint, Does.Contain("apply_edit"));
        });
    }

    [Test]
    public void New_polymorphic_object_without_discriminator_returns_actionable_validation_error()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c"),
            [nameof(Element.Objects)] = new JsonArray(new JsonObject
            {
                [nameof(TextBlock.Text)] = "Title"
            })
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$type"));
            Assert.That(ex.Error.Hint, Does.Contain("get_schema"));
        });
    }

    [Test]
    public void New_element_without_discriminator_returns_actionable_validation_error()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c"),
            [nameof(Element.Objects)] = new JsonArray()
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$type"));
            Assert.That(ex.Error.Target, Does.Contain("/Elements["));
            Assert.That(ex.Error.Hint, Does.Contain("[Beutl.ProjectSystem]:Element"));
            Assert.That(ex.Error.Hint, Does.Contain("omit Id"));
        });
    }

    [Test]
    public void New_engine_object_with_unknown_property_returns_actionable_validation_error()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c"),
            [nameof(Element.Objects)] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(TextBlock)),
                [nameof(TextBlock.Text)] = "Title",
                ["FontSize"] = 72
            })
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("FontSize"));
            Assert.That(ex.Error.Message, Does.Contain(nameof(TextBlock)));
            Assert.That(ex.Error.Hint, Does.Contain("get_schema"));
        });
    }

    [Test]
    public void Full_document_with_a_non_positive_frame_size_is_rejected()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        desired["Width"] = 0;

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("frame size"));
            Assert.That(scene.FrameSize.Width, Is.EqualTo(1920));
        });
    }

    [Test]
    public void Inserted_element_with_an_invalid_timeline_names_the_element_in_the_rejection()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
            [nameof(CoreObject.Name)] = "Intro",
            [nameof(Element.Start)] = TimeSpan.FromSeconds(-1).ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c")
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("Intro"));
            Assert.That(ex.Error.Message, Does.Contain("Start"));
        });
    }

    [Test]
    public void Full_document_with_a_non_positive_scene_duration_is_rejected()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(scene);
        desired[nameof(Scene.Duration)] = TimeSpan.Zero.ToString("c");

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("duration"));
        });
    }

    [Test]
    public void Editing_an_existing_element_to_an_invalid_timeline_is_rejected()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element
        {
            Name = "Intro",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "first.belm"))
        };
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!)[0]!.AsObject()[nameof(Element.Start)] = TimeSpan.FromSeconds(-1).ToString("c");

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.PlanFromCurrent(session, _ => (desired, null)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("Intro"));
            Assert.That(ex.Error.Message, Does.Contain("Start"));
            Assert.That(scene.Children[0].Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Dry_run_plan_does_not_create_element_sidecar_directories()
    {
        string root = TestWorkspace.CreateDirectory();
        // A scene Uri whose directory does not exist stands in for a hand-edited/untrusted path.
        string sceneDir = Path.Combine(root, "unwritten");
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(sceneDir, "Scene.scene")) };
        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c")
        });

        reconciler.PlanFromCurrent(session, _ => (desired, null));

        Assert.That(Directory.Exists(sceneDir), Is.False, "Plan (dry-run) must not create sidecar directories.");
    }
}
