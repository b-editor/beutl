using System.Text.Json.Nodes;
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
