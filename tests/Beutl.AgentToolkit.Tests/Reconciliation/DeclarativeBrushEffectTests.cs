using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio.Effects;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class DeclarativeBrushEffectTests
{
    [Test]
    public void Patch_applies_gradient_brush_and_effect_chain_declaratively()
    {
        Scene scene = CreateSceneWithRect(out Element element, out RectShape rect);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject createPatch = PatchRectObject(
            element,
            rect,
            new JsonObject
            {
                [nameof(Shape.Fill)] = SerializeWithoutIds(CreateGradientBrush()),
                [nameof(Drawable.FilterEffect)] = SerializeWithoutIds(CreateEffectChain())
            });

        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        var fill = (LinearGradientBrush)rect.Fill.CurrentValue!;
        var effects = (FilterEffectGroup)rect.FilterEffect.CurrentValue!;
        var firstStop = fill.GradientStops[0];
        var blur = (Blur)effects.Children[0]!;

        JsonObject updatePatch = PatchRectObject(
            element,
            rect,
            new JsonObject
            {
                [nameof(Shape.Fill)] = new JsonObject
                {
                    [nameof(CoreObject.Id)] = fill.Id.ToString(),
                    [nameof(GradientBrush.GradientStops)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = firstStop.Id.ToString(),
                        [nameof(GradientStop.Offset)] = 0.2,
                        [nameof(GradientStop.Color)] = CoreSerializer.SerializeToJsonNode(Color.FromRgb(0x33, 0x66, 0xff))
                    })
                },
                [nameof(Drawable.FilterEffect)] = new JsonObject
                {
                    [nameof(CoreObject.Id)] = effects.Id.ToString(),
                    [nameof(FilterEffectGroup.Children)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = blur.Id.ToString(),
                        [nameof(Blur.Sigma)] = CoreSerializer.SerializeToJsonNode(new Size(12, 12))
                    })
                }
            });

        ToolResult<ApplyEditResponse> updateApply = tools.ApplyEdit(
            patch: updatePatch,
            schemaVersion: SchemaVersion.Current);

        fill = (LinearGradientBrush)rect.Fill.CurrentValue!;
        effects = (FilterEffectGroup)rect.FilterEffect.CurrentValue!;
        firstStop = fill.GradientStops.Single(stop => stop.Id == firstStop.Id);
        blur = (Blur)effects.Children.Single(child => child.Id == blur.Id)!;

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(createApply.Value!.Changes!.Select(change => change.Operation), Does.Contain(ChangeOperations.SetProperty));
            Assert.That(fill.GradientStops, Has.Count.EqualTo(2));
            Assert.That(effects.Children, Has.Count.EqualTo(2));
            Assert.That(effects.Children[1], Is.InstanceOf<Brightness>());
            Assert.That(updateApply.IsSuccess, Is.True, updateApply.Error?.Message);
            Assert.That(firstStop.Offset.CurrentValue, Is.EqualTo(0.2f));
            Assert.That(firstStop.Color.CurrentValue, Is.EqualTo(Color.FromRgb(0x33, 0x66, 0xff)));
            Assert.That(blur.Sigma.CurrentValue, Is.EqualTo(new Size(12, 12)));
        });

        session.History.Undo();

        blur = (Blur)((FilterEffectGroup)rect.FilterEffect.CurrentValue!).Children[0]!;
        Assert.That(blur.Sigma.CurrentValue, Is.EqualTo(new Size(8, 8)));
    }

    [Test]
    public void Desired_document_absent_typed_property_clears_existing_value()
    {
        Scene scene = CreateSceneWithRect(out Element element, out RectShape rect);
        rect.FilterEffect.CurrentValue = CreateEffectChain();
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject desired = session.Documents.Read(session.Root);
        JsonObject rectJson = (JsonObject)((JsonArray)((JsonObject)((JsonArray)desired["Elements"]!)[0]!)["Objects"]!)[0]!;
        rectJson.Remove(nameof(Drawable.FilterEffect));

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(rect.FilterEffect.CurrentValue, Is.Null);
        });
    }

    [Test]
    public void Non_object_list_property_entry_reports_the_actual_field_name_in_the_error_target()
    {
        Scene scene = CreateSceneWithRect(out Element element, out _);
        // EqualizerEffect.Bands is an identity list property whose JSON field name ("Bands") is not the
        // one inferred from its element type (which would be "Children"): the rejection Target must
        // name the real property, so agent clients can locate the failing field.
        var equalizer = new EqualizerEffect();
        element.AddObject(equalizer);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject desired = session.Documents.Read(session.Root);
        JsonArray objects = (JsonArray)((JsonObject)((JsonArray)desired["Elements"]!)[0]!)["Objects"]!;
        JsonObject equalizerJson = (JsonObject)objects.Single(node =>
            node is JsonObject obj && (string?)obj[nameof(CoreObject.Id)] == equalizer.Id.ToString())!;
        var bands = (JsonArray)equalizerJson[nameof(EqualizerEffect.Bands)]!;
        equalizerJson[nameof(EqualizerEffect.Bands)] = new JsonArray(bands[0]!.DeepClone(), JsonValue.Create(42));

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Target, Is.EqualTo("Bands[1]"));
        });
    }

    [Test]
    public void Null_member_in_a_typed_replacement_list_is_rejected()
    {
        Scene scene = CreateSceneWithRect(out Element element, out _);
        var equalizer = new EqualizerEffect();
        element.AddObject(equalizer);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject desired = session.Documents.Read(session.Root);
        JsonArray objects = (JsonArray)((JsonObject)((JsonArray)desired["Elements"]!)[0]!)["Objects"]!;
        JsonObject equalizerJson = (JsonObject)objects.Single(node =>
            node is JsonObject obj && (string?)obj[nameof(CoreObject.Id)] == equalizer.Id.ToString())!;
        // No Id-bearing entries, so this is a wholesale replacement (not an identity merge): a null
        // member must be rejected rather than inserted for later rendering to dereference.
        equalizerJson[nameof(EqualizerEffect.Bands)] = new JsonArray((JsonNode?)null);

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Target, Is.EqualTo("Bands[0]"));
        });
    }

    private static LinearGradientBrush CreateGradientBrush()
    {
        return new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x1a, 0xd8, 0xff), 0),
                new GradientStop(Color.FromRgb(0xff, 0x45, 0xb5), 1)
            }
        };
    }

    private static FilterEffectGroup CreateEffectChain()
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(8, 8);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 115;

        return new FilterEffectGroup
        {
            Children =
            {
                blur,
                brightness
            }
        };
    }

    private static JsonObject SerializeWithoutIds(ICoreSerializable value)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(value);
        RemoveIds(json);
        return json;
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

    private static JsonObject PatchRectObject(Element element, RectShape rect, JsonObject rectPatch)
    {
        rectPatch[nameof(CoreObject.Id)] = rect.Id.ToString();
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(rectPatch)
            })
        };
    }

    private static Scene CreateSceneWithRect(out Element element, out RectShape rect)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        rect = new RectShape
        {
            Width = { CurrentValue = 640 },
            Height = { CurrentValue = 360 }
        };
        element.AddObject(rect);
        scene.Children.Add(element);
        return scene;
    }
}
