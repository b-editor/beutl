using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
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
        Fill.Value = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<Sound?> Source { get; } = Property.CreateExpression<Sound?>();

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(1, float.MaxValue)]
    [CombinedEditor(nameof(Height), Name = nameof(Strings.Size), ResourceType = typeof(Strings))]
    public IExpressionProperty<float> Width { get; } = Property.CreateAnimatable(640f);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(1, float.MaxValue)]
    public IExpressionProperty<float> Height { get; } = Property.CreateAnimatable(120f);

    [Display(Name = nameof(GraphicsStrings.Fill), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Brush?> Fill { get; } = Property.CreateExpression<Brush?>();

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Gain), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 1000f)]
    public IExpressionProperty<float> Gain { get; } = Property.CreateAnimatable(1f);

    public abstract partial class Resource
    {
        private const int DefaultComposerSampleRate = 44100;

        private Composer? _composer;
        private Sound.Resource? _source;
        private Sound? _observedSource;
        private bool _sourceEdited = true;

        private float[] _cachedSamples = [];
        private int _cachedSampleLength;
        private int _cachedSampleRate;
        private MediaTime _cachedStart;
        private MediaTime _cachedDuration;
        private int _cachedSourceVersion = -1;

        // CompositionFrame の Objects 配列は _source が変わった時だけ作り直す。
        private ImmutableArray<EngineObject.Resource> _frameObjects;
        private Sound.Resource? _frameObjectsSource;

        public Sound.Resource? Source => _source;

        /// <summary>
        /// Gets the number of valid samples in the cache. Zero means there is nothing to draw.
        /// </summary>
        protected internal int CachedSampleLength => _cachedSampleLength;

        /// <summary>
        /// Gets the sample rate the cached samples were composed at, or zero while the cache is empty.
        /// </summary>
        protected int CachedSampleRate => _cachedSampleRate;

        /// <summary>
        /// Gets the start of the time window the cached samples cover.
        /// </summary>
        protected MediaTime CachedStart => _cachedStart;

        /// <summary>
        /// Gets the length of the time window the cached samples cover.
        /// </summary>
        protected MediaTime CachedDuration => _cachedDuration;

        /// <summary>
        /// Gets the rate the visualizer's own composer runs at, which is available before any source has
        /// been composed and so can size the window <see cref="ComputeSampleWindow"/> asks for.
        /// </summary>
        protected int ComposerSampleRate => _composer?.SampleRate ?? DefaultComposerSampleRate;

        /// <summary>
        /// Gets the composed samples, mixed down to mono.
        /// </summary>
        /// <remarks>
        /// The span borrows the engine's pooled buffer, so it is valid only for the duration of the call
        /// that reads it; copy out whatever has to outlive that call.
        /// </remarks>
        protected ReadOnlySpan<float> CachedSampleSpan => _cachedSamples.AsSpan(0, _cachedSampleLength);

        protected override Size MeasureCore(Size availableSize)
        {
            return new Size(MathF.Max(1f, Width), MathF.Max(1f, Height));
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            context.DrawNode(
                this,
                p => new AudioVisualizerRenderNode(p),
                (node, p) => node.Update(p));
        }

        partial void PostReconcile(AudioVisualizerDrawable obj, CompositionContext context)
        {
            // 音声処理は専用コンテキストで実行し、MediaReader 等のリソース共有を無効化する。
            // これにより、プレビュー/エンコード側が保持する共有カウンタを visualizer 側の読み出しで汚染しない。
            var audioContext = new CompositionContext(context.Time) { DisableResourceShare = true };
            bool sourceVersionBumped = true;
            ResourceReconciler.ReconcileChild(this, audioContext, obj.Source, ref _source, ref sourceVersionBumped);

            Sound? sound = _source is { IsDisposed: false } ? _source.RequireOriginal() : null;
            if (!ReferenceEquals(_observedSource, sound))
            {
                if (_observedSource != null) _observedSource.Edited -= OnSourceEdited;
                _observedSource = sound;
                if (sound != null) sound.Edited += OnSourceEdited;
                _sourceEdited = true;
            }

            if (_source == null || _source.IsDisposed)
            {
                if (_cachedSampleLength != 0)
                {
                    _cachedSampleLength = 0;
                    Version++;
                }
                _cachedSampleRate = 0;
                _cachedDuration = MediaTime.Zero;
                _cachedSourceVersion = -1;
                return;
            }

            _composer ??= new Composer { SampleRate = DefaultComposerSampleRate };
            (MediaTime start, MediaTime duration) = ComputeSampleWindow(context.Time);
            EnsureSamplesComposed(start, duration);
        }

        partial void PostDispose(bool disposing)
        {
            if (_observedSource != null) _observedSource.Edited -= OnSourceEdited;
            _observedSource = null;
            if (disposing)
            {
                _composer?.Dispose();
                _source?.Dispose();
            }
            _composer = null;
            _source = null;
            _cachedSamples = [];
        }

        private void OnSourceEdited(object? sender, EventArgs e) => _sourceEdited = true;

        /// <summary>
        /// Returns the time range of audio this visualizer draws at <paramref name="currentTime"/>.
        /// </summary>
        /// <remarks>
        /// The base resource calls this while it reconciles, before a derived resource has reconciled its own
        /// properties, so those still hold the previous reconcile's values, or <see langword="default"/> on a new
        /// resource. Evaluate the properties the window depends on in the derived resource's <c>PreReconcile</c>.
        /// </remarks>
        protected abstract (MediaTime Start, MediaTime Duration) ComputeSampleWindow(MediaTime currentTime);

        internal void RenderToCanvas(ImmediateCanvas canvas, Rect bounds)
        {
            if (Fill is null) return;
            RenderForeground(canvas, bounds);
        }

        protected abstract void RenderForeground(ImmediateCanvas canvas, Rect bounds);

        private void EnsureSamplesComposed(MediaTime targetStart, MediaTime targetDuration)
        {
            int rate = _composer!.SampleRate;

            if (targetDuration <= MediaTime.Zero)
            {
                if (_cachedSampleLength != 0)
                {
                    _cachedSampleLength = 0;
                    Version++;
                }
                _cachedSampleRate = rate;
                _cachedStart = targetStart;
                _cachedDuration = MediaTime.Zero;
                _cachedSourceVersion = _source!.Version;
                return;
            }

            bool sourceChanged = _sourceEdited || _cachedSourceVersion != _source!.Version
                || !ReferenceEquals(_frameObjectsSource, _source);
            bool needsRecompose = sourceChanged
                || _cachedStart != targetStart
                || _cachedDuration != targetDuration
                || _cachedSampleRate != rate;

            if (!needsRecompose) return;

            var targetRange = new TimeRange(targetStart, targetDuration);
            Sound sound = _source.RequireOriginal();
            MediaTime composeStart = targetStart;
            int retained = 0;
            long skip = (targetStart - _cachedStart).ToSamples(rate, MidpointRounding.AwayFromZero);
            int targetCount = AudioProcessContext.GetSampleCount(targetRange, rate);
            // Reuse only a forward, sample-aligned overlap. A backwards/disjoint seek, changed source or
            // unaligned window rebuilds the graph and history just like a fresh resource.
            if (!sourceChanged && _cachedSampleRate == rate
                && skip >= 0 && skip < _cachedSampleLength
                && targetRange.End > _cachedStart + _cachedDuration
                && targetStart - _cachedStart == MediaTime.FromSamples(skip, rate)
                && _cachedDuration == MediaTime.FromSamples(_cachedSampleLength, rate)
                && targetDuration == MediaTime.FromSamples(targetCount, rate))
            {
                retained = _cachedSampleLength - (int)skip;
                composeStart = _cachedStart + _cachedDuration;
            }
            else if (_frameObjectsSource != null)
            {
                _composer.Dispose();
                _composer = new Composer { SampleRate = rate };
            }

            // If composing throws, retry with a full window rather than a partially moved cache.
            _sourceEdited = true;
            if (_cachedSamples.Length < targetCount) Array.Resize(ref _cachedSamples, targetCount);
            if (retained > 0)
                _cachedSamples.AsSpan((int)skip, retained).CopyTo(_cachedSamples);

            if (!ReferenceEquals(_frameObjectsSource, _source))
            {
                _frameObjects = ImmutableArray.Create<EngineObject.Resource>(_source);
                _frameObjectsSource = _source;
            }

            var frame = new CompositionFrame(
                _frameObjects,
                sound.TimeRange.Value,
                default,
                new CompositionEligibility([sound]));

            AudioBuffer? buffer = _composer.Compose(new TimeRange(composeStart, targetRange.End - composeStart), frame);
            try
            {
                if (buffer == null || buffer.SampleCount == 0)
                {
                    _cachedSampleLength = retained;
                }
                else
                {
                    int n = buffer.SampleCount;
                    if (_cachedSamples.Length < retained + n)
                    {
                        Array.Resize(ref _cachedSamples, retained + n);
                    }
                    Span<float> dst = _cachedSamples.AsSpan(retained, n);
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
                    _cachedSampleLength = retained + n;
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
            _sourceEdited = false;
            Version++;
        }
    }
}
