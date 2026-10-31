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
        if (!scene.Children.Contains(element) || scene.IsElementLocked(element) || scene.Uri == null || ranges.Count == 0) return false;
        var group = includeLinked ? scene.Groups.FirstOrDefault(g => g.Contains(element.Id.Value)) : null;
        int groupIndex = group == null ? scene.Groups.Count : scene.Groups.IndexOf(group);
        Element[] originals = group == null ? [element] : scene.Children.Where(e => group.Contains(e.Id.Value)).ToArray();
        // Scene references, portals and extension callbacks can depend on live objects outside the
        // clip. They retain the existing Razor path, without limiting which clips can be edited.
        if (!originals.All(e => e.GetType() == typeof(Element)
            && e.Objects.All(o => o.GetType() == typeof(SourceSound) || o.GetType() == typeof(SourceVideo))))
        {
            progress?.Report(100);
            Apply(scene, element, ranges, mode, includeLinked);
            return true;
        }

        Rational frameRate = SceneTimeRangeService.GetFrameRate(scene);
        int sampleRate = scene.FindHierarchicalParent<Project>().GetSampleRate();
        SpeakerLayout channelLayout = scene.FindHierarchicalParent<Project>()?.ChannelLayout.Value ?? SpeakerLayout.Stereo;
        var project = new Project { FrameRate = { Value = frameRate }, SampleRate = { Value = sampleRate }, ChannelLayout = { Value = channelLayout } };
        var snapshot = new Scene { Uri = scene.Uri, Start = { Value = scene.Start.Value }, Duration = { Value = scene.Duration.Value } };
        project.Items.Add(snapshot);
        foreach (TimelineLayer layer in scene.Layers)
        {
            var copy = new TimelineLayer();
            CoreSerializer.PopulateFromJsonObject(copy, CoreSerializer.SerializeToJsonObject(layer));
            snapshot.Layers.Add(copy);
        }
        // Clone the handful of source clips while the document is stable. Thousands of later
        // regenerations, source-time calculations and ripple scans use only these private objects.
        var copies = new Dictionary<Element, Element>();
        foreach (Element original in originals)
        {
            var copy = new Element();
            CoreSerializer.PopulateFromJsonObject(copy, CoreSerializer.SerializeToJsonObject(original, new CoreSerializerOptions
            {
                Mode = CoreSerializationMode.EmbedReferencedObjects,
                EmbedsCopy = true
            }));
            copy.IsLocked.Value = scene.IsElementLocked(original);
            copies.Add(original, copy);
        }
        HashSet<int> layers = originals.Select(e => e.ZIndex.Value).ToHashSet();
        foreach (Element other in scene.Children.Where(e => layers.Contains(e.ZIndex.Value) && !copies.ContainsKey(e)))
        {
            // Ripple needs other clips' geometry and lock anchors, not their media or effects.
            copies.Add(other, new Element
            {
                Id = { Value = other.Id.Value },
                Start = { Value = other.Start.Value },
                Length = { Value = other.Length.Value },
                ZIndex = { Value = other.ZIndex.Value },
                IsLocked = { Value = scene.IsElementLocked(other) }
            });
        }
        snapshot.Children.AddRange(copies.Values);
        if (group != null) snapshot.Groups.Add(group);
        TimeRange[] cuts = ranges.ToArray();
        long changeVersion = history.ChangeVersion;
        bool changed = false;
        void OnEdited(object? sender, EventArgs args) => changed = true;
        scene.Edited += OnEdited;
        try
        {
            progress?.Report(0);
            Element[] pending = await Task.Run(() =>
            {
                ApplyCore(snapshot, copies[element], cuts, mode, includeLinked, cancellationToken: cancellationToken, progress: progress);
                HashSet<Element> initial = copies.Values.ToHashSet();
                Element[] added = snapshot.Children.Where(e => !initial.Contains(e)).ToArray();
                // Detach in one notification, avoiding a removal scan for each generated clip.
                snapshot.Children.RemoveRange(snapshot.Children.Count - added.Length, added.Length);
                return added;
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
            // Playback may have resumed while preparation yielded the UI. Relinquish its readers
            // again immediately before publishing, then recheck edits made during that pause.
            if (player != null) await player.Pause();
            cancellationToken.ThrowIfCancellationRequested();
            history.FlushPendingMutations();
            if (changed || history.ChangeVersion != changeVersion || frameRate != SceneTimeRangeService.GetFrameRate(scene)
                || sampleRate != scene.FindHierarchicalParent<Project>().GetSampleRate()
                || channelLayout != (scene.FindHierarchicalParent<Project>()?.ChannelLayout.Value ?? SpeakerLayout.Stereo)) return false;

            HashSet<Element> retained = snapshot.Children.ToHashSet();
            Element[] removed = originals.Where(e => !retained.Contains(copies[e])).ToArray();
            history.ExecuteInTransaction(() =>
            {
                if (group != null) scene.Groups.Remove(group);
                foreach (Element original in removed) scene.RemoveChild(original);
                scene.RemoveElementsFromGroups(removed.Select(e => e.Id.Value).ToArray());
                foreach ((Element original, Element copy) in copies)
                {
                    if (!retained.Contains(copy)) continue;
                    original.Start.Value = copy.Start.Value;
                    if (!originals.Contains(original)) continue;
                    original.Length.Value = copy.Length.Value;
                    if (copy.ExitTransition.Value == null) original.ExitTransition.Value = null;
                }
                if (group != null)
                {
                    scene.Groups.InsertRange(Math.Min(groupIndex, scene.Groups.Count), snapshot.Groups);
                }
                scene.Children.AddRange(pending);
                scene.Duration.Value = snapshot.Duration.Value;
            }, CommandNames.AutoSplitBySilence, cancellationToken: cancellationToken);
            return true;
        }
        finally { scene.Edited -= OnEdited; }
    }
}
