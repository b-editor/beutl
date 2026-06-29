using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
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
            Assert.That(apply.Value!.Changes.Select(change => change.Operation), Is.EqualTo(plan.Value!.Changes.Select(change => change.Operation)));
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
        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
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
    public void Apply_edit_accepts_plan_id_from_plan_edit()
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
                [nameof(Element.Start)] = TimeSpan.FromSeconds(4.5).ToString("c")
            })
        };

        ToolResult<ReconcilePlan> plan = tools.PlanEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(planId: plan.Value!.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.PlanId, Is.Not.Null.And.Not.Empty);
            Assert.That(plan.Value.UsageHint, Does.Contain("planId"));
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(4.5)));
        });
    }

    [Test]
    public void Plan_edit_rejects_payloads_that_deserialize_to_fallback_objects()
    {
        Scene scene = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                [nameof(CoreObject.Name)] = "Invalid rect element",
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
                    [nameof(CoreObject.Name)] = "Invalid rect",
                    [nameof(RectShape.Width)] = "not-a-number"
                })
            })
        };

        ToolResult<ReconcilePlan> plan = tools.PlanEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.False);
            Assert.That(plan.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(plan.Error.Message, Does.Contain("fallback object"));
            Assert.That(plan.Error.Target, Does.Contain("Objects[0]"));
            Assert.That(plan.Error.Hint, Does.Contain("get_schema"));
            Assert.That(plan.Error.Hint, Does.Contain("Objects require concrete EngineObject discriminators"));
            Assert.That(scene.Children, Is.Empty);
        });
    }

    [Test]
    public void Large_plan_edit_omits_inline_change_details_but_remains_applyable_by_plan_id()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);
        string longName = new('x', 20_000);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(CoreObject.Name)] = longName
            })
        };

        ToolResult<ReconcilePlan> plan = tools.PlanEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        string serializedPlan = JsonSerializer.Serialize(plan.Value);
        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(planId: plan.Value!.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.PlanId, Is.Not.Null.And.Not.Empty);
            Assert.That(plan.Value.DetailedChangesIncluded, Is.False);
            Assert.That(plan.Value.ExpectedChangeSetIncluded, Is.False);
            Assert.That(plan.Value.ExpectedChangeSet, Has.Count.EqualTo(1));
            Assert.That(serializedPlan, Does.Not.Contain("\"changes\""));
            Assert.That(serializedPlan, Does.Not.Contain("\"expectedChangeSet\""));
            Assert.That(serializedPlan, Does.Not.Contain(longName));
            Assert.That(plan.Value.UsageHint, Does.Contain("Pass planId"));
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(scene.Children.Single().Name, Is.EqualTo(longName));
        });
    }

    [Test]
    public void Apply_edit_rejects_shorthand_expected_change_set_with_verbatim_hint()
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
                [nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c")
            })
        };

        ToolResult<ReconcilePlan> plan = tools.PlanEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        ToolResult<ApplyEditResponse> rejected = tools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current,
            expectedChangeSet: JsonValue.Create($"{plan.Value!.ExpectedChangeSet.Count} changes"));

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Message, Does.Contain("not a shorthand summary"));
            Assert.That(rejected.Error.Hint, Does.Contain("verbatim"));
            Assert.That(rejected.Error.Hint, Does.Contain("2 changes"));
        });
    }

    [Test]
    public void Apply_edit_returns_compact_response_and_optional_document()
    {
        Scene compactScene = CreateScene();
        using var compactSession = new AgentToolkitTestSession(compactScene);
        var compactManager = new AgentSessionManager();
        compactManager.UseSource(new AgentToolkitTestSessionSource(compactSession));
        var compactTools = new EditTools(compactManager);

        ToolResult<ApplyEditResponse> compact = compactTools.ApplyEdit(
            patch: CreateInsertedTextElementPatch(),
            schemaVersion: SchemaVersion.Current);

        Scene documentScene = CreateScene();
        using var documentSession = new AgentToolkitTestSession(documentScene);
        var documentManager = new AgentSessionManager();
        documentManager.UseSource(new AgentToolkitTestSessionSource(documentSession));
        var documentTools = new EditTools(documentManager);

        ToolResult<ApplyEditResponse> withDocument = documentTools.ApplyEdit(
            patch: CreateInsertedTextElementPatch(),
            schemaVersion: SchemaVersion.Current,
            includeDocument: true);

        Assert.Multiple(() =>
        {
            Assert.That(compact.IsSuccess, Is.True, compact.Error?.Message);
            Assert.That(compact.Value!.Document, Is.Null);
            Assert.That(compact.Value.Valid, Is.True);
            Assert.That(compact.Value.AppliedChangeSet, Has.Count.EqualTo(compact.Value.Changes.Count));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted element"));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted title"));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Path), Has.Some.Contains("/Elements"));
            Assert.That(withDocument.IsSuccess, Is.True, withDocument.Error?.Message);
            Assert.That(withDocument.Value!.Document, Is.Not.Null);
            Assert.That(withDocument.Value.Document!.ToJsonString(), Does.Contain("Inserted title"));
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

    private static JsonObject CreateInsertedTextElementPatch()
    {
        var element = new Element
        {
            Name = "Inserted element",
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2)
        };
        element.AddObject(new TextBlock
        {
            Name = "Inserted title",
            Text = { CurrentValue = "Inserted title" }
        });

        JsonObject elementJson = CoreSerializer.SerializeToJsonObject(element);
        RemoveIds(elementJson);
        return new JsonObject
        {
            ["Elements"] = new JsonArray(elementJson)
        };
    }

    private static void RemoveIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(nameof(CoreObject.Id));
            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RemoveIds(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RemoveIds(child);
            }
        }
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
