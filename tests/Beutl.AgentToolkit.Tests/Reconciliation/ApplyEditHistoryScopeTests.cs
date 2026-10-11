using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Models;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// A merge patch is resolved into a full desired document, so every element reaches the applier on
// every apply_edit. Members whose serialized value did not change must keep their instances;
// otherwise each edit records equal-copy replacements across the whole timeline.
public sealed class ApplyEditHistoryScopeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Apply_edit_keeps_unchanged_pen_dash_array(bool rename)
    {
        var pen = new Pen { Name = "original", DashArray = { CurrentValue = [2, 3] } };
        CoreList<float>? original = pen.DashArray.CurrentValue;
        int changed = 0;
        pen.DashArray.ValueChanged += (_, _) => changed++;
        using var session = new AgentToolkitTestSession(pen);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: rename ? new JsonObject { [nameof(CoreObject.Name)] = "updated" } : new JsonObject(),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value?.ChangeCount, Is.EqualTo(rename ? 1 : 0));
            Assert.That(DescribeOperations(session.History.PeekUndo()), Is.EqualTo(rename
                ? new[] { $"{pen.Id}:Name original -> updated" }
                : Array.Empty<string>()));
            Assert.That(pen.DashArray.CurrentValue, Is.SameAs(original));
            Assert.That(changed, Is.Zero);
        });

        if (rename)
        {
            Assert.That(session.History.Undo(), Is.True);
            Assert.That(pen.Name, Is.EqualTo("original"));
            Assert.That(session.History.Redo(), Is.True);
            Assert.That(pen.Name, Is.EqualTo("updated"));
            Assert.That(pen.DashArray.CurrentValue, Is.SameAs(original));
            Assert.That(changed, Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Apply_edit_keeps_unchanged_mesh_arrays(bool rename)
    {
        var mesh = new ModelMesh
        {
            Name = "original",
            Vertices = { CurrentValue = [new Vertex3D()] },
            Indices = { CurrentValue = [0] }
        };
        ImmutableArray<Vertex3D> vertices = mesh.Vertices.CurrentValue;
        ImmutableArray<uint> indices = mesh.Indices.CurrentValue;
        int changed = 0;
        mesh.Vertices.ValueChanged += (_, _) => changed++;
        mesh.Indices.ValueChanged += (_, _) => changed++;
        using var session = new AgentToolkitTestSession(mesh);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: rename ? new JsonObject { [nameof(CoreObject.Name)] = "updated" } : new JsonObject(),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value?.ChangeCount, Is.EqualTo(rename ? 1 : 0));
            Assert.That(DescribeOperations(session.History.PeekUndo()), Is.EqualTo(rename
                ? new[] { $"{mesh.Id}:Name original -> updated" }
                : Array.Empty<string>()));
            Assert.That(mesh.Vertices.CurrentValue == vertices, Is.True);
            Assert.That(mesh.Indices.CurrentValue == indices, Is.True);
            Assert.That(changed, Is.Zero);
        });

        if (rename)
        {
            Assert.That(session.History.Undo(), Is.True);
            Assert.That(mesh.Name, Is.EqualTo("original"));
            Assert.That(session.History.Redo(), Is.True);
            Assert.That(mesh.Name, Is.EqualTo("updated"));
            Assert.That(mesh.Vertices.CurrentValue == vertices, Is.True);
            Assert.That(mesh.Indices.CurrentValue == indices, Is.True);
            Assert.That(changed, Is.Zero);
        }
    }

    [Test]
    public void Apply_edit_still_replaces_changed_pen_dash_array()
    {
        var pen = new Pen { DashArray = { CurrentValue = [2, 3] } };
        CoreList<float>? original = pen.DashArray.CurrentValue;
        using var session = new AgentToolkitTestSession(pen);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject { [nameof(Pen.DashArray)] = new JsonArray(4, 5) },
            schemaVersion: SchemaVersion.Current);
        CoreList<float>? applied = pen.DashArray.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value?.ChangeCount, Is.EqualTo(1));
            Assert.That(session.History.PeekUndo()?.Operations, Has.Count.EqualTo(1));
            Assert.That(applied, Is.EqualTo(new[] { 4f, 5f }));
            Assert.That(applied, Is.Not.SameAs(original));
        });

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(pen.DashArray.CurrentValue, Is.SameAs(original));
        Assert.That(session.History.Redo(), Is.True);
        Assert.That(pen.DashArray.CurrentValue, Is.SameAs(applied));
    }

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

    // Custom members can be assigned unconditionally by Deserialize, just like clip transitions.
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

    [TestCase(false)]
    [TestCase(true)]
    public void Apply_edit_keeps_unchanged_members_that_deserialize_assigns_unconditionally(bool editAnotherElement)
    {
        Scene scene = CreateScene();
        CustomSerializedTransformElement element = AddCustomElement(scene);
        RectShape otherRect = AddRectElement(scene, "other", TimeSpan.FromSeconds(2), zIndex: 1);
        Element otherElement = scene.Children[1];
        Transform? transform = element.CustomTransform;
        ClipTransition? enter = element.EnterTransition;
        ClipTransition? exit = element.ExitTransition;
        int edited = 0;
        element.Edited += (_, _) => edited++;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: editAnotherElement
                ? CreateObjectPatch(otherElement, otherRect, new JsonObject { [nameof(RectShape.Width)] = 200 })
                : new JsonObject(),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value?.ChangeCount, Is.EqualTo(editAnotherElement ? 1 : 0));
            Assert.That(result.Value?.CreatedIdCount, Is.Zero);
            Assert.That(DescribeOperations(session.History.PeekUndo()), Is.EqualTo(editAnotherElement
                ? new[] { $"{otherRect.Id}:Width 100 -> 200" }
                : Array.Empty<string>()));
            Assert.That(element.CustomTransform, Is.SameAs(transform));
            Assert.That(element.EnterTransition, Is.SameAs(enter));
            Assert.That(element.ExitTransition, Is.SameAs(exit));
            Assert.That(edited, Is.Zero);
        });

        if (editAnotherElement)
        {
            Assert.That(session.History.Undo(), Is.True);
            Assert.That(otherRect.Width.CurrentValue, Is.EqualTo(100f));
            Assert.That(session.History.Redo(), Is.True);
            Assert.That(otherRect.Width.CurrentValue, Is.EqualTo(200f));
            Assert.Multiple(() =>
            {
                Assert.That(element.CustomTransform, Is.SameAs(transform));
                Assert.That(element.EnterTransition, Is.SameAs(enter));
                Assert.That(element.ExitTransition, Is.SameAs(exit));
                Assert.That(edited, Is.Zero);
            });
        }
    }

    [TestCase(nameof(Element.EnterTransition), false)]
    [TestCase(nameof(Element.ExitTransition), false)]
    [TestCase(nameof(CustomSerializedTransformElement.CustomTransform), false)]
    [TestCase(nameof(Element.EnterTransition), true)]
    [TestCase(nameof(Element.ExitTransition), true)]
    [TestCase(nameof(CustomSerializedTransformElement.CustomTransform), true)]
    public void Apply_edit_changes_or_removes_only_the_requested_custom_member(string propertyName, bool remove)
    {
        Scene scene = CreateScene();
        CustomSerializedTransformElement element = AddCustomElement(scene);
        CoreProperty[] properties =
        [
            Element.EnterTransitionProperty,
            Element.ExitTransitionProperty,
            CustomSerializedTransformElement.CustomTransformProperty
        ];
        var original = properties.ToDictionary(property => property, element.GetValue);
        CoreProperty changedProperty = properties.Single(property => property.Name == propertyName);
        JsonObject? value = null;
        if (!remove)
        {
            value = CoreSerializer.SerializeToJsonObject((ICoreSerializable)original[changedProperty]!);
            if (propertyName == nameof(CustomSerializedTransformElement.CustomTransform))
            {
                value[nameof(TranslateTransform.X)] = 50;
            }
            else
            {
                value[nameof(ClipTransition.Duration)] = CoreSerializer.SerializeToJsonNode(TimeSpan.FromSeconds(1));
            }
        }

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [propertyName] = value
                })
            },
            schemaVersion: SchemaVersion.Current);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        // Attaching a changed transition also aligns its transient TimeRange with the element.
        int expectedOperations = !remove && propertyName != nameof(CustomSerializedTransformElement.CustomTransform) ? 2 : 1;
        Assert.That(session.History.PeekUndo()?.Operations, Has.Count.EqualTo(expectedOperations));
        var operations = session.History.PeekUndo()!.Operations.OfType<IUpdatePropertyValueOperation>().ToArray();
        IUpdatePropertyValueOperation operation = operations.Single(update => ReferenceEquals(update.Object, element));
        object? applied = element.GetValue(changedProperty);
        Assert.Multiple(() =>
        {
            Assert.That(operation.Object, Is.SameAs(element));
            Assert.That(operation.PropertyPath.Split('.')[^1], Is.EqualTo(propertyName));
            Assert.That(operation.OldValue, Is.SameAs(original[changedProperty]));
            Assert.That(applied, Is.Not.SameAs(original[changedProperty]));
            Assert.That(remove ? applied is null : JsonNode.DeepEquals(
                CoreSerializer.SerializeToJsonObject((ICoreSerializable)applied!), value), Is.True);
            if (expectedOperations == 2)
            {
                IUpdatePropertyValueOperation timeRange = operations.Single(update => !ReferenceEquals(update.Object, element));
                Assert.That(timeRange.Object, Is.SameAs(applied));
                Assert.That(timeRange.PropertyPath.Split('.')[^1], Is.EqualTo(nameof(ClipTransition.TimeRange)));
            }
            foreach (CoreProperty property in properties.Where(property => property != changedProperty))
            {
                Assert.That(element.GetValue(property), Is.SameAs(original[property]));
            }
        });

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(element.GetValue(changedProperty), Is.SameAs(original[changedProperty]));
        Assert.That(session.History.Redo(), Is.True);
        Assert.That(element.GetValue(changedProperty), Is.SameAs(applied));
    }

    private static CustomSerializedTransformElement AddCustomElement(Scene scene)
    {
        var element = new CustomSerializedTransformElement
        {
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "custom.belm")),
            CustomTransform = new TranslateTransform(10, 20),
            EnterTransition = new CrossDissolveTransition(),
            ExitTransition = new WipeTransition()
        };
        element.AddObject(new RectShape());
        scene.Children.Add(element);
        return element;
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
        Dimensions? dimensions = element.Dimensions;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(element, rect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(rect.Width.CurrentValue, Is.EqualTo(200f));
            Assert.That(element.Dimensions, Is.SameAs(dimensions));
            Assert.That(DescribeOperations(session.History.PeekUndo()),
                Is.EqualTo(new[] { $"{rect.Id}:Width 100 -> 200" }));
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
