using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class PlanApplyParityTests
{
    [Test]
    public void Patch_plan_matches_apply_and_expected_change_set_is_checked()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Start)] = TimeSpan.FromSeconds(3).ToString("c")
            })
        };

        var plan = tools.PlanEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        JsonArray expected = plan.Value!.ExpectedChangeSet;

        var rejected = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current, expectedChangeSet: []);
        var apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current, expectedChangeSet: expected);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.Value!.ExpectedChangeSet, Has.Count.EqualTo(plan.Value.Changes.Count));
            Assert.That(apply.IsSuccess, Is.True);
            Assert.That(apply.Value!.Plan.Changes.Select(change => change.Operation), Is.EqualTo(plan.Value!.Changes.Select(change => change.Operation)));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        scene.Children.Add(element);
        return scene;
    }
}
