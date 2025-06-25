# Audio Graph API - Basic Usage Examples

This document provides practical examples of how to use the new Audio Graph API in Beutl.

## Table of Contents

1. [Simple Audio Playback](#simple-audio-playback)
2. [Adding Gain Control](#adding-gain-control)
3. [Mixing Multiple Sources](#mixing-multiple-sources)
4. [Adding Effects](#adding-effects)
5. [Animation Integration](#animation-integration)
6. [Custom Graph Building](#custom-graph-building)

## Simple Audio Playback

The most basic usage involves creating a source node and processing it:

```csharp
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Audio.Graph.Animation;
using Beutl.Media;
using Beutl.Media.Source;

// Load an audio file
var audioFile = new SoundFile("path/to/audio.wav");

// Create graph builder
var builder = new AudioGraphBuilder();

// Add source node
var sourceNode = builder.AddNode(new SourceNode
{
    Source = audioFile
});

// Set as output
builder.SetOutput(sourceNode);

// Build the graph
using var graph = builder.Build();

// Create processing context
var timeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5));
var sampleRate = 44100;
var animationSampler = new AnimationSampler();
var context = new AudioProcessContext(timeRange, sampleRate, animationSampler);

// Process audio
using var result = graph.Process(context);

// Use the result buffer...
```

## Adding Gain Control

Add volume control with static gain:

```csharp
var builder = new AudioGraphBuilder();

// Add source
var sourceNode = builder.AddNode(new SourceNode { Source = audioFile });

// Add gain node with static gain (50% volume)
var gainNode = builder.AddNode(new GainNode { StaticGain = 0.5f });

// Connect nodes
builder.Connect(sourceNode, gainNode);
builder.SetOutput(gainNode);

using var graph = builder.Build();
```

## Mixing Multiple Sources

Combine multiple audio sources:

```csharp
var builder = new AudioGraphBuilder();

// Add multiple sources
var source1 = builder.AddNode(new SourceNode { Source = audioFile1 });
var source2 = builder.AddNode(new SourceNode { Source = audioFile2 });
var source3 = builder.AddNode(new SourceNode { Source = audioFile3 });

// Add individual gain controls
var gain1 = builder.AddNode(new GainNode { StaticGain = 0.7f });
var gain2 = builder.AddNode(new GainNode { StaticGain = 0.5f });
var gain3 = builder.AddNode(new GainNode { StaticGain = 0.8f });

// Connect sources to gains
builder.Connect(source1, gain1);
builder.Connect(source2, gain2);
builder.Connect(source3, gain3);

// Add mixer
var mixer = builder.AddNode(new MixerNode
{
    Gains = new float[] { 1.0f, 1.0f, 1.0f } // Equal mixing
});

// Connect gains to mixer
builder.Connect(gain1, mixer);
builder.Connect(gain2, mixer);
builder.Connect(gain3, mixer);

builder.SetOutput(mixer);
using var graph = builder.Build();
```

## Adding Effects

Apply audio effects to the signal chain:

```csharp
using Beutl.Audio.Graph.Effects;
using Beutl.Audio.Graph.Nodes;

var builder = new AudioGraphBuilder();

// Source and gain
var sourceNode = builder.AddNode(new SourceNode { Source = audioFile });
var gainNode = builder.AddNode(new GainNode { StaticGain = 0.8f });

// Create delay effect
var delayEffect = new GraphDelayEffect
{
    DelayTime = 250f,  // 250ms delay
    Feedback = 40f,    // 40% feedback
    Mix = 25f          // 25% wet signal
};

// Add effect node
var effectNode = builder.AddNode(new GraphEffectNode { Effect = delayEffect });

// Connect the chain
builder.Connect(sourceNode, gainNode);
builder.Connect(gainNode, effectNode);
builder.SetOutput(effectNode);

using var graph = builder.Build();
```

## Animation Integration

Use the Animation API with audio processing:

```csharp
// Create an animatable object for gain control
public class AnimatedGainControl : Animatable
{
    public static readonly CoreProperty<float> GainProperty;
    
    private float _gain = 100f;
    
    static AnimatedGainControl()
    {
        GainProperty = ConfigureProperty<float, AnimatedGainControl>(nameof(Gain))
            .Accessor(o => o.Gain, (o, v) => o.Gain = v)
            .DefaultValue(100f)
            .Register();
    }
    
    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, value);
    }
}

// Usage with animation
var gainControl = new AnimatedGainControl();

// Add keyframe animation (fade in over 2 seconds)
var animation = new KeyFrameAnimation<float>(AnimatedGainControl.GainProperty);
animation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.Zero, 0f));
animation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.FromSeconds(2), 100f));
gainControl.Animations.Add(animation);

// Create graph with animated gain
var builder = new AudioGraphBuilder();
var sourceNode = builder.AddNode(new SourceNode { Source = audioFile });
var gainNode = builder.AddNode(new GainNode 
{ 
    Target = gainControl,
    GainProperty = AnimatedGainControl.GainProperty
});

builder.Connect(sourceNode, gainNode);
builder.SetOutput(gainNode);

using var graph = builder.Build();

// Process with animation sampler
var animationSampler = new AnimationSampler();
animationSampler.PrepareAnimations(gainControl, timeRange, sampleRate);

var context = new AudioProcessContext(timeRange, sampleRate, animationSampler);
using var result = graph.Process(context);
```

## Custom Graph Building

Create complex custom processing graphs:

```csharp
public class CustomAudioProcessor
{
    public AudioGraph CreateComplexGraph(ISoundSource mainSource, ISoundSource backgroundSource)
    {
        var builder = new AudioGraphBuilder();
        
        // Main audio chain
        var mainSrc = builder.AddNode(new SourceNode { Source = mainSource });
        var mainGain = builder.AddNode(new GainNode { StaticGain = 0.8f });
        
        // Create compression effect
        var compressor = new GraphDelayEffect { /* configure compressor */ };
        var compressorNode = builder.AddNode(new GraphEffectNode { Effect = compressor });
        
        // Background audio chain
        var bgSrc = builder.AddNode(new SourceNode { Source = backgroundSource });
        var bgGain = builder.AddNode(new GainNode { StaticGain = 0.3f }); // Lower volume
        
        // Low-pass filter for background
        var bgFilter = new GraphDelayEffect { /* configure as low-pass */ };
        var bgFilterNode = builder.AddNode(new GraphEffectNode { Effect = bgFilter });
        
        // Connect main chain
        builder.Connect(mainSrc, mainGain);
        builder.Connect(mainGain, compressorNode);
        
        // Connect background chain  
        builder.Connect(bgSrc, bgGain);
        builder.Connect(bgGain, bgFilterNode);
        
        // Final mixer
        var finalMixer = builder.AddNode(new MixerNode 
        { 
            Gains = new float[] { 1.0f, 1.0f } 
        });
        
        builder.Connect(compressorNode, finalMixer);
        builder.Connect(bgFilterNode, finalMixer);
        
        // Master limiter
        var limiter = new GraphDelayEffect { /* configure as limiter */ };
        var limiterNode = builder.AddNode(new GraphEffectNode { Effect = limiter });
        
        builder.Connect(finalMixer, limiterNode);
        builder.SetOutput(limiterNode);
        
        return builder.Build();
    }
}
```

## Integration with Existing Beutl Classes

Use the new GraphSound classes that integrate with the existing system:

```csharp
using Beutl.Audio.Graph.Integration;

// Create a graph-based sound source
var graphSound = new GraphSourceSound
{
    Source = audioFile,
    Gain = 80f,  // 80% volume
    OffsetPosition = TimeSpan.FromSeconds(1) // Start 1 second in
};

// Add animations
var gainAnimation = new KeyFrameAnimation<float>(GraphSound.GainProperty);
gainAnimation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.Zero, 0f));
gainAnimation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.FromSeconds(3), 80f));
graphSound.Animations.Add(gainAnimation);

// Render directly
var timeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));
using var pcm = graphSound.Render(timeRange, 44100);

// Or use with composer
var composer = new AudioComposer();
var track = new SimpleAudioTrack(graphSound, timeRange, volume: 90f);
composer.AddTrack(track);

using var composedPcm = composer.Compose(timeRange, 44100);
```

## Performance Tips

1. **Reuse Graphs**: Cache and reuse AudioGraph instances when possible
2. **Proper Disposal**: Always dispose graphs and buffers when done
3. **Chunk Processing**: For large audio files, process in smaller chunks
4. **Animation Preparation**: Prepare animations once per processing session

```csharp
// Good: Reuse graph
var graph = sound.GetOrBuildGraph();
for (int i = 0; i < chunks.Length; i++)
{
    using var result = graph.Process(contexts[i]);
    // Process result...
}

// Good: Proper disposal
using var graph = builder.Build();
using var result = graph.Process(context);
// Automatically disposed at end of scope
```

## Error Handling

The Audio Graph API provides detailed error information:

```csharp
try
{
    using var graph = builder.Build();
    using var result = graph.Process(context);
}
catch (AudioGraphBuildException ex)
{
    Console.WriteLine($"Graph build failed: {ex.Message}");
}
catch (AudioNodeException ex)
{
    Console.WriteLine($"Node processing failed: {ex.Message}");
    Console.WriteLine($"Failed node: {ex.Node?.GetType().Name}");
}
catch (AudioEffectException ex)
{
    Console.WriteLine($"Effect processing failed: {ex.Message}");
    Console.WriteLine($"Effect type: {ex.EffectType}");
}
```