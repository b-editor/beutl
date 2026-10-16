using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Decoding;

[Export]
[Display(Name = nameof(Strings.FFmpegDecoder), ResourceType = typeof(Strings))]
public class FFmpegDecodingExtension : DecodingExtension
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegDecodingExtension>();

    public override FFmpegDecodingSettings Settings { get; } = new FFmpegDecodingSettings();

    public override IDecoderInfo GetDecoderInfo()
    {
        return new FFmpegDecoderInfo(Settings);
    }

    public override void Load()
    {
        try
        {
            FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            FFmpegInstallNotifier.NotifyMissing();
        }
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        base.Load();
    }

    public override void Unload()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        base.Unload();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FFmpegDecodingSettings.ForceSrgbGamma))
        {
            return;
        }

        // Readers opened later read the settings at open, so only a running worker needs the update.
        UpdateWorkerSettings(FFmpegWorkerProcess.DecodingInstance);
        UpdateWorkerSettings(FFmpegWorkerProcess.BackgroundDecodingInstance);
    }

    private void UpdateWorkerSettings(FFmpegWorkerProcess worker)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (worker.IsRunning)
                {
                    var connection = await worker.EnsureStartedAsync();
                    var request = new UpdateDecoderSettingsRequest
                    {
                        ForceSrgbGamma = Settings.ForceSrgbGamma,
                    };

                    await connection.SendAndReceiveAsync(IpcMessage.Create(connection.NextId(), MessageType.UpdateDecoderSettings, request));
                }
            }
            catch (FFmpegLibrariesNotFoundException)
            {
                FFmpegInstallNotifier.NotifyMissing();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update decoder settings on worker");
            }
        });
    }
}
