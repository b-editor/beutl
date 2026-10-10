using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// A merge patch is resolved into a full desired document, so every element reaches the applier on
// every apply_edit. Members whose serialized value did not change must keep their instances;
// otherwise each edit records equal-copy replacements across the whole timeline.
public sealed class ApplyEditHistoryScopeTests
{
    [Test]
    public void Apply_edit_records_only_the_patched_property()
    {
        Scene scene = CreateScene();
        RectShape firstRect = AddRectElement(scene, "first", TimeSpan.Zero, zIndex: 0);
        RectShape secondRect = AddRectElement(scene, "second", TimeSpan.FromSeconds(2), zIndex: 1);
        Element second = scene.Children[1];
        object?[] firstNested = CaptureNested(firstRect);
        object?[] secondNested = CaptureNested(secondRect);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(second, secondRect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        HistoryTransaction? transaction = session.History.PeekUndo();
        string[] operations = DescribeOperations(transaction);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(secondRect.Width.CurrentValue, Is.EqualTo(200f));
            Assert.That(operations, Is.EqualTo(new[] { $"{secondRect.Id}:Width 100 -> 200" }));
            AssertSameNested(firstRect, firstNested);
            AssertSameNested(secondRect, secondNested);
        });
    }

    [Test]
    public void Undo_and_redo_of_an_apply_edit_keep_unpatched_instances()
    {
        Scene scene = CreateScene();
        RectShape firstRect = AddRectElement(scene, "first", TimeSpan.Zero, zIndex: 0);
        RectShape secondRect = AddRectElement(scene, "second", TimeSpan.FromSeconds(2), zIndex: 1);
        Element second = scene.Children[1];
        object?[] firstNested = CaptureNested(firstRect);
        object?[] secondNested = CaptureNested(secondRect);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(second, secondRect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);
        bool undone = session.History.Undo();
        float undoneWidth = secondRect.Width.CurrentValue;
        bool redone = session.History.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(undone, Is.True);
            Assert.That(undoneWidth, Is.EqualTo(100f));
            Assert.That(redone, Is.True);
            Assert.That(secondRect.Width.CurrentValue, Is.EqualTo(200f));
            AssertSameNested(firstRect, firstNested);
            AssertSameNested(secondRect, secondNested);
        });
    }

    [Test]
    public void Apply_edit_replaces_only_the_nested_value_whose_json_changed()
    {
        Scene scene = CreateScene();
        RectShape firstRect = AddRectElement(scene, "first", TimeSpan.Zero, zIndex: 0);
        RectShape secondRect = AddRectElement(scene, "second", TimeSpan.FromSeconds(2), zIndex: 1);
        Element second = scene.Children[1];
        object?[] firstNested = CaptureNested(firstRect);
        Transform? secondTransform = secondRect.Transform.CurrentValue;
        FilterEffect? secondFilterEffect = secondRect.FilterEffect.CurrentValue;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(second, secondRect, new JsonObject
            {
                [nameof(Shape.Fill)] = new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SolidColorBrush)),
                    [nameof(SolidColorBrush.Color)] = CoreSerializer.SerializeToJsonNode(Colors.Blue)
                }
            }),
            schemaVersion: SchemaVersion.Current);

        string[] operations = DescribeOperations(session.History.PeekUndo());
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(((SolidColorBrush)secondRect.Fill.CurrentValue!).Color.CurrentValue, Is.EqualTo(Colors.Blue));
            Assert.That(operations, Has.Length.EqualTo(1));
            Assert.That(operations[0], Does.StartWith($"{secondRect.Id}:Fill "));
            Assert.That(secondRect.Transform.CurrentValue, Is.SameAs(secondTransform));
            Assert.That(secondRect.FilterEffect.CurrentValue, Is.SameAs(secondFilterEffect));
            AssertSameNested(firstRect, firstNested);
        });
    }

    // Element.EnterTransition/ExitTransition follow this pattern: Deserialize assigns them whether
    // or not the JSON carries them, so the applier must keep passing such members through even when
    // they are unchanged.
    private sealed class CustomSerializedTransformElement : Element
    {
        public static readonly CoreProperty<Transform?> CustomTransformProperty;
        private Transform? _customTransform;

        static CustomSerializedTransformElement()
        {
            CustomTransformProperty = ConfigureProperty<Transform?, CustomSerializedTransformElement>(nameof(CustomTransform))
                .Accessor(o => o.CustomTransform, (o, v) => o.CustomTransform = v)
                .Register();
        }

        [NotAutoSerialized]
        public Transform? CustomTransform
        {
            get => _customTransform;
            set => SetAndRaise(CustomTransformProperty, ref _customTransform, value);
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (CustomTransform != null)
            {
                context.SetValue(nameof(CustomTransform), CustomTransform);
            }
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            CustomTransform = context.GetValue<Transform>(nameof(CustomTransform));
        }
    }

    [Test]
    public void Apply_edit_keeps_unchanged_members_that_deserialize_assigns_unconditionally()
    {
        Scene scene = CreateScene();
        var element = new CustomSerializedTransformElement
        {
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "custom.belm")),
            CustomTransform = new TranslateTransform(10, 20)
        };
        var rect = new RectShape();
        element.AddObject(rect);
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(element, rect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(rect.Width.CurrentValue, Is.EqualTo(200f));
            Assert.That(element.CustomTransform, Is.InstanceOf<TranslateTransform>());
            Assert.That((element.CustomTransform as TranslateTransform)?.X.CurrentValue, Is.EqualTo(10f));
        });
    }

    public sealed record Dimensions(int Width, int Height);

    public sealed class DimensionsJsonConverter : JsonConverter<Dimensions>
    {
        public override Dimensions Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonObject obj = JsonNode.Parse(ref reader)!.AsObject();
            return new Dimensions(obj["W"]!.GetValue<int>(), obj["H"]!.GetValue<int>());
        }

        public override void Write(Utf8JsonWriter writer, Dimensions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("W", value.Width);
            writer.WriteNumber("H", value.Height);
            writer.WriteEndObject();
        }
    }

    // CoreProperty.RouteDeserialize reads a converter-backed property even when its key is absent.
    private sealed class ConvertedPropertyElement : Element
    {
        public static readonly CoreProperty<Dimensions?> DimensionsProperty;

        static ConvertedPropertyElement()
        {
            DimensionsProperty = ConfigureProperty<Dimensions?, ConvertedPropertyElement>(nameof(Dimensions))
                .Register();
        }

        [JsonConverter(typeof(DimensionsJsonConverter))]
        public Dimensions? Dimensions
        {
            get => GetValue(DimensionsProperty);
            set => SetValue(DimensionsProperty, value);
        }
    }

    [Test]
    public void Apply_edit_keeps_unchanged_converter_backed_registered_properties()
    {
        Scene scene = CreateScene();
        var element = new ConvertedPropertyElement
        {
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "converted.belm")),
            Dimensions = new Dimensions(3, 4)
        };
        var rect = new RectShape();
        element.AddObject(rect);
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(element, rect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(rect.Width.CurrentValue, Is.EqualTo(200f));
            Assert.That(element.Dimensions, Is.EqualTo(new Dimensions(3, 4)));
        });
    }

    // A fallback replaces its Json with whatever it is populated from and saves that Json verbatim,
    // so a member left out of the payload would be dropped from the project file.
    [Test]
    public void Apply_edit_keeps_the_full_json_of_an_untouched_fallback()
    {
        Scene scene = CreateScene();
        RectShape rect = AddRectElement(scene, "edited", TimeSpan.Zero, zIndex: 0);
        Element edited = scene.Children[0];
        var transform = new TranslateTransform(10, 20);
        var fallbackJson = new JsonObject
        {
            ["$type"] = "[Missing.Plugin]Missing.Namespace:MissingDrawable",
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
            [nameof(Drawable.Transform)] = CoreSerializer.SerializeToJsonObject(transform)
        };
        var fallback = (Drawable)CoreSerializer.DeserializeFromJsonObject(fallbackJson, typeof(Drawable));
        var holder = new Element
        {
            Name = "holder",
            Start = TimeSpan.FromSeconds(2),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = 1,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "holder.belm"))
        };
        holder.AddObject(fallback);
        scene.Children.Add(holder);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(edited, rect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(holder.Objects.Single(), Is.SameAs(fallback));
            Assert.That(((IFallback)fallback).Json?.ContainsKey(nameof(Drawable.Transform)), Is.True);
        });
    }

    private static EditTools CreateTools(AgentToolkitTestSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return new EditTools(manager);
    }

    private static JsonObject CreateObjectPatch(Element element, RectShape rect, JsonObject properties)
    {
        properties[nameof(CoreObject.Id)] = rect.Id.ToString();
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(properties)
            })
        };
    }

    private static object?[] CaptureNested(RectShape rect)
    {
        return
        [
            rect.Fill.CurrentValue,
            rect.Pen.CurrentValue,
            rect.Transform.CurrentValue,
            rect.FilterEffect.CurrentValue
        ];
    }

    private static void AssertSameNested(RectShape rect, object?[] expected)
    {
        object?[] actual = CaptureNested(rect);
        string[] names = [nameof(Shape.Fill), nameof(Shape.Pen), nameof(Drawable.Transform), nameof(Drawable.FilterEffect)];
        for (int i = 0; i < names.Length; i++)
        {
            Assert.That(actual[i], Is.SameAs(expected[i]), $"{rect.Name}.{names[i]} was replaced.");
        }
    }

    private static string[] DescribeOperations(HistoryTransaction? transaction)
    {
        return transaction?.Operations.Select(DescribeOperation).ToArray() ?? [];
    }

    private static string DescribeOperation(ChangeOperation operation)
    {
        // The recorded path is prefixed with the owner chain (e.g. "Children.Objects.Width").
        return operation is IUpdatePropertyValueOperation update
            ? $"{update.Object.Id}:{update.PropertyPath.Split('.')[^1]} {update.OldValue} -> {update.NewValue}"
            : operation.GetType().Name;
    }

    private static RectShape AddRectElement(Scene scene, string name, TimeSpan start, int zIndex)
    {
        var element = new Element
        {
            Name = name,
            Start = start,
            Length = TimeSpan.FromSeconds(2),
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{name}.belm"))
        };
        var rect = new RectShape
        {
            Name = $"{name} rect",
            Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
            Pen = { CurrentValue = new Pen { Brush = { CurrentValue = new SolidColorBrush(Colors.Red) } } },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(10, 20) }
                }
            },
            FilterEffect =
            {
                CurrentValue = new FilterEffectGroup
                {
                    Children = { new Blur() }
                }
            }
        };
        element.AddObject(rect);
        scene.Children.Add(element);
        return rect;
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
