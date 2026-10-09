using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;

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

    private void DrainEntryTails(
        AudioNodeEntry entry,
        AudioNode[] outputNodes,
        AudioProcessContext context,
        int sampleCount,
        List<AudioBuffer> buffers)
    {
        foreach (AudioNode outputNode in outputNodes)
        {
            int outputLatency = GetOutputLatency(entry, outputNode, SampleRate);
            if (outputLatency <= 0)
                continue;

            buffers.Add(FlushTail(outputNode, context, outputLatency, sampleCount, out int drainedSamples));
            RecordTailAfterDrain(entry, outputNode, outputLatency, drainedSamples);
        }
    }

    private AudioBuffer FlushTail(
        AudioNode outputNode,
        AudioProcessContext context,
        int outputLatency,
        int sampleCount,
        out int drainedSamples)
    {
        drainedSamples = Math.Min(outputLatency, sampleCount);
        return AudioLatency.FlushIntoWindow(outputNode, context, drainedSamples, sampleCount);
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
            AudioLatency.ThrowIfNegative(outputNode, outputLatency, "total latency");

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
                AudioLatency.SaturatingAdd(branch.LatencySamples, branch.DownstreamLatencySamples), branch.PaddingSamples);
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
                AudioLatency.ThrowIfNegative(clipNode, latency, "drain latency");

                branches.Add(new InlineDrainBranch(
                    latency,
                    clipNode.InlineDrainedSamples,
                    clipNode.InlinePaddingSamples,
                    downstreamLatency,
                    outputScale));
                return;
            }

            int ownLatency = node.GetLatencySamples(sampleRate);
            AudioLatency.ThrowIfNegative(node, ownLatency, "latency");

            int nextDownstreamLatency = AudioLatency.SaturatingAdd(downstreamLatency, ownLatency);
            int nextSampleRate = sampleRate;
            double nextOutputScale = outputScale;
            if (node is ResampleNode resampleNode)
            {
                nextDownstreamLatency = AudioLatency.ScaleSampleCount(
                    nextDownstreamLatency,
                    sampleRate,
                    resampleNode.SourceSampleRate);
                nextSampleRate = resampleNode.SourceSampleRate;
                nextOutputScale = MultiplyScale(
                    outputScale,
                    sampleRate / (double)resampleNode.SourceSampleRate);
            }
            else if (node is IAudioTimeMappingNode speedNode)
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
        if (TryGetBudgetedLatency(entry, outputNode, sampleRate, out int budgeted))
            return budgeted;

        int latency = outputNode.GetTotalLatencySamples(sampleRate);
        AudioLatency.ThrowIfNegative(outputNode, latency, "total latency");
        return latency;
    }

    private int GetDrainOutputLatency(AudioNodeEntry entry, AudioNode outputNode, int sampleRate)
    {
        if (TryGetBudgetedLatency(entry, outputNode, sampleRate, out int budgeted))
            return budgeted;

        int latency = outputNode.GetDrainLatencySamples(sampleRate);
        AudioLatency.ThrowIfNegative(outputNode, latency, "drain latency");
        return latency;
    }

    // Once part of an output's tail has been drained, its recorded budget replaces the node's own report.
    private bool TryGetBudgetedLatency(AudioNodeEntry entry, AudioNode outputNode, int sampleRate, out int latency)
    {
        if (entry.TailBudgets.TryGetValue(outputNode, out var budget))
        {
            if (budget.StopFurtherDrains)
            {
                latency = 0;
                return true;
            }

            if (budget.UnknownFollowUpPending)
            {
                latency = int.MaxValue;
                return true;
            }

            if (budget.IsKnown)
            {
                latency = AudioLatency.ScaleSampleCount(budget.RemainingSamples, SampleRate, sampleRate);
                return true;
            }
        }

        latency = 0;
        return false;
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

    private static int SubtractTail(int latency, int samples)
    {
        if (latency == int.MaxValue)
            return int.MaxValue;

        return Math.Max(0, latency - samples);
    }
}
