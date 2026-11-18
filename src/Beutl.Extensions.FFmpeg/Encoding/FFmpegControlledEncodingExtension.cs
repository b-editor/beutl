using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensions.FFmpeg.Encoding;

// Beutl 1.x built this extension into the app as Beutl.Embedding.FFmpeg (FFmpegBuildIn, on by default up to
// v1.1.0), so the output profiles and presets it saved select this encoder by that name.
[Export]
[Display(Name = nameof(Strings.FFmpegEncoder), ResourceType = typeof(Strings))]
[FormerPersistedName("[Beutl.Embedding.FFmpeg].Encoding:FFmpegControlledEncodingExtension")]
public class FFmpegControlledEncodingExtension : ControllableEncodingExtension
{
    private INotificationService? _notifications;

    public override FFmpegEncodingSettings Settings { get; } = new();

    public override IEnumerable<string> SupportExtensions() => FFmpegHeadlessEncodingExtension.SupportedExtensions;

    public override EncodingController CreateController(string file)
    {
        return new FFmpegEncodingControllerProxy(file, Settings);
    }

    public override void Load(IExtensionContext context)
    {
        _notifications = context.Services.GetRequiredService<INotificationService>();
        FFmpegLibraryState.LibrariesMissing += OnLibrariesMissing;
        base.Load(context);
    }

    public override void Unload(IExtensionContext context)
    {
        FFmpegLibraryState.LibrariesMissing -= OnLibrariesMissing;
        _notifications = null;
        base.Unload(context);
    }

    private void OnLibrariesMissing(object? sender, EventArgs e)
    {
        if (_notifications is { } notifications)
            FFmpegInstallNotifier.ShowMissingNotification(notifications);
    }
}
