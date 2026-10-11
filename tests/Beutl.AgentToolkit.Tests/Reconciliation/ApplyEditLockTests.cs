using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Clip and layer locks are an editor rule (Scene.IsElementLocked). Agent edits follow it: a locked
// element cannot change, nothing lands on a locked layer, and only the user unlocks.
public sealed class ApplyEditLockTests
{
    private const int LockedLayer = 2;

    // MCP binds tool arguments with the Web serializer defaults, so a JsonObject argument, and every
    // clone of it, finds members whatever their case.
    private static readonly JsonNodeOptions s_mcpArguments = new() { PropertyNameCaseInsensitive = true };

    [TestCase("start")]
    [TestCase("delete")]
    [TestCase("insert")]
    public void Apply_edit_rejects_edits_under_a_locked_element_or_layer(string edit)
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Locked title", zIndex: 0);
        locked.IsLocked = true;
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        JsonObject patch = edit switch
        {
            "start" => ElementPatch(locked, new JsonObject { [nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c") }),
            "delete" => ElementPatch(locked, new JsonObject { ["$delete"] = true }),
            _ => InsertPatch("New title", LockedLayer),
        };

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, edit == "insert"
                ? Does.Contain($"Layer {LockedLayer} is locked").And.Contain("'New title'")
                : Does.Contain($"'Locked title' ({locked.Id}) is locked"));
            Assert.That(result.Error.Hint, Does.Contain("ask the user to unlock"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
        });
    }

    [Test]
    public void Apply_edit_rejects_edits_to_an_element_on_a_locked_layer()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", LockedLayer);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: ElementPatch(element, new JsonObject { [nameof(Element.ZIndex)] = 0 }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"is on locked layer {LockedLayer}"));
            Assert.That(result.Error.Target, Is.EqualTo($"$/Elements[Id={element.Id}]"));
            Assert.That(element.ZIndex, Is.EqualTo(LockedLayer));
        });
    }

    [Test]
    public void Apply_edit_rejects_moving_an_element_onto_a_locked_layer()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: ElementPatch(element, new JsonObject { [nameof(Element.ZIndex)] = LockedLayer }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"Layer {LockedLayer} is locked"));
            Assert.That(element.ZIndex, Is.Zero);
        });
    }

    [Test]
    public void Apply_edit_lets_an_agent_lock_elements_and_layers()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        Element onLockedLayer = AddElement(scene, "Caption", LockedLayer);
        var layer = new TimelineLayer { ZIndex = 1 };
        scene.Layers.Add(layer);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(
                    new JsonObject { ["Id"] = element.Id.ToString(), [nameof(Element.IsLocked)] = true },
                    new JsonObject { ["Id"] = onLockedLayer.Id.ToString(), [nameof(Element.IsLocked)] = true }),
                ["Layers"] = new JsonArray(
                    new JsonObject { ["Id"] = layer.Id.ToString(), [nameof(TimelineLayer.IsLocked)] = true }),
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(element.IsLocked, Is.True);
            Assert.That(onLockedLayer.IsLocked, Is.True);
            Assert.That(scene.IsLayerLocked(1), Is.True);
        });
    }

    [TestCase("element")]
    [TestCase("layer")]
    [TestCase("remove-layer")]
    [TestCase("move-layer")]
    public void Apply_edit_refuses_to_unlock_an_element_or_a_layer(string target)
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        element.IsLocked = true;
        var layer = new TimelineLayer { ZIndex = LockedLayer, IsLocked = true };
        scene.Layers.Add(layer);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        JsonObject patch = target switch
        {
            "element" => ElementPatch(element, new JsonObject { [nameof(Element.IsLocked)] = false }),
            "layer" => LayerPatch(layer, new JsonObject { [nameof(TimelineLayer.IsLocked)] = false }),
            "remove-layer" => LayerPatch(layer, new JsonObject { ["$delete"] = true }),
            _ => LayerPatch(layer, new JsonObject { [nameof(TimelineLayer.ZIndex)] = 3 }),
        };

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("cannot").And.Contain("unlock"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(scene.IsElementLocked(element), Is.True);
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    [Test]
    public void Apply_edit_keeps_editing_unlocked_content_beside_locks()
    {
        Scene scene = CreateScene();
        Element unlocked = AddElement(scene, "Intro", zIndex: 0);
        Element locked = AddElement(scene, "Outro", zIndex: 1);
        locked.IsLocked = true;
        var layer = new TimelineLayer { ZIndex = LockedLayer, IsLocked = true };
        scene.Layers.Add(layer);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        // Deleting the first element shifts the locked one in the Elements array, and a locked layer
        // keeps its name editable, as in the timeline.
        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject { ["Id"] = unlocked.Id.ToString(), ["$delete"] = true }),
                ["Layers"] = new JsonArray(new JsonObject { ["Id"] = layer.Id.ToString(), ["Name"] = "Music" }),
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(scene.Children, Is.EqualTo(new[] { locked }));
            Assert.That(scene.Layers.Single().Name, Is.EqualTo("Music"));
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    [Test]
    public void Apply_edit_rejects_a_full_document_that_drops_a_locked_element()
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        JsonObject desired = session.Documents.Read(scene);
        desired.Remove("Elements");

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"({locked.Id}) is locked"));
            Assert.That(scene.Children, Is.EqualTo(new[] { locked }));
        });
    }

    // The plan reports the whole Layers array as removed, so the edit is refused like one that drops
    // a locked element.
    // The diff reports the case-insensitive "layers" member as a new whole array at $/layers, while
    // the applier reads it as Layers and drops the locked layer.
    [Test]
    public void Apply_edit_rejects_a_full_document_that_replaces_the_layers_while_one_is_locked()
    {
        Scene scene = CreateScene();
        AddElement(scene, "Title", zIndex: 0);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();
        JsonObject desired = AsMcpArgument(session.Documents.Read(scene));
        desired.Remove("Layers");
        desired["layers"] = new JsonArray();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"Layer {LockedLayer} is locked, so an agent cannot unlock"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    // The applier writes only the members a desired document has, so omitting Layers leaves them, and
    // their locks, as they are.
    [Test]
    public void Apply_edit_keeps_a_locked_layer_when_a_full_document_omits_the_layers()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        JsonObject desired = session.Documents.Read(scene);
        desired.Remove("Layers");
        desired["Elements"]![0]![nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c");

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    // The diff names paths after the desired document's own keys, so "elements" yields $/elements, while
    // the case-insensitive document still applies as Elements.
    [Test]
    public void Apply_edit_rejects_edits_to_a_locked_element_under_a_differently_cased_elements_member()
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        var rect = new RectShape { Name = "Box" };
        locked.AddObject(rect);
        locked.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();
        JsonObject desired = AsMcpArgument(session.Documents.Read(scene));
        JsonNode elements = desired["Elements"]!.DeepClone();
        desired.Remove("Elements");
        desired["elements"] = elements;
        elements[0]![nameof(CoreObject.Name)] = "Renamed";
        elements[0]![nameof(Element.Objects)] = new JsonArray();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"({locked.Id}) is locked"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(locked.Name, Is.EqualTo("Title"));
            Assert.That(locked.Objects, Is.EqualTo(new[] { rect }));
        });
    }

    // Ids are matched as Guids, so another spelling still addresses the locked element.
    [TestCase("upper")]
    [TestCase("braced")]
    public void Apply_edit_rejects_an_edit_that_spells_the_id_of_a_locked_element_differently(string spelling)
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();
        string id = spelling == "upper" ? locked.Id.ToString().ToUpperInvariant() : locked.Id.ToString("B");

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject { ["Id"] = id, [nameof(CoreObject.Name)] = "Renamed" })
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(locked.Name, Is.EqualTo("Title"));
        });
    }

    // An equal copy under a new Id is not the locked element: the user's lock would be gone.
    [TestCase("Elements")]
    [TestCase("elements")]
    public void Apply_edit_rejects_replacing_a_locked_element_with_an_equal_copy(string member)
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.AddObject(new RectShape { Name = "Box" });
        locked.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();
        JsonObject desired = AsMcpArgument(session.Documents.Read(scene));
        JsonObject copy = desired["Elements"]![0]!.DeepClone().AsObject();
        RemoveIds(copy);
        desired.Remove("Elements");
        desired[member] = new JsonArray(copy);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"({locked.Id}) is locked"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(scene.Children, Is.EqualTo(new[] { locked }));
        });
    }

    // The locked element's serialized form must come back the same from the sandbox; transitions,
    // keyframes, brushes, effects and an object of a missing plugin type must not read as edits.
    [Test]
    public void Apply_edit_accepts_edits_beside_a_locked_element_with_rich_content()
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.EnterTransition = new CrossDissolveTransition();
        locked.ExitTransition = new WipeTransition();
        var text = new TextBlock { Name = "Caption", Text = { CurrentValue = "Hello" } };
        var opacity = new KeyFrameAnimation<float>();
        opacity.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0, Easing = new LinearEasing() }, out _);
        opacity.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 100, Easing = new SineEaseOut() }, out _);
        text.Opacity.Animation = opacity;
        locked.AddObject(text);
        var group = new TransformGroup();
        group.Children.Add(new RotationTransform { Rotation = { CurrentValue = 15 } });
        group.Children.Add((Transform)CoreSerializer.DeserializeFromJsonObject(
            new JsonObject
            {
                ["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform",
                ["Id"] = Guid.NewGuid().ToString(),
            },
            typeof(Transform)));
        locked.AddObject(new RectShape
        {
            Name = "Box",
            Fill = { CurrentValue = new SolidColorBrush(Colors.Orange) },
            Transform = { CurrentValue = group },
            FilterEffect = { CurrentValue = new Blur { Sigma = { CurrentValue = new Size(4, 4) } } },
        });
        locked.IsLocked = true;
        Element other = AddElement(scene, "Other", zIndex: 1);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: ElementPatch(other, new JsonObject { [nameof(CoreObject.Name)] = "Renamed" }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(other.Name, Is.EqualTo("Renamed"));
        });
    }

    // The diff reports "islocked" or "zindex" under that spelling, while the applier reads the
    // case-insensitive document as IsLocked or ZIndex.
    [TestCase("desired", "islocked")]
    [TestCase("desired", "zindex")]
    [TestCase("replace", "islocked")]
    public void Apply_edit_refuses_to_unlock_a_layer_through_differently_cased_members(string shape, string member)
    {
        Scene scene = CreateScene();
        AddElement(scene, "Title", zIndex: 0);
        var layer = new TimelineLayer { ZIndex = LockedLayer, IsLocked = true };
        scene.Layers.Add(layer);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        ToolResult<ApplyEditResponse> result;
        if (shape == "desired")
        {
            JsonObject desired = AsMcpArgument(session.Documents.Read(scene));
            JsonObject layerJson = desired["Layers"]![0]!.AsObject();
            // The object ignores case, so this removes IsLocked or ZIndex before adding the new spelling.
            layerJson.Remove(member);
            layerJson[member] = member == "islocked" ? false : LockedLayer + 1;
            result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);
        }
        else
        {
            // $replace rebuilds the array from the patch's own objects, so the replacement keeps its
            // spelling and the case-insensitive lookup.
            JsonObject patch = AsMcpArgument(new JsonObject
            {
                ["Layers"] = new JsonArray(
                    new JsonObject { ["$replace"] = true },
                    new JsonObject { ["Id"] = layer.Id.ToString(), ["zindex"] = LockedLayer, [member] = false })
            });
            result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"Layer {LockedLayer} is locked, so an agent cannot unlock"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
            Assert.That(scene.IsLayerLocked(LockedLayer), Is.True);
        });
    }

    [Test]
    public void Apply_edit_rejects_moving_an_element_onto_a_locked_layer_through_a_differently_cased_member()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        scene.Layers.Add(new TimelineLayer { ZIndex = LockedLayer, IsLocked = true });
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        JsonObject desired = AsMcpArgument(session.Documents.Read(scene));
        JsonObject elementJson = desired["Elements"]![0]!.AsObject();
        elementJson.Remove("ZIndex");
        elementJson["zindex"] = LockedLayer;

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"Layer {LockedLayer} is locked"));
            Assert.That(element.ZIndex, Is.Zero);
        });
    }

    // The diff skips $type: an Id-less replacement gets a fresh Id, and LumaColor and PartsSplitEffect
    // have the same members, so the plan has no change while the applier swaps the effect.
    [Test]
    public void Apply_edit_rejects_swapping_the_type_of_an_object_inside_a_locked_element()
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        var rect = new RectShape { Name = "Box" };
        rect.FilterEffect.CurrentValue = new LumaColor();
        element.AddObject(rect);
        element.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        JsonObject desired = session.Documents.Read(scene);
        JsonObject effect = desired["Elements"]![0]!["Objects"]![0]!["FilterEffect"]!.AsObject();
        effect.Remove("Id");
        effect["$type"] = IdentityHelper.WriteDiscriminator(typeof(PartsSplitEffect));

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"({element.Id}) is locked"));
            Assert.That(rect.FilterEffect.CurrentValue, Is.InstanceOf<LumaColor>());
        });
    }

    // As in the editor, a locked element keeps its group: it is not added to one, not taken out of
    // one, not added to a second group beside the one it keeps, and not left behind when a full
    // document drops Groups.
    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("second")]
    [TestCase("drop")]
    public void Apply_edit_refuses_to_regroup_a_locked_element(string edit)
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        Element first = AddElement(scene, "First", zIndex: 1);
        Element second = AddElement(scene, "Second", zIndex: 2);
        if (edit != "add")
        {
            scene.Groups.Add(ImmutableHashSet.Create(locked.Id, first.Id, second.Id));
        }

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        ToolResult<ApplyEditResponse> result;
        if (edit == "drop")
        {
            JsonObject desired = session.Documents.Read(scene);
            desired.Remove("Groups");
            result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);
        }
        else
        {
            JsonArray groups = edit switch
            {
                "add" => new JsonArray($"{locked.Id}:{first.Id}"),
                "remove" => new JsonArray($"{first.Id}:{second.Id}"),
                // Scene.Groups accepts an Id in two groups; the unchanged group stays the first match.
                _ => new JsonArray($"{locked.Id}:{first.Id}:{second.Id}", $"{locked.Id}:{first.Id}"),
            };
            result = tools.ApplyEdit(
                patch: new JsonObject { ["Groups"] = groups },
                schemaVersion: SchemaVersion.Current);
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain($"({locked.Id}) is locked, so an agent cannot add it to a group"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
        });
    }

    // Only a corrupted project puts an element in two groups, and then the groups before and after the
    // edit cannot be matched: one group could disappear while the other still holds the element.
    [Test]
    public void Apply_edit_refuses_edits_while_a_locked_element_is_in_overlapping_groups()
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        Element first = AddElement(scene, "First", zIndex: 1);
        Element second = AddElement(scene, "Second", zIndex: 2);
        scene.Groups.Add(ImmutableHashSet.Create(locked.Id, first.Id));
        scene.Groups.Add(ImmutableHashSet.Create(locked.Id, first.Id, second.Id));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: new JsonObject { ["Groups"] = new JsonArray($"{locked.Id}:{first.Id}") },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain("is in more than one group"));
            Assert.That(result.Error.Hint, Does.Contain("overlapping groups"));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
        });
    }

    // Other members may leave a locked element's group, and deleting the last of them dissolves it,
    // as in the editor.
    [TestCase("leave")]
    [TestCase("delete")]
    public void Apply_edit_lets_other_members_leave_a_locked_elements_group(string edit)
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        Element first = AddElement(scene, "First", zIndex: 1);
        Element second = AddElement(scene, "Second", zIndex: 2);
        scene.Groups.Add(edit == "leave"
            ? ImmutableHashSet.Create(locked.Id, first.Id, second.Id)
            : ImmutableHashSet.Create(locked.Id, first.Id));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: edit == "leave"
                ? new JsonObject { ["Groups"] = new JsonArray($"{locked.Id}:{second.Id}") }
                : ElementPatch(first, new JsonObject { ["$delete"] = true }),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            if (edit == "leave")
            {
                Assert.That(scene.Groups.Single(), Is.EquivalentTo(new[] { locked.Id, second.Id }));
            }
            else
            {
                Assert.That(scene.Groups, Is.Empty);
                Assert.That(scene.Children, Does.Not.Contain(first));
            }
        });
    }

    // A corrupted project can repeat an element Id, and the applier edits whichever copy comes first.
    // This edit renames the locked second copy and locks the first one so that the copies look alike.
    [Test]
    public void Apply_edit_refuses_edits_while_a_locked_element_shares_its_id()
    {
        Scene scene = CreateScene();
        Element first = AddElement(scene, "A", zIndex: 0);
        Element locked = AddElement(scene, "B", zIndex: 1);
        locked.Id = first.Id;
        locked.IsLocked = true;
        AddElement(scene, "Y", zIndex: 2);
        AddElement(scene, "Z", zIndex: 3);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        JsonObject desired = session.Documents.Read(scene);
        JsonArray elements = desired["Elements"]!.AsArray();
        JsonObject original = elements[0]!.DeepClone().AsObject();
        JsonObject renamed = original.DeepClone().AsObject();
        renamed[nameof(CoreObject.Name)] = "C";
        original[nameof(Element.IsLocked)] = true;
        JsonObject lockedCopy = elements[1]!.DeepClone().AsObject();
        lockedCopy[nameof(CoreObject.Name)] = "E";
        // The first two entries with the Id edit the first copy; once it has moved behind the locked
        // copy, the last entry edits the locked one.
        desired["Elements"] = new JsonArray(renamed, elements[2]!.DeepClone(), original, lockedCopy);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Message, Does.Contain("shares its Id with another element"));
            Assert.That(result.Error.Hint, Does.Contain("duplicate element Ids"));
            Assert.That(locked.Name, Is.EqualTo("B"));
            Assert.That(scene.Children, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void Apply_edit_refuses_a_second_element_with_the_id_of_a_locked_element()
    {
        Scene scene = CreateScene();
        Element locked = AddElement(scene, "Title", zIndex: 0);
        locked.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);
        string before = session.Documents.Read(scene).ToJsonString();
        JsonObject desired = session.Documents.Read(scene);
        JsonObject copy = desired["Elements"]![0]!.DeepClone().AsObject();
        copy[nameof(CoreObject.Name)] = "Copy";
        copy[nameof(Element.ZIndex)] = 1;
        desired["Elements"]!.AsArray().Add(copy);

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(session.Documents.Read(scene).ToJsonString(), Is.EqualTo(before));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Duplicate_object_rejects_an_object_in_a_locked_element(bool wrapInGroup)
    {
        Scene scene = CreateScene();
        Element element = AddElement(scene, "Title", zIndex: 0);
        var rect = new RectShape { Name = "Box" };
        element.AddObject(rect);
        element.IsLocked = true;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: wrapInGroup);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain($"({element.Id}) is locked"));
            Assert.That(element.Objects, Is.EqualTo(new[] { rect }));
        });
    }

    private static EditTools CreateTools(IEditingSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return new EditTools(manager);
    }

    private static JsonObject AsMcpArgument(JsonObject json)
    {
        return JsonNode.Parse(json.ToJsonString(), s_mcpArguments)!.AsObject();
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

    private static JsonObject ElementPatch(Element element, JsonObject members)
    {
        members["Id"] = element.Id.ToString();
        return new JsonObject { ["Elements"] = new JsonArray(members) };
    }

    private static JsonObject LayerPatch(TimelineLayer layer, JsonObject members)
    {
        members["Id"] = layer.Id.ToString();
        return new JsonObject { ["Layers"] = new JsonArray(members) };
    }

    private static JsonObject InsertPatch(string name, int zIndex)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                [nameof(CoreObject.Name)] = name,
                [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                [nameof(Element.ZIndex)] = zIndex,
            })
        };
    }

    private static Element AddElement(Scene scene, string name, int zIndex)
    {
        var element = new Element
        {
            Name = name,
            Start = TimeSpan.FromSeconds(scene.Children.Count * 3),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm"))
        };
        scene.Children.Add(element);
        return element;
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
