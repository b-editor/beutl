using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Editor.Components.OutputTab;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class VideoUploadTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Upload_interrupted_before_context_entry_releases_its_job_and_allows_encode_and_retry(bool cancel)
    {
        using var fixture = await OutputFixture.CreateAsync();
        await fixture.Output.RunAsync(CancellationToken.None);
        using var item = new OutputProfileItem(fixture.Output, fixture.Editor, new InterruptedStartHost(cancel));

        Assert.That(fixture.Output.TryStartUpload(item, out Task? upload), Is.True);
        Exception? failure = await Failure(upload!);
        if (cancel) Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
        else Assert.That(failure, Is.TypeOf<IOException>());
        Assert.That(item.IsRunning.Value, Is.False);
        Assert.That(fixture.Extension.Uploads, Is.Empty);
        Assert.That(fixture.Extension.DisposalsOnUiThread, Is.EqualTo(new[] { true }));

        Assert.That(item.TryStart(out Task? encode), Is.True);
        await encode!;
        Assert.That(fixture.Encodes, Is.EqualTo(2), "Encode must not consume the abandoned upload.");
        Assert.That(fixture.Extension.Uploads, Is.Empty);

        Assert.That(fixture.Output.TryStartUpload(item, out upload), Is.True);
        await upload!;
        Assert.That(fixture.Encodes, Is.EqualTo(2));
        Assert.That(fixture.Extension.Uploads, Has.Count.EqualTo(1));
        Assert.That(fixture.Extension.DisposalsOnUiThread, Is.EqualTo(new[] { true, true }));
    }

    [AvaloniaTest]
    public async Task Manual_upload_uses_committed_file_without_reencoding_even_after_destination_changes()
    {
        using var fixture = await OutputFixture.CreateAsync();
        await fixture.Output.RunAsync(CancellationToken.None);
        Assert.That(fixture.Extension.Uploads, Is.Empty);
        Assert.That(fixture.Output.Upload.CompletedFile.Value, Is.EqualTo(fixture.FilePath));
        fixture.Output.DestinationFile.Value = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "next.mp4");

        using var item = new OutputProfileItem(fixture.Output, fixture.Editor, TestShell.Editor);
        Assert.That(fixture.Output.TryStartUpload(item, out Task? upload), Is.True);
        await upload!;

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Encodes, Is.EqualTo(1));
            Assert.That(fixture.Extension.Uploads.Single().Path, Is.EqualTo(fixture.FilePath));
            Assert.That(fixture.Extension.Uploads.Single().Contents, Is.EqualTo("complete video"));
            Assert.That(fixture.Output.Upload.UploadedUrl.Value, Is.EqualTo("https://example.com/video/1"));
            Assert.That(item.IsRunning.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Automatic_upload_snapshots_metadata_and_waits_for_committed_output()
    {
        using var fixture = await OutputFixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Encode = async (controller, token) =>
        {
            File.WriteAllText(controller.OutputFile, "complete video");
            started.SetResult();
            await release.Task.WaitAsync(token);
        };
        fixture.Output.Upload.Automatic.Value = true;
        Task execution = fixture.Output.RunAsync(CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(fixture.Extension.Uploads, Is.Empty);
            fixture.Context.Title = "changed for the next upload";
            fixture.Output.Upload.Automatic.Value = false;
        }
        finally { release.TrySetResult(); }
        await execution;

        Assert.That(fixture.Extension.Uploads.Single(), Is.EqualTo((fixture.FilePath, "original", "complete video")));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Failed_or_cancelled_encode_never_starts_upload(bool cancel)
    {
        using var fixture = await OutputFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        File.WriteAllText(fixture.FilePath, "previous video");
        fixture.Output.Upload.Automatic.Value = true;
        fixture.Encode = (controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "partial");
            if (cancel) cancellation.Cancel();
            else throw new IOException("encoding failed");
            return Task.CompletedTask;
        };
        Assert.That(await Failure(fixture.Output.RunAsync(cancellation.Token)), Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Extension.Uploads, Is.Empty);
            Assert.That(fixture.Output.Upload.CompletedFile.Value, Is.Null);
            Assert.That(File.ReadAllText(fixture.FilePath), Is.EqualTo("previous video"));
        });
    }

    [AvaloniaTest]
    public async Task Cancellation_between_encode_and_upload_keeps_the_completed_video()
    {
        using var fixture = await OutputFixture.CreateAsync();
        fixture.Output.Upload.Automatic.Value = true;
        using var cancellation = new CancellationTokenSource();
        using var subscription = fixture.Output.ProgressMain.Subscribe(value =>
        {
            if (value == Beutl.Language.Strings.Completed) cancellation.Cancel();
        });
        Assert.That(await Failure(fixture.Output.RunAsync(cancellation.Token)), Is.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Extension.Uploads, Is.Empty);
            Assert.That(fixture.Output.Upload.CompletedFile.Value, Is.EqualTo(fixture.FilePath));
            Assert.That(File.ReadAllText(fixture.FilePath), Is.EqualTo("complete video"));
            Assert.That(fixture.Output.WasCancelled.Value, Is.False, "Encoding completed successfully.");
        });
    }

    [AvaloniaTest]
    public async Task Upload_failure_preserves_successful_encode_and_retry_only_uploads()
    {
        using var fixture = await OutputFixture.CreateAsync();
        fixture.Output.Upload.Automatic.Value = true;
        fixture.Extension.Failure = new IOException("network failed");
        Assert.That(await Failure(fixture.Output.RunAsync(CancellationToken.None)), Is.TypeOf<IOException>());
        Assert.That(fixture.Output.ProgressMain.Value, Is.EqualTo(Beutl.Language.Strings.Completed));
        Assert.That(File.ReadAllText(fixture.FilePath), Is.EqualTo("complete video"));

        fixture.Extension.Failure = null;
        using var item = new OutputProfileItem(fixture.Output, fixture.Editor, TestShell.Editor);
        Assert.That(fixture.Output.TryStartUpload(item, out Task? upload), Is.True);
        await upload!;
        Assert.That(fixture.Encodes, Is.EqualTo(1));
        Assert.That(fixture.Output.Upload.UploadedUrl.Value, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task Upload_cancellation_and_double_click_share_the_host_lifetime()
    {
        using var fixture = await OutputFixture.CreateAsync();
        await fixture.Output.RunAsync(CancellationToken.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Extension.Run = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var item = new OutputProfileItem(fixture.Output, fixture.Editor, TestShell.Editor);
        Assert.That(fixture.Output.TryStartUpload(item, out Task? upload), Is.True);
        await started.Task;
        try
        {
            var view = new OutputView(item) { DataContext = fixture.Output };
            using var host = new ControlHost(new ScrollViewer { Content = view }, 420, 800);
            HeadlessTestHelpers.Render();
            TextBlock status = view.FindControl<TextBlock>("UploadStatusText")!;
            Assert.That(status.IsVisible, Is.True);
            Assert.That(status.Text, Is.EqualTo(Beutl.Language.Strings.VideoUpload_Uploading));
            view.FindControl<TabStrip>("SectionStrip")!.SelectedIndex = 1;
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<Grid>("RunningProgress")!.IsEffectivelyVisible, Is.True);
            Assert.That(view.FindControl<ToggleSwitch>("AutomaticUploadSwitch")!.IsEffectivelyEnabled, Is.False);
            var panel = view.FindControl<VideoUploadView>("UploadPanel")!;
            Assert.That(panel.FindControl<Button>("UploadButton")!.IsEffectivelyEnabled, Is.False);
            HeadlessCapture.SaveIfRequested(view, nameof(VideoUploadTests), "upload-running");
            Assert.That(fixture.Output.TryStartUpload(item, out _), Is.False);
            using IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation();
            Assert.That(mutation, Is.Null);
            view.FindControl<Button>("CancelOutputButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { item.Cancel(); }
        Assert.That(await Failure(upload!), Is.InstanceOf<OperationCanceledException>());
        Assert.That(File.ReadAllText(fixture.FilePath), Is.EqualTo("complete video"));
        Assert.That(fixture.Output.Upload.IsUploading.Value, Is.False);
        Assert.That(fixture.Extension.Uploads, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task Saved_upload_settings_and_completed_file_restore_but_do_not_enter_encoding_presets()
    {
        using var fixture = await OutputFixture.CreateAsync();
        await fixture.Output.RunAsync(CancellationToken.None);
        fixture.Context.Title = "saved title";
        fixture.Output.Upload.Automatic.Value = true;
        var json = new JsonObject();
        fixture.Output.WriteToJson(json);
        using var restored = new OutputViewModel(fixture.Editor);
        restored.ReadFromJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Upload.CompletedFile.Value, Is.EqualTo(fixture.FilePath));
            Assert.That(restored.Upload.Automatic.Value, Is.True);
            Assert.That(((FakeContext)restored.Upload.SettingsControl.Value!.DataContext!).Title, Is.EqualTo("saved title"));
            Assert.That(fixture.Output.ToPreset().ContainsKey("Upload"), Is.False);
        });
        using var presetTarget = new OutputViewModel(fixture.Editor);
        presetTarget.Apply(json);
        Assert.That(presetTarget.Upload.Automatic.Value, Is.False);
        Assert.That(presetTarget.Upload.CompletedFile.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task Audio_only_export_clears_the_video_record_and_still_encodes_normally()
    {
        using var fixture = await OutputFixture.CreateAsync();
        await fixture.Output.RunAsync(CancellationToken.None);
        fixture.Output.SelectedEncoder.Value = new AudioOnlyEncoder();
        await fixture.Output.RunAsync(CancellationToken.None);
        Assert.That(fixture.Output.Upload.CompletedFile.Value, Is.Null);
        Assert.That(fixture.Output.Upload.CanUpload.Value, Is.False);
        Assert.That(File.ReadAllText(fixture.FilePath), Is.EqualTo("audio only"));
        fixture.Output.Upload.Automatic.Value = true;
        Assert.That(fixture.Output.CanEncode.Value, Is.False);
        Assert.That(fixture.Output.AutoUploadVideoWarning.Value, Is.Not.Null);
        fixture.Output.Upload.Automatic.Value = false;
        Assert.That(fixture.Output.CanEncode.Value, Is.True);
    }

    [AvaloniaTest]
    public void Registry_changes_preserve_connections_and_keep_missing_provider_settings()
    {
        var registry = new ExtensionProvider();
        var extension = new FakeExtension();
        registry.AddExtensions(7701, [extension]);
        using var vm = new VideoUploadViewModel(registry);
        Control control = vm.SettingsControl.Value!;
        ((FakeContext)control.DataContext!).Title = "remember me";
        registry.AddExtensions(7702, [new TestEncoder()]);
        Assert.That(vm.SettingsControl.Value, Is.SameAs(control));
        registry.RemoveExtensions(7701);
        Assert.That(vm.IsReady.Value, Is.False);
        Assert.That(vm.SettingsControl.Value, Is.Null);
        registry.AddExtensions(7701, [extension]);
        Assert.That(((FakeContext)vm.SettingsControl.Value!.DataContext!).Title, Is.EqualTo("remember me"));
    }

    private static async Task<Exception?> Failure(Task task)
    {
        try { await task; return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class OutputFixture : IDisposable
    {
        private const int PackageId = 7700;
        public required EditViewModel Editor { get; init; }
        public required OutputViewModel Output { get; init; }
        public required FakeExtension Extension { get; init; }
        public required string FilePath { get; init; }
        public int Encodes { get; private set; }
        public Func<TestController, CancellationToken, Task>? Encode { get; set; }
        public FakeContext Context => (FakeContext)Output.Upload.SettingsControl.Value!.DataContext!;

        public static async Task<OutputFixture> CreateAsync()
        {
            TestGpu.RequireAnyDevice();
            var (_, _, editor) = await TestShell.OpenNewSceneAsync("video-upload", 32, 32,
                duration: MediaTime.FromSeconds(0.1));
            var extension = new FakeExtension();
            TestShell.Extensions.AddExtensions(PackageId, [extension]);
            var output = new OutputViewModel(editor);
            string file = Path.Combine(TestShell.ProjectRoot(editor), "movie.mp4");
            var fixture = new OutputFixture { Editor = editor, Output = output, Extension = extension, FilePath = file };
            output.DestinationFile.Value = file;
            output.SelectedEncoder.Value = new TestEncoder(encode: (controller, token) =>
            {
                fixture.Encodes++;
                if (fixture.Encode != null) return fixture.Encode(controller, token);
                File.WriteAllText(controller.OutputFile, "complete video");
                return Task.CompletedTask;
            });
            output.Upload.SelectedTarget.Value = output.Upload.Targets.Single(x => x.TypeName.Contains(nameof(FakeExtension)));
            return fixture;
        }

        public void Dispose()
        {
            Output.Dispose();
            TestShell.Extensions.RemoveExtensions(PackageId);
        }
    }

    private sealed class FakeExtension : VideoUploadExtension
    {
        public List<(string Path, string Title, string Contents)> Uploads { get; } = [];
        public List<bool> DisposalsOnUiThread { get; } = [];
        public Exception? Failure { get; set; }
        public Func<CancellationToken, Task>? Run { get; set; }
        public override IVideoUploadContext CreateContext() => new FakeContext(this);
        public override Control CreateControl(IVideoUploadContext context) => new Border { DataContext = context };

        public async Task<Uri?> Upload(string path, string title, CancellationToken token)
        {
            Uploads.Add((path, title, File.ReadAllText(path)));
            if (Failure != null) throw Failure;
            if (Run != null) await Run(token);
            return new Uri("https://example.com/video/1");
        }
    }

    private sealed class FakeContext(FakeExtension extension) : IVideoUploadContext
    {
        public string Title { get; set; } = "original";
        public IReadOnlyReactiveProperty<bool> IsReady { get; } = new ReactivePropertySlim<bool>(true);
        public IVideoUploadOperation CreateUpload(string filePath) => new FakeOperation(extension, filePath, Title);
        public void WriteToJson(JsonObject json) => json["Title"] = Title;
        public void ReadFromJson(JsonObject json) => Title = json["Title"]!.GetValue<string>();
        public void Dispose() { }
    }

    private sealed class FakeOperation(FakeExtension extension, string path, string title) : IVideoUploadOperation
    {
        public Task<Uri?> UploadAsync(IProgress<double> progress, CancellationToken cancellationToken)
            => extension.Upload(path, title, cancellationToken);
        public void Dispose() => extension.DisposalsOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
    }

    private sealed class InterruptedStartHost(bool cancel) : IOutputRunHost
    {
        private bool _interrupt = true;

        public IDisposable? TryBeginOutputOperation() => TestShell.Editor.TryBeginOutputOperation();

        public IDisposable TrackRunningOutput(OutputProfileItem item)
        {
            if (_interrupt)
            {
                _interrupt = false;
                if (cancel) Task.Run(item.Cancel).GetAwaiter().GetResult();
                else throw new IOException("tracking failed before context entry");
            }
            return TestShell.Editor.TrackRunningOutput(item);
        }
    }

    private sealed class AudioOnlyEncoder : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions() => [".mp4"];
        public override EncodingController CreateController(string file) => new AudioOnlyController(file);
    }

    private sealed class AudioOnlyController(string file) : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();
        public override AudioEncoderSettings AudioSettings { get; } = new();
        public override bool IsVideoEnabled => false;
        public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken cancellationToken)
        {
            File.WriteAllText(OutputFile, "audio only");
            return ValueTask.CompletedTask;
        }
    }
}
