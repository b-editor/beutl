using System.Collections.Immutable;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Graphics.Transitions;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using NAudio.Wave;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed partial class TimelineEditToolsTests
{
    [Test]
    public async Task Find_gaps_lists_only_the_space_between_covered_runs_and_can_filter_by_layer()
    {
        Scene scene = TestWorkspace.CreateScene();
        AddElement(scene, 1, 3);
        AddElement(scene, 2, 1);
        AddElement(scene, 6, 1).IsLocked.Value = true;
        AddElement(scene, 7, 1);
        AddElement(scene, 0, 1, layer: 1);
        AddElement(scene, 3, 1, layer: 1);
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var all = ToolResultReader.Read<FindGapsResponse>(await server.Client.CallToolAsync("find_gaps"));
        var selected = ToolResultReader.Read<FindGapsResponse>(await server.Client.CallToolAsync("find_gaps",
            new Dictionary<string, object?> { ["layers"] = new[] { 1 } }));

        Assert.Multiple(() =>
        {
            Assert.That(all.IsSuccess, Is.True, all.Error?.Message);
            Assert.That(all.Value?.Gaps, Is.EqualTo(new TimelineGap[] { new(0, 4, 6), new(1, 1, 3) }));
            Assert.That(selected.IsSuccess, Is.True, selected.Error?.Message);
            Assert.That(selected.Value?.Gaps, Is.EqualTo(new TimelineGap[] { new(1, 1, 3) }));
            Assert.That(session.History.HasPendingOperations, Is.False);
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Close_all_gaps_undoes_every_shift_together(bool filterLayers)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element anchor = AddElement(scene, 1, 1);
        Element first = AddElement(scene, 4, 1);
        Element second = AddElement(scene, 7, 1);
        AddElement(scene, 0, 1, layer: 1);
        Element otherLayer = AddElement(scene, 3, 1, layer: 1);
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = filterLayers
            ? await CallAsync(server.Client, "close_all_gaps", ("layers", new[] { 0 }))
            : await CallAsync(server.Client, "close_all_gaps");

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(result.Value.ClosedGapCount, Is.EqualTo(filterLayers ? 2 : 3));
            Assert.That(anchor.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(first.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(second.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(otherLayer.Start.Value, Is.EqualTo(MediaTime.FromSeconds(filterLayers ? 3 : 1)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(first.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(second.Start.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(otherLayer.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public async Task Close_all_gaps_rolls_back_if_a_locked_clips_transition_would_change()
    {
        Scene scene = TestWorkspace.CreateScene();
        AddElement(scene, 0, 1);
        Element outgoing = AddElement(scene, 3, 2);
        Element locked = AddElement(scene, 5, 1);
        locked.IsLocked.Value = true;
        var transition = new CrossDissolveTransition { Duration = { Value = MediaTime.FromSeconds(0.5) } };
        outgoing.ExitTransition.Value = transition;
        Assert.That(ElementTransitions.GetBoundaryAtStart(locked)?.Transition, Is.SameAs(transition));
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "close_all_gaps");

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain("transition at its start"));
            Assert.That(outgoing.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(ElementTransitions.GetBoundaryAtStart(locked)?.Transition, Is.SameAs(transition));
            Assert.That(session.History.UndoCount, Is.Zero);
            Assert.That(session.History.HasPendingOperations, Is.False);
        });
    }

    [TestCase("split_only", false, 4, 2.4, 5.4)]
    [TestCase("delete_silence", true, 2, 2.4, 5.4)]
    [TestCase("ripple_delete", true, 2, 1.6, 3.8)]
    public async Task Silence_split_decodes_a_WAV_reports_original_ranges_and_undoes_the_batch(
        string mode, bool useLayers, int newClipCount, double firstTail, double secondTail)
    {
        Scene scene = TestWorkspace.CreateScene();
        scene.FrameRate.Value = new(30);
        scene.SampleRate.Value = 48000;
        var source = WriteWavWithSilence(scene);
        Element first = AddElement(scene, 1, 2);
        Element second = AddElement(scene, 4, 2);
        Element otherLayer = AddElement(scene, 1, 2, layer: 1);
        foreach (Element element in new[] { first, second, otherLayer })
            element.AddObject(new SourceSound { Source = { Value = source } });
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "split_on_silence",
            useLayers ? ("layers", new[] { 0 }) : ("elementIds", new[] { Id(first), Id(second), Id(first) }),
            ("mode", mode), ("thresholdDb", -35), ("minimumMilliseconds", 250), ("paddingMilliseconds", 100));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Element[] created = scene.Children.Except([first, second, otherLayer]).ToArray();
        TimelineSilenceRange[] removed = mode == "split_only" ? [] : [new(Id(first), 1.6, 2.4), new(Id(second), 4.6, 5.4)];
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Changed, Is.True);
            Assert.That(result.Value.CreatedElementIds, Has.Count.EqualTo(newClipCount));
            Assert.That(result.Value.CreatedElementIds, Is.EquivalentTo(created.Select(Id)));
            Assert.That(result.Value.RemovedRanges, Is.EqualTo(removed));
            Assert.That(created.Where(element => element.Length.Value == MediaTime.FromSeconds(0.6))
                .Select(element => element.Start.Value), Is.EquivalentTo(new[] { MediaTime.FromSeconds(firstTail), MediaTime.FromSeconds(secondTail) }));
            Assert.That(first.Length.Value, Is.EqualTo(MediaTime.FromSeconds(0.6)));
            Assert.That(second.Length.Value, Is.EqualTo(MediaTime.FromSeconds(0.6)));
            Assert.That(otherLayer.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
        await UndoOnlyEditAsync(server.Client);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Is.EquivalentTo(new[] { first, second, otherLayer }));
            Assert.That(first.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(second.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(second.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public async Task Silence_split_refuses_a_group_with_a_locked_linked_clip()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element sound = AddElement(scene, 1, 2);
        sound.AddObject(new SourceSound { Source = { Value = WriteWavWithSilence(scene) } });
        Element linked = AddElement(scene, 1, 2, layer: 1);
        linked.IsLocked.Value = true;
        scene.Groups.Add(ImmutableHashSet.Create(sound.Id.Value, linked.Id.Value));
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);

        var result = await CallAsync(server.Client, "split_on_silence", ("elementIds", new[] { Id(sound) }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Target, Is.EqualTo(Id(linked)));
            Assert.That(scene.Children, Is.EquivalentTo(new[] { sound, linked }));
            Assert.That(scene.Groups.Single(), Is.EquivalentTo(new[] { sound.Id.Value, linked.Id.Value }));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    [Test]
    public async Task Silence_split_rejects_analysis_if_a_user_edit_is_flushed_before_application()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element sound = AddElement(scene, 1, 2);
        sound.AddObject(new SourceSound { Source = { Value = WriteWavWithSilence(scene) } });
        using var session = new AgentToolkitTestSession(scene);
        await using InProcessAgentToolkitServer server = await StartAsync(session);
        using IDisposable pendingEdit = session.History.BeforeMutation.Subscribe(_ => sound.Start.Value = MediaTime.FromSeconds(3));

        var result = await CallAsync(server.Client, "split_on_silence", ("elementIds", new[] { Id(sound) }), ("mode", "delete_silence"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.StaleHandle));
            Assert.That(scene.Children.Single(), Is.SameAs(sound));
            Assert.That(sound.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(sound.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(session.History.UndoCount, Is.EqualTo(1));
        });
    }

    private static SoundSource WriteWavWithSilence(Scene scene)
    {
        const int sampleRate = 48000;
        string path = Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "silence.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)))
        {
            for (int i = 0; i < sampleRate * 2; i++)
                writer.WriteSample(i >= sampleRate / 2 && i < sampleRate * 3 / 2 ? 0 : 0.4f);
        }
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        return source;
    }
}
