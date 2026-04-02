using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.FFmpeg.Decoding;

[Export]
[Display(Name = nameof(Strings.FFmpegDecoder), ResourceType = typeof(Strings))]
public class FFmpegDecodingExtension : DecodingExtension
{
    public override FFmpegDecodingSettings Settings { get; } = new FFmpegDecodingSettings();

    public override IDecoderInfo GetDecoderInfo()
    {
        return new FFmpegDecoderInfo(Settings);
    }

    public override void Load()
    {
#if !FFMPEG_OUT_OF_PROCESS
        FFmpegLoader.Initialize();
#else
        FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
#endif
        base.Load();
    }

    public override void Unload()
    {
#if FFMPEG_OUT_OF_PROCESS
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
#endif
    }

#if FFMPEG_OUT_OF_PROCESS
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Task.Run(async () =>
        {
            if (e.PropertyName is not (nameof(FFmpegDecodingSettings.ThreadCount)
                or nameof(FFmpegDecodingSettings.Acceleration)
                or nameof(FFmpegDecodingSettings.ForceSrgbGamma)))
            {
                return;
            }

            var worker = FFmpegWorkerProcess.DecodingInstance;
            if (worker.IsRunning)
            {
                var connection = await worker.EnsureStartedAsync();
                var request = new UpdateDecoderSettingsRequest
                {
                    ThreadCount = Settings.ThreadCount,
                    ForceSrgbGamma = Settings.ForceSrgbGamma,
                    Acceleration = (int)Settings.Acceleration,
                };

                await connection.SendAndReceiveAsync(IpcMessage.Create(connection.NextId(), MessageType.UpdateDecoderSettings, request));
            }
        });
    }
#endif
}
