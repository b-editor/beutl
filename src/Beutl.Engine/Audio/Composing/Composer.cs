using System;
using System.Collections.Generic;
using System.Linq;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Math;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private readonly List<Sound> _sounds = new();
    private readonly List<AudioNode> _nodes = new();
    private readonly AnimationSampler _animationSampler = new();
    private readonly InstanceClock _instanceClock = new();
    private bool _disposed;

    public Composer()
    {
        SampleRate = 44100;
    }

    ~Composer()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public IClock Clock => _instanceClock;

    public int SampleRate { get; }

    public bool IsDisposed { get; private set; }

    public bool IsAudioRendering { get; private set; }

    public void AddSound(Sound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _sounds.Add(sound);
    }

    public void RemoveSound(Sound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _sounds.Remove(sound);
    }

    public void ClearSounds()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _sounds.Clear();
    }

    public void AddNode(AudioNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _nodes.Add(node);
    }

    public void RemoveNode(AudioNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _nodes.Remove(node);
    }

    public void ClearNodes()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _nodes.Clear();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void ComposeCore(AudioContext context)
    {
        // Default implementation: build nodes from sounds
        // Subclasses should override this to build custom audio graphs
        
        if (_sounds.Count == 0)
            return;
            
        // Create a mixer node if we have multiple sounds
        MixerNode? mixer = _sounds.Count > 1 ? context.CreateMixerNode() : null;
        
        foreach (var sound in _sounds.Where(s => s.IsVisible))
        {
            // Apply animations
            sound.ApplyAnimations(_instanceClock);
            
            // Get the sound's graph
            var soundGraph = sound.GetOrBuildGraph();
            if (soundGraph == null)
                continue;
                
            // Add the graph's output to context
            var outputNode = soundGraph.OutputNode;
            context.AddNode(outputNode);
            
            // Apply gain
            var gainNode = context.CreateGainNode(sound.Gain / 100f);
            context.Connect(outputNode, gainNode);
            
            if (mixer != null)
            {
                context.Connect(gainNode, mixer);
            }
            else
            {
                // Single sound, mark gain node as output
                context.MarkAsOutput(gainNode);
            }
        }
        
        if (mixer != null)
        {
            context.MarkAsOutput(mixer);
        }
    }

    public Pcm<Stereo32BitFloat>? Compose(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;
                _instanceClock.AudioStartTime = timeSpan;
                
                // Create audio context
                using var context = new AudioContext(SampleRate, 2);
                
                // Let subclass build the audio graph
                ComposeCore(context);
                
                // Build and process the graph
                var range = new TimeRange(timeSpan, TimeSpan.FromSeconds(1));
                return ComposeWithContext(context, range);
            }
            finally
            {
                IsAudioRendering = false;
            }
        }
        else
        {
            return default;
        }
    }

    private Pcm<Stereo32BitFloat> ComposeWithContext(AudioContext context, TimeRange range)
    {
        try
        {
            // Build the audio graph from context
            var graph = context.BuildGraph();
            
            // Create process context
            var sampleCount = (int)(range.Duration.TotalSeconds * context.SampleRate);
            var processContext = new AudioProcessContext(range, context.SampleRate, _animationSampler);
            
            // Process the graph
            using var outputBuffer = graph.Process(processContext);
            
            // Apply master effects
            ApplyMasterEffects(outputBuffer);
            
            // Convert to output format
            return ConvertToStereo32BitFloat(outputBuffer);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException("Failed to compose audio", ex);
        }
    }

    private static unsafe void MixPcmIntoBuffer(Pcm<Stereo32BitFloat> pcm, AudioBuffer buffer, float gain)
    {
        var pcmPtr = (Stereo32BitFloat*)pcm.Data;
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);
        
        var mixLength = Math.Min(pcm.NumSamples, buffer.SampleCount);
        
        for (int i = 0; i < mixLength; i++)
        {
            leftChannel[i] += pcmPtr[i].Left * gain;
            rightChannel[i] += pcmPtr[i].Right * gain;
        }
    }

    private static void ApplyMasterEffects(AudioBuffer buffer)
    {
        // Apply master limiter to prevent clipping
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var channelData = buffer.GetChannelData(ch);
            AudioMath.ApplyLimiter(channelData, 1.0f, 10.0f);
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

    protected virtual void OnDispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var sound in _sounds)
            {
                sound.Dispose();
            }
            _sounds.Clear();
            
            foreach (var node in _nodes)
            {
                node.Dispose();
            }
            _nodes.Clear();
        }
    }
}
