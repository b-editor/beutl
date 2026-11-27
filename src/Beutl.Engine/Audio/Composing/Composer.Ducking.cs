using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;

namespace Beutl.Audio.Composing;

public partial class Composer
{
    private sealed class DuckingLayer(Guid id)
    {
        public Guid Id { get; } = id;
        public List<int> EntryIndices { get; } = [];

        // The top-level layers this layer detects on. A source that a SoundGroup mixes is replaced by the
        // group's layer, which owns the tap that reads it.
        public HashSet<Guid> Sources { get; } = [];

        // For each source layer, the ids the ducking nodes selected: the layer itself, or a child it mixes.
        public Dictionary<Guid, HashSet<Guid>> SourceRoutes { get; } = [];
        public List<DuckingLayer> Dependents { get; } = [];
        public Dictionary<Guid, AudioBuffer> SourcePeaks { get; } = [];
        public Dictionary<Guid, AudioBuffer> TapPeaks { get; } = [];
        public AudioBuffer? Peaks { get; set; }
        public int PendingSources { get; set; }
    }

    private static bool HasDucking(AudioNodeEntry entry) => entry.DuckingNodes.Length != 0;

    private void ProcessDucking(
        AudioProcessContext context, Func<int, Guid> layerId, IReadOnlyList<AudioNodeEntry> liveEntries,
        IEnumerable<AudioNodeEntry> tails, List<MixInput> buffers, bool isPlayback)
    {
        AudioNodeEntry[] entries = [.. liveEntries, .. tails];
        var layers = new Dictionary<Guid, DuckingLayer>();
        for (int i = 0; i < entries.Length; i++)
        {
            AudioNodeEntry entry = entries[i];
            Guid id = layerId(entry.Sound.ZIndex.Value);
            if (!layers.TryGetValue(id, out DuckingLayer? layer))
            {
                layers.Add(id, layer = new DuckingLayer(id));
            }
            layer.EntryIndices.Add(i);

            foreach (DuckingNode node in entry.DuckingNodes)
                layer.Sources.UnionWith(node.SourceLayerIds);
        }

        // A selected source that a SoundGroup mixes has no entry of its own, so the group's entry holds its tap.
        var providers = new Dictionary<Guid, DuckingLayer>();
        foreach (AudioNodeEntry entry in entries)
        {
            if (entry.LevelTaps.Length == 0) continue;

            DuckingLayer layer = layers[layerId(entry.Sound.ZIndex.Value)];
            foreach (LevelTapNode tap in entry.LevelTaps)
            {
                Guid sourceId = layerId(tap.ZIndex);
                if (sourceId != Guid.Empty)
                    providers.TryAdd(sourceId, layer);
            }
        }

        // Resolve every selected layer to the top-level layer that renders it.
        foreach (DuckingLayer layer in layers.Values)
        {
            var resolved = new HashSet<Guid>();
            var routes = new Dictionary<Guid, HashSet<Guid>>();
            foreach (Guid sourceId in layer.Sources)
            {
                Guid target;
                if (sourceId == layer.Id)
                    continue;
                if (layers.ContainsKey(sourceId))
                    target = sourceId;
                else if (providers.TryGetValue(sourceId, out DuckingLayer? provider) && provider.Id != layer.Id)
                    target = provider.Id;
                else
                    continue;

                resolved.Add(target);
                if (!routes.TryGetValue(target, out HashSet<Guid>? ids))
                    routes.Add(target, ids = []);
                ids.Add(sourceId);
            }

            layer.Sources.Clear();
            layer.Sources.UnionWith(resolved);
            foreach ((Guid target, HashSet<Guid> ids) in routes)
                layer.SourceRoutes.Add(target, ids);
        }

        var ready = new Queue<DuckingLayer>();
        foreach (DuckingLayer layer in layers.Values)
        {
            foreach (Guid sourceId in layer.Sources)
            {
                DuckingLayer source = layers[sourceId];
                if (ReachesLayer(source, layer.Id, layers))
                    continue;

                layer.PendingSources++;
                source.Dependents.Add(layer);
            }

            if (layer.PendingSources == 0)
                ready.Enqueue(layer);
        }

        int sampleCount = context.GetSampleCount();
        var outputs = new List<MixInput>?[entries.Length];
        try
        {
            while (ready.TryDequeue(out DuckingLayer? layer))
            {
                var sidechain = layer.SourcePeaks.Count == 0
                    ? null
                    : new DuckingNode.SidechainBlock(context.TimeRange.Start, context.SampleRate, layer.SourcePeaks);
                var layerBuffers = layer.Dependents.Count > 0 ? new List<MixInput>() : null;
                foreach (int index in layer.EntryIndices)
                {
                    AudioNodeEntry entry = entries[index];
                    var entryBuffers = outputs[index] = new List<MixInput>();
                    foreach (DuckingNode node in entry.DuckingNodes)
                        node.Sidechain = sidechain;
                    // Effects that read ahead pull one output block in several pieces; start their taps fresh so
                    // the segments describe this block (and the read-ahead past its end) only.
                    foreach (LevelTapNode tap in entry.LevelTaps)
                        tap.BeginBlock(context.TimeRange);
                    try
                    {
                        if (index < liveEntries.Count)
                            ProcessEntry(entry, context, isPlayback, entryBuffers);
                        else
                            DrainEntryTails(entry, entry.OutputNodes!, context, sampleCount, entryBuffers);
                    }
                    finally
                    {
                        foreach (DuckingNode node in entry.DuckingNodes)
                            node.Sidechain = null;
                    }
                    layerBuffers?.AddRange(entryBuffers);
                }

                if (layerBuffers is null) continue;

                layer.Peaks = GetLayerPeaks(layerBuffers);
                foreach (DuckingLayer dependent in layer.Dependents)
                {
                    if (dependent.SourceRoutes.TryGetValue(layer.Id, out HashSet<Guid>? sourceIds))
                    {
                        foreach (Guid sourceId in sourceIds)
                        {
                            AudioBuffer? peaks = sourceId == layer.Id
                                ? layer.Peaks
                                : GetTapPeaks(layer, entries, layerId, sourceId, context, sampleCount);
                            if (peaks is not null)
                                dependent.SourcePeaks.Add(sourceId, peaks);
                        }
                    }

                    if (--dependent.PendingSources == 0)
                        ready.Enqueue(dependent);
                }
            }

            // Render in dependency order, then add the outputs in the original mix order.
            foreach (List<MixInput>? output in outputs)
            {
                if (output is null) continue;
                buffers.AddRange(output);
                output.Clear();
            }
        }
        finally
        {
            foreach (DuckingLayer layer in layers.Values)
            {
                layer.Peaks?.Dispose();
                foreach (AudioBuffer peaks in layer.TapPeaks.Values)
                    peaks.Dispose();
            }

            foreach (List<MixInput>? output in outputs)
            {
                if (output is null) continue;
                foreach (MixInput input in output)
                    input.Buffer?.Dispose();
            }
        }
    }

