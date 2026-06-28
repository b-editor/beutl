using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
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

        var rejected = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current, expectedChangeSet: new JsonArray());
        var apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current, expectedChangeSet: expected);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.Value!.Valid, Is.True);
            Assert.That(plan.Value!.ExpectedChangeSet, Has.Count.EqualTo(plan.Value.Changes.Count));
            Assert.That(apply.IsSuccess, Is.True);
            Assert.That(apply.Value!.Plan.Changes.Select(change => change.Operation), Is.EqualTo(plan.Value!.Changes.Select(change => change.Operation)));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public void Apply_edit_accepts_stringified_expected_change_set_entries()
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
                [nameof(Element.Start)] = TimeSpan.FromSeconds(4).ToString("c")
            })
        };

        ToolResult<ReconcilePlan> plan = tools.PlanEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        JsonArray stringifiedExpectedChangeSet = new(plan.Value!.ExpectedChangeSet
            .Select(change => JsonValue.Create(change!.ToJsonString()))
            .ToArray<JsonNode?>());
        ToolResult<ReconcileResult> apply = tools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current,
            expectedChangeSet: stringifiedExpectedChangeSet);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
        });
    }

    [Test]
    public void Composition_patch_plans_and_applies_through_declarative_loop()
    {
        Scene scene = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);
        var inputProps = new JsonObject { ["title"] = "APPLY PATCH" };

        ToolResult<PlanCompositionResponse> plan = tools.PlanComposition(
            name: "kinetic-ribbon-title",
            inputProps: inputProps,
            seed: "apply-seed",
            avoidRecent: false);
        ToolResult<ApplyCompositionResponse> apply = tools.ApplyComposition(
            planId: plan.Value!.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.Plan.Valid, Is.True);
            Assert.That(plan.Value.PlanId, Is.Not.Empty);
            Assert.That(plan.Value.DetailedPlan, Is.Null);
            Assert.That(plan.Value.Composition.Name, Is.EqualTo("kinetic-ribbon-title"));
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.AppliedPlanId, Is.EqualTo(plan.Value.PlanId));
            Assert.That(apply.Value!.Composition.ResolvedProps["title"]!.GetValue<string>(), Is.EqualTo("APPLY PATCH"));
            Assert.That(scene.Children, Has.Count.GreaterThan(3));
            Assert.That(scene.Children.Select(element => element.Name), Does.Contain("Kinetic ribbon title"));
            Assert.That(apply.Value.Result.Document.ToJsonString(), Does.Contain("APPLY PATCH"));
        });
    }

    [Test]
    public void Composition_plan_can_include_detailed_expected_change_set_when_requested()
    {
        Scene scene = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<PlanCompositionResponse> plan = tools.PlanComposition(
            name: "kinetic-ribbon-title",
            seed: "detailed-plan-seed",
            avoidRecent: false,
            includeDetailedPlan: true);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.DetailedPlan, Is.Not.Null);
            Assert.That(plan.Value.DetailedPlan!.ExpectedChangeSet, Has.Count.EqualTo(plan.Value.Plan.ChangeCount));
            Assert.That(plan.Value.Plan.UsageHint, Does.Contain("planId"));
        });
    }

    [Test]
    public void Composition_patch_plans_and_applies_after_existing_elements()
    {
        Scene scene = CreateSceneWithElement(out Element existingElement);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<PlanCompositionResponse> plan = tools.PlanComposition(
            name: "glitch-cutout-collage",
            inputProps: new JsonObject { ["title"] = "COMPOSITION PROBE" },
            seed: "existing-scene-seed",
            avoidRecent: false);
        ToolResult<ApplyCompositionResponse> apply = tools.ApplyComposition(planId: plan.Value!.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.Plan.Valid, Is.True);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(scene.Children.Select(element => element.Id), Does.Contain(existingElement.Id));
            Assert.That(scene.Children.Select(element => element.Name), Does.Contain("Glitch title"));
            Assert.That(apply.Value!.Result.Document.ToJsonString(), Does.Contain("COMPOSITION PROBE"));
        });
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        Scene scene = CreateScene();
        element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "element.belm"))
        };
        scene.Children.Add(element);
        return scene;
    }

    private static Scene CreateScene()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
    }
}
