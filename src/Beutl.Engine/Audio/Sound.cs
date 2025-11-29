using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Audio;

[SuppressResourceClassGeneration]
public abstract class Sound : EngineObject
{
    public Sound()
    {
        ScanProperties<Sound>();
    }

    [Range(0, float.MaxValue)]
    public IProperty<float> Gain { get; } = Property.CreateAnimatable(100f);

    [Range(0, float.MaxValue)]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    public IProperty<AudioEffect?> Effect { get; } = Property.Create<AudioEffect?>();

    public TimeSpan Duration { get; private set; }

    protected abstract ISoundSource? GetSoundSource();

    public virtual void Compose(AudioContext context)
    {
        var soundSource = GetSoundSource();
        if (soundSource == null)
        {
            context.Clear();
            return;
        }

        // Create source node
        var sourceNode = context.CreateSourceNode(soundSource);

        var resampleNode = context.CreateResampleNode(soundSource.SampleRate);
        context.Connect(sourceNode, resampleNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(resampleNode, speedNode);

        // Create gain node with animation support
        var gainNode = context.CreateGainNode(Gain);
        context.Connect(speedNode, gainNode);

        AudioNode currentNode = gainNode;

        // Add effect if present
        if (Effect.CurrentValue != null && Effect.CurrentValue.IsEnabled)
        {
            var effectNode = context.CreateEffectNode(Effect.CurrentValue);
            context.Connect(currentNode, effectNode);
            currentNode = effectNode;
        }

        var clipNode = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    public void Time(TimeSpan available)
    {
        Duration = TimeCore(available);
    }

    protected abstract TimeSpan TimeCore(TimeSpan available);
}
