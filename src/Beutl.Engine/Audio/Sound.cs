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
        MovePropertyAfter(Pan, Effect);
        MovePropertyAfter(SurroundPan, Pan);
        MovePropertyAfter(CenterLevel, SurroundPan);
        MovePropertyAfter(LfeLevel, CenterLevel);
        Effect.Value = new AudioEffectGroup();
    }

    public override CompositionTarget GetCompositionTarget() => CompositionTarget.Audio;

    // Groups and scenes feed the panner in the project layout.
    public virtual AudioChannelLayout GetClipLayout(AudioChannelLayout projectLayout, MediaTime time) => projectLayout;

    /// <summary>Whether <see cref="GetClipLayout"/> can answer differently at another time.</summary>
    public virtual bool HasTimeDependentClipLayout => false;

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

        var sourceNode = context.CreateSourceNode(soundSource, resource.GetChannelMap());
        var clipLayout = sourceNode.ClipLayout;

        var shiftNode = context.CreateShiftNode(OffsetPosition);
        context.Connect(sourceNode, shiftNode);

        var speedNode = context.CreateSpeedNode(Speed, clipLayout.ChannelCount);
        context.Connect(shiftNode, speedNode);

        ComposeOutputStages(context, speedNode, clipLayout, soundSource.SampleRate);
    }

    // What every kind of sound ends with, after input delivers its audio in inputLayout. Input at a source's own rate
    // keeps it through the effects, so that a ResamplingEffect can convert it the way it is asked to, and whatever is
    // left at the source's rate is converted after them.
    private protected void ComposeOutputStages(
        AudioContext context, AudioNode input, AudioChannelLayout inputLayout, int? sourceSampleRate = null)
    {
        context.ChainSampleRate = sourceSampleRate ?? context.SampleRate;
        var pannerNode = context.CreatePannerNode(Pan, SurroundPan, CenterLevel, LfeLevel, inputLayout);
        context.Connect(input, pannerNode);

        var gainNode = context.CreateGainNode(Gain);
        context.Connect(pannerNode, gainNode);

        AudioNode currentNode = gainNode;

        if (Effect.Value != null && Effect.Value.IsEnabled.Value)
        {
            currentNode = Effect.Value.CreateNode(context, currentNode);
        }

        if (sourceSampleRate.HasValue)
        {
            // After a ResamplingEffect the audio is at the project's rate already, and this passes it through.
            var resampleNode = context.CreateResampleNode(context.ChainSampleRate);
            context.Connect(currentNode, resampleNode);
            currentNode = resampleNode;
            context.ChainSampleRate = context.SampleRate;
        }

        // ClipNodeを作成（EffectがローカルTimeRangeを前提としているため）
        var clipNode = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    public partial class Resource
    {
        public abstract SoundSource.Resource? GetSoundSource();

        public virtual AudioChannelMap GetChannelMap() => AudioChannelMap.Auto;
    }
}
