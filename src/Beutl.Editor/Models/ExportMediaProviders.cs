using System.Reactive.Subjects;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Models;

/// <summary>
/// The frame and sample providers an export passes to <see cref="EncodingController.Encode"/>, built from what the
/// controller writes: a side the output format cannot hold gets an empty provider, so it neither renders nor reaches
/// the encoder.
/// </summary>
public sealed class ExportMediaProviders : IDisposable
{
    private readonly Subject<MediaTime> _frameProgress = new();
    private readonly Subject<MediaTime> _sampleProgress = new();
    private SceneRenderer? _renderer;
    private FrameProviderImpl? _frameProvider;
    private SceneComposer? _composer;
    private SampleProviderImpl? _sampleProvider;

    private ExportMediaProviders()
    {
    }

    public IFrameProvider FrameProvider => (IFrameProvider?)_frameProvider ?? DisabledMediaProvider.Instance;

    public ISampleProvider SampleProvider => (ISampleProvider?)_sampleProvider ?? DisabledMediaProvider.Instance;

    /// <summary>Signals the time of each frame as the encoder reads it; silent when video is disabled.</summary>
    public IObservable<MediaTime> FrameProgress => _frameProgress;

    /// <summary>Signals the time of each block of samples as the encoder reads it; silent when audio is disabled.</summary>
    public IObservable<MediaTime> SampleProgress => _sampleProgress;

    public static ExportMediaProviders Create(
        Scene scene,
        bool videoEnabled,
        bool audioEnabled,
        float renderScale,
        Rational frameRate,
        int sampleRate,
        AudioChannelLayout channelLayout)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var providers = new ExportMediaProviders();
        try
        {
            if (videoEnabled)
            {
                providers._renderer = ExportRendererFactory.Create(scene, renderScale);
                providers._frameProvider = new FrameProviderImpl(scene, frameRate, providers._renderer, providers._frameProgress);
            }

            if (audioEnabled)
            {
                providers._composer = ExportRendererFactory.CreateComposer(scene, sampleRate);
                providers._sampleProvider = new SampleProviderImpl(
                    scene, providers._composer, sampleRate, providers._sampleProgress, channelLayout);
            }

            return providers;
        }
        catch
        {
            providers.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        // The providers read from the renderer and the composer on their own tasks, so they stop first. Each step
        // runs even when an earlier one throws, as separate using declarations would.
        try
        {
            _sampleProvider?.Dispose();
            _sampleProvider = null;
        }
        finally
        {
            try
            {
                _composer?.Dispose();
                _composer = null;
            }
            finally
            {
                try
                {
                    _frameProvider?.Dispose();
                    _frameProvider = null;
                }
                finally
                {
                    try
                    {
                        _renderer?.Dispose();
                        _renderer = null;
                    }
                    finally
                    {
                        try
                        {
                            _sampleProgress.Dispose();
                        }
                        finally
                        {
                            _frameProgress.Dispose();
                        }
                    }
                }
            }
        }
    }

    private sealed class DisabledMediaProvider : IFrameProvider, ISampleProvider
    {
        public static DisabledMediaProvider Instance { get; } = new();

        public long FrameCount => 0;

        public Rational FrameRate => new(0, 1);

        public long SampleCount => 0;

        public long SampleRate => 0;

        public ValueTask<Bitmap?> ReadNextFrame() => ValueTask.FromResult<Bitmap?>(null);

        public ValueTask<AudioBuffer?> ReadNextSamples(int length) => ValueTask.FromResult<AudioBuffer?>(null);

        public void Dispose()
        {
        }
    }
}
