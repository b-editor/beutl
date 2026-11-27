using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;

namespace Beutl.Audio.Composing;

public partial class Composer
{
    private sealed class DuckingLayer(Guid id)
    {
        public Guid Id { get; } = id;
        public List<int> EntryIndices { get; } = [];
        public HashSet<Guid> Sources { get; } = [];
        public List<DuckingLayer> Dependents { get; } = [];
        public Dictionary<Guid, AudioBuffer> SourcePeaks { get; } = [];
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
            layer.Sources.Remove(id);
        }

        var ready = new Queue<DuckingLayer>();
        foreach (DuckingLayer layer in layers.Values)
        {
            foreach (Guid sourceId in layer.Sources)
            {
                if (!layers.TryGetValue(sourceId, out DuckingLayer? source) || ReachesLayer(source, layer.Id, layers))
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
                    if (layer.Peaks is { } peaks)
                        dependent.SourcePeaks.Add(layer.Id, peaks);
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
                layer.Peaks?.Dispose();

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
