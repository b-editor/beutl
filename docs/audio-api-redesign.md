# Audio API改修設計案

## 概要

現在のAudio APIは1秒区切りの処理制限があり、Animation APIとの統合も不完全です。本設計案では、描画APIと同様の宣言的なグラフ構造を採用し、柔軟で高性能な音声処理システムを実現します。

## 現状の問題点

1. **1秒区切りの処理制限**
   - バッファサイズが固定で柔軟性がない
   - ストリーミング処理に不向き

2. **Animation API統合の不完全性**
   - Gainなどのパラメータを時間軸で滑らかに変化させられない
   - 手動でのアニメーション対応のみ

3. **エフェクトの状態管理**
   - シーク時の処理が不十分
   - Delay/Reverbなど過去のサンプルを保持するエフェクトの扱いが困難

## 新設計の基本方針

1. **宣言的なグラフ構造**
   - 描画APIと同様のノードベース設計
   - 処理の最適化が可能

2. **柔軟なバッファ処理**
   - 任意のバッファサイズに対応
   - ストリーミング処理をサポート

3. **完全なAnimation統合**
   - 既存のKeyFrameAnimationを活用
   - サンプル単位での値の補間

## アーキテクチャ

### 基本構造

```csharp
// 音声処理ノード基底クラス
public abstract class AudioNode : IDisposable
{
    public List<AudioNode> Inputs { get; } = new();
    protected AudioBuffer? CachedOutput { get; set; }
    
    public abstract AudioBuffer Process(AudioProcessContext context);
}

// 音声バッファ
public class AudioBuffer
{
    public int SampleRate { get; }
    public int ChannelCount { get; }
    public int SampleCount { get; }
    public Span<float> GetChannelData(int channel);
}

// 処理コンテキスト
public class AudioProcessContext
{
    public TimeRange TimeRange { get; }
    public int SampleRate { get; }
    public IAnimationSampler AnimationSampler { get; }
}
```

### ノード実装例

```csharp
// ソースノード（音源）
public class SourceNode : AudioNode
{
    public string SourceName { get; set; }
    public IPcmSource Source { get; set; }
    
    public override AudioBuffer Process(AudioProcessContext context)
    {
        var buffer = new AudioBuffer(
            context.SampleRate, 
            2, // ステレオ
            context.TimeRange.Duration.TotalSeconds * context.SampleRate
        );
        
        Source.Read(context.TimeRange, buffer);
        return buffer;
    }
}

// エフェクトノード
public class EffectNode : AudioNode
{
    public IAudioEffect Effect { get; set; }
    private IAudioEffectProcessor? _processor;
    
    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Effect node requires exactly one input");
            
        _processor ??= Effect.CreateProcessor();
        
        var input = Inputs[0].Process(context);
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        
        _processor.Process(input, output, context);
        
        return output;
    }
}

// ミキサーノード
public class MixerNode : AudioNode
{
    public float[] Gains { get; set; } = Array.Empty<float>();
    
    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count == 0)
            throw new InvalidOperationException("Mixer requires at least one input");
            
        var buffers = Inputs.Select(n => n.Process(context)).ToArray();
        var output = new AudioBuffer(
            buffers[0].SampleRate, 
            buffers[0].ChannelCount, 
            buffers[0].SampleCount
        );
        
        // ミキシング処理
        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var outData = output.GetChannelData(ch);
            outData.Clear();
            
            for (int i = 0; i < buffers.Length; i++)
            {
                var gain = i < Gains.Length ? Gains[i] : 1.0f;
                var inData = buffers[i].GetChannelData(ch);
                
                AudioMath.AddWithGain(inData, outData, gain);
            }
        }
        
        return output;
    }
}

// ゲインノード（アニメーション対応）
public class GainNode : AudioNode
{
    public IAnimatable Target { get; set; }
    public CoreProperty<float> GainProperty { get; set; }
    
    public override AudioBuffer Process(AudioProcessContext context)
    {
        var input = Inputs[0].Process(context);
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        
        // アニメーション値をサンプル単位で取得
        Span<float> gains = stackalloc float[input.SampleCount];
        context.AnimationSampler.SampleBuffer(
            Target, 
            GainProperty, 
            context.TimeRange, 
            input.SampleCount, 
            gains
        );
        
        // 各チャンネルに適用
        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            AudioMath.MultiplyBuffers(inData, gains, outData);
        }
        
        return output;
    }
}
```

