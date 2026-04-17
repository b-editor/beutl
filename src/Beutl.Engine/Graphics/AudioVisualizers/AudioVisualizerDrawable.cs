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

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_ForegroundColor), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> ForegroundColor { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BackgroundColor), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> BackgroundColor { get; } = Property.CreateAnimatable(Colors.Transparent);

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
        r.Render(context);
    }

    public abstract partial class Resource
    {
        private const int DefaultComposerSampleRate = 44100;

        private SolidColorBrush.Resource? _foregroundBrushResource;
        private Color _foregroundBrushColor;
        private SolidColorBrush.Resource? _backgroundBrushResource;
        private Color _backgroundBrushColor;

        private Composer? _composer;
        private Sound.Resource? _source;

        private float[] _cachedSamples = [];
        private int _cachedSampleRate;
        private TimeSpan _cachedStart;
        private TimeSpan _cachedDuration;
        private int _cachedSourceVersion = -1;

        public Sound.Resource? Source => _source;

        internal float[] CachedSamples => _cachedSamples;
        internal int CachedSampleRate => _cachedSampleRate;
        internal TimeSpan CachedStart => _cachedStart;
        internal TimeSpan CachedDuration => _cachedDuration;
        internal int ComposerSampleRate => _composer?.SampleRate ?? DefaultComposerSampleRate;
        internal SolidColorBrush.Resource? ForegroundBrush => _foregroundBrushResource;

        partial void PostUpdate(AudioVisualizerDrawable obj, CompositionContext context)
        {
            EnsureBrushes(context);

            // 音声処理は専用コンテキストで実行し、MediaReader 等のリソース共有を無効化する。
            // これにより、プレビュー/エンコード側が保持する共有カウンタを visualizer 側の読み出しで汚染しない。
            var audioContext = new CompositionContext(context.Time) { DisableResourceShare = true };
            bool sourceUpdateOnly = true;
            CompareAndUpdateObject(audioContext, obj.Source, ref _source, ref sourceUpdateOnly);

            if (_source == null || _source.IsDisposed)
            {
                _cachedSamples = [];
                _cachedSampleRate = 0;
                _cachedDuration = TimeSpan.Zero;
                _cachedSourceVersion = -1;
                Version++;
                return;
            }

            _composer ??= new Composer { SampleRate = DefaultComposerSampleRate };
            (TimeSpan start, TimeSpan duration) = ComputeSampleWindow(context.Time);
            EnsureSamplesComposed(start, duration);
            Version++;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _foregroundBrushResource?.Dispose();
                _backgroundBrushResource?.Dispose();
                _composer?.Dispose();
                _source?.Dispose();
            }
            _foregroundBrushResource = null;
            _backgroundBrushResource = null;
            _composer = null;
            _source = null;
            _cachedSamples = [];
        }

        protected abstract (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime);

        internal void Render(GraphicsContext2D context)
        {
            var bounds = new Rect(0, 0, Math.Max(1f, Width), Math.Max(1f, Height));

            if (_backgroundBrushResource != null && BackgroundColor.A > 0)
            {
                context.DrawRectangle(bounds, _backgroundBrushResource, null);
            }

            if (_foregroundBrushResource == null) return;

            RenderForeground(context, bounds);
        }

        protected abstract void RenderForeground(GraphicsContext2D context, Rect bounds);

        private void EnsureBrushes(CompositionContext context)
        {
            if (_foregroundBrushResource == null || _foregroundBrushColor != ForegroundColor)
            {
                _foregroundBrushResource?.Dispose();
                _foregroundBrushResource = new SolidColorBrush(ForegroundColor).ToResource(context) as SolidColorBrush.Resource;
                _foregroundBrushColor = ForegroundColor;
            }

            if (_backgroundBrushResource == null || _backgroundBrushColor != BackgroundColor)
            {
                _backgroundBrushResource?.Dispose();
                _backgroundBrushResource = new SolidColorBrush(BackgroundColor).ToResource(context) as SolidColorBrush.Resource;
                _backgroundBrushColor = BackgroundColor;
            }
        }

        private void EnsureSamplesComposed(TimeSpan targetStart, TimeSpan targetDuration)
        {
            int rate = _composer!.SampleRate;

            if (targetDuration <= TimeSpan.Zero)
            {
                _cachedSamples = [];
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
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(_source),
                sound.TimeRange,
                default);

            AudioBuffer? buffer = _composer.Compose(targetRange, frame);
            try
            {
                if (buffer == null || buffer.SampleCount == 0)
                {
                    _cachedSamples = [];
                }
                else
                {
                    int n = buffer.SampleCount;
                    var mono = new float[n];
                    Span<float> leftChannel = buffer.GetChannelData(0);
                    if (buffer.ChannelCount >= 2)
                    {
                        Span<float> rightChannel = buffer.GetChannelData(1);
                        for (int i = 0; i < n; i++)
                        {
                            mono[i] = (leftChannel[i] + rightChannel[i]) * 0.5f;
                        }
                    }
                    else
                    {
                        leftChannel.CopyTo(mono);
                    }
                    _cachedSamples = mono;
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
        }
    }
}
