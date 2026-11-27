using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

public sealed partial class SilenceSplitService
{
    // Called on the document's owning thread. Preparation owns a detached scene; only publication
    // touches the open document and its history. A pending edit never holds a transaction across await.
    public async Task<bool> ApplyAsync(Scene scene, Element element, IReadOnlyList<TimeRange> ranges,
        SilenceSplitMode mode, bool includeLinked, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveTargets(scene, element, ranges, includeLinked, out _, out _, out _))
            return false;

        long changeVersion = history.ChangeVersion;
        bool changed = false;
        void OnEdited(object? sender, EventArgs args) => changed = true;
        scene.Edited += OnEdited;
        try
        {
            PreparedSplit? prepared = await PrepareAsync(scene, [new(element, ranges)], mode, includeLinked, cancellationToken, progress);
            if (prepared == null)
            {
                progress?.Report(100);
                Apply(scene, element, ranges, mode, includeLinked);
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
            // Playback may have resumed while preparation yielded the UI. Relinquish its readers
            // again immediately before publishing, then recheck edits made during that pause.
            if (player != null) await player.Pause();
            cancellationToken.ThrowIfCancellationRequested();
            history.FlushPendingMutations();
            if (changed || history.ChangeVersion != changeVersion || !prepared.MatchesSceneSettings(scene)) return false;

            history.ExecuteInTransaction(() => prepared.Publish(scene), CommandNames.AutoSplitBySilence, cancellationToken: cancellationToken);
            return true;
        }
        finally { scene.Edited -= OnEdited; }
    }

    internal async Task<PreparedSplit?> PrepareAsync(Scene scene, IReadOnlyList<SilenceSplitRequest> requests,
        SilenceSplitMode mode, bool includeLinked, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var originals = new HashSet<Element>();
        var targets = new List<SilenceSplitRequest>(requests.Count);
        foreach (SilenceSplitRequest request in requests)
        {
            if (!TryResolveTargets(scene, request.Element, request.Ranges, includeLinked, out _, out _, out Element[] members)) continue;
            targets.Add(request);
            originals.UnionWith(members);
        }
        if (targets.Count == 0) return null;

        // Scene references, portals, extension callbacks and the speed expressions a cut reads can depend on live
        // objects outside the clip. They retain the existing Razor path, without limiting which clips can be edited.
        if (!originals.All(e => e.GetType() == typeof(Element)
            && e.Objects.All(o => (o.GetType() == typeof(SourceSound) || o.GetType() == typeof(SourceVideo))
                && o is not ITimeMappedSource { Speed.HasExpression: true })))
        {
            return null;
        }

        var snapshot = new Scene
        {
            Uri = scene.Uri,
            Start = { Value = scene.Start.Value },
            Duration = { Value = scene.Duration.Value },
            FrameRate = { Value = SceneTimeRangeService.GetFrameRate(scene) },
            SampleRate = { Value = scene.GetSampleRate() },
            ChannelLayout = { Value = scene.GetSpeakerLayout() },
        };
        foreach (TimelineLayer layer in scene.Layers)
        {
            var copy = new TimelineLayer();
            CoreSerializer.PopulateFromJsonObject(copy, CoreSerializer.SerializeToJsonObject(layer));
            snapshot.Layers.Add(copy);
        }
        // Clone the source clips while the document is stable. Thousands of later
        // regenerations, source-time calculations and ripple scans use only these private objects.
        var copies = new Dictionary<Element, Element>();
        HashSet<int> layers = originals.Select(e => e.ZIndex.Value).ToHashSet();
        foreach (Element original in scene.Children.Where(e => layers.Contains(e.ZIndex.Value)))
        {
            var copy = new Element();
            if (originals.Contains(original))
            {
                CoreSerializer.PopulateFromJsonObject(copy, CoreSerializer.SerializeToJsonObject(original, new CoreSerializerOptions
                {
                    Mode = CoreSerializationMode.EmbedReferencedObjects,
                    EmbedsCopy = true
                }));
            }
            else
            {
                // Ripple needs other clips' geometry and lock anchors, not their media or effects.
                copy.Id.Value = original.Id.Value;
                copy.Start.Value = original.Start.Value;
                copy.Length.Value = original.Length.Value;
                copy.ZIndex.Value = original.ZIndex.Value;
            }
            copy.IsLocked.Value = scene.IsElementLocked(original);
            copies.Add(original, copy);
        }
        snapshot.Children.AddRange(copies.Values);
        snapshot.Groups.AddRange(scene.Groups);
        var requestsByCopy = targets.ToDictionary(request => copies[request.Element]);
        SilenceSplitRequest[] copiedRequests = targets.Select(request =>
            new SilenceSplitRequest(copies[request.Element], request.Ranges.ToArray())).ToArray();

        progress?.Report(0);
        return await Task.Run(() =>
        {
            var applied = ApplyBatch(snapshot, copiedRequests, mode, includeLinked, cancellationToken, progress);
            HashSet<Element> initial = copies.Values.ToHashSet();
            Element[] pending = snapshot.Children.Where(e => !initial.Contains(e)).ToArray();
            // Detach in one notification, avoiding a removal scan for each generated clip.
            snapshot.Children.RemoveRange(snapshot.Children.Count - pending.Length, pending.Length);
            return new PreparedSplit(snapshot, copies, originals, pending, applied.Select(request => requestsByCopy[request.Element]).ToArray());
        }, cancellationToken).ConfigureAwait(false);
    }

    internal sealed class PreparedSplit(Scene snapshot, Dictionary<Element, Element> copies,
        HashSet<Element> originals, Element[] pending, SilenceSplitRequest[] applied)
    {
        private readonly HashSet<Element> _retained = snapshot.Children.ToHashSet();
        public IReadOnlyList<SilenceSplitRequest> Applied { get; } = applied;

        public bool MatchesSceneSettings(Scene scene)
            => snapshot.FrameRate.Value == SceneTimeRangeService.GetFrameRate(scene)
               && snapshot.SampleRate.Value == scene.GetSampleRate()
               && snapshot.ChannelLayout.Value == scene.GetSpeakerLayout();

        // The caller has revalidated the scene and owns the transaction on its document thread.
        public void Publish(Scene scene)
        {
            foreach (Element original in originals)
            {
                if (!_retained.Contains(copies[original])) scene.RemoveChild(original);
            }
            foreach ((Element original, Element copy) in copies)
            {
                if (!_retained.Contains(copy)) continue;
                original.Start.Value = copy.Start.Value;
                if (!originals.Contains(original)) continue;
                original.Length.Value = copy.Length.Value;
                if (copy.ExitTransition.Value == null) original.ExitTransition.Value = null;
            }
            scene.Groups.Replace(snapshot.Groups);
            scene.Children.AddRange(pending);
            scene.Duration.Value = snapshot.Duration.Value;
        }
    }
}
