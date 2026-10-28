using System.Text.Json.Nodes;
using Avalonia.Controls;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.PublicApiContractTests;

public class VideoUploadAuthoringContractTests
{
    [Test]
    public async Task External_provider_can_snapshot_settings_and_upload_after_its_editor_is_disposed()
    {
        var extension = new UploadExtension();
        var context = (UploadContext)extension.CreateContext();
        using IVideoUploadOperation operation = context.CreateUpload("completed.mp4");
        context.Title = "edited later";
        context.Dispose();
        Assert.That(await operation.UploadAsync(new Progress<double>(), CancellationToken.None),
            Is.EqualTo(new Uri("https://example.com/original")));
    }

    private sealed class UploadExtension : VideoUploadExtension
    {
        public override IVideoUploadContext CreateContext() => new UploadContext();
        public override Control CreateControl(IVideoUploadContext context) => new Border { DataContext = context };
    }

    private sealed class UploadContext : IVideoUploadContext
    {
        public string Title { get; set; } = "original";
        public IReadOnlyReactiveProperty<bool> IsReady { get; } = new ReactivePropertySlim<bool>(true);
        public IVideoUploadOperation CreateUpload(string filePath) => new UploadOperation(Title);
        public void ReadFromJson(JsonObject json) => Title = json["Title"]!.GetValue<string>();
        public void WriteToJson(JsonObject json) => json["Title"] = Title;
        public void Dispose() { }
    }

    private sealed class UploadOperation(string title) : IVideoUploadOperation
    {
        public Task<Uri?> UploadAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(1);
            return Task.FromResult<Uri?>(new Uri("https://example.com/" + title));
        }

        public void Dispose() { }
    }
}
