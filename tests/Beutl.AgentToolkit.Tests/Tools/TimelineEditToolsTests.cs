using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class TimelineEditToolsTests
{
    [Test]
    public async Task The_stdio_schema_exposes_required_edit_inputs_and_optional_live_targeting()
    {
        var inputs = new Dictionary<string, string[]>
        {
            ["split_elements"] = ["elementIds", "timeSeconds"],
            ["slip_elements"] = ["elementIds", "deltaSeconds"],
            ["ripple_delete"] = ["elementIds"],
            ["ripple_trim"] = ["elementId", "edge", "timeSeconds"],
            ["close_gap"] = ["elementId"]
        };
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        IList<McpClientTool> tools = await server.Client.ListToolsAsync();

        foreach ((string name, string[] required) in inputs)
        {
            McpClientTool tool = tools.Single(tool => tool.Name == name);
            JsonElement properties = tool.JsonSchema.GetProperty("properties");
            Assert.Multiple(() =>
            {
                Assert.That(tool.Description, Is.Not.Empty, name);
                Assert.That(tool.JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                    Is.EquivalentTo(required), name);
                Assert.That(properties.EnumerateObject().Select(item => item.Name),
                    Is.EquivalentTo(required.Concat(["instanceId", "sceneId"])), name);
            });
        }
    }

    [Test]
    public async Task Split_rounds_to_a_frame_returns_both_new_ids_and_is_one_dirty_file_edit()
    {
        string path = Path.Combine(TestWorkspace.CreateDirectory(), "Split.bep");
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var create = ToolResultReader.Read<CreateProjectResponse>(await server.Client.CallToolAsync(
            "create_project", new Dictionary<string, object?>
            {
                ["path"] = path,
                ["width"] = 1920,
                ["height"] = 1080,
                ["frameRate"] = 30,
                ["duration"] = "00:00:10"
            }));
        Assert.That(create.IsSuccess, Is.True, create.Error?.Message);
        var session = (FileEditingSession)server.Services.GetRequiredService<AgentSessionManager>().RequireSession();
        Scene scene = session.Scene;
        Element first = AddElement(scene, 1, 4);
        Element second = AddElement(scene, 1, 4, layer: 1);
        scene.Groups.Add(ImmutableHashSet.Create(first.Id.Value, second.Id.Value));
        session.History.Commit();
        session.History.Clear();

        var result = await CallAsync(server.Client, "split_elements",
            ("elementIds", new[] { Id(first), Id(second), Id(first) }), ("timeSeconds", 2.01));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Element[] created = scene.Children.Where(element => element != first && element != second).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(result.Value.CreatedElementIds, Has.Count.EqualTo(2));
            Assert.That(result.Value.CreatedElementIds, Is.EquivalentTo(created.Select(Id)));
            Assert.That(created.Select(element => element.Start.Value), Is.All.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(created.Select(element => element.Length.Value), Is.All.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(first.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(second.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(scene.Groups, Has.Count.EqualTo(2));
            Assert.That(session.IsDirty, Is.True);
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Is.EquivalentTo(new[] { first, second }));
            Assert.That(first.Length.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(second.Length.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(scene.Groups, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Slip_shifts_both_source_offsets_without_moving_elements_and_undoes_together()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element picture = AddElement(scene, 1, 2);
        Element audio = AddElement(scene, 1, 2, layer: 1);
        var video = new SourceVideo { OffsetPosition = { Value = MediaTime.FromSeconds(2) } };
        var sound = new SourceSound { OffsetPosition = { Value = MediaTime.FromSeconds(3) } };
        picture.Objects.Add(video);
        audio.Objects.Add(sound);
        var pictureRange = picture.Range;
        var audioRange = audio.Range;
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "slip_elements",
            ("elementIds", new[] { Id(picture), Id(audio) }), ("deltaSeconds", -1));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(picture.Range, Is.EqualTo(pictureRange));
            Assert.That(audio.Range, Is.EqualTo(audioRange));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public async Task Ripple_delete_skips_locked_targets_and_undoes_deletion_and_shifts_together()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element removed = AddElement(scene, 1, 2);
        Element follower = AddElement(scene, 3, 1);
        Element barrier = AddElement(scene, 4, 4);
        barrier.IsLocked.Value = true;
        Element beyondBarrier = AddElement(scene, 8, 1);
        Element otherLayer = AddElement(scene, 3, 1, layer: 1);
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "ripple_delete",
            ("elementIds", new[] { Id(removed), Id(barrier) }));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(result.Value.SkippedLockedElementIds, Is.EqualTo(new[] { Id(barrier) }));
            Assert.That(scene.Children, Does.Not.Contain(removed));
            Assert.That(follower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(barrier.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(beyondBarrier.Start.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
            Assert.That(otherLayer.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Does.Contain(removed));
            Assert.That(follower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public async Task Ripple_delete_preserves_the_transition_set_by_a_locked_clips_neighbor()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element outgoing = AddElement(scene, 0, 2);
        Element locked = AddElement(scene, 2, 2);
        locked.Name.Value = "Locked";
        locked.IsLocked.Value = true;
        var transition = new CrossDissolveTransition { Duration = { Value = MediaTime.FromSeconds(0.5) } };
        outgoing.ExitTransition.Value = transition;
        Assert.That(ElementTransitions.GetBoundaryAtStart(locked)?.Transition, Is.SameAs(transition));
        using var session = new AgentToolkitTestSession(scene);
        ToolResult<ApplyEditResponse> apply = session.CreateEditTools().ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject { ["Id"] = Id(outgoing), ["$delete"] = true })
            },
            schemaVersion: SchemaVersion.Current);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "ripple_delete", ("elementIds", new[] { Id(outgoing) }));

        Assert.Multiple(() =>
        {
            Assert.That(apply.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error, Is.EqualTo(apply.Error));
            Assert.That(result.Error?.Message, Does.Contain("transition at its start"));
            Assert.That(scene.Children, Is.EqualTo(new[] { outgoing, locked }));
            Assert.That(ElementTransitions.GetBoundaryAtStart(locked)?.Transition, Is.SameAs(transition));
            Assert.That(session.History.HasPendingOperations, Is.False);
            Assert.That(session.History.UndoCount, Is.Zero);
            Assert.That(session.History.RedoCount, Is.Zero);
        });
    }

    [TestCase("start", 1, 4)]
    [TestCase("end", 0, 3)]
    public async Task Ripple_trim_moves_the_neighbor_on_the_selected_side_and_undoes_as_one_edit(
        string edge, double precedingStart, double followingStart)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element preceding = AddElement(scene, 0, 2);
        Element target = AddElement(scene, 2, 2);
        Element following = AddElement(scene, 4, 2);
        var original = target.Range;
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "ripple_trim",
            ("elementId", Id(target)), ("edge", edge), ("timeSeconds", 3));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(edge == "start" ? 3 : 2)));
            Assert.That(target.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(preceding.Start.Value, Is.EqualTo(MediaTime.FromSeconds(precedingStart)));
            Assert.That(following.Start.Value, Is.EqualTo(MediaTime.FromSeconds(followingStart)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(target.Range, Is.EqualTo(original));
            Assert.That(preceding.Start.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(following.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
        });
    }

    [Test]
    public async Task Close_gap_can_use_a_locked_anchor_and_stops_at_a_locked_follower()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element anchor = AddElement(scene, 0, 2);
        anchor.IsLocked.Value = true;
        Element follower = AddElement(scene, 4, 1);
        Element barrier = AddElement(scene, 6, 1);
        barrier.IsLocked.Value = true;
        Element beyondBarrier = AddElement(scene, 8, 1);
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "close_gap", ("elementId", Id(anchor)));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(anchor.Start.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(follower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(barrier.Start.Value, Is.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(beyondBarrier.Start.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.That(follower.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
    }

    [Test]
    public async Task Timeline_tools_never_modify_a_user_locked_element_or_layer(
        [Values("split_elements", "slip_elements", "ripple_delete", "ripple_trim", "close_gap")] string tool,
        [Values] bool layerLocked)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element anchor = AddElement(scene, 0, 1);
        Element target = AddElement(scene, 2, 2);
        var media = new SourceVideo();
        target.Objects.Add(media);
        if (layerLocked)
            scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsLocked = { Value = true } });
        else
            target.IsLocked.Value = true;
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, tool, EditArguments(tool, target, anchor));

        if (tool == "close_gap")
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Changed, Is.False);
        }
        else
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        }
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Is.EquivalentTo(new[] { anchor, target }));
            Assert.That(target.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(target.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(media.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    [Test]
    public async Task Recovery_protection_blocks_changes_but_allows_removal_while_retaining_the_file()
    {
        Scene original = TestWorkspace.CreateScene();
        Element originalAnchor = AddElement(original, 0, 1);
        Element originalTarget = AddElement(original, 2, 2);
        originalTarget.AddObject(new RectShape());
        CoreSerializer.StoreToUri(original, original.Uri!);
        string retainedFile = originalTarget.Uri!.LocalPath;
        JsonNode elementJson = JsonNode.Parse(File.ReadAllText(retainedFile))!;
        JsonObject shapeJson = elementJson[nameof(Element.Objects)]![0]!.AsObject();
        shapeJson["Animations"] ??= new JsonObject();
        UnreadableEasing.AddTo(shapeJson);
        string unavailableContent = elementJson.ToJsonString();
        File.WriteAllText(retainedFile, unavailableContent);
        Scene scene = CoreSerializer.RestoreFromUri<Scene>(original.Uri!);
        Element anchor = scene.Children.Single(element => element.Id.Value == originalAnchor.Id.Value);
        Element target = scene.Children.Single(element => element.Id.Value == originalTarget.Id.Value);
        Assert.That(scene.IsElementLocked(target), Is.True);
        Assert.That(scene.IsElementLockedByUser(target), Is.False);
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        foreach (string tool in new[] { "split_elements", "slip_elements", "ripple_trim" })
        {
            var result = await CallAsync(server.Client, tool, EditArguments(tool, target, anchor));
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected), tool);
        }

        var close = await CallAsync(server.Client, "close_gap", ("elementId", Id(anchor)));
        Assert.That(close.Value?.Changed, Is.False);
        Assert.That(session.History.UndoCount, Is.Zero);

        var removal = await CallAsync(server.Client, "ripple_delete", ("elementIds", new[] { Id(target) }));
        Assert.That(removal.Value?.Changed, Is.True, removal.Error?.Message);
        Assert.That(scene.Children, Does.Not.Contain(target));
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Assert.That(File.ReadAllText(retainedFile), Is.EqualTo(unavailableContent));
        await UndoOnlyEditAsync(server.Client);
        Assert.That(scene.Children, Does.Contain(target));
    }

    [TestCase(false, ErrorCode.StaleHandle)]
    [TestCase(true, ErrorCode.ValidationRejected)]
    public async Task An_unknown_or_ambiguous_id_rejects_a_batch_before_changing_any_element(
        bool duplicateId, string expectedError)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element target = AddElement(scene, 2, 2);
        Guid invalidId = Guid.NewGuid();
        if (duplicateId)
        {
            AddElement(scene, 4, 1).Id.Value = invalidId;
            AddElement(scene, 5, 1).Id.Value = invalidId;
        }
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "ripple_delete", ("elementIds", new[] { Id(target), invalidId.ToString() }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(expectedError));
            Assert.That(scene.Children, Does.Contain(target));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    private static Element AddElement(Scene scene, double start, double length, int layer = 0)
    {
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), $"{Guid.NewGuid():N}.belm");
        element.ZIndex.Value = layer;
        return element;
    }

    private static string Id(Element element) => element.Id.Value.ToString();

    private static (string Key, object Value)[] EditArguments(string tool, Element target, Element anchor)
        => tool switch
        {
            "split_elements" => [("elementIds", new[] { Id(target) }), ("timeSeconds", 3)],
            "slip_elements" => [("elementIds", new[] { Id(target) }), ("deltaSeconds", 1)],
            "ripple_delete" => [("elementIds", new[] { Id(target) })],
            "ripple_trim" => [("elementId", Id(target)), ("edge", "end"), ("timeSeconds", 3)],
            "close_gap" => [("elementId", Id(anchor))],
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };

    private static Task<InProcessAgentToolkitServer> StartAsync(AgentToolkitTestSession session)
        => InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory(),
            services => services.AddSingleton(session.CreateManager()));

    private static async Task<ToolResult<TimelineEditResponse>> CallAsync(
        McpClient client, string tool, params (string Key, object Value)[] arguments)
        => ToolResultReader.Read<TimelineEditResponse>(await client.CallToolAsync(tool,
            arguments.ToDictionary(argument => argument.Key, argument => (object?)argument.Value)));

    private static async Task UndoOnlyEditAsync(McpClient client)
    {
        var undo = ToolResultReader.Read<HistoryStateResponse>(await client.CallToolAsync("undo"));
        Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(undo.Value!.Applied, Has.Count.EqualTo(1));
            Assert.That(undo.Value.UndoCount, Is.Zero);
            Assert.That(undo.Value.RedoCount, Is.EqualTo(1));
        });
    }
}
