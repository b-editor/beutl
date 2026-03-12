using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Audio;

public sealed partial class FallbackSound : Sound, IFallback;

[FallbackType(typeof(FallbackSound))]
public abstract partial class Sound : EngineObject
{
    public Sound()
    {
        ScanProperties<Sound>();
        Effect.CurrentValue = new AudioEffectGroup();
    }

    public override CompositionTarget GetCompositionTarget() => CompositionTarget.Audio;

    [Display(Name = nameof(AudioStrings.Sound_OffsetPosition), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<TimeSpan> OffsetPosition { get; } = Property.Create<TimeSpan>();

    [Range(0, float.MaxValue)]
    [Display(Name = nameof(AudioStrings.Sound_Gain), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Gain { get; } = Property.CreateAnimatable(100f);

    [Range(0, float.MaxValue)]
    [Display(Name = nameof(AudioStrings.Sound_Speed), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(AudioStrings.AudioEffect), ResourceType = typeof(AudioStrings))]
    public IProperty<AudioEffect?> Effect { get; } = Property.Create<AudioEffect?>();

    public virtual void Compose(AudioContext context, Resource resource)
    {
        if (!IsEnabled)
        {
            context.Clear();
            return;
        }

        var soundSource = resource.GetSoundSource();
        if (soundSource == null)
        {
            context.Clear();
            return;
        }

        // Create source node
        var sourceNode = context.CreateSourceNode(soundSource);

        var shiftNode = context.CreateShiftNode(OffsetPosition.CurrentValue);
        context.Connect(sourceNode, shiftNode);

        var resampleNode = context.CreateResampleNode(soundSource.SampleRate);
        context.Connect(shiftNode, resampleNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(resampleNode, speedNode);

        // Create gain node with animation support
        var gainNode = context.CreateGainNode(Gain);
        context.Connect(speedNode, gainNode);

        AudioNode currentNode = gainNode;

        // Add effect if present
        if (Effect.CurrentValue != null && Effect.CurrentValue.IsEnabled)
        {
            currentNode = Effect.CurrentValue.CreateNode(context, currentNode);
        }

        var clipNode = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    public partial class Resource
    {
        public abstract SoundSource.Resource? GetSoundSource();
    }
}
