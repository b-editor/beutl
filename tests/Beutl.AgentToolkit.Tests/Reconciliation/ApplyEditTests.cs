using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ApplyEditTests
{
    [Test]
    public void Apply_edit_applies_patch_directly()
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

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(apply.Value.AppliedChangeSet, Has.Count.EqualTo(apply.Value.Changes!.Count));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public void Apply_edit_applies_desired_document_directly()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);
        JsonObject desired = session.Documents.Read(session.Root);
        ((JsonObject)((JsonArray)desired["Elements"]!)[0]!)[nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c");

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(scene.Children.Single().Id, Is.EqualTo(element.Id));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void Apply_edit_rejects_payloads_that_deserialize_to_fallback_objects_without_mutation()
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

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain("fallback object"));
            Assert.That(apply.Error.Target, Does.Contain("Objects[0]"));
            Assert.That(apply.Error.Hint, Does.Contain("get_schema"));
            Assert.That(apply.Error.Hint, Does.Contain("Objects require concrete EngineObject discriminators"));
            Assert.That(scene.Children, Is.Empty);
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
            Assert.That(compact.Value.AppliedChangeSet, Has.Count.EqualTo(compact.Value.Changes!.Count));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted element"));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted title"));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Path), Has.Some.Contains("/Elements"));
            Assert.That(withDocument.IsSuccess, Is.True, withDocument.Error?.Message);
            Assert.That(withDocument.Value!.Document, Is.Not.Null);
            Assert.That(withDocument.Value.Document!.ToJsonString(), Does.Contain("Inserted title"));
        });
    }

    [Test]
    public void Apply_edit_quiet_response_returns_summary_without_details()
    {
        Scene scene = CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateInsertedTextElementPatch(),
            schemaVersion: SchemaVersion.Current,
            quiet: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(result.Value.ChangeCount, Is.GreaterThan(0));
            Assert.That(result.Value.Operations.Values.Sum(), Is.EqualTo(result.Value.ChangeCount));
            Assert.That(result.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted title"));
            Assert.That(result.Value.Changes, Is.Null);
            Assert.That(result.Value.Validation, Is.Null);
            Assert.That(result.Value.AppliedChangeSet, Is.Null);
        });
    }

    [Test]
    public void Apply_edit_null_filter_effect_clears_then_replacing_does_not_append_children()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape
        {
            Name = "Filtered rect",
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 100 },
            FilterEffect =
            {
                CurrentValue = new FilterEffectGroup
                {
                    Children = { new Blur() }
                }
            }
        };
        element.AddObject(rect);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<ApplyEditResponse> clear = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = rect.Id.ToString(),
                        [nameof(Drawable.FilterEffect)] = null
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);
        bool clearedFilterEffect = rect.FilterEffect.CurrentValue is null;

        ToolResult<ApplyEditResponse> replace = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = rect.Id.ToString(),
                        [nameof(Drawable.FilterEffect)] = new JsonObject
                        {
                            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(FilterEffectGroup)),
                            [nameof(FilterEffectGroup.Children)] = new JsonArray(new JsonObject
                            {
                                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Brightness)),
                                [nameof(Brightness.Amount)] = 120
                            })
                        }
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        var effects = (FilterEffectGroup)rect.FilterEffect.CurrentValue!;

        Assert.Multiple(() =>
        {
            Assert.That(clear.IsSuccess, Is.True, clear.Error?.Message);
            Assert.That(clear.Value!.Valid, Is.True);
            Assert.That(clearedFilterEffect, Is.True);
            Assert.That(replace.IsSuccess, Is.True, replace.Error?.Message);
            Assert.That(effects.Children, Has.Count.EqualTo(1));
            Assert.That(effects.Children[0], Is.InstanceOf<Brightness>());
        });
    }

    [Test]
    public void Apply_edit_accepts_enum_member_names()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var text = new TextBlock { Text = { CurrentValue = "Enum title" } };
        element.AddObject(text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = text.Id.ToString(),
                    [nameof(TextBlock.BlendMode)] = "Plus",
                    [nameof(TextBlock.FontWeight)] = "bold"
                })
            })
        };

        ToolResult<ApplyEditResponse> namedApply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        BlendMode namedBlendMode = text.BlendMode.CurrentValue;
        FontWeight namedFontWeight = text.FontWeight.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(namedApply.IsSuccess, Is.True, namedApply.Error?.Message);
            Assert.That(namedApply.Value!.Valid, Is.True);
            Assert.That(namedBlendMode, Is.EqualTo(BlendMode.Plus));
            Assert.That(namedFontWeight, Is.EqualTo(FontWeight.Bold));
        });
    }

    [Test]
    public void Apply_edit_rejects_numeric_string_enum_values()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var text = new TextBlock { Text = { CurrentValue = "Enum title" } };
        element.AddObject(text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = text.Id.ToString(),
                    [nameof(TextBlock.BlendMode)] = ((int)BlendMode.Screen).ToString()
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain(nameof(TextBlock.BlendMode)));
            Assert.That(text.BlendMode.CurrentValue, Is.EqualTo(BlendMode.SrcOver));
        });
    }

    [Test]
    public void Apply_edit_rejects_unknown_enum_member_names()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var text = new TextBlock { Text = { CurrentValue = "Enum title" } };
        element.AddObject(text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = text.Id.ToString(),
                    [nameof(TextBlock.BlendMode)] = "NotARealMode"
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain(nameof(TextBlock.BlendMode)));
            Assert.That(text.BlendMode.CurrentValue, Is.EqualTo(BlendMode.SrcOver));
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