    private AudioBuffer? GetLayerPeaks(List<MixInput> inputs)
    {
        // Detect on the layer's own output. The scene alignment delay is applied when the outputs are mixed,
        // so the delay lines must not advance here.
        List<MixInput> buffers = inputs.FindAll(input => input.Buffer is not null)
            .ConvertAll(input => new MixInput(input.Buffer, null, input.TrailingSilence));
        if (buffers.Count == 0) return null;
        using var mixed = buffers.Count > 1 ? MixBuffers(buffers, buffers[0].Buffer!.SampleCount) : null;
        AudioBuffer layer = mixed ?? buffers[0].Buffer!;
        var buffer = new AudioBuffer(layer.SampleRate, 1, layer.SampleCount);
        try
        {
            Span<float> peaks = buffer.GetChannelData(0);
            for (int channel = 0; channel < layer.ChannelCount; channel++)
            {
                ReadOnlySpan<float> samples = layer.GetChannelData(channel);
                for (int i = 0; i < peaks.Length; i++)
                {
                    float magnitude = MathF.Abs(samples[i]);
                    if (float.IsFinite(magnitude) && magnitude > peaks[i])
                        peaks[i] = magnitude;
                }
            }
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    // Builds the peaks of a layer that a SoundGroup mixes, from the taps placed on its child branches.
    private AudioBuffer? GetTapPeaks(
        DuckingLayer layer, AudioNodeEntry[] entries, Func<int, Guid> layerId, Guid sourceId,
        AudioProcessContext context, int sampleCount)
    {
        if (layer.TapPeaks.TryGetValue(sourceId, out AudioBuffer? cached))
            return cached;
        if (sampleCount == 0) return null;

        AudioBuffer? peaks = null;
        try
        {
            foreach (int index in layer.EntryIndices)
            {
                foreach (LevelTapNode tap in entries[index].LevelTaps)
                {
                    if (layerId(tap.ZIndex) != sourceId) continue;

                    foreach (LevelTapNode.PeakSegment segment in tap.Segments)
                    {
                        if (segment.Count == 0) continue;

                        MediaTime segmentEnd = segment.Start + MediaTime.FromSamples(segment.Count, segment.SampleRate);
                        if (segmentEnd <= context.TimeRange.Start || segment.Start >= context.TimeRange.End)
                            continue;

                        ReadOnlySpan<float> tapped = segment.Buffer.GetChannelData(0)[..segment.Count];
                        peaks ??= new AudioBuffer(context.SampleRate, 1, sampleCount);
                        Span<float> target = peaks.GetChannelData(0);
                        // The tap may have read a shifted window, so place its samples by time rather than by index.
                        long firstSample = (context.TimeRange.Start - segment.Start)
                            .ToSamples(segment.SampleRate, MidpointRounding.ToZero);
                        double step = (double)segment.SampleRate / context.SampleRate;
                        for (int i = 0; i < target.Length; i++)
                        {
                            long sample = firstSample + (long)(i * step);
                            if ((ulong)sample < (ulong)tapped.Length)
                            {
                                target[i] = Math.Max(target[i], tapped[(int)sample]);
                            }
                        }
                    }
                }
            }

            if (peaks is not null)
                layer.TapPeaks.Add(sourceId, peaks);
            return peaks;
        }
        catch
        {
            peaks?.Dispose();
            throw;
        }
    }

    // Discard each edge that leads back to its destination, preserving all other sources of cyclic layers.
    private static bool ReachesLayer(DuckingLayer source, Guid destination, Dictionary<Guid, DuckingLayer> layers)
    {
        if (source.Sources.Count == 0) return false;
        var visited = new HashSet<Guid> { source.Id };
        var pending = new Stack<DuckingLayer>();
        pending.Push(source);
        while (pending.TryPop(out DuckingLayer? layer))
        {
            foreach (Guid id in layer.Sources)
            {
                if (id == destination) return true;
                if (visited.Add(id) && layers.TryGetValue(id, out DuckingLayer? next))
                    pending.Push(next);
            }
        }
        return false;
    }
}
