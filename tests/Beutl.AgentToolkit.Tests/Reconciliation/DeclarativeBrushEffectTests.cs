using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
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
        EditTools tools = session.CreateEditTools();

        JsonObject createPatch = PatchRectObject(
            element,
            rect,
            new JsonObject
            {
                [nameof(Shape.Fill)] = SamplePatchBuilder.SerializeWithoutIds(CreateGradientBrush()),
                [nameof(Drawable.FilterEffect)] = SamplePatchBuilder.SerializeWithoutIds(CreateEffectChain())
            });

        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        var fill = (LinearGradientBrush)rect.Fill.Value!;
        var effects = (FilterEffectGroup)rect.FilterEffect.Value!;
        var firstStop = fill.GradientStops[0];
        var blur = (Blur)effects.Children[0]!;

        JsonObject updatePatch = PatchRectObject(
            element,
            rect,
            new JsonObject
            {
                [nameof(Shape.Fill)] = new JsonObject
                {
                    [nameof(CoreObject.Id)] = fill.Id.Value.ToString(),
                    [nameof(GradientBrush.GradientStops)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = firstStop.Id.Value.ToString(),
                        [nameof(GradientStop.Offset)] = 0.2,
                        [nameof(GradientStop.Color)] = CoreSerializer.SerializeToJsonNode(Color.FromRgb(0x33, 0x66, 0xff))
                    })
                },
                [nameof(Drawable.FilterEffect)] = new JsonObject
                {
                    [nameof(CoreObject.Id)] = effects.Id.Value.ToString(),
                    [nameof(FilterEffectGroup.Children)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = blur.Id.Value.ToString(),
                        [nameof(Blur.Sigma)] = CoreSerializer.SerializeToJsonNode(new Size(12, 12))
                    })
                }
            });

        ToolResult<ApplyEditResponse> updateApply = tools.ApplyEdit(
            patch: updatePatch,
            schemaVersion: SchemaVersion.Current);

        fill = (LinearGradientBrush)rect.Fill.Value!;
        effects = (FilterEffectGroup)rect.FilterEffect.Value!;
        firstStop = fill.GradientStops.Single(stop => stop.Id.Value == firstStop.Id.Value);
        blur = (Blur)effects.Children.Single(child => child.Id.Value == blur.Id.Value)!;

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(createApply.Value!.Changes!.Select(change => change.Operation), Does.Contain(ChangeOperations.SetProperty));
            Assert.That(fill.GradientStops, Has.Count.EqualTo(2));
            Assert.That(effects.Children, Has.Count.EqualTo(2));
            Assert.That(effects.Children[1], Is.InstanceOf<Brightness>());
            Assert.That(updateApply.IsSuccess, Is.True, updateApply.Error?.Message);
            Assert.That(firstStop.Offset.Value, Is.EqualTo(0.2f));
            Assert.That(firstStop.Color.Value, Is.EqualTo(Color.FromRgb(0x33, 0x66, 0xff)));
            Assert.That(blur.Sigma.Value, Is.EqualTo(new Size(12, 12)));
        });

        session.History.Undo();

        blur = (Blur)((FilterEffectGroup)rect.FilterEffect.Value!).Children[0]!;
        Assert.That(blur.Sigma.Value, Is.EqualTo(new Size(8, 8)));
    }

    [Test]
    public void Desired_document_absent_typed_property_clears_existing_value()
    {
        Scene scene = CreateSceneWithRect(out Element element, out RectShape rect);
        rect.FilterEffect.Value = CreateEffectChain();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject desired = session.Documents.Read(session.Root);
        JsonObject rectJson = (JsonObject)((JsonArray)((JsonObject)((JsonArray)desired["Elements"]!)[0]!)["Objects"]!)[0]!;
        rectJson.Remove(nameof(Drawable.FilterEffect));

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(rect.FilterEffect.Value, Is.Null);
        });
    }

    private static readonly TestCaseData[] s_invalidBandLists =
    [
        // EqualizerEffect.Bands is an identity list property whose JSON field name ("Bands") is not the
        // one inferred from its element type (which would be "Children"): the rejection Target must
        // name the real property, so agent clients can locate the failing field.
        new TestCaseData(
                (Func<JsonArray, JsonArray>)(bands => new JsonArray(bands[0]!.DeepClone(), JsonValue.Create(42))),
                "Bands[1]")
            .SetName("Non_object_list_property_entry_reports_the_actual_field_name_in_the_error_target"),
        // No Id-bearing entries, so this is a wholesale replacement (not an identity merge): a null
        // member must be rejected rather than inserted for later rendering to dereference.
        new TestCaseData((Func<JsonArray, JsonArray>)(_ => new JsonArray((JsonNode?)null)), "Bands[0]")
            .SetName("Null_member_in_a_typed_replacement_list_is_rejected"),
    ];

    [TestCaseSource(nameof(s_invalidBandLists))]
    public void Invalid_equalizer_band_entries_are_rejected(Func<JsonArray, JsonArray> replaceBands, string expectedTarget)
    {
        Scene scene = CreateSceneWithRect(out Element element, out _);
        var equalizer = new EqualizerEffect();
        element.AddObject(equalizer);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject desired = session.Documents.Read(session.Root);
        JsonArray objects = (JsonArray)((JsonObject)((JsonArray)desired["Elements"]!)[0]!)["Objects"]!;
        JsonObject equalizerJson = (JsonObject)objects.Single(node =>
            node is JsonObject obj && (string?)obj[nameof(CoreObject.Id)] == equalizer.Id.Value.ToString())!;
        equalizerJson[nameof(EqualizerEffect.Bands)] =
            replaceBands((JsonArray)equalizerJson[nameof(EqualizerEffect.Bands)]!);

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Target, Is.EqualTo(expectedTarget));
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
        blur.Sigma.Value = new Size(8, 8);
        var brightness = new Brightness();
        brightness.Amount.Value = 115;

        return new FilterEffectGroup
        {
            Children =
            {
                blur,
                brightness
            }
        };
    }

    private static JsonObject PatchRectObject(Element element, RectShape rect, JsonObject rectPatch)
    {
        rectPatch[nameof(CoreObject.Id)] = rect.Id.Value.ToString();
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                [nameof(Element.Objects)] = new JsonArray(rectPatch)
            })
        };
    }

    private static Scene CreateSceneWithRect(out Element element, out RectShape rect)
    {
        Scene scene = TestWorkspace.CreateScene();
        element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(4));
        rect = new RectShape
        {
            Width = { Value = 640 },
            Height = { Value = 360 }
        };
        element.AddObject(rect);
        return scene;
    }
}
