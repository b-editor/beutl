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

    public virtual AudioNode Compose(AudioContext context)
    {
        var soundSource = GetSoundSource();
        if (soundSource == null)
            throw new AudioGraphBuildException("Sound source is not available");

        // Create source node
        var sourceNode = context.CreateSourceNode(soundSource);

        // Create gain node with animation support
        var gainNode = context.CreateGainNode(Gain / 100f, this, GainProperty);
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

    public void Time(TimeSpan available)
    {
        Duration = TimeCore(available);
    }

    protected abstract TimeSpan TimeCore(TimeSpan available);
}
