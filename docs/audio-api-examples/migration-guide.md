# Migration Guide: Legacy Audio API to Graph Audio API

This guide helps you migrate from the existing Audio API to the new Graph-based Audio API in Beutl.

## Overview of Changes

### What's New
- **Graph-based processing**: Build audio processing chains as directed graphs
- **Flexible buffer sizes**: No longer limited to 1-second chunks
- **Full Animation integration**: Sample-accurate animation interpolation
- **Better performance**: SIMD optimizations and efficient memory management
- **Improved error handling**: Detailed exception types with context information

### What's Changed
- Processing model shifted from callback-based to graph-based
- Buffer management is now automatic and optimized
- Effect processing uses new interfaces for better state management
- Animation sampling is built-in and efficient

## Migration Strategies

### Strategy 1: Adapter Pattern (Quick Migration)

Use the `LegacySoundAdapter` to quickly wrap existing Sound objects:

```csharp
// OLD: Direct usage of Sound
var oldSound = new SourceSound { Source = audioFile };
var pcm = oldSound.Render(timeRange, sampleRate);

// NEW: Using adapter
var oldSound = new SourceSound { Source = audioFile };
using var adapter = new LegacySoundAdapter(oldSound);
var pcm = adapter.RenderWithGraph(timeRange, sampleRate);
```

### Strategy 2: Gradual Replacement

Replace Sound objects with GraphSound equivalents:

```csharp
// OLD: SourceSound
var oldSourceSound = new SourceSound 
{ 
    Source = audioFile,
    Gain = 80f,
    OffsetPosition = TimeSpan.FromSeconds(1)
};

// NEW: GraphSourceSound
var newSourceSound = new GraphSourceSound
{
    Source = audioFile,
    Gain = 80f,
    OffsetPosition = TimeSpan.FromSeconds(1)
};
```

### Strategy 3: Full Graph Rewrite

Completely rewrite audio processing using the graph API:

```csharp
// OLD: Composition with multiple sounds
public class OldAudioComposition
{
    private readonly List<Sound> _sounds = new();
    
    public void AddSound(Sound sound) => _sounds.Add(sound);
    
    public Pcm<Stereo32BitFloat> Render(TimeRange range, int sampleRate)
    {
        // Complex mixing logic...
    }
}

// NEW: Graph-based composition
public class NewAudioComposition
{
    private readonly AudioComposer _composer = new();
    
    public void AddTrack(IAudioTrack track) => _composer.AddTrack(track);
    
    public Pcm<Stereo32BitFloat> Render(TimeRange range, int sampleRate)
    {
        return _composer.Compose(range, sampleRate);
    }
}
```

## Common Migration Patterns

### 1. Simple Audio Playback

```csharp
// OLD
var sound = new SourceSound { Source = audioFile };
sound.ApplyAnimations(clock);
var result = sound.Render(timeRange, sampleRate);

// NEW
var sound = new GraphSourceSound { Source = audioFile };
var result = sound.Render(timeRange, sampleRate);
// Animation is automatically applied during rendering
```

### 2. Gain Control

```csharp
// OLD
var sound = new SourceSound { Source = audioFile, Gain = 50f };

// NEW (Option 1: Direct property)
var sound = new GraphSourceSound { Source = audioFile, Gain = 50f };

// NEW (Option 2: Explicit graph)
var builder = new AudioGraphBuilder();
var source = builder.AddNode(new SourceNode { Source = audioFile });
var gain = builder.AddNode(new GainNode { StaticGain = 0.5f });
builder.Connect(source, gain);
builder.SetOutput(gain);
using var graph = builder.Build();
```

### 3. Effects Processing

```csharp
// OLD
var sound = new SourceSound 
{ 
    Source = audioFile,
    Effect = new Delay 
    { 
        DelayTime = 250f,
        Feedback = 40f 
    }
};

// NEW (Option 1: Using GraphSound)
var sound = new GraphSourceSound { Source = audioFile };
// Note: Effect integration with GraphSound needs custom implementation

// NEW (Option 2: Explicit graph)
var builder = new AudioGraphBuilder();
var source = builder.AddNode(new SourceNode { Source = audioFile });
var delay = new GraphDelayEffect 
{ 
    DelayTime = 250f,
    Feedback = 40f,
    Mix = 30f 
};
var effect = builder.AddNode(new GraphEffectNode { Effect = delay });
builder.Connect(source, effect);
builder.SetOutput(effect);
using var graph = builder.Build();
```

### 4. Animation Integration

```csharp
// OLD
var sound = new SourceSound { Source = audioFile };
var gainAnimation = new KeyFrameAnimation<float>(Sound.GainProperty);
gainAnimation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.Zero, 0f));
gainAnimation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.FromSeconds(2), 100f));
sound.Animations.Add(gainAnimation);

sound.ApplyAnimations(clock);
var result = sound.Render(timeRange, sampleRate);

// NEW
var sound = new GraphSourceSound { Source = audioFile };
var gainAnimation = new KeyFrameAnimation<float>(GraphSound.GainProperty);
gainAnimation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.Zero, 0f));
gainAnimation.KeyFrames.Add(new KeyFrame<float>(TimeSpan.FromSeconds(2), 100f));
sound.Animations.Add(gainAnimation);

// Animation is automatically applied - no need for manual ApplyAnimations
var result = sound.Render(timeRange, sampleRate);
```

### 5. Custom Effects

If you have custom effects implementing `ISoundEffect`:

```csharp
// OLD
public class CustomEffect : SoundEffect
{
    public override ISoundProcessor CreateProcessor()
    {
        return new CustomProcessor(this);
    }
}

// NEW (Option 1: Use EffectNode)
var builder = new AudioGraphBuilder();
var source = builder.AddNode(new SourceNode { Source = audioFile });
var effect = builder.AddNode(new EffectNode { Effect = customEffect });
builder.Connect(source, effect);
builder.SetOutput(effect);

// NEW (Option 2: Create graph-native effect)
public class GraphCustomEffect : Animatable, IAudioEffect
{
    public IAudioEffectProcessor CreateProcessor()
    {
        return new GraphCustomProcessor(this);
    }
}
```

## Performance Considerations

### Memory Management

```csharp
// OLD: Manual buffer management
var buffer = new float[sampleCount * channelCount];
// Manual copying and processing...

// NEW: Automatic buffer management
using var buffer = new AudioBuffer(sampleRate, channelCount, sampleCount);
// Automatic disposal and memory pooling
```

### SIMD Optimizations

```csharp
// OLD: Manual loop processing
for (int i = 0; i < samples.Length; i++)
{
    output[i] = input[i] * gain;
}

// NEW: Automatic SIMD optimization
AudioMath.ApplyGain(samples, gain);
// Automatically uses vectorized operations when available
```

### Graph Caching

```csharp
// NEW: Automatic graph caching
var sound = new GraphSourceSound { Source = audioFile };

// First call builds graph
var result1 = sound.Render(range1, sampleRate);

// Subsequent calls reuse cached graph (if properties unchanged)
var result2 = sound.Render(range2, sampleRate);
```

## Breaking Changes

### 1. Removed Methods/Properties
- `Sound.OnRecord()` - Replace with graph processing
- Manual animation application - Now automatic

### 2. Changed Interfaces
- `ISoundProcessor.Process()` signature changed
- New `IAudioEffectProcessor` interface for graph effects

### 3. New Requirements
- All audio processing must use `AudioBuffer` instead of raw arrays
- Graph disposal is mandatory - use `using` statements

## Migration Checklist

- [ ] Identify all uses of `Sound` classes
- [ ] Choose migration strategy (adapter, replacement, or rewrite)
- [ ] Update effect implementations if needed
- [ ] Replace manual animation calls
- [ ] Add proper disposal patterns
- [ ] Test performance improvements
- [ ] Update error handling for new exception types

## Common Pitfalls

### 1. Forgetting Disposal
```csharp
// BAD
var graph = builder.Build();
var result = graph.Process(context);
// Memory leak!

// GOOD
using var graph = builder.Build();
using var result = graph.Process(context);
```

### 2. Not Preparing Animations
```csharp
// BAD
var context = new AudioProcessContext(range, sampleRate, new AnimationSampler());

// GOOD
var sampler = new AnimationSampler();
sampler.PrepareAnimations(animatable, range, sampleRate);
var context = new AudioProcessContext(range, sampleRate, sampler);
```

### 3. Inefficient Graph Rebuilding
```csharp
// BAD
for (int i = 0; i < iterations; i++)
{
    using var graph = sound.GetOrBuildGraph(); // Rebuilds every time
    // Process...
}

// GOOD
using var graph = sound.GetOrBuildGraph(); // Build once
for (int i = 0; i < iterations; i++)
{
    // Process with same graph...
}
```

## Getting Help

If you encounter issues during migration:

1. Check the [Basic Usage Examples](basic-usage.md)
2. Review error messages - the new API provides detailed context
3. Use the `LegacySoundAdapter` as a temporary solution
4. Gradually migrate one component at a time

The new Audio Graph API provides significant improvements in flexibility, performance, and maintainability while maintaining compatibility with existing Animation and Property systems.