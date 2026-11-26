using System.Collections.Immutable;
using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record TimelineSilenceRange(string ElementId, double StartSeconds, double EndSeconds);

public sealed partial class TimelineEditTools
{
    [McpServerTool(Name = "split_on_silence")]
    [Description("Analyzes clips with audio and splits them at silences using the editor's Silence Split settings. Returns new element IDs and the intervals removed from the analyzed clips in the original timeline.")]
    public ValueTask<ToolResult<TimelineEditResponse>> SplitOnSilence(
        [Description("Element IDs from read_document_summary. Supply either elementIds or layers.")]
        string[]? elementIds = null,
        [Description("Timeline layer indices (ZIndex) whose audio clips to analyze. Supply either layers or elementIds.")]
        int[]? layers = null,
        [Description("Silence threshold in dBFS, from -120 to 0.")]
        double thresholdDb = -40,
        [Description("Minimum detected silence in milliseconds, from 20 to 3600000.")]
        double minimumMilliseconds = 300,
        [Description("Audio to retain at each edge of a detected silence, in milliseconds, from 0 to 3600000.")]
        double paddingMilliseconds = 100,
        [Description("One of split_only, delete_silence, or ripple_delete.")]
        string mode = "split_only",
        [Description("Apply the first eligible clip's cuts to its linked group, including on other layers. A locked group member blocks that group.")]
        bool includeLinked = true,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            if (elementIds == null ? layers is not { Length: > 0 } : elementIds.Length == 0 || layers != null)
                throw InvalidEdit("Supply either a non-empty elementIds array or a non-empty layers array.", nameof(elementIds));
            if (!double.IsFinite(thresholdDb) || thresholdDb is < -120 or > 0)
                throw InvalidEdit("Supply a threshold between -120 and 0 dBFS.", nameof(thresholdDb));
            if (!double.IsFinite(minimumMilliseconds) || minimumMilliseconds is < 20 or > 3600000)
                throw InvalidEdit("Supply a minimum silence between 20 and 3600000 milliseconds.", nameof(minimumMilliseconds));
            if (!double.IsFinite(paddingMilliseconds) || paddingMilliseconds is < 0 or > 3600000)
                throw InvalidEdit("Supply padding between 0 and 3600000 milliseconds.", nameof(paddingMilliseconds));
            SilenceSplitMode splitMode = mode switch
            {
                "split_only" => SilenceSplitMode.SplitOnly,
                "delete_silence" => SilenceSplitMode.DeleteSilence,
                "ripple_delete" => SilenceSplitMode.RippleDelete,
                _ => throw InvalidEdit("Choose 'split_only', 'delete_silence', or 'ripple_delete'.", nameof(mode))
            };

            IEditingSession session = sessions.RequireSession();
            MediaTime minimum = MediaTime.FromSeconds(minimumMilliseconds / 1000);
            MediaTime padding = MediaTime.FromSeconds(paddingMilliseconds / 1000);
            var observed = new HashSet<Scene>();
            int edited = 0;
            try
            {
                var initial = session.ReadOnSession(() =>
                {
                    Scene scene = RequireSceneRoot(session);
                    var (targets, locked) = SelectSilenceTargets(scene, elementIds, layers, includeLinked);
                    ObserveScene(scene);
                    var history = session.History;
                    return (Scene: scene, History: history, Version: history.ChangeVersion, Targets: targets, Locked: locked);
                });

                var analyzed = new List<(Element Element, IReadOnlyList<TimeRange> LocalRanges)>();
                foreach (Element element in initial.Targets)
                {
                    var ranges = await session.ReadOnSession(() =>
                    {
                        CheckAnalysisIsCurrent(initial.Scene);
                        return Analyze(element, initial.Scene);
                    }).ConfigureAwait(false);
                    if (ranges.Count > 0) analyzed.Add((element, ranges));
                }

                var service = new SilenceSplitService(initial.History, configuration.EditorConfig, decoders: decoders);
                var (requests, preparation) = session.ReadOnSession(() =>
                {
                    Scene scene = RequireSceneRoot(session);
                    CheckAnalysisIsCurrent(scene);
                    SilenceSplitRequest[] requests = analyzed.Select(item =>
                            new SilenceSplitRequest(item.Element, SilenceSplitService.Plan(scene, item.Element, item.LocalRanges)))
                        .Where(request => request.Ranges.Count > 0).ToArray();
                    return (requests, service.PrepareAsync(scene, requests, splitMode, includeLinked, cancellationToken));
                });
                SilenceSplitService.PreparedSplit? prepared = await preparation.ConfigureAwait(false);

                return await ApplyTimelineAsync(session, CommandNames.AutoSplitBySilence, (scene, _) =>
                {
                    CheckAnalysisIsCurrent(scene);
                    HashSet<Element> originals = [.. scene.Children];
                    IReadOnlyList<SilenceSplitRequest> applied;
                    if (prepared != null)
                    {
                        if (!prepared.MatchesSceneSettings(scene)) throw StaleAnalysis();
                        prepared.Publish(scene);
                        applied = prepared.Applied;
                    }
                    else
                    {
                        applied = service.ApplyBatch(scene, requests, splitMode, includeLinked, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    TimelineSilenceRange[] removed = splitMode == SilenceSplitMode.SplitOnly ? [] : applied.SelectMany(request =>
                        request.Ranges.Select(range => new TimelineSilenceRange(request.Element.Id.Value.ToString(),
                            range.Start.ToSeconds(), range.End.ToSeconds()))).ToArray();
                    string[] created = scene.Children.Where(element => !originals.Contains(element))
                        .Select(element => element.Id.Value.ToString()).ToArray();
                    return new TimelineEditResponse(applied.Count > 0, created, initial.Locked) { RemovedRanges = removed };
                }, cancellationToken).ConfigureAwait(false);

                void CheckAnalysisIsCurrent(Scene scene)
                {
                    if (!ReferenceEquals(scene, initial.Scene) || !ReferenceEquals(session.Root, scene)
                        || !ReferenceEquals(session.History, initial.History) || initial.History.ChangeVersion != initial.Version
                        || Volatile.Read(ref edited) != 0)
                    {
                        throw StaleAnalysis();
                    }
                }
            }
            finally
            {
                foreach (Scene scene in observed) scene.Edited -= OnEdited;
            }

            async Task<IReadOnlyList<TimeRange>> Analyze(Element element, Scene scene)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeRange range = element.Range;
                string id = element.Id.Value.ToString();
                int count = SilenceWaveform.GetChunkCount(range.Duration);
                var chunks = new WaveformChunk[count];
                Array.Fill(chunks, new WaveformChunk(-1, float.NaN, float.NaN));
                try
                {
                    await foreach (WaveformChunk chunk in SilenceWaveform.GetChunksAsync(session.Fonts, scene, element, count,
                        cancellationToken, ObserveScene, decoders,
                        invokeOnOwner: session.Source == EditingSessionSource.File ? session.Invoke : null).ConfigureAwait(false))
                    {
                        if (Volatile.Read(ref edited) != 0) throw StaleAnalysis();
                        chunks[chunk.Index] = chunk;
                    }
                }
                catch (InvalidDataException)
                {
                    throw new ReconcileException(new ToolError(ErrorCode.MediaUnsupported, "The element's audio could not be read.", id));
                }
                return SilenceDetector.Detect(chunks, range.Duration, thresholdDb, minimum, padding);
            }

            void ObserveScene(Scene scene)
            {
                if (observed.Add(scene)) scene.Edited += OnEdited;
            }

            void OnEdited(object? sender, EventArgs args) => Volatile.Write(ref edited, 1);

            static ReconcileException StaleAnalysis()
                => new(new ToolError(ErrorCode.StaleHandle, "The scene changed during silence analysis.",
                    Hint: "Retry split_on_silence on the current scene."));
        });

    private static (Element[] Targets, string[]? Locked) SelectSilenceTargets(
        Scene scene, string[]? elementIds, int[]? layers, bool includeLinked)
    {
        var byId = scene.Children.ToLookup(element => element.Id.Value);
        IEnumerable<Element> candidates;
        if (elementIds != null)
        {
            candidates = elementIds.Select(ReadElementId).Distinct().Select(id => RequireElement(byId[id], id));
        }
        else
        {
            var selectedLayers = layers!.ToHashSet();
            candidates = scene.Children.Where(element => selectedLayers.Contains(element.ZIndex.Value)
                && SilenceWaveform.HasAnalyzableAudio(element))
                .OrderBy(element => element.Start.Value).ThenBy(element => element.ZIndex.Value)
                .Select(element => RequireElement(byId[element.Id.Value], element.Id.Value));
        }

        var targets = new List<Element>();
        var locked = new HashSet<Element>();
        var groups = new HashSet<ImmutableHashSet<Guid>>();
        foreach (Element element in candidates)
        {
            if (scene.IsElementLocked(element))
            {
                locked.Add(element);
                continue;
            }
            if (!SilenceWaveform.HasAnalyzableAudio(element)) continue;

            if (includeLinked && scene.FindGroup(element.Id.Value) is { } linked)
            {
                if (!groups.Add(linked)) continue;
                Element[] blockers = linked.SelectMany(id => byId[id]).Where(scene.IsElementLocked).ToArray();
                if (blockers.Length > 0)
                {
                    locked.UnionWith(blockers);
                    continue;
                }
            }
            targets.Add(element);
        }

        if (targets.Count == 0 && locked.FirstOrDefault() is { } firstLocked)
            throw new ReconcileException(Reconciler.CreateLockedElementError(scene, firstLocked, firstLocked.Id.Value.ToString()));

        return ([.. targets], locked.Count == 0 ? null : locked.Select(element => element.Id.Value.ToString()).ToArray());
    }
}
