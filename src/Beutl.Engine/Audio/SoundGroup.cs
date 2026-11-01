using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(AudioStrings.SoundGroup), ResourceType = typeof(AudioStrings))]
public sealed partial class SoundGroup : Sound, IFlowOperator
{
    public SoundGroup()
    {
        ScanProperties<SoundGroup>();
        HideProperties(OffsetPosition, Speed);
    }

    [SuppressResourceClassGeneration]
    public IListProperty<Sound> Children { get; } = Property.CreateList<Sound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Children.Count == 0)
        {
            return;
        }

        // このSoundGroupが1-5秒で、処理範囲が0-2秒の場合、0-1秒はそのまま通して、1-2秒はSoundGroupの処理を加える必要がある
        // そのまま通す
        foreach (var child in r.Children)
        {
            Sound original = child.RequireOriginal();
            if (original.TimeRange.Value.Start < TimeRange.Value.Start)
            {
                PassThroughOutsideGroup(
                    context, original, child,
                    original.TimeRange.Value.Start, TimeRange.Value.Start - original.TimeRange.Value.Start);
            }

            if (original.TimeRange.Value.End > TimeRange.Value.End)
            {
                PassThroughOutsideGroup(
                    context, original, child,
                    TimeRange.Value.End, original.TimeRange.Value.End - TimeRange.Value.End);
            }
        }

        // SoundGroupの処理を加える
        var mixerNode = context.CreateMixerNode();

        foreach (var child in r.Children)
        {
            Sound original = child.RequireOriginal();

            foreach (var outputNode in ComposeChildInto(context, original, child))
            {
                // ShiftNodeでSoundGroupのStartを加算して打ち消す
                var shiftNode = context.CreateShiftNode(TimeRange.Value.Start);
                context.Connect(outputNode, shiftNode);
                context.Connect(shiftNode, mixerNode);
                // Let the mixer skip a child's already-recovered tail.
                mixerNode.SetBranchEndTime(shiftNode, original.TimeRange.Value.End - TimeRange.Value.Start);
            }
        }

        var pannerNode = context.CreatePannerNode(Pan, SurroundPan, CenterLevel, LfeLevel, context.ChannelLayout);
        context.Connect(mixerNode, pannerNode);
        AudioNode currentNode = pannerNode;

        var gainNode = context.CreateGainNode(Gain);
        context.Connect(currentNode, gainNode);
        currentNode = gainNode;

        if (Effect.Value != null && Effect.Value.IsEnabled.Value)
        {
            currentNode = Effect.Value.CreateNode(context, currentNode);
        }

        // ClipNodeを作成（EffectがローカルTimeRangeを前提としているため）
        var clipNode = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    // Composes the child in a private context that reuses the child's previous nodes, moves its nodes into
    // the group's context and returns the child's outputs for the caller to route.
    private static IEnumerable<AudioNode> ComposeChildInto(AudioContext context, Sound original, Sound.Resource child)
    {
        AudioContext internalContext = context.CreateChildContext();
        original.Compose(internalContext, child);
        foreach (AudioNode node in internalContext.Nodes)
        {
            context.AddNode(node);
        }

        return internalContext.GetOutputNodes();
    }

    // Routes the part of a child outside the group's range straight to the output, bypassing the group's
    // gain and effect; start is both the shift and the clip start.
    private static void PassThroughOutsideGroup(
        AudioContext context,
        Sound original,
        Sound.Resource child,
        MediaTime start,
        MediaTime duration)
    {
        foreach (var outputNode in ComposeChildInto(context, original, child))
        {
            var shiftNode = context.CreateShiftNode(start);
            var clipNode2 = context.CreateClipNode(start, duration);
            context.Connect(outputNode, shiftNode);
            context.Connect(shiftNode, clipNode2);
            context.MarkAsOutput(clipNode2);
        }
    }

    public partial class Resource
    {
        private readonly PooledList<int> _childrenVersion = [];

        public List<Sound.Resource> Children { get; set; } = [];

        public override SoundSource.Resource? GetSoundSource() => null;

        partial void PreReconcile(SoundGroup obj, CompositionContext context)
        {
            if (ResourceReconciler.ReconcileChildrenFromFlow(context, obj.Children, Children, _childrenVersion))
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            ResourceReconciler.ReleaseReconciledChildren(Children, _childrenVersion);
        }
    }
}
