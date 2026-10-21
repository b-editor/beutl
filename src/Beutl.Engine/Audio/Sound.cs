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
    public IExpressionProperty<TimeSpan> OffsetPosition { get; } = Property.CreateWithExpression<TimeSpan>();

    [Range(0, float.MaxValue)]
    [Display(Name = nameof(AudioStrings.Sound_Gain), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Gain { get; } = Property.CreateAnimatable(100f);

    [Range(0, float.MaxValue)]
    [Display(Name = nameof(AudioStrings.Sound_Speed), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(AudioStrings.AudioEffect), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<AudioEffect?> Effect { get; } = Property.CreateWithExpression<AudioEffect?>();

    public virtual void Compose(AudioContext context, Resource resource)
    {
        if (!IsEnabled)
        {
            return;
        }

        var soundSource = resource.GetSoundSource();
        // SampleRate <= 0 marks an unreadable source (e.g. a video-only file, #2183); composing a
        // ResampleNode at zero rate would throw, so emit silence instead.
        if (soundSource == null || soundSource.SampleRate <= 0)
        {
            return;
        }

        var sourceNode = context.CreateSourceNode(soundSource);

        var shiftNode = context.CreateShiftNode(OffsetPosition.CurrentValue);
        context.Connect(sourceNode, shiftNode);

        var resampleNode = context.CreateResampleNode(soundSource.SampleRate);
        context.Connect(shiftNode, resampleNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(resampleNode, speedNode);

        var gainNode = context.CreateGainNode(Gain);
        context.Connect(speedNode, gainNode);

        AudioNode currentNode = gainNode;

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