### アニメーション統合

```csharp
public interface IAnimationSampler
{
    // 単一の値を取得
    T Sample<T>(IAnimatable target, CoreProperty<T> property, TimeSpan time);
    
    // バッファ全体を効率的にサンプリング
    void SampleBuffer<T>(
        IAnimatable target, 
        CoreProperty<T> property, 
        TimeRange range, 
        int sampleCount,
        Span<T> output);
}

public class AnimationSampler : IAnimationSampler
{
    private readonly Dictionary<CoreProperty, Func<int, object>> _cache = new();
    
    public void PrepareAnimations(IAnimatable target, TimeRange range, int sampleRate)
    {
        _cache.Clear();
        
        foreach (var animation in target.Animations)
        {
            var getter = CompileGetter(animation, range, sampleRate);
            _cache[animation.Property] = getter;
        }
    }
    
    public void SampleBuffer<T>(
        IAnimatable target, 
        CoreProperty<T> property, 
        TimeRange range, 
        int sampleCount,
        Span<T> output)
    {
        if (_cache.TryGetValue(property, out var getter))
        {
            for (int i = 0; i < sampleCount; i++)
            {
                output[i] = (T)getter(i);
            }
        }
        else
        {
            // アニメーションがない場合は現在値を使用
            var value = target.GetValue(property);
            output.Fill(value);
        }
    }
}
```

### エフェクトシステム

```csharp
public interface IAudioEffect
{
    bool IsEnabled { get; }
    IAudioEffectProcessor CreateProcessor();
}

public interface IAudioEffectProcessor : IDisposable
{
    void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context);
    void Reset(); // シーク時のリセット
    void Prepare(TimeRange range, int sampleRate); // プリロール
}

// ステートフルなエフェクトの例
public class DelayEffect : IAudioEffect
{
    public CoreProperty<float> DelayTimeProperty { get; }
    public CoreProperty<float> FeedbackProperty { get; }
    public CoreProperty<float> MixProperty { get; }
    
    public bool IsEnabled { get; set; } = true;
    
    public IAudioEffectProcessor CreateProcessor()
    {
        return new DelayProcessor(this);
    }
    
    private class DelayProcessor : IAudioEffectProcessor
    {
        private readonly DelayEffect _effect;
        private readonly CircularBuffer<float>[] _delayLines;
        
        public void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context)
        {
            // アニメーション値を取得
            Span<float> delayTimes = stackalloc float[input.SampleCount];
            context.AnimationSampler.SampleBuffer(
                _effect, 
                _effect.DelayTimeProperty, 
                context.TimeRange, 
                input.SampleCount, 
                delayTimes
            );
            
            // ディレイ処理
            for (int ch = 0; ch < input.ChannelCount; ch++)
            {
                var inData = input.GetChannelData(ch);
                var outData = output.GetChannelData(ch);
                var delayLine = _delayLines[ch];
                
                for (int i = 0; i < input.SampleCount; i++)
                {
                    var delaySamples = (int)(delayTimes[i] * context.SampleRate / 1000);
                    var delayed = delayLine.Read(delaySamples);
                    var mix = _effect.Mix / 100f;
                    
                    outData[i] = inData[i] * (1 - mix) + delayed * mix;
                    delayLine.Write(inData[i] + delayed * (_effect.Feedback / 100f));
                }
            }
        }
        
        public void Reset()
        {
            foreach (var line in _delayLines)
                line.Clear();
        }
        
        public void Prepare(TimeRange range, int sampleRate)
        {
            // 必要に応じてプリロール処理
        }
    }
}
```

### グラフ構築と実行

