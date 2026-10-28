using Avalonia.Controls;
using Beutl.Views.Tools;

namespace Beutl.Services.VideoUpload;

public sealed class YouTubeUploadExtension : VideoUploadExtension
{
    public static readonly YouTubeUploadExtension Instance = new();

    public override string Name => "YouTube Upload";

    public override string DisplayName => "YouTube";

    public override IVideoUploadContext CreateContext() => new YouTubeUploadContext();

    public override Control CreateControl(IVideoUploadContext context)
        => new YouTubeUploadSettingsView { DataContext = (YouTubeUploadContext)context };
}
