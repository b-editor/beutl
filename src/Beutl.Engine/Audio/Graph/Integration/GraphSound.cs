using System;
using Beutl.Animation;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph.Integration;

public abstract class GraphSound : Animatable
{
    public static readonly CoreProperty<float> GainProperty;
    public static readonly CoreProperty<float> SpeedProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;

    private float _gain = 100f;
    private float _speed = 100f;
    private bool _isEnabled = true;
    private AudioGraph? _cachedGraph;
    private int _cacheVersion = -1;

    static GraphSound()
    {
        GainProperty = ConfigureProperty<float, GraphSound>(nameof(Gain))
            .Accessor(o => o.Gain, (o, v) => o.Gain = v)
            .DefaultValue(100f)
            .Register();

        SpeedProperty = ConfigureProperty<float, GraphSound>(nameof(Speed))
            .Accessor(o => o.Speed, (o, v) => o.Speed = v)
            .DefaultValue(100f)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, GraphSound>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();
    }

    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, Math.Max(0f, value));
    }

    public float Speed
    {
        get => _speed;
        set => SetAndRaise(SpeedProperty, ref _speed, Math.Max(0.1f, value));
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    protected abstract ISoundSource? GetSoundSource();

    protected virtual void BuildAudioGraph(AudioGraphBuilder builder, AudioNode outputNode)
    {
        // Default implementation - no additional processing
        // Derived classes can override to add custom processing
    }

    public AudioGraph GetOrBuildGraph()
    {
        if (_cachedGraph != null && _cacheVersion == GetHashCode())
            return _cachedGraph;

        var builder = new AudioGraphBuilder();

        try
        {
            // Build the basic graph
            AudioNode currentNode = BuildBasicGraph(builder);

            // Allow derived classes to add custom processing
            BuildAudioGraph(builder, currentNode);

            // Set the final output
            builder.SetOutput(currentNode);

            // Build and cache the graph
            _cachedGraph?.Dispose();
            _cachedGraph = builder.Build();
            _cacheVersion = GetHashCode();

            return _cachedGraph;
        }
        catch (Exception ex)
        {
            throw new AudioGraphBuildException($"Failed to build audio graph for {GetType().Name}", ex);
        }
    }

    private AudioNode BuildBasicGraph(AudioGraphBuilder builder)
    {
        var soundSource = GetSoundSource();
        if (soundSource == null)
            throw new AudioGraphBuildException("Sound source is not available");

        // Create source node
        var sourceNode = builder.AddNode(new SourceNode
        {
            Source = soundSource
        });

        // Create gain node with animation support
        var gainNode = builder.AddNode(new GainNode
        {
            Target = this,
            GainProperty = GainProperty
        });

        // Connect source to gain
        builder.Connect(sourceNode, gainNode);

        return gainNode;
    }

    public Pcm<Stereo32BitFloat> Render(TimeRange range, int sampleRate)
    {
        if (!IsEnabled)
        {
            // Return silence
            var sampleCount = (int)(range.Duration.TotalSeconds * sampleRate);
            return new Pcm<Stereo32BitFloat>(sampleRate, sampleCount);
        }

        var graph = GetOrBuildGraph();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cachedGraph?.Dispose();
            _cachedGraph = null;
        }
        
        base.Dispose(disposing);
    }

    public override int GetHashCode()
    {
        // Create a hash based on properties that affect the graph structure
        var hash = new HashCode();
        hash.Add(_gain);
        hash.Add(_speed);
        hash.Add(_isEnabled);
        hash.Add(GetSoundSource()?.GetHashCode() ?? 0);
        
        // Include animation state
        foreach (var animation in Animations)
        {
            hash.Add(animation.GetHashCode());
        }
        
        return hash.ToHashCode();
    }
}