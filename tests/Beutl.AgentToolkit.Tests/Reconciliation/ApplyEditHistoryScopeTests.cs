using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
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
using Beutl.Services;

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
        var pen = new Pen { Name = "original", DashArray = { Value = [2, 3] } };
        CoreList<float>? original = pen.DashArray.Value;
        int changed = 0;
        pen.DashArray.ValueChanged += (_, _) => changed++;
        using var session = new AgentToolkitTestSession(pen);
        EditTools tools = session.CreateEditTools();

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
            Assert.That(pen.DashArray.Value, Is.SameAs(original));
            Assert.That(changed, Is.Zero);
        });

        if (rename)
        {
            Assert.That(session.History.Undo(), Is.True);
            Assert.That(pen.Name, Is.EqualTo("original"));
            Assert.That(session.History.Redo(), Is.True);
            Assert.That(pen.Name, Is.EqualTo("updated"));
            Assert.That(pen.DashArray.Value, Is.SameAs(original));
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
            Vertices = { Value = [new Vertex3D()] },
            Indices = { Value = [0] }
        };
        ImmutableArray<Vertex3D> vertices = mesh.Vertices.Value;
        ImmutableArray<uint> indices = mesh.Indices.Value;
        int changed = 0;
        mesh.Vertices.ValueChanged += (_, _) => changed++;
        mesh.Indices.ValueChanged += (_, _) => changed++;
        using var session = new AgentToolkitTestSession(mesh);
        EditTools tools = session.CreateEditTools();

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
            Assert.That(mesh.Vertices.Value == vertices, Is.True);
            Assert.That(mesh.Indices.Value == indices, Is.True);
            Assert.That(changed, Is.Zero);
        });

        if (rename)
        {
            Assert.That(session.History.Undo(), Is.True);
            Assert.That(mesh.Name, Is.EqualTo("original"));
            Assert.That(session.History.Redo(), Is.True);
            Assert.That(mesh.Name, Is.EqualTo("updated"));
            Assert.That(mesh.Vertices.Value == vertices, Is.True);
            Assert.That(mesh.Indices.Value == indices, Is.True);
            Assert.That(changed, Is.Zero);
        }
    }

    [Test]
    public void Apply_edit_still_replaces_changed_pen_dash_array()
    {
        var pen = new Pen { DashArray = { Value = [2, 3] } };
        CoreList<float>? original = pen.DashArray.Value;
        using var session = new AgentToolkitTestSession(pen);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject { [nameof(Pen.DashArray)] = new JsonArray(4, 5) },
            schemaVersion: SchemaVersion.Current);
        CoreList<float>? applied = pen.DashArray.Value;

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value?.ChangeCount, Is.EqualTo(1));
            Assert.That(session.History.PeekUndo()?.Operations, Has.Count.EqualTo(1));
            Assert.That(applied, Is.EqualTo(new[] { 4f, 5f }));
            Assert.That(applied, Is.Not.SameAs(original));
        });

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(pen.DashArray.Value, Is.SameAs(original));
        Assert.That(session.History.Redo(), Is.True);
        Assert.That(pen.DashArray.Value, Is.SameAs(applied));
    }

    [Test]
    public void Apply_edit_records_only_the_patched_property_and_undo_redo_keep_unpatched_instances()
    {
        Scene scene = TestWorkspace.CreateScene();
        RectShape firstRect = AddRectElement(scene, "first", MediaTime.Zero, zIndex: 0);
        RectShape secondRect = AddRectElement(scene, "second", MediaTime.FromSeconds(2), zIndex: 1);
        Element second = scene.Children[1];
        object?[] firstNested = CaptureNested(firstRect);
        object?[] secondNested = CaptureNested(secondRect);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(second, secondRect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(DescribeOperations(session.History.PeekUndo()), Is.EqualTo(new[] { $"{secondRect.Id}:Width 100 -> 200" }));

        bool undone = session.History.Undo();
        float undoneWidth = secondRect.Width.Value;
        bool redone = session.History.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(undone, Is.True);
            Assert.That(undoneWidth, Is.EqualTo(100f));
            Assert.That(redone, Is.True);
            Assert.That(secondRect.Width.Value, Is.EqualTo(200f));
            AssertSameNested(firstRect, firstNested);
            AssertSameNested(secondRect, secondNested);
        });
    }

    [Test]
    public void Apply_edit_replaces_only_the_nested_value_whose_json_changed()
    {
        Scene scene = TestWorkspace.CreateScene();
        RectShape firstRect = AddRectElement(scene, "first", MediaTime.Zero, zIndex: 0);
        RectShape secondRect = AddRectElement(scene, "second", MediaTime.FromSeconds(2), zIndex: 1);
        Element second = scene.Children[1];
        object?[] firstNested = CaptureNested(firstRect);
        Transform? secondTransform = secondRect.Transform.Value;
        FilterEffect? secondFilterEffect = secondRect.FilterEffect.Value;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

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
            Assert.That(((SolidColorBrush)secondRect.Fill.Value!).Color.Value, Is.EqualTo(Colors.Blue));
            Assert.That(operations, Has.Length.EqualTo(1));
            Assert.That(operations[0], Does.StartWith($"{secondRect.Id}:Fill "));
            Assert.That(secondRect.Transform.Value, Is.SameAs(secondTransform));
            Assert.That(secondRect.FilterEffect.Value, Is.SameAs(secondFilterEffect));
            AssertSameNested(firstRect, firstNested);
        });
    }

    // Custom members can be assigned unconditionally by Deserialize, just like clip transitions.
    private sealed class CustomSerializedTransformElement : Element
    {
        public CustomSerializedTransformElement()
        {
            ScanProperties<CustomSerializedTransformElement>();
        }

        [NotAutoSerialized]
        public IProperty<Transform?> CustomTransformProperty { get; } = new ValueOnlyProperty<Transform?>(null);

        public Transform? CustomTransform
        {
            get => CustomTransformProperty.Value;
            set => CustomTransformProperty.Value = value;
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
        Scene scene = TestWorkspace.CreateScene();
        CustomSerializedTransformElement element = AddCustomElement(scene);
        RectShape otherRect = AddRectElement(scene, "other", MediaTime.FromSeconds(2), zIndex: 1);
        Element otherElement = scene.Children[1];
        Transform? transform = element.CustomTransform;
        ClipTransition? enter = element.EnterTransition;
        ClipTransition? exit = element.ExitTransition;
        int edited = 0;
        element.Edited += (_, _) => edited++;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

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
            Assert.That(otherRect.Width.Value, Is.EqualTo(100f));
            Assert.That(session.History.Redo(), Is.True);
            Assert.That(otherRect.Width.Value, Is.EqualTo(200f));
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
        Scene scene = TestWorkspace.CreateScene();
        CustomSerializedTransformElement element = AddCustomElement(scene);
        var properties = new Dictionary<string, Func<object?>>
        {
            [nameof(Element.EnterTransition)] = () => element.EnterTransition,
            [nameof(Element.ExitTransition)] = () => element.ExitTransition,
            [nameof(CustomSerializedTransformElement.CustomTransform)] = () => element.CustomTransform,
        };
        var original = properties.ToDictionary(pair => pair.Key, pair => pair.Value());
        Func<object?> changedProperty = properties[propertyName];
        JsonObject? value = null;
        if (!remove)
        {
            value = CoreSerializer.SerializeToJsonObject((ICoreSerializable)original[propertyName]!);
            if (propertyName == nameof(CustomSerializedTransformElement.CustomTransform))
            {
                value[nameof(TranslateTransform.X)] = 50;
            }
            else
            {
                value[nameof(ClipTransition.Duration)] = CoreSerializer.SerializeToJsonNode(MediaTime.FromSeconds(1));
            }
        }

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
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
        // A transition takes its TimeRange from the element when attached, so only the element's member is recorded.
        Assert.That(session.History.PeekUndo()?.Operations, Has.Count.EqualTo(1));
        var operations = session.History.PeekUndo()!.Operations.OfType<IUpdatePropertyValueOperation>().ToArray();
        IUpdatePropertyValueOperation operation = operations.Single(update => ReferenceEquals(update.Object, element));
        object? applied = changedProperty();
        Assert.Multiple(() =>
        {
            Assert.That(operation.Object, Is.SameAs(element));
            Assert.That(operation.PropertyPath.Split('.')[^1], Is.EqualTo(propertyName));
            Assert.That(operation.OldValue, Is.SameAs(original[propertyName]));
            Assert.That(applied, Is.Not.SameAs(original[propertyName]));
            Assert.That(remove ? applied is null : JsonNode.DeepEquals(
                CoreSerializer.SerializeToJsonObject((ICoreSerializable)applied!), value), Is.True);
            if (applied is ClipTransition transition)
            {
                Assert.That(transition.TimeRange, Is.EqualTo(element.Range));
            }
            foreach ((string name, Func<object?> property) in properties.Where(pair => pair.Key != propertyName))
            {
                Assert.That(property(), Is.SameAs(original[name]));
            }
        });

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(changedProperty(), Is.SameAs(original[propertyName]));
        Assert.That(session.History.Redo(), Is.True);
        Assert.That(changedProperty(), Is.SameAs(applied));
    }

    private static CustomSerializedTransformElement AddCustomElement(Scene scene)
    {
        var element = new CustomSerializedTransformElement
        {
            Length = MediaTime.FromSeconds(2),
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

    // A registered property an edit does not name keeps its value.
    private sealed class ConvertedPropertyElement : Element
    {
        public ConvertedPropertyElement()
        {
            ScanProperties<ConvertedPropertyElement>();
        }

        public IProperty<Dimensions?> DimensionsProperty { get; } = new ValueOnlyProperty<Dimensions?>(null);

        public Dimensions? Dimensions
        {
            get => DimensionsProperty.Value;
            set => DimensionsProperty.Value = value;
        }
    }

    [Test]
    public void Apply_edit_keeps_unchanged_converter_backed_registered_properties()
    {
        Scene scene = TestWorkspace.CreateScene();
        var element = new ConvertedPropertyElement
        {
            Length = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "converted.belm")),
            Dimensions = new Dimensions(3, 4)
        };
        var rect = new RectShape();
        element.AddObject(rect);
        scene.Children.Add(element);
        Dimensions? dimensions = element.Dimensions;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateObjectPatch(element, rect, new JsonObject { [nameof(RectShape.Width)] = 200 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(rect.Width.Value, Is.EqualTo(200f));
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
        Scene scene = TestWorkspace.CreateScene();
        RectShape rect = AddRectElement(scene, "edited", MediaTime.Zero, zIndex: 0);
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
            Start = MediaTime.FromSeconds(2),
            Length = MediaTime.FromSeconds(2),
            ZIndex = 1,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "holder.belm"))
        };
        holder.AddObject(fallback);
        scene.Children.Add(holder);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

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

    // The cases above over a scene that holds every registered type; it would have caught #2836.
    [Test]
    public void Empty_patch_over_every_registered_type_records_nothing_and_keeps_every_instance()
    {
        Scene scene = CreateSceneWithEveryRegisteredType();
        CoreObject[] before = [.. scene.EnumerateAllChildren<CoreObject>()];
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(
            patch: new JsonObject(),
            schemaVersion: SchemaVersion.Current);

        CoreObject[] after = [.. scene.EnumerateAllChildren<CoreObject>()];
        Assert.Multiple(() =>
        {
            Assert.That(
                before.Select(item => item.GetType()),
                Is.SupersetOf(RegisteredTypes().Where(type => type.IsAssignableTo(typeof(CoreObject)))));
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value?.ChangeCount, Is.Zero);
            Assert.That(DescribeOperations(session.History.PeekUndo()), Is.Empty);
            Assert.That(
                before.Except<CoreObject>(after, ReferenceEqualityComparer.Instance).Select(item => item.GetType().Name),
                Is.Empty,
                "Replaced");
        });
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
            rect.Fill.Value,
            rect.Pen.Value,
            rect.Transform.Value,
            rect.FilterEffect.Value
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

    // The library's types and TypeRegistration's schema-only ones. Other tests add library types of their own.
    private static Type[] RegisteredTypes() =>
    [
        .. TypeRegistration.Types.Select(entry => entry.Type)
            .Concat(typeof(KnownLibraryItemFormats).GetFields()
                .SelectMany(field => LibraryService.Current.GetTypesFromFormat((string)field.GetValue(null)!)))
            .Where(type => type.Assembly != typeof(ApplyEditHistoryScopeTests).Assembly)
            .Distinct()
    ];

    private static Scene CreateSceneWithEveryRegisteredType()
    {
        // Drawables and sounds first, so that each Wrap chain ends at one.
        Type[] types =
        [
            .. RegisteredTypes().OrderBy(type => type.IsAssignableTo(typeof(Drawable)) || type.IsAssignableTo(typeof(Sound)) ? 0 : 1)
        ];
        object[] instances = [.. types.Select(type => Activator.CreateInstance(type)!)];
        Scene scene = TestWorkspace.CreateScene();
        foreach (EngineObject obj in instances.OfType<EngineObject>().Where(obj => obj is not ClipTransition))
        {
            AddObjectElement(scene, Wrap(obj, types));
        }

        ClipTransition[] transitions = [.. instances.OfType<ClipTransition>()];
        for (int i = 0; i < transitions.Length; i += 2)
        {
            Element element = AddObjectElement(scene, new RectShape());
            element.EnterTransition = transitions[i];
            element.ExitTransition = transitions.ElementAtOrDefault(i + 1);
        }

        var easings = new KeyFrameAnimation<float>();
        foreach (Easing easing in instances.OfType<Easing>())
        {
            easings.KeyFrames.Add(
                new KeyFrame<float> { KeyTime = MediaTime.FromSeconds((50 * easings.KeyFrames.Count) / 1000d), Easing = easing },
                out _);
        }

        var animated = new RectShape();
        animated.Opacity.SetKeyFrameAnimation(easings);
        AddObjectElement(scene, animated);
        return scene;
    }

    // Puts obj into a new instance of the first type with a property that holds it, until a drawable or sound holds it.
    private static EngineObject Wrap(EngineObject obj, Type[] types)
    {
        if (obj is Drawable or Sound)
        {
            return obj;
        }

        foreach (Type type in types)
        {
            if (type != obj.GetType()
                && Activator.CreateInstance(type) is EngineObject host
                && host.Properties.FirstOrDefault(property => Holds(property, obj)) is { } slot)
            {
                if (slot is IListProperty list)
                {
                    list.Add(obj);
                }
                else
                {
                    slot.Value = obj;
                }

                return Wrap(host, types);
            }
        }

        // Nothing holds it, as for PortalObject, so it is an element object itself.
        return obj;
    }

    private static bool Holds(IProperty property, EngineObject obj)
        => (property is IListProperty list ? list.ElementType : property.ValueType).IsInstanceOfType(obj);

    private static Element AddObjectElement(Scene scene, EngineObject obj)
    {
        var element = new Element
        {
            Length = MediaTime.FromSeconds(2),
            ZIndex = scene.Children.Count,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{scene.Children.Count}.belm"))
        };
        element.AddObject(obj);
        scene.Children.Add(element);
        return element;
    }

    private static RectShape AddRectElement(Scene scene, string name, MediaTime start, int zIndex)
    {
        var element = new Element
        {
            Name = name,
            Start = start,
            Length = MediaTime.FromSeconds(2),
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{name}.belm"))
        };
        var rect = new RectShape
        {
            Name = $"{name} rect",
            Fill = { Value = new SolidColorBrush(Colors.White) },
            Pen = { Value = new Pen { Brush = { Value = new SolidColorBrush(Colors.Red) } } },
            Transform =
            {
                Value = new TransformGroup
                {
                    Children = { new TranslateTransform(10, 20) }
                }
            },
            FilterEffect =
            {
                Value = new FilterEffectGroup
                {
                    Children = { new Blur() }
                }
            }
        };
        element.AddObject(rect);
        scene.Children.Add(element);
        return rect;
    }
}
