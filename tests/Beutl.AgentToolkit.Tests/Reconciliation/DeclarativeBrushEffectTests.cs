using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
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
            Assert.That(createApply.Value!.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.SetProperty));
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
