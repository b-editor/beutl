using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public partial class Composer
{
    internal sealed class TailBudget
    {
        public int RemainingSamples { get; set; }
        public bool IsKnown { get; set; }
        public bool StopFurtherDrains { get; set; }
        public bool UnknownFollowUpPending { get; set; }
    }

    private AudioBuffer FlushTail(
        AudioNode outputNode,
        AudioProcessContext context,
        int outputLatency,
        int sampleCount,
        out int drainedSamples)
    {
        drainedSamples = Math.Min(outputLatency, sampleCount);
        AudioProcessContext drainContext = drainedSamples == sampleCount
            ? context
            : new AudioProcessContext(
                new TimeRange(
                    context.TimeRange.Start,
                    AudioProcessContext.GetDurationForSampleCount(drainedSamples, SampleRate)),
                SampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

        using AudioBuffer drained = outputNode.Flush(drainContext);
        var output = new AudioBuffer(drained.SampleRate, drained.ChannelCount, sampleCount);
        try
        {
            int copyCount = Math.Min(drained.SampleCount, sampleCount);
            if (copyCount > 0)
            {
                drained.CopyTo(output, 0, copyCount);
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private void RecordInlineDrainBudget(AudioNodeEntry entry, AudioNode[] outputNodes)
    {
        bool hasInlineDrain = outputNodes.Any(
            outputNode => TryGetInlineDrain(outputNode, SampleRate, out _));
        if (!hasInlineDrain)
            return;

        foreach (AudioNode outputNode in outputNodes)
        {
            int outputLatency = outputNode.GetDrainLatencySamples(SampleRate);
            if (outputLatency < 0)
            {
                throw new InvalidOperationException(
                    $"{outputNode.GetType().Name} returned negative total latency {outputLatency}.");
            }

            bool inlineDrainAttempted = TryGetInlineDrain(
                outputNode,
                SampleRate,
                out int inlineDrain,
                outputLatency);
            if (!inlineDrainAttempted)
                continue;

            if (outputLatency == int.MaxValue)
                inlineDrain = 0;
            else
                inlineDrain = Math.Min(outputLatency, inlineDrain);
            SetTailBudget(
                entry,
                outputNode,
                outputLatency,
                inlineDrain,
                allowUnknownFollowUp: inlineDrainAttempted);
        }
    }

    private static bool TryGetInlineDrain(
        AudioNode outputNode,
        int sampleRate,
        out int inlineDrain,
        int outputLatency = 0)
    {
        var branches = new List<InlineDrainBranch>();
        CollectInlineDrainBranches(
            outputNode,
            sampleRate,
            branches,
            new HashSet<AudioNode>(ReferenceEqualityComparer.Instance));
        if (branches.Count == 0)
        {
            inlineDrain = 0;
            return false;
        }

        int remainingLatency = 0;
        foreach (InlineDrainBranch branch in branches)
        {
            int branchRemaining = SubtractTail(
                AddLatency(branch.LatencySamples, branch.DownstreamLatencySamples), branch.PaddingSamples);
            if (branchRemaining == int.MaxValue)
            {
                remainingLatency = int.MaxValue;
                inlineDrain = 0;
                break;
            }

            remainingLatency = Math.Max(
                remainingLatency,
                ScaleByFactor(branchRemaining, branch.OutputScale));
        }

        inlineDrain = remainingLatency == int.MaxValue
            ? 0
            : Math.Max(0, outputLatency - remainingLatency);
        return true;
    }

    private sealed record InlineDrainBranch(
        int LatencySamples,
        int DrainedSamples,
        int PaddingSamples,
        int DownstreamLatencySamples,
        double OutputScale);

    private static void CollectInlineDrainBranches(
        AudioNode node,
        int sampleRate,
        List<InlineDrainBranch> branches,
        HashSet<AudioNode> visited,
        int downstreamLatency = 0,
        double outputScale = 1d)
    {
        if (!visited.Add(node))
            return;

        try
        {
            if (node is ClipNode { InlineDrainAttempted: true } clipNode)
            {
                int latency = clipNode.GetDrainLatencySamples(sampleRate);
                if (latency < 0)
                {
                    throw new InvalidOperationException(
                        $"{clipNode.GetType().Name} returned negative drain latency {latency}.");
                }

                branches.Add(new InlineDrainBranch(
                    latency,
                    clipNode.InlineDrainedSamples,
                    clipNode.InlinePaddingSamples,
                    downstreamLatency,
                    outputScale));
                return;
            }

            int ownLatency = node.GetLatencySamples(sampleRate);
            if (ownLatency < 0)
            {
                throw new InvalidOperationException(
                    $"{node.GetType().Name} returned negative latency {ownLatency}.");
            }

            int nextDownstreamLatency = AddLatency(downstreamLatency, ownLatency);
            int nextSampleRate = sampleRate;
            double nextOutputScale = outputScale;
            if (node is ResampleNode resampleNode)
            {
                nextDownstreamLatency = ScaleSampleCount(
                    nextDownstreamLatency,
                    sampleRate,
                    resampleNode.SourceSampleRate);
                nextSampleRate = resampleNode.SourceSampleRate;
                nextOutputScale = MultiplyScale(
                    outputScale,
                    sampleRate / (double)resampleNode.SourceSampleRate);
            }
            else if (node is SpeedNode speedNode)
            {
                if (speedNode.TryGetDrainSpeedFactor(sampleRate, out double drainSpeed))
                {
                    nextDownstreamLatency = ScaleByFactor(nextDownstreamLatency, drainSpeed);
                    nextOutputScale = MultiplyScale(outputScale, 1d / drainSpeed);
                }
                else
                {
                    nextDownstreamLatency = int.MaxValue;
                    nextOutputScale = double.PositiveInfinity;
                }
            }

            foreach (AudioNode input in node.Inputs)
                CollectInlineDrainBranches(
                    input,
                    nextSampleRate,
                    branches,
                    visited,
                    nextDownstreamLatency,
                    nextOutputScale);
        }
        finally
        {
            // The same descendant can feed multiple fan-in paths. Keep cycle detection local to the
            // current recursion path so each distinct path contributes its downstream latency.
            visited.Remove(node);
        }
    }

    private static int AddLatency(int first, int second)
    {
        if (first == int.MaxValue || second == int.MaxValue)
            return int.MaxValue;

        long sum = (long)first + second;
        return sum >= int.MaxValue ? int.MaxValue : (int)sum;
    }

    private static int ScaleByFactor(int sampleCount, double factor)
    {
        if (sampleCount == int.MaxValue)
            return int.MaxValue;
        if (sampleCount == 0)
            return 0;
        if (!double.IsFinite(factor) || factor <= 0)
            return int.MaxValue;

        double scaled = sampleCount * factor;
        return !double.IsFinite(scaled) || scaled >= int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(scaled);
    }

    private static double MultiplyScale(double first, double second)
    {
        double product = first * second;
        return double.IsFinite(product) && product > 0
            ? product
            : double.PositiveInfinity;
    }

    private int GetEntryLatency(AudioNodeEntry entry, AudioNode[] outputNodes, int sampleRate)
    {
        int latency = 0;
        foreach (AudioNode outputNode in outputNodes)
        {
            latency = Math.Max(latency, GetOutputLatency(entry, outputNode, sampleRate));
        }

        return latency;
    }

    private int GetOutputLatency(AudioNodeEntry entry, AudioNode outputNode, int sampleRate)
    {
        if (entry.TailBudgets.TryGetValue(outputNode, out var budget))
        {
            if (budget.StopFurtherDrains)
                return 0;
            if (budget.UnknownFollowUpPending)
                return int.MaxValue;
            if (budget.IsKnown)
                return ScaleSampleCount(budget.RemainingSamples, SampleRate, sampleRate);
        }

        int latency = outputNode.GetTotalLatencySamples(sampleRate);
        if (latency < 0)
        {
            throw new InvalidOperationException(
                $"{outputNode.GetType().Name} returned negative total latency {latency}.");
        }

        return latency;
    }

    private int GetDrainOutputLatency(AudioNodeEntry entry, AudioNode outputNode, int sampleRate)
    {
        if (entry.TailBudgets.TryGetValue(outputNode, out var budget))
        {
            if (budget.StopFurtherDrains)
                return 0;
            if (budget.UnknownFollowUpPending)
                return int.MaxValue;
            if (budget.IsKnown)
                return ScaleSampleCount(budget.RemainingSamples, SampleRate, sampleRate);
        }

        int latency = outputNode.GetDrainLatencySamples(sampleRate);
        if (latency < 0)
        {
            throw new InvalidOperationException(
                $"{outputNode.GetType().Name} returned negative drain latency {latency}.");
        }

        return latency;
    }

    private static void RecordTailAfterDrain(
        AudioNodeEntry entry,
        AudioNode outputNode,
        int outputLatency,
        int drainedSamples)
    {
        SetTailBudget(entry, outputNode, outputLatency, drainedSamples);
    }

    private static void SetTailBudget(
        AudioNodeEntry entry,
        AudioNode outputNode,
        int latency,
        int drainedSamples,
        bool allowUnknownFollowUp = false)
    {
        var budget = entry.TailBudgets.TryGetValue(outputNode, out var existing)
            ? existing
            : new TailBudget();
        budget.IsKnown = true;
        if (latency == int.MaxValue)
        {
            bool preserveUnknownFollowUp = allowUnknownFollowUp
                || (drainedSamples == 0 && budget.UnknownFollowUpPending);
            budget.RemainingSamples = 0;
            budget.UnknownFollowUpPending = preserveUnknownFollowUp;
            budget.StopFurtherDrains = !preserveUnknownFollowUp;
        }
        else
        {
            budget.RemainingSamples = SubtractTail(latency, drainedSamples);
            budget.UnknownFollowUpPending = false;
            budget.StopFurtherDrains = false;
        }

        entry.TailBudgets[outputNode] = budget;
    }

    private static int ScaleSampleCount(int sampleCount, int sourceSampleRate, int destinationSampleRate)
    {
        if (sampleCount == int.MaxValue || sourceSampleRate == destinationSampleRate)
            return sampleCount;

        double scaled = sampleCount * (double)destinationSampleRate / sourceSampleRate;
        return scaled >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(scaled);
    }

    private static int SubtractTail(int latency, int samples)
    {
        if (latency == int.MaxValue)
            return int.MaxValue;

        return Math.Max(0, latency - samples);
    }
}
