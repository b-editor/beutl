using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class FullDocumentApplyTests
{
    [Test]
    public void Desired_document_plans_and_applies_property_set_and_child_insert()
    {
        using var session = TestEditingSession.Create(new Scene(1920, 1080, "Scene"));
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(session.Scene);
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

        ReconcilePlan plan = reconciler.Plan(session, desired);

        Assert.That(plan.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.SetProperty));
        Assert.That(plan.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.InsertChild));

        ReconcileResult result = reconciler.Apply(session, desired);

        Assert.Multiple(() =>
        {
            Assert.That(session.Scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(session.Scene.Children, Has.Count.EqualTo(1));
            Assert.That(session.Scene.Children[0].Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(session.Scene.Children[0].Objects, Has.Count.EqualTo(1));
            Assert.That(result.Plan.Changes, Is.Not.Empty);
        });
    }

    [Test]
    public void Full_document_that_omits_groups_clears_the_existing_scene_groups()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var first = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "first.belm")) };
        var second = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "second.belm")) };
        scene.Children.Add(first);
        scene.Children.Add(second);
        scene.Groups.Add(ImmutableHashSet.Create(first.Id, second.Id));

        using var session = TestEditingSession.Create(scene);
        var reconciler = new Reconciler();

        JsonObject desired = session.Documents.Read(scene);
        desired.Remove("Groups");

        reconciler.Apply(session, desired);

        Assert.That(session.Scene.Groups, Is.Empty);
    }

    [Test]
    public void Mid_reconcile_failure_rolls_back_prior_live_mutations()
    {
        var root = new ThrowingCoreObject();
        using var session = TestEditingSession.Create(root);
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(root);
        desired[nameof(ThrowingCoreObject.First)] = 42;
        desired[nameof(ThrowingCoreObject.Throwing)] = 1;

        Assert.Throws<InvalidOperationException>(() => reconciler.Apply(session, desired));

        Assert.That(root.First, Is.Zero);
    }

    [Test]
    public void Desired_document_with_unknown_id_returns_actionable_stale_handle()
    {
        using var session = TestEditingSession.Create(new Scene(1920, 1080, "Scene"));
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(session.Scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c")
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.Plan(session, desired))!;

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
        using var session = TestEditingSession.Create(new Scene(1920, 1080, "Scene"));
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(session.Scene);
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

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.Plan(session, desired))!;

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
        using var session = TestEditingSession.Create(new Scene(1920, 1080, "Scene"));
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(session.Scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(1).ToString("c"),
            [nameof(Element.Objects)] = new JsonArray()
        });

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.Plan(session, desired))!;

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
        using var session = TestEditingSession.Create(new Scene(1920, 1080, "Scene"));
        var reconciler = new Reconciler();
        JsonObject desired = session.Documents.Read(session.Scene);
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

        ReconcileException ex = Assert.Throws<ReconcileException>(() => reconciler.Plan(session, desired))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("FontSize"));
            Assert.That(ex.Error.Message, Does.Contain(nameof(TextBlock)));
            Assert.That(ex.Error.Hint, Does.Contain("get_schema"));
        });
    }

    private sealed class TestEditingSession : IEditingSession, IDisposable
    {
        private readonly RecordingPipeline _recording;

        private TestEditingSession(CoreObject root)
        {
            if (root is Scene { Uri: null } scene)
            {
                string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                scene.Uri = new Uri(Path.Combine(dir, "Scene.scene"));
            }

            Root = root;
            _recording = RecordingPipeline.Create(root);
        }

        public static TestEditingSession Create(CoreObject root)
        {
            return new TestEditingSession(root);
        }

        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public EditingSessionSource Source => EditingSessionSource.File;

        public Scene Scene => (Scene)Root;

        public CoreObject Root { get; }

        public HistoryManager History => _recording.History;

        public DocumentAdapter Documents { get; } = new();

        public bool IsDirty => false;

        public void Dispose()
        {
            _recording.Dispose();
        }
    }

    private sealed class ThrowingCoreObject : CoreObject
    {
        public static readonly CoreProperty<int> FirstProperty;
        public static readonly CoreProperty<int> ThrowingProperty;

        private int _first;

        static ThrowingCoreObject()
        {
            FirstProperty = ConfigureProperty<int, ThrowingCoreObject>(nameof(First))
                .Accessor(o => o.First, (o, v) => o.First = v)
                .Register();

            ThrowingProperty = ConfigureProperty<int, ThrowingCoreObject>(nameof(Throwing))
                .Accessor(o => o.Throwing, (o, v) => o.Throwing = v)
                .Register();
        }

        public int First
        {
            get => _first;
            set => SetAndRaise(FirstProperty, ref _first, value);
        }

        public int Throwing
        {
            get => 0;
            set => throw new InvalidOperationException("Injected reconcile failure.");
        }
    }
}
