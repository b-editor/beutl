using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Audio;

public sealed partial class FallbackSound : Sound, IFallback
{
    public partial class Resource
    {
        // A sound whose type did not load is silent.
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}

[FallbackType(typeof(FallbackSound))]
public abstract partial class Sound : EngineObject
{
    public Sound()
    {
        ScanProperties<Sound>();
        Effect.Value = new AudioEffectGroup();
    }

    public override CompositionTarget GetCompositionTarget() => CompositionTarget.Audio;

    [Display(Name = nameof(AudioStrings.Sound_OffsetPosition), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<MediaTime> OffsetPosition { get; } = Property.CreateExpression<MediaTime>();

    [Range(0, float.MaxValue)]
    [Display(Name = nameof(AudioStrings.Sound_Gain), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Gain { get; } = Property.CreateAnimatable(100f);

    [Range(-100, 100)]
    [Display(Name = nameof(AudioStrings.Sound_Pan), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Pan { get; } = Property.CreateAnimatable(0f);

    [Range(0, 100)]
    [Display(Name = nameof(AudioStrings.Sound_SurroundPan), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> SurroundPan { get; } = Property.CreateAnimatable(0f);

    [Range(0, 100)]
    [Display(Name = nameof(AudioStrings.Sound_CenterLevel), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> CenterLevel { get; } = Property.CreateAnimatable(0f);

    [Range(0, 100)]
    [Display(Name = nameof(AudioStrings.Sound_LfeLevel), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> LfeLevel { get; } = Property.CreateAnimatable(0f);

    [Range(0, float.MaxValue)]
    [Display(Name = nameof(AudioStrings.Sound_Speed), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(AudioStrings.AudioEffect), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<AudioEffect?> Effect { get; } = Property.CreateExpression<AudioEffect?>();

    public virtual void Compose(AudioContext context, Resource resource)
    {
        if (!IsEnabled.Value)
        {
            return;
        }

        var soundSource = resource.GetSoundSource();
        // SampleRate <= 0 marks an unreadable source (e.g. a video-only file, #2183); composing a
        // ResampleNode at zero rate would throw, so emit silence instead.
        if (soundSource == null || soundSource.SampleRate <= 0)
        {
            AudioReadScope.AddUnavailableSource(context, TimeRange.Value);
            return;
        }

        var sourceNode = context.CreateSourceNode(soundSource);

        var shiftNode = context.CreateShiftNode(OffsetPosition.Value);
        context.Connect(sourceNode, shiftNode);

        var resampleNode = context.CreateResampleNode(soundSource.SampleRate);
        context.Connect(shiftNode, resampleNode);

        var speedNode = context.CreateSpeedNode(Speed, soundSource.ChannelLayout.ChannelCount);
        context.Connect(resampleNode, speedNode);

        var pannerNode = context.CreatePannerNode(Pan, SurroundPan, CenterLevel, LfeLevel, soundSource.ChannelLayout);
        context.Connect(speedNode, pannerNode);

        var gainNode = context.CreateGainNode(Gain);
        context.Connect(pannerNode, gainNode);

        AudioNode currentNode = gainNode;

        if (Effect.Value != null && Effect.Value.IsEnabled.Value)
        {
            currentNode = Effect.Value.CreateNode(context, currentNode);
        }

        var clipNode = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    public partial class Resource
    {
        public abstract SoundSource.Resource? GetSoundSource();
    }
}
