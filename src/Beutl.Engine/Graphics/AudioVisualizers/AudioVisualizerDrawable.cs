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

    [Display(Name = nameof(GraphicsStrings.Fill), ResourceType = typeof(GraphicsStrings))]
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

        private IComposer? _composer;
        private Sound.Resource? _source;

        private float[] _cachedSamples = [];
        private int _cachedSampleLength;
        private int _cachedSampleRate;
        private TimeSpan _cachedStart;
        private TimeSpan _cachedDuration;
        private int _cachedSourceVersion = -1;
        private Sound.Resource? _cachedSource;
        private RenderIntent? _cachedRenderIntent;
        private RenderPullPurpose? _cachedPullPurpose;
        private RenderIntent _renderIntent = RenderIntent.Preview;
        private RenderPullPurpose _pullPurpose = RenderPullPurpose.Frame;

        // CompositionFrame の Objects 配列は _source が変わった時だけ作り直す。
        private ImmutableArray<EngineObject.Resource> _frameObjects;
        private Sound.Resource? _frameObjectsSource;

        internal Func<RenderIntent, IComposer> ComposerFactory { get; set; }
            = static renderIntent => new Composer(renderIntent) { SampleRate = DefaultComposerSampleRate };

        public Sound.Resource? Source => ReadGeneratedResourceState(ref _source);

        internal float[] CachedSamples => _cachedSamples;
        internal int CachedSampleLength => _cachedSampleLength;
        internal int CachedSampleRate => _cachedSampleRate;
        internal TimeSpan CachedStart => _cachedStart;
        internal TimeSpan CachedDuration => _cachedDuration;
        internal int ComposerSampleRate => _composer?.SampleRate ?? DefaultComposerSampleRate;
        internal ReadOnlySpan<float> CachedSampleSpan => _cachedSamples.AsSpan(0, _cachedSampleLength);

        partial void PostUpdate(AudioVisualizerDrawable obj, CompositionContext context)
        {
            _renderIntent = context.RenderIntent;
            _pullPurpose = context.PullPurpose;
            // 音声処理は専用コンテキストで実行し、MediaReader 等のリソース共有を無効化する。
            // これにより、プレビュー/エンコード側が保持する共有カウンタを visualizer 側の読み出しで汚染しない。
            var audioContext = new CompositionContext(
                context.Time,
                context.RenderIntent,
                context.PullPurpose)
            {
                DisableResourceShare = true,
                PreferProxy = context.PreferProxy,
                PreferredProxyPreset = context.PreferredProxyPreset,
            };
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
                _cachedSource = null;
                _cachedRenderIntent = null;
                _cachedPullPurpose = null;
                return;
            }

            if (_composer?.RenderIntent != context.RenderIntent)
            {
                IComposer replacement = ComposerFactory(context.RenderIntent)
                    ?? throw new InvalidOperationException("The audio visualizer composer factory returned null.");
                IComposer? oldComposer = _composer;
                _composer = replacement;
                oldComposer?.Dispose();
            }
            (TimeSpan start, TimeSpan duration) = ComputeSampleWindow(context.Time);
            EnsureSamplesComposed(start, duration);
        }

        partial void PrepareResourceDispose(
            bool disposing,
            EngineObject.Resource.GeneratedResourceCleanupContext context)
        {
            if (disposing)
                context.Reserve(_source);
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            IComposer? composer = _composer;
            _composer = null;
            _source = null;
            _cachedSamples = [];
            _cachedSampleLength = 0;
            _cachedSampleRate = 0;
            _cachedStart = default;
            _cachedDuration = default;
            _cachedSourceVersion = -1;
            _cachedSource = null;
            _cachedRenderIntent = null;
            _cachedPullPurpose = null;
            _frameObjects = default;
            _frameObjectsSource = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, composer);
            ThrowIfCleanupFailed(failure);
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
            Sound.Resource source = _source
                ?? throw new InvalidOperationException("An audio visualizer cannot compose without a source resource.");

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
                _cachedSourceVersion = source.Version;
                _cachedSource = source;
                _cachedRenderIntent = _renderIntent;
                _cachedPullPurpose = _pullPurpose;
                return;
            }

            bool needsRecompose = !ReferenceEquals(_cachedSource, source)
                || _cachedSourceVersion != source.Version
                || _cachedStart != targetStart
                || _cachedDuration != targetDuration
                || _cachedSampleRate != rate
                || _cachedRenderIntent != _renderIntent
                || _cachedPullPurpose != _pullPurpose;

            if (!needsRecompose) return;

            var targetRange = new TimeRange(targetStart, targetDuration);
            Sound sound = source.GetOriginal();

            if (!ReferenceEquals(_frameObjectsSource, source))
            {
                _frameObjects = ImmutableArray.Create<EngineObject.Resource>(source);
                _frameObjectsSource = source;
            }

            var frame = new CompositionFrame(
                _frameObjects, sound.TimeRange, default, _renderIntent, _pullPurpose);

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
            _cachedSourceVersion = source.Version;
            _cachedSource = source;
            _cachedRenderIntent = _renderIntent;
            _cachedPullPurpose = _pullPurpose;
            Version++;
        }
    }
}
