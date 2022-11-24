using System.Collections.ObjectModel;

using Avalonia.Platform.Storage;

using Beutl.Framework;
using Beutl.Rendering;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Utilities;

using DynamicData;

using Reactive.Bindings;
using Beutl.Media;

namespace Beutl.ViewModels;

public sealed class VideoOutputViewModel
{
    private double _aspectRatio;
    private bool _changingSize;

    public VideoOutputViewModel(int width, int height, Rational frameRate, int bitRate, int keyframeRate)
    {
        Width.Value = width;
        Height.Value = height;
        InputFrameRate.Value = frameRate.ToString();
        BitRate.Value = bitRate;
        KeyFrameRate.Value = keyframeRate;
        FrameRate = new ReactivePropertySlim<Rational>(frameRate);

        FixAspectRatio.Where(x => x)
            .Subscribe(_ => _aspectRatio = Width.Value / (double)Height.Value);

        Width.Skip(1)
            .Where(_ => FixAspectRatio.Value && !_changingSize)
            .Subscribe(w =>
            {
                try
                {
                    _changingSize = true;
                    Height.Value = (int)(w / _aspectRatio);
                }
                finally
                {
                    _changingSize = false;
                }
            });
        Height.Skip(1)
            .Where(_ => FixAspectRatio.Value && !_changingSize)
            .Subscribe(h =>
            {
                try
                {
                    _changingSize = true;
                    Width.Value = (int)(h * _aspectRatio);
                }
                finally
                {
                    _changingSize = false;
                }
            });

        Width.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        Height.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        BitRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        KeyFrameRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        InputFrameRate.SetValidateNotifyError(x =>
        {
            if (Rational.TryParse(x, null, out Rational rate))
            {
                if (MathUtilities.LessThanOrClose(rate.ToDouble(), 0))
                {
                    return Message.ValueLessThanOrEqualToZero;
                }
                else
                {
                    FrameRate.Value = rate;
                    return null;
                }
            }
            else
            {
                return Message.InvalidString;
            }
        });

        IsValid = Width.ObserveHasErrors
            .AnyTrue(Height.ObserveHasErrors,
                     BitRate.ObserveHasErrors,
                     KeyFrameRate.ObserveHasErrors,
                     InputFrameRate.ObserveHasErrors)
            .Not()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<int> Width { get; } = new();

    public ReactiveProperty<int> Height { get; } = new();

    public ReactiveProperty<bool> FixAspectRatio { get; } = new(true);

    public ReactivePropertySlim<bool> IsFrameSizeExpanded { get; } = new(true);

    public ReactiveProperty<string> InputFrameRate { get; } = new();

    public ReactivePropertySlim<Rational> FrameRate { get; }

    public ReactiveProperty<int> BitRate { get; } = new();

    public ReactiveProperty<int> KeyFrameRate { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public VideoEncoderSettings ToSettings(PixelSize sourceSize)
    {
        return new VideoEncoderSettings(
            sourceSize,
            new PixelSize(Width.Value, Height.Value),
            FrameRate.Value,
            BitRate.Value,
            KeyFrameRate.Value);
    }

    private static string? ValidateLessThanOrEqualToZero(int value)
    {
        if (value <= 0)
            return Message.ValueLessThanOrEqualToZero;
        else
            return null;
    }
}

public sealed class AudioOutputViewModel
{
    public AudioOutputViewModel(int sampleRate, int channels, int bitrate)
    {
        SampleRate.Value = sampleRate;
        BitRate.Value = bitrate;
        Channels.Value = channels;

        SampleRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        BitRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        Channels.SetValidateNotifyError(x => x is >= 1 and <= 2 ? null : "Invalid choice.");

        IsValid = SampleRate.ObserveHasErrors
            .AnyTrue(Channels.ObserveHasErrors, BitRate.ObserveHasErrors)
            .Not()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<int> SampleRate { get; } = new();

    public ReactiveProperty<int> Channels { get; } = new();

    public ReactiveProperty<int> BitRate { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public AudioEncoderSettings ToSettings()
    {
        return new AudioEncoderSettings(
            SampleRate.Value,
            Channels.Value,
            BitRate.Value);
    }

    private static string? ValidateLessThanOrEqualToZero(int value)
    {
        if (value <= 0)
            return Message.ValueLessThanOrEqualToZero;
        else
            return null;
    }
}

public sealed class OutputViewModel : IOutputContext
{
    private readonly ReactiveProperty<bool> _isIndeterminate = new();
    private readonly ReactiveProperty<bool> _isEncoding = new();
    private readonly ReactivePropertySlim<double> _progress = new();
    private readonly ReadOnlyObservableCollection<IEncoderInfo> _encoders;
    private readonly IDisposable _disposable1;

    public OutputViewModel(Scene model)
    {
        Model = model;

        int framerate = 30;
        int samplerate = 44100;
        if (model.Parent is Project proj)
        {
            framerate = proj.GetFrameRate();
            samplerate = proj.GetSampleRate();
        }

        VideoSettings = new VideoOutputViewModel(
            width: model.Width,
            height: model.Height,
            frameRate: new Rational(framerate),
            bitRate: 5_000_000,
            keyframeRate: 12);
        AudioSettings = new AudioOutputViewModel(samplerate, 1, 128_000);

        CanEncode = VideoSettings.IsValid
            .AreTrue(AudioSettings.IsValid,
                     DestinationFile.Select(x => x != null),
                     SelectedEncoder.Select(x => x != null))
            .ToReadOnlyReactivePropertySlim();

        _disposable1 = EncoderRegistry.EnumerateEncoders().AsObservableChangeSet()
            .Filter(DestinationFile.Select<string?, Func<IEncoderInfo, bool>>(
                f => f == null
                    ? _ => false
                    : ext => ext.IsSupported(f)))
            .Bind(out _encoders)
            .Subscribe();
    }

    public OutputExtension Extension => SceneOutputExtension.Instance;

    public Scene Model { get; }

    public string TargetFile => Model.FileName;

    public ReactivePropertySlim<string?> DestinationFile { get; } = new();

    public ReactivePropertySlim<IEncoderInfo?> SelectedEncoder { get; } = new();

    public ReadOnlyObservableCollection<IEncoderInfo> Encoders => _encoders;

    public ReactivePropertySlim<bool> IsEncodersExpanded { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanEncode { get; }

    public VideoOutputViewModel VideoSettings { get; }

    public AudioOutputViewModel AudioSettings { get; }

    public ReactivePropertySlim<Avalonia.Vector> ScrollOffset { get; } = new();

    public ReactiveProperty<double> ProgressMax { get; } = new();

    public ReactiveProperty<double> ProgressValue { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsIndeterminate => _isIndeterminate;

    public IReadOnlyReactiveProperty<bool> IsEncoding => _isEncoding;

    IReadOnlyReactiveProperty<double> IOutputContext.Progress => _progress;

    public event EventHandler? Started;

    public event EventHandler? Finished;

    public static FilePickerFileType[] GetFilePickerFileTypes()
    {
        static string[] ToPatterns(IEncoderInfo encoder)
        {
            return encoder.SupportExtensions()
                .Select(x =>
                {
                    if (x.Contains('*', StringComparison.Ordinal))
                    {
                        return x;
                    }
                    else
                    {
                        if (x.StartsWith('.'))
                        {
                            return $"*{x}";
                        }
                        else
                        {
                            return $"*.{x}";
                        }
                    }
                })
                .ToArray();
        }

        return EncoderRegistry.EnumerateEncoders()
            .Select(x => new FilePickerFileType(x.Name) { Patterns = ToPatterns(x) })
            .ToArray();
    }

    private async Task StartEncode()
    {
        try
        {
            _isEncoding.Value = true;
            await Model.Renderer.Dispatcher.InvokeAsync(() =>
            {
                VideoEncoderSettings videoSettings = VideoSettings.ToSettings(new PixelSize(Model.Width, Model.Height));
                AudioEncoderSettings audioSettings = AudioSettings.ToSettings();

                TimeSpan duration = Model.Duration;
                Rational frameRate = videoSettings.FrameRate;
                double frameRateD = frameRate.ToDouble();
                double frames = duration.TotalSeconds * frameRateD;
                double samples = duration.TotalSeconds;
                ProgressMax.Value = frames + samples;

                MediaWriter? writer = SelectedEncoder.Value!.Create(DestinationFile.Value!, videoSettings, audioSettings);
                if (writer == null)
                {
                    return;
                }

                IRenderer renderer = Model.Renderer;
                for (double i = 0; i < frames; i += frameRateD)
                {
                    var ts = TimeSpan.FromSeconds(i / frameRateD);
                    IRenderer.RenderResult result = renderer.Render(ts);
                    ProgressValue.Value += i;

                    writer.AddVideo(result.Bitmap);
                    result.Bitmap.Dispose();
                }

                writer.Dispose();
            });
        }
        finally
        {
            _isEncoding.Value = false;
        }
    }

    public void Dispose()
    {
        _disposable1.Dispose();
    }
}
