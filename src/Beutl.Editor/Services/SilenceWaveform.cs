using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Editor.Services;

/// <summary>Full-coverage, channel-preserving peak envelopes for silence analysis.</summary>
internal static class SilenceWaveform
{
    public static async IAsyncEnumerable<WaveformChunk> GetChunksAsync(
        Scene scene, Element element, int chunkCount, [EnumeratorCancellation] CancellationToken cancellationToken = default,
        Action<Scene>? observeScene = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkCount);
        TaskScheduler owner = SynchronizationContext.Current is null
            ? TaskScheduler.Current : TaskScheduler.FromCurrentSynchronizationContext();
        var range = element.Range;
        int sampleRate = scene.FindHierarchicalParent<Project>().GetSampleRate();
        SpeakerLayout channelLayout = scene.FindHierarchicalParent<Project>()?.ChannelLayout.Value ?? SpeakerLayout.Stereo;
        if (range.Duration <= MediaTime.Zero) yield break;
        long totalSamples = range.Duration.ToSamples(sampleRate, MidpointRounding.ToZero);
        if (totalSamples < chunkCount) yield break;

        // Capture on the document thread, before yielding. The serializer's visitor includes scene
        // references and sound groups without maintaining a second audio-dependency walker.
        bool canCopy = true;
        var timing = new Dictionary<Guid, (TimeRange Range, int Layer)>();
        var inspected = new HashSet<Scene>();
        var pending = new Queue<Scene>();
        pending.Enqueue(scene);
        while (pending.TryDequeue(out Scene? current))
        {
            if (inspected.Contains(current)) continue;
            CoreSerializer.Visit(current, visit =>
            {
                if (visit.Value is EngineObject obj)
                {
                    timing[obj.Id.Value] = (obj.TimeRange.Value, obj.ZIndex.Value);
                    // Expressions can read outside the saved scene graph. Keep their live lookup
                    // context and compose one window at a time on its owner, as for custom objects.
                    if (obj.Properties.OfType<IExpressionProperty>().Any(p => p.HasExpression))
                    {
                        canCopy = false;
                    }
                }
                if (visit.Value is Scene referenced)
                {
                    observeScene?.Invoke(referenced);
                    if (inspected.Add(referenced))
                        foreach (Scene target in SceneSnapshot.EnumerateReferenceTargets(referenced).OfType<Scene>())
                            if (!inspected.Contains(target)) pending.Enqueue(target);
                    for (var parent = visit.Parent; parent != null; parent = parent.Parent)
                        if (ReferenceEquals(parent.Value, referenced)) canCopy = false;
                }
                if (visit.Value is MediaSource { HasUri: false } or IFallback) canCopy = false;
                return false;
            });
        }
        JsonObject? json = null;
        if (canCopy)
        {
            try { json = CoreSerializer.SerializeToJsonObject(scene, new() { Mode = CoreSerializationMode.EmbedReferencedObjects }); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException) { }
        }
        bool isolated = false;
        if (json != null)
        {
            Uri? uri = scene.Uri;
            Guid elementId = element.Id.Value;
            Rational frameRate = SceneTimeRangeService.GetFrameRate(scene);
            Scene? snapshot = await Task.Run(() =>
            {
                try
                {
                    var copy = new Scene { Uri = uri };
                    CoreSerializer.PopulateFromJsonObject(copy, json, new() { BaseUri = uri });
                    var scenes = new HashSet<Scene>();
                    if (CoreSerializer.Visit(copy, visit =>
                    {
                        if (visit.Value is Scene referenced) scenes.Add(referenced);
                        // Flow children can have evaluated timing that is not persisted unless
                        // they are time anchors. Keep the captured composition window intact.
                        if (visit.Value is EngineObject obj && timing.TryGetValue(obj.Id.Value, out var time))
                        {
                            obj.TimeRange.Value = time.Range;
                            obj.ZIndex.Value = time.Layer;
                        }
                        return visit.Value is IFallback;
                    })) return null;
                    var project = new Project { FrameRate = { Value = frameRate }, SampleRate = { Value = sampleRate }, ChannelLayout = { Value = channelLayout } };
                    foreach (Scene referenced in scenes) project.Items.Add(referenced);
                    return copy;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException) { return null; }
            }, cancellationToken).ConfigureAwait(false);
            if (snapshot != null)
            {
                scene = snapshot;
                element = snapshot.Children.Single(e => e.Id.Value == elementId);
                isolated = true;
            }
        }

        using var composer = new Composer { SampleRate = sampleRate, ChannelLayout = channelLayout.ToAudioChannelLayout() };
        SceneCompositor compositor = isolated ? new SceneCompositor(scene) { ForceOriginalSource = true }
            : await Task.Factory.StartNew(() => new SceneCompositor(scene) { ForceOriginalSource = true },
                cancellationToken, TaskCreationOptions.DenyChildAttach, owner).ConfigureAwait(false);
        try
        {
            // Compose about one second per dispatch, then reduce it into small envelope bins. This
            // avoids tens of thousands of dispatcher hops and preserves state across contiguous blocks.
            int batchSize = Math.Max(1, (int)Math.Min(chunkCount, sampleRate * (long)chunkCount / totalSamples));
            for (int first = 0; first < chunkCount; first += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int from = first;
                int to = Math.Min(chunkCount, first + batchSize);
                long start = Position(from);
                long end = Position(to);
                int count = checked((int)(end - start));
                var window = new TimeRange(range.Start + MediaTime.FromSamples(start, sampleRate),
                    AudioProcessContext.GetDurationForSampleCount(count, sampleRate));
                AudioBuffer? ComposeWindow()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var reads = new AudioReadScope();
                    var buffer = composer.Compose(window, compositor.EvaluateElementAudio(element, window));
                    if (!reads.HasFailures && !cancellationToken.IsCancellationRequested) return buffer;
                    buffer?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidDataException("Audio could not be read.");
                }
                // A cyclic, incomplete or custom object need not round-trip. Preserve its normal
                // composition on the owning thread; never let fallback analysis race live edits.
                using var buffer = await (isolated
                    ? ComposeThread.Dispatcher.InvokeAsync(ComposeWindow, DispatchPriority.Low, cancellationToken)
                    : Task.Factory.StartNew(ComposeWindow, cancellationToken, TaskCreationOptions.DenyChildAttach, owner)).ConfigureAwait(false);
                var result = new WaveformChunk[to - from];
                for (int i = from; i < to; i++)
                {
                    int begin = checked((int)(Position(i) - start));
                    int finish = checked((int)(Position(i + 1) - start));
                    float min = float.PositiveInfinity;
                    float max = float.NegativeInfinity;
                    if (buffer != null && buffer.SampleCount >= finish)
                    {
                        // Averaging channels would label audible opposite-phase stereo as silence.
                        for (int channel = 0; channel < buffer.ChannelCount; channel++)
                        {
                            var data = buffer.GetChannelData(channel);
                            for (int sample = begin; sample < finish; sample++)
                            {
                                min = Math.Min(min, data[sample]);
                                max = Math.Max(max, data[sample]);
                            }
                        }
                    }
                    result[i - from] = new WaveformChunk(i, min, max);
                }
                foreach (var chunk in result) yield return chunk;
            }
        }
        finally
        {
            if (isolated) compositor.Dispose();
            else await Task.Factory.StartNew(compositor.Dispose, CancellationToken.None, TaskCreationOptions.DenyChildAttach, owner).ConfigureAwait(false);
        }

        long Position(int index) => (long)((Int128)index * totalSamples / chunkCount);
    }
}
