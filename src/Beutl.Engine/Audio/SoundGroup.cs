using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Audio;

[Display(Name = nameof(Strings.Group), ResourceType = typeof(Strings))]
public sealed partial class SoundGroup : Sound
{
    public SoundGroup()
    {
        ScanProperties<SoundGroup>();
    }

    public IListProperty<Sound> Children { get; } = Property.CreateList<Sound>();

    protected override SoundSource? GetSoundSource() => null;

    public override void Compose(AudioContext context)
    {
        if (Children.Count == 0)
        {
            context.Clear();
            return;
        }

        AudioNode currentNode;

        if (Children.Count == 1)
        {
            // 子要素が1つの場合
            Children[0].Compose(context);
            var childOutput = context.GetOutputNodes().First();

            // ShiftNodeでSoundGroupのStartを加算して打ち消す
            var shiftNode = context.CreateShiftNode(TimeRange.Start);
            context.Connect(childOutput, shiftNode);
            currentNode = shiftNode;
        }
        else
        {
            // 複数の子要素をMixerNodeでミックス
            var mixerNode = context.CreateMixerNode();

            foreach (var child in Children)
            {
                child.Compose(context);

                // 各子要素の出力ノードにShiftNodeを挿入してMixerに接続
                foreach (var outputNode in context.GetOutputNodes().ToArray())
                {
                    // ShiftNodeでSoundGroupのStartを加算して打ち消す
                    var shiftNode = context.CreateShiftNode(TimeRange.Start);
                    context.Connect(outputNode, shiftNode);
                    context.Connect(shiftNode, mixerNode);
                }
            }

            currentNode = mixerNode;
        }

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
}
