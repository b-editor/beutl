using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// A presenter's Target is declared as a reference, so it is set through the Expressions form like a scene reference.
[TestFixture]
public sealed class PresenterTargetApplyTests
{
    private static (AgentToolkitTestSession Session, Scene Scene, RectShape Target, DrawablePresenter Presenter) CreateSession()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "scene") { Uri = new Uri(Path.Combine(dir, "scene.scene")) };
        var project = new Project();
        project.Items.Add(scene);
        _ = new BeutlApplication { Project = project };

        var target = new RectShape();
        var presenter = new DrawablePresenter();
        scene.Children.Add(CreateElement(dir, "target", 0, target));
        scene.Children.Add(CreateElement(dir, "presenter", 1, presenter));
        return (new AgentToolkitTestSession(scene), scene, target, presenter);
    }

    private static Element CreateElement(string dir, string name, int zIndex, Drawable drawable)
    {
        var element = new Element
        {
            Name = name,
            ZIndex = zIndex,
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, $"{name}.belm")),
        };
        element.AddObject(drawable);
        return element;
    }

    private static JsonObject ObjectNode(JsonObject document, string elementName)
    {
        JsonObject element = ((JsonArray)document["Elements"]!).OfType<JsonObject>()
            .Single(node => (string?)node["Name"] == elementName);
        return (JsonObject)((JsonArray)element["Objects"]!)[0]!;
    }

    [Test]
    public void Apply_DirectTargetValue_IsRejectedWithExpressionsGuidance()
    {
        (AgentToolkitTestSession session, Scene scene, _, DrawablePresenter presenter) = CreateSession();
        using (session)
        {
            JsonObject desired = session.Documents.Read(scene);
            ObjectNode(desired, "presenter")["Target"] = new JsonObject
            {
                ["$type"] = ObjectNode(desired, "target")["$type"]!.DeepClone(),
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => new Reconciler().Apply(session, desired));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("Expressions"));
                Assert.That(presenter.Target.CurrentValue, Is.Null);
                Assert.That(presenter.Target.Expression, Is.Null);
            }
        }
    }

    [Test]
    public void Apply_TargetReference_PointsThePresenterAtTheExistingDrawable()
    {
        (AgentToolkitTestSession session, Scene scene, RectShape target, DrawablePresenter presenter) = CreateSession();
        using (session)
        {
            JsonObject desired = session.Documents.Read(scene);
            ObjectNode(desired, "presenter")["Expressions"] = new JsonObject
            {
                ["Target"] = new JsonObject { ["ObjectId"] = JsonValue.Create(target.Id) },
            };

            new Reconciler().Apply(session, desired);

            Assert.That((presenter.Target.Expression as IReferenceExpression)?.ObjectId, Is.EqualTo(target.Id));
        }
    }
}
