using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

public abstract partial class AudioVisualizerDrawable : Drawable
{
    public AudioVisualizerDrawable()
    {
        ScanProperties<AudioVisualizerDrawable>();
        Fill.CurrentValue = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<Sound?> Source { get; } = Property.Create<Sound?>();

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(1, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(640f);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(1, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(120f);

    [Display(Name = nameof(GraphicsStrings.Fill), ResourceType = typeof(GraphicsStrings), GroupName = nameof(GraphicsStrings.Fill))]
    public IProperty<Brush?> Fill { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Gain), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 1000f)]
    public IProperty<float> Gain { get; } = Property.CreateAnimatable(1f);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return new Size(MathF.Max(1f, r.Width), MathF.Max(1f, r.Height));
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        context.DrawNode(
            r,
            p => new AudioVisualizerRenderNode(p),
            (node, p) => node.Update(p));
    }

    public abstract partial class Resource
    {
        private const int DefaultComposerSampleRate = 44100;

        private Composer? _composer;
        private Sound.Resource? _source;

        private float[] _cachedSamples = [];
        private int _cachedSampleLength;
        private int _cachedSampleRate;
        private TimeSpan _cachedStart;
        private TimeSpan _cachedDuration;
        private int _cachedSourceVersion = -1;

        // CompositionFrame の Objects 配列は _source が変わった時だけ作り直す。
        private ImmutableArray<EngineObject.Resource> _frameObjects;
        private Sound.Resource? _frameObjectsSource;

        public Sound.Resource? Source => _source;

        internal float[] CachedSamples => _cachedSamples;
        internal int CachedSampleLength => _cachedSampleLength;
        internal int CachedSampleRate => _cachedSampleRate;
        internal TimeSpan CachedStart => _cachedStart;
        internal TimeSpan CachedDuration => _cachedDuration;
        internal int ComposerSampleRate => _composer?.SampleRate ?? DefaultComposerSampleRate;
        internal ReadOnlySpan<float> CachedSampleSpan => _cachedSamples.AsSpan(0, _cachedSampleLength);

        partial void PostUpdate(AudioVisualizerDrawable obj, CompositionContext context)
        {
            // 音声処理は専用コンテキストで実行し、MediaReader 等のリソース共有を無効化する。
            // これにより、プレビュー/エンコード側が保持する共有カウンタを visualizer 側の読み出しで汚染しない。
            var audioContext = new CompositionContext(context.Time) { DisableResourceShare = true };
            bool sourceUpdateOnly = true;
            CompareAndUpdateObject(audioContext, obj.Source, ref _source, ref sourceUpdateOnly);

            if (_source == null || _source.IsDisposed)
            {
                if (_cachedSampleLength != 0)
                {
                    _cachedSampleLength = 0;
                    Version++;
                }
                _cachedSampleRate = 0;
                _cachedDuration = TimeSpan.Zero;
                _cachedSourceVersion = -1;
                return;
            }

            _composer ??= new Composer { SampleRate = DefaultComposerSampleRate };
            (TimeSpan start, TimeSpan duration) = ComputeSampleWindow(context.Time);
            EnsureSamplesComposed(start, duration);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _composer?.Dispose();
                _source?.Dispose();
            }
            _composer = null;
            _source = null;
            _cachedSamples = [];
        }

        protected abstract (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime);

        internal void RenderToCanvas(ImmediateCanvas canvas, Rect bounds)
        {
            if (Fill is null) return;
            RenderForeground(canvas, bounds);
        }

        protected abstract void RenderForeground(ImmediateCanvas canvas, Rect bounds);

        private void EnsureSamplesComposed(TimeSpan targetStart, TimeSpan targetDuration)
        {
            int rate = _composer!.SampleRate;

            if (targetDuration <= TimeSpan.Zero)
            {
                if (_cachedSampleLength != 0)
                {
                    _cachedSampleLength = 0;
                    Version++;
                }
                _cachedSampleRate = rate;
                _cachedStart = targetStart;
                _cachedDuration = TimeSpan.Zero;
                _cachedSourceVersion = _source!.Version;
                return;
            }

            bool needsRecompose = _cachedSourceVersion != _source!.Version
                || _cachedStart != targetStart
                || _cachedDuration != targetDuration
                || _cachedSampleRate != rate;

            if (!needsRecompose) return;

            var targetRange = new TimeRange(targetStart, targetDuration);
            Sound sound = _source.GetOriginal();

            if (!ReferenceEquals(_frameObjectsSource, _source))
            {
                _frameObjects = ImmutableArray.Create<EngineObject.Resource>(_source);
                _frameObjectsSource = _source;
            }

            var frame = new CompositionFrame(_frameObjects, sound.TimeRange, default);

            AudioBuffer? buffer = _composer.Compose(targetRange, frame);
            try
            {
                if (buffer == null || buffer.SampleCount == 0)
                {
                    _cachedSampleLength = 0;
                }
                else
                {
                    int n = buffer.SampleCount;
                    if (_cachedSamples.Length < n)
                    {
                        _cachedSamples = new float[n];
                    }
                    Span<float> dst = _cachedSamples.AsSpan(0, n);
                    Span<float> leftChannel = buffer.GetChannelData(0);
                    if (buffer.ChannelCount >= 2)
                    {
                        Span<float> rightChannel = buffer.GetChannelData(1);
                        for (int i = 0; i < n; i++)
                        {
                            dst[i] = (leftChannel[i] + rightChannel[i]) * 0.5f;
                        }
                    }
                    else
                    {
                        leftChannel.CopyTo(dst);
                    }
                    _cachedSampleLength = n;
                    rate = buffer.SampleRate;
                }
            }
            finally
            {
                buffer?.Dispose();
            }

            _cachedSampleRate = rate;
            _cachedStart = targetStart;
            _cachedDuration = targetDuration;
            _cachedSourceVersion = _source.Version;
            Version++;
        }
    }
}