```csharp
// グラフビルダー
public class AudioGraphBuilder
{
    private readonly List<AudioNode> _nodes = new();
    private AudioNode? _outputNode;
    
    public T AddNode<T>(T node) where T : AudioNode
    {
        _nodes.Add(node);
        return node;
    }
    
    public void Connect(AudioNode from, AudioNode to)
    {
        to.Inputs.Add(from);
    }
    
    public void SetOutput(AudioNode node)
    {
        _outputNode = node;
    }
    
    public AudioGraph Build()
    {
        if (_outputNode == null)
            throw new InvalidOperationException("Output node not set");
            
        // トポロジカルソート
        var sorted = TopologicalSort(_nodes);
        
        return new AudioGraph(_outputNode, sorted);
    }
}

// 構築されたグラフ
public class AudioGraph : IDisposable
{
    private readonly AudioNode _outputNode;
    private readonly List<AudioNode> _sortedNodes;
    
    internal AudioGraph(AudioNode outputNode, List<AudioNode> sortedNodes)
    {
        _outputNode = outputNode;
        _sortedNodes = sortedNodes;
    }
    
    public AudioBuffer Process(AudioProcessContext context)
    {
        // キャッシュをクリア
        foreach (var node in _sortedNodes)
        {
            node.CachedOutput = null;
        }
        
        // 出力ノードから再帰的に処理
        return _outputNode.Process(context);
    }
    
    public void Dispose()
    {
        foreach (var node in _sortedNodes)
        {
            node.Dispose();
        }
    }
}
```

### 使用例

```csharp
// Soundクラスの実装
public abstract class Sound : Renderable
{
    private AudioGraph? _cachedGraph;
    private int _cacheVersion = -1;
    
    protected abstract void BuildAudioGraph(AudioGraphBuilder builder);
    
    public AudioGraph GetOrBuildGraph()
    {
        if (_cachedGraph != null && _cacheVersion == Version)
            return _cachedGraph;
            
        var builder = new AudioGraphBuilder();
        
        // 基本的なノードを追加
        var source = builder.AddNode(new SourceNode { Source = GetPcmSource() });
        var gain = builder.AddNode(new GainNode { Target = this, GainProperty = GainProperty });
        
        builder.Connect(source, gain);
        
        // エフェクトがある場合
        AudioNode current = gain;
        if (Effect != null)
        {
            var effect = builder.AddNode(new EffectNode { Effect = Effect });
            builder.Connect(current, effect);
            current = effect;
        }
        
        // 派生クラスで追加のグラフ構築
        BuildAudioGraph(builder);
        
        builder.SetOutput(current);
        
        _cachedGraph = builder.Build();
        _cacheVersion = Version;
        
        return _cachedGraph;
    }
}

// Composerでの使用
public class AudioComposer
{
    public Pcm<Stereo32BitFloat> ComposeBuffer(TimeRange range, int sampleRate)
    {
        // アニメーションサンプラーを準備
        var animationSampler = new AnimationSampler();
        
        foreach (var sound in _sounds)
        {
            // アニメーションを適用
            sound.ApplyAnimations(clock);
            
            // グラフを取得（キャッシュされている可能性）
            var graph = sound.GetOrBuildGraph();
            
            // コンテキストを作成
            var context = new AudioProcessContext
            {
                TimeRange = range,
                SampleRate = sampleRate,
                AnimationSampler = animationSampler
            };
            
            // アニメーションを準備
            animationSampler.PrepareAnimations(sound, range, sampleRate);
            
            // 処理を実行
            var buffer = graph.Process(context);
            
            // 出力にミックス
            MixToOutput(buffer);
        }
    }
}
```

## 利点

1. **柔軟性**
   - 任意のバッファサイズ
   - 複雑なルーティングが可能
   - プラグイン拡張が容易

2. **パフォーマンス**
   - グラフのキャッシュ
   - SIMD最適化可能
   - 並列処理対応

3. **一貫性**
   - 描画APIと同じメンタルモデル
   - 既存のAnimation APIを完全活用
   - プロパティシステムとの統合

4. **保守性**
   - 明確な責任分離
   - テスタブルな設計
   - 拡張が容易