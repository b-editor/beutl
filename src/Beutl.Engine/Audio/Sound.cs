using System;
using Beutl.Animation;
using Beutl.Audio.Graph.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Nodes;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.Audio;

public abstract class Sound : Renderable
{
    public static readonly CoreProperty<float> GainProperty;
    public static readonly CoreProperty<float> SpeedProperty;
    public static readonly CoreProperty<IAudioEffect?> EffectProperty;

    private float _gain = 100;
    private float _speed = 100;
    private IAudioEffect? _effect;

    static Sound()
    {
        GainProperty = ConfigureProperty<float, Sound>(nameof(Gain))
            .Accessor(o => o.Gain, (o, v) => o.Gain = v)
            .DefaultValue(100)
            .Register();

        SpeedProperty = ConfigureProperty<float, Sound>(nameof(Speed))
            .Accessor(o => o.Speed, (o, v) => o.Speed = v)
            .DefaultValue(100)
            .Register();

        EffectProperty = ConfigureProperty<IAudioEffect?, Sound>(nameof(Effect))
            .Accessor(o => o.Effect, (o, v) => o.Effect = v)
            .DefaultValue(null)
            .Register();

        AffectsRender<Sound>(GainProperty, EffectProperty);
    }

    public Sound()
    {
        Invalidated += OnInvalidated;
    }


    private void OnInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        // Notify any external cache that this sound has changed
        // The Composer will handle cache invalidation
    }

    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, value);
    }

    public float Speed
    {
        get => _speed;
        set => SetAndRaise(SpeedProperty, ref _speed, value);
    }

    public TimeSpan Duration { get; private set; }

    public IAudioEffect? Effect
    {
        get => _effect;
        set => SetAndRaise(EffectProperty, ref _effect, value);
    }


    protected abstract ISoundSource? GetSoundSource();

    protected virtual void BuildAudioGraph(AudioGraphBuilder builder, AudioNode currentNode)
    {
        // Default implementation - derived classes can override to customize
    }

    public virtual AudioNode Compose(AudioContext context)
    {
        var soundSource = GetSoundSource();
        if (soundSource == null)
            throw new AudioGraphBuildException("Sound source is not available");

        // Create source node
        var sourceNode = context.CreateSourceNode(soundSource);

        // Create gain node with animation support
        var gainNode = context.CreateGainNode(Gain / 100f);
        context.Connect(sourceNode, gainNode);

        AudioNode currentNode = gainNode;

        // Add effect if present
        if (_effect != null && _effect.IsEnabled)
        {
            var effectNode = context.CreateEffectNode(_effect);
            context.Connect(currentNode, effectNode);
            currentNode = effectNode;
        }

        return currentNode;
    }

    public AudioGraph BuildGraph()
    {
        var builder = new AudioGraphBuilder();

        try
        {
            var soundSource = GetSoundSource();
            if (soundSource == null)
                throw new AudioGraphBuildException("Sound source is not available");

            // Create source node
            var sourceNode = builder.AddNode(new SourceNode
            {
                Source = soundSource,
                SourceName = GetType().Name
            });

            // Create gain node with animation support
            var gainNode = builder.AddNode(new GainNode
            {
                Target = this,
                GainProperty = GainProperty
            });

            // Connect source to gain
            builder.Connect(sourceNode, gainNode);

            AudioNode currentNode = gainNode;

            // Add effect if present
            if (_effect != null && _effect.IsEnabled)
            {
                var effectNode = builder.AddNode(new EffectNode
                {
                    Effect = _effect
                });

                builder.Connect(currentNode, effectNode);
                currentNode = effectNode;
            }

            // Allow derived classes to add custom processing
            BuildAudioGraph(builder, currentNode);

            // Set the final output
            builder.SetOutput(currentNode);

            // Build the graph
            return builder.Build();
        }
        catch (Exception ex)
        {
            throw new AudioGraphBuildException($"Failed to build audio graph for {GetType().Name}", ex);
        }
    }

    public Pcm<Stereo32BitFloat> ToPcm(int sampleRate)
    {
        return Render(new TimeRange(TimeSpan.Zero, Duration), sampleRate);
    }

    public Pcm<Stereo32BitFloat> Render(TimeRange range, int sampleRate)
    {
        // Build a new graph for standalone rendering
        // For composed audio, the Composer will handle graph management
        using var graph = BuildGraph();
        var animationSampler = new AnimationSampler();

        // Prepare animations
        animationSampler.PrepareAnimations(this, range, sampleRate);

        // Create processing context
        var context = new AudioProcessContext(range, sampleRate, animationSampler);

        try
        {
            using var buffer = graph.Process(context);
            return ConvertToStereo32BitFloat(buffer);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException($"Failed to render audio for {GetType().Name}", ex);
        }
    }

    public void Render(IAudio audio)
    {
        // For compatibility with existing code
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        using var pcm = Render(range, audio.SampleRate);
        audio.Write(pcm);
    }

    private static unsafe Pcm<Stereo32BitFloat> ConvertToStereo32BitFloat(AudioBuffer buffer)
    {
        var pcm = new Pcm<Stereo32BitFloat>(buffer.SampleRate, buffer.SampleCount);
        var pcmPtr = (Stereo32BitFloat*)pcm.Data;

        if (buffer.ChannelCount == 1)
        {
            // Mono to stereo
            var monoChannel = buffer.GetChannelData(0);
            for (int i = 0; i < buffer.SampleCount; i++)
            {
                float sample = monoChannel[i];
                pcmPtr[i] = new Stereo32BitFloat(sample, sample);
            }
        }
        else if (buffer.ChannelCount >= 2)
        {
            // Stereo or multi-channel (take first two channels)
            var leftChannel = buffer.GetChannelData(0);
            var rightChannel = buffer.GetChannelData(1);
            for (int i = 0; i < buffer.SampleCount; i++)
            {
                pcmPtr[i] = new Stereo32BitFloat(leftChannel[i], rightChannel[i]);
            }
        }

        return pcm;
    }

    protected abstract void OnRecord(IAudio audio, TimeRange range);

    public void Time(TimeSpan available)
    {
        Duration = TimeCore(available);
    }

    protected abstract TimeSpan TimeCore(TimeSpan available);

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        // No need for UpdateTime - the graph system handles time management
    }

    internal int GetCacheKey()
    {
        return GetHashCode();
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_gain);
        hash.Add(_speed);
        hash.Add(_effect?.GetHashCode() ?? 0);
        hash.Add(GetSoundSource()?.GetHashCode() ?? 0);

        // Include animation state
        foreach (var animation in Animations)
        {
            hash.Add(animation.GetHashCode());
        }

        return hash.ToHashCode();
    }
}
