using System.Collections.Immutable;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed partial class TimelineEditToolsTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Duplicate_includes_the_group_and_undoes_placement_and_grouping_together(bool destinationLocked)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element first = AddElement(scene, 1, 2);
        Element second = AddElement(scene, 1.5, 1, layer: 2);
        var group = ImmutableHashSet.Create(first.Id.Value, second.Id.Value);
        scene.Groups.Add(group);
        if (destinationLocked)
            scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "duplicate_elements",
            ("elementIds", new[] { Id(first) }), ("timeSeconds", 5.01), ("layer", 3));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Element[] copies = scene.Children.Where(element => element != first && element != second)
            .OrderBy(element => element.ZIndex.Value).ToArray();
        Assert.That(copies, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(result.Value.CreatedElementIds, Is.EquivalentTo(copies.Select(Id)));
            Assert.That(copies[1].ZIndex.Value - copies[0].ZIndex.Value, Is.EqualTo(2));
            Assert.That(copies[1].Start.Value - copies[0].Start.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(copies.Select(element => element.Length.Value), Is.EqualTo(new[] { first.Length.Value, second.Length.Value }));
            Assert.That(copies.Any(scene.IsElementLocked), Is.False);
            Assert.That(scene.Groups, Has.Count.EqualTo(2));
            Assert.That(scene.Groups.Any(ids => ids.SetEquals(copies.Select(element => element.Id.Value))), Is.True);
        });
        if (!destinationLocked)
        {
            Assert.That(copies[0].Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(copies[0].ZIndex.Value, Is.EqualTo(3));
        }

        await UndoOnlyEditAsync(server.Client);
        Assert.That(scene.Children, Is.EquivalentTo(new[] { first, second }));
        Assert.That(scene.Groups.Single(), Is.EqualTo(group));
    }

    [Test]
    public async Task Duplicate_skips_an_implicitly_selected_locked_group_member()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element source = AddElement(scene, 1, 2);
        Element locked = AddElement(scene, 1, 2, layer: 1);
        locked.IsLocked.Value = true;
        var group = ImmutableHashSet.Create(source.Id.Value, locked.Id.Value);
        scene.Groups.Add(group);
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "duplicate_elements",
            ("elementIds", new[] { Id(source) }), ("timeSeconds", 5), ("layer", 0));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Element copy = scene.Children.Single(element => element != source && element != locked);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.CreatedElementIds, Is.EqualTo(new[] { Id(copy) }));
            Assert.That(result.Value.SkippedLockedElementIds, Is.EqualTo(new[] { Id(locked) }));
            Assert.That(scene.Groups.Single(), Is.EqualTo(group));
        });
        await UndoOnlyEditAsync(server.Client);
    }

    [TestCase("start")]
    [TestCase("end")]
    public async Task Grouped_ripple_trim_moves_only_matching_edges_and_reports_each_shifted_neighbor(string edge)
    {
        bool leftEdge = edge == "start";
        Scene scene = TestWorkspace.CreateScene();
        Element first = AddElement(scene, 2, 2);
        Element second = AddElement(scene, 2, 2, layer: 1);
        Element unaligned = leftEdge ? AddElement(scene, 1, 3, layer: 2) : AddElement(scene, 2, 1, layer: 2);
        Element locked = AddElement(scene, 2, 2, layer: 3);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });
        scene.Groups.Add(ImmutableHashSet.Create(first.Id.Value, second.Id.Value, unaligned.Id.Value, locked.Id.Value));
        double neighborStart = leftEdge ? 0 : 4;
        Element firstNeighbor = AddElement(scene, neighborStart, 2);
        Element secondNeighbor = AddElement(scene, neighborStart, 2, layer: 1);
        TimeRange unalignedRange = unaligned.Range;
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "ripple_trim",
            ("elementId", Id(first)), ("edge", edge), ("timeSeconds", 3.01), ("includeLinked", true));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.ChangedElementIds, Is.EquivalentTo(new[] { Id(first), Id(second), Id(firstNeighbor), Id(secondNeighbor) }));
            Assert.That(result.Value.SkippedLockedElementIds, Is.EqualTo(new[] { Id(locked) }));
            Assert.That(first.Range, Is.EqualTo(TimeRange.FromSeconds(leftEdge ? 3 : 2, 1)));
            Assert.That(second.Range, Is.EqualTo(first.Range));
            Assert.That(firstNeighbor.Start.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 1 : 3)));
            Assert.That(secondNeighbor.Range, Is.EqualTo(firstNeighbor.Range));
            Assert.That(unaligned.Range, Is.EqualTo(unalignedRange));
            Assert.That(locked.Range, Is.EqualTo(TimeRange.FromSeconds(2, 2)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(first.Range, Is.EqualTo(TimeRange.FromSeconds(2, 2)));
            Assert.That(second.Range, Is.EqualTo(first.Range));
            Assert.That(firstNeighbor.Range, Is.EqualTo(TimeRange.FromSeconds(neighborStart, 2)));
            Assert.That(secondNeighbor.Range, Is.EqualTo(firstNeighbor.Range));
        });
    }

    [TestCase("start")]
    [TestCase("end")]
    public async Task Roll_moves_aligned_group_cuts_and_undoes_with_the_shared_media_offset(string edge)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element frontA = AddElement(scene, 0, 2);
        Element backA = AddElement(scene, 2, 3);
        Element frontB = AddElement(scene, 0, 2, layer: 1);
        Element backB = AddElement(scene, 2, 3, layer: 1);
        var video = new SourceVideo();
        backA.Objects.Add(video);
        backB.Objects.Add(new DrawablePresenter { Target = { Value = video } });
        (Element anchor, Element peer) = edge == "start" ? (backA, backB) : (frontA, frontB);
        scene.Groups.Add(ImmutableHashSet.Create(anchor.Id.Value, peer.Id.Value));
        Element[] participants = [frontA, backA, frontB, backB];
        TimeRange[] before = participants.Select(element => element.Range).ToArray();
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "roll_elements",
            ("elementId", Id(anchor)), ("edge", edge), ("deltaSeconds", 0.51));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.ChangedElementIds, Is.EquivalentTo(participants.Select(Id)));
            Assert.That(frontA.Range, Is.EqualTo(TimeRange.FromSeconds(0, 2.5)));
            Assert.That(frontB.Range, Is.EqualTo(frontA.Range));
            Assert.That(backA.Range, Is.EqualTo(TimeRange.FromSeconds(2.5, 2.5)));
            Assert.That(backB.Range, Is.EqualTo(backA.Range));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(participants.Select(element => element.Range), Is.EqualTo(before));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public async Task Slide_moves_contiguous_grouped_blocks_and_undoes_with_the_adjacent_trims()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element frontA = AddElement(scene, 0, 2);
        Element middleA = AddElement(scene, 2, 1);
        Element nextA = AddElement(scene, 3, 1);
        Element backA = AddElement(scene, 4, 2);
        Element frontB = AddElement(scene, 0, 2, layer: 1);
        Element middleB = AddElement(scene, 2, 2, layer: 1);
        Element backB = AddElement(scene, 4, 2, layer: 1);
        var sound = new SourceSound { OffsetPosition = { Value = MediaTime.FromSeconds(1) } };
        backB.Objects.Add(sound);
        scene.Groups.Add(ImmutableHashSet.Create(middleA.Id.Value, nextA.Id.Value, middleB.Id.Value));
        Element[] participants = [frontA, middleA, nextA, backA, frontB, middleB, backB];
        TimeRange[] before = participants.Select(element => element.Range).ToArray();
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "slide_elements",
            ("elementIds", new[] { Id(middleA) }), ("deltaSeconds", -0.51));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.ChangedElementIds, Is.EquivalentTo(participants.Select(Id)));
            Assert.That(frontA.Range, Is.EqualTo(TimeRange.FromSeconds(0, 1.5)));
            Assert.That(frontB.Range, Is.EqualTo(frontA.Range));
            Assert.That(middleA.Range, Is.EqualTo(TimeRange.FromSeconds(1.5, 1)));
            Assert.That(nextA.Range, Is.EqualTo(TimeRange.FromSeconds(2.5, 1)));
            Assert.That(middleB.Range, Is.EqualTo(TimeRange.FromSeconds(1.5, 2)));
            Assert.That(backA.Range, Is.EqualTo(TimeRange.FromSeconds(3.5, 2.5)));
            Assert.That(backB.Range, Is.EqualTo(backA.Range));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(participants.Select(element => element.Range), Is.EqualTo(before));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public async Task A_locked_participant_on_another_layer_blocks_the_entire_coupled_trim(
        [Values("roll_elements", "slide_elements")] string tool, [Values] bool layerLocked)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element frontA = AddElement(scene, 0, 2);
        Element middleA = AddElement(scene, 2, 2);
        AddElement(scene, 4, 2);
        AddElement(scene, 0, 2, layer: 1);
        Element middleB = AddElement(scene, 2, 2, layer: 1);
        Element backB = AddElement(scene, 4, 2, layer: 1);
        scene.Groups.Add(ImmutableHashSet.Create(middleA.Id.Value, middleB.Id.Value));
        if (layerLocked)
            scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        else
            backB.IsLocked.Value = true;
        TimeRange[] before = scene.Children.Select(element => element.Range).ToArray();
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, tool, EditArguments(tool, middleA, frontA));

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Target, Is.EqualTo(Id(layerLocked ? middleB : backB)));
            Assert.That(scene.Children.Select(element => element.Range), Is.EqualTo(before));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }
}
