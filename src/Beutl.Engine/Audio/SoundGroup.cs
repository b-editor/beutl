using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(Strings.SoundGroup), ResourceType = typeof(Strings))]
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
            context.Clear();
            return;
        }

        // このSoundGroupが1-5秒で、処理範囲が0-2秒の場合、0-1秒はそのまま通して、1-2秒はSoundGroupの処理を加える必要がある
        // そのまま通す
        foreach (var child in r.Children)
        {
            var original = child.GetOriginal();
            if (original.TimeRange.Start < TimeRange.Start)
            {
                var internalContext = new AudioContext(context.SampleRate, context.ChannelCount);
                original.Compose(internalContext, child);
                foreach (AudioNode node in internalContext.Nodes)
                {
                    context.AddNode(node);
                }

                foreach (var outputNode in internalContext.GetOutputNodes())
                {
                    var shiftNode = context.CreateShiftNode(original.TimeRange.Start);
                    var clipNode2 = context.CreateClipNode(
                        original.TimeRange.Start, TimeRange.Start - original.TimeRange.Start);
                    context.Connect(outputNode, shiftNode);
                    context.Connect(shiftNode, clipNode2);
                    context.MarkAsOutput(clipNode2);
                }
            }

            if (original.TimeRange.End > TimeRange.End)
            {
                var internalContext = new AudioContext(context.SampleRate, context.ChannelCount);
                original.Compose(internalContext, child);
                foreach (AudioNode node in internalContext.Nodes)
                {
                    context.AddNode(node);
                }

                foreach (var outputNode in internalContext.GetOutputNodes())
                {
                    var shiftNode = context.CreateShiftNode(TimeRange.End);
                    var clipNode2 = context.CreateClipNode(
                        TimeRange.End, original.TimeRange.End - TimeRange.End);
                    context.Connect(outputNode, shiftNode);
                    context.Connect(shiftNode, clipNode2);
                    context.MarkAsOutput(clipNode2);
                }
            }
        }

        // SoundGroupの処理を加える
        var mixerNode = context.CreateMixerNode();

        foreach (var child in r.Children)
        {
            var original = child.GetOriginal();
            var internalContext = new AudioContext(context.SampleRate, context.ChannelCount);
            original.Compose(internalContext, child);
            foreach (AudioNode node in internalContext.Nodes)
            {
                context.AddNode(node);
            }

            // 各子要素の出力ノードにShiftNodeを挿入してMixerに接続
            foreach (var outputNode in internalContext.GetOutputNodes())
            {
                // ShiftNodeでSoundGroupのStartを加算して打ち消す
                var shiftNode = context.CreateShiftNode(TimeRange.Start);
                context.Connect(outputNode, shiftNode);
                context.Connect(shiftNode, mixerNode);
            }
        }

        AudioNode currentNode = mixerNode;

        // SoundGroup全体のGainを適用
        var gainNode = context.CreateGainNode(Gain);
        context.Connect(currentNode, gainNode);
        currentNode = gainNode;

        // SoundGroup全体のEffectを適用
        if (Effect.CurrentValue != null && Effect.CurrentValue.IsEnabled)
        {
            currentNode = Effect.CurrentValue.CreateNode(context, currentNode);
        }

        // ClipNodeを作成（EffectがローカルTimeRangeを前提としているため）
        var clipNode = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    public partial class Resource
    {
        private readonly PooledList<int> _childrenVersion = [];

        public List<Sound.Resource> Children { get; set; } = [];

        public override SoundSource.Resource? GetSoundSource() => null;

        partial void PreUpdate(SoundGroup obj, RenderContext context)
        {
            using var consumed = new PooledList<Sound.Resource>();
            if (context is ICompositionRenderContext ctx)
            {
                for (int i = ctx.Flow.Count - 1; i >= 0; i--)
                {
                    if (ctx.Flow[i] is Sound.Resource d)
                    {
                        consumed.Insert(0, d);
                        ctx.Flow.RemoveAt(i);
                    }
                }
            }

            bool changed = false;
            ResourceReconciler.ReconcileListFromFlow(
                context: context,
                property: obj.Children,
                consumed: consumed,
                field: Children,
                versions: _childrenVersion,
                changed: ref changed);

            if (changed)
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            for (int i = _childrenVersion.Count; i < Children.Count; i++)
            {
                Children[i].Dispose();
            }

            Children.Clear();
            _childrenVersion.Dispose();
        }
    }
}
