using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;
using Moq;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    [TestCase("generation")]
    [TestCase("editing")]
    public async Task FileDrop_ImagesUseTheExistingSelectionAndPreviewPipeline(string kind)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var generation = CreateImageGenerationDialog(scope.Clients);
        await using var edit = CreateImageEditDialog(scope.Clients);
        await WaitUntilAsync(() => generation.ModelPicker.IsLoaded.Value && edit.ModelPicker.IsLoaded.Value);
        string first = scope.File("first.PNG", s_png);
        string second = scope.File("second.png", s_png);
        string unsupported = scope.File("unsupported.txt", s_png);
        Control view = kind == "generation"
            ? new AiImageGenerationView { DataContext = generation }
            : new AiImageEditView { DataContext = edit };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, kind == "generation" ? AiFileDropTarget.ReferenceImage : AiFileDropTarget.SourceImage);
            int parentDrops = 0;
            view.AddHandler(DragDrop.DropEvent, (_, _) => parentDrops++);
            using var data = FileDropTransfer(unsupported, first, second);
            await DropFiles(window, target, data);
            if (kind == "generation")
            {
                Assert.That(generation.ReferenceImages.Select(image => image.Path), Is.EqualTo(new[] { first, second }));
                Assert.That(generation.ReferenceImages.All(image => image.Preview != null), Is.True);
            }
            else
            {
                Assert.That(edit.SourceFilePath.Value, Is.EqualTo(first));
                Assert.That(edit.OriginalImage.Value, Is.Not.Null);
            }
            Assert.That(parentDrops, Is.Zero);
            Assert.That(target.Classes, Does.Not.Contain("dragover"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task FileDrop_VideoFramesUpdateTheCorrespondingPreview(bool first)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        string path = scope.File("frame.png", s_png);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, first ? AiFileDropTarget.FirstFrame : AiFileDropTarget.LastFrame);
            using var data = FileDropTransfer(path);
            await DropFiles(window, target, data);
            Assert.That(first ? video.FirstFramePath.Value : video.LastFramePath.Value, Is.EqualTo(path));
            Assert.That(first ? video.FirstFramePreview.Value : video.LastFramePreview.Value, Is.Not.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("image", "png")]
    [TestCase("video", "webm")]
    [TestCase("audio", "wave")]
    public async Task FileDrop_VideoReferencesAddMultipleFilesToTheirOwnGroup(string kind, string extension)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        await WaitUntilAsync(() => video.ReferenceGroups.All(group => group.IsSupported.Value));
        var group = video.ReferenceGroups.Single(group => group.Kind == kind);
        string first = scope.File($"first.{extension}", s_png);
        string second = scope.File($"second.{extension}", s_png);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.VideoReference, kind);
            using var data = FileDropTransfer(first, second);
            await DropFiles(window, target, data);
            Assert.That(group.Files.Select(file => file.Path), Is.EqualTo(new[] { first, second }));
            Assert.That(video.ReferenceGroups.Where(other => other != group).All(other => other.Files.Count == 0), Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileDrop_SourceVideoAndCharacterImageUseTheActiveEditingTask()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var tasks = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(scope.Clients, sourceMode: mode));
        tasks.SelectedTask.Value = tasks.Tasks.Single(task => task.Mode == AiSourceVideoMode.Motion);
        var video = tasks.ActiveContent.Value!;
        video.VideoDurationReader = _ => TimeSpan.FromSeconds(4);
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        string source = scope.File("source.webm", s_png);
        string character = scope.File("character.png", s_png);
        var view = new AiVideoEditingView { DataContext = tasks };
        var window = ShowFileDropView(view);
        try
        {
            using var sourceData = FileDropTransfer(source);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.SourceVideo), sourceData);
            using var characterData = FileDropTransfer(character);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.CharacterImage), characterData);
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(source));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(4));
            Assert.That(video.CharacterImagePath.Value, Is.EqualTo(character));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileDrop_CaptionsUseTheRegisteredDecoderWithoutDisposingTheBorrowedStorageFile()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var subtitles = CreateSubtitleDialog(scope.Clients);
        subtitles.SelectedSubtitlePageIndex.Value = 1;
        string path = scope.File("captions.SRT", Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nDropped caption\n"));
        var file = FileDropStorageFile(path);
        using var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file.Object));
        var view = new AiSubtitleView { DataContext = subtitles };
        var window = ShowFileDropView(view);
        try
        {
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.Captions), data);
            Assert.That(subtitles.Cues, Has.Count.EqualTo(1));
            Assert.That(subtitles.Cues[0].Text, Is.EqualTo("Dropped caption"));
            file.Verify(item => item.Dispose(), Times.Never);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("unsupported")]
    [TestCase("missing")]
    [TestCase("folder")]
    [TestCase("virtual")]
    [TestCase("busy")]
    [TestCase("disabled")]
    public async Task FileDrop_InvalidOrUnavailableTargetsDoNotChangeTheSelection(string kind)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var edit = CreateImageEditDialog(scope.Clients);
        var view = new AiImageEditView { DataContext = edit };
        var window = ShowFileDropView(view);
        using var data = new DataTransfer();
        if (kind == "folder")
        {
            var folder = new Mock<IStorageFolder>();
            folder.SetupGet(item => item.Path).Returns(new Uri(scope.DirectoryPath));
            data.Add(DataTransferItem.CreateFile(folder.Object));
        }
        else if (kind == "virtual")
        {
            var file = new Mock<IStorageFile>();
            file.SetupGet(item => item.Path).Returns(new Uri("https://example.invalid/image.png"));
            data.Add(DataTransferItem.CreateFile(file.Object));
        }
        else
        {
            string path = kind == "missing" ? Path.Combine(scope.DirectoryPath, "missing.png")
                : scope.File(kind == "unsupported" ? "image.txt" : "image.png", s_png);
            data.Add(DataTransferItem.CreateFile(FileDropStorageFile(path).Object));
        }
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.SourceImage);
            if (kind == "busy") edit.IsEditing.Value = true;
            if (kind == "disabled") target.IsEnabled = false;
            var over = RaiseFileDrag(target, DragDrop.DragOverEvent, data);
            var drop = RaiseFileDrag(target, DragDrop.DropEvent, data);
            Assert.That(over.DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(drop.DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(drop.Handled, Is.True);
            Assert.That(edit.SourceFilePath.Value, Is.Null);
            Assert.That(target.Classes, Does.Not.Contain("dragover"));
        }
        finally { edit.IsEditing.Value = false; window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileDrop_RevalidatesBusyStateAndKeepsTheImageReferenceLimit()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var generation = CreateImageGenerationDialog(scope.Clients);
        await WaitUntilAsync(() => generation.ModelPicker.IsLoaded.Value);
        var view = new AiImageGenerationView { DataContext = generation };
        var window = ShowFileDropView(view);
        using var data = FileDropTransfer(Enumerable.Range(0, 6).Select(i => scope.File($"image-{i}.png", s_png)).ToArray());
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.ReferenceImage);
            Assert.That(RaiseFileDrag(target, DragDrop.DragEnterEvent, data).DragEffects, Is.EqualTo(DragDropEffects.Copy));
            generation.IsGenerating.Value = true;
            Assert.That(RaiseFileDrag(target, DragDrop.DropEvent, data).DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(generation.ReferenceImages, Is.Empty);
            generation.IsGenerating.Value = false;
            await DropFiles(window, target, data);
            Assert.That(generation.ReferenceImages.Count, Is.EqualTo(generation.MaxReferenceImages.Value));
            Assert.That(RaiseFileDrag(target, DragDrop.DragOverEvent, data).DragEffects, Is.EqualTo(DragDropEffects.None));
        }
        finally { generation.IsGenerating.Value = false; window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileDrop_CaptionDecodeFailureKeepsTheCurrentCues()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var subtitles = CreateSubtitleDialog(scope.Clients);
        subtitles.SelectedSubtitlePageIndex.Value = 1;
        subtitles.ImportCaptionBytes(Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nExisting caption\n"), Beutl.Editor.Services.Captions.CaptionFormats.Srt);
        string path = scope.File("invalid.srt", [0xff, 0xfe]);
        var view = new AiSubtitleView { DataContext = subtitles };
        var window = ShowFileDropView(view);
        try
        {
            using var data = FileDropTransfer(path);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.Captions), data);
            Assert.That(subtitles.Cues, Has.Count.EqualTo(1));
            Assert.That(subtitles.Cues[0].Text, Is.EqualTo("Existing caption"));
            Assert.That(subtitles.Error.Value, Is.Not.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileDrop_OversizedFrameKeepsThePreviousSelection()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        string previous = scope.File("previous.png", s_png);
        string oversized = scope.File("large.png", s_png);
        using (var stream = System.IO.File.OpenWrite(oversized)) stream.SetLength(AiRequestLimits.MaxFrameUploadBytes + 1);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.FirstFrame);
            using var previousData = FileDropTransfer(previous);
            await DropFiles(window, target, previousData);
            using var oversizedData = FileDropTransfer(oversized);
            await DropFiles(window, target, oversizedData);
            Assert.That(video.FirstFramePath.Value, Is.EqualTo(previous));
            Assert.That(video.FirstFramePreview.Value, Is.Not.Null);
            Assert.That(video.Error.Value, Is.Not.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task FileDrop_CorruptFrameKeepsThePreviousPreviewAndTemporaryFile(bool first, bool captured)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        var editor = captured ? await OpenEditor("file-drop-corrupt-captured-frame") : null;
        await using var video = CreateVideoGenerationDialog(scope.Clients, editor);
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        if (captured)
        {
            video.CurrentFrameRenderer = _ => Task.FromResult(new Beutl.Media.Bitmap(2, 2));
            await video.CaptureCurrentFrame.ExecuteAsync();
        }
        else
        {
            await video.SelectFrameAsync(first, scope.File("previous.png", s_png));
        }
        string previous = (first ? video.FirstFramePath.Value : video.LastFramePath.Value)!;
        var previousPreview = first ? video.FirstFramePreview.Value : video.LastFramePreview.Value;
        var previousBitmap = previousPreview!.Value;
        string corrupt = scope.File("corrupt.png", [1, 2, 3]);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            using var data = FileDropTransfer(corrupt);
            await DropFiles(window, FindFileDropTarget(view, first ? AiFileDropTarget.FirstFrame : AiFileDropTarget.LastFrame), data);
            Assert.That(first ? video.FirstFramePath.Value : video.LastFramePath.Value, Is.EqualTo(previous));
            Assert.That(first ? video.FirstFramePreview.Value : video.LastFramePreview.Value, Is.SameAs(previousPreview));
            Assert.That(previousPreview.Value, Is.SameAs(previousBitmap));
            Assert.That(System.IO.File.Exists(previous), Is.True);
            Assert.That(video.Error.Value, Is.Not.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task FileDrop_EarlierVideoProbeCannotReplaceANewerBrowseSelection(bool earlierFails)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var tasks = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(scope.Clients, sourceMode: mode));
        var video = tasks.ActiveContent.Value!;
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        string earlier = scope.File("earlier.webm", s_png);
        string newer = scope.File("newer.webm", s_png);
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseProbe = new ManualResetEventSlim();
        video.VideoDurationReader = path =>
        {
            if (path == earlier)
            {
                probeStarted.TrySetResult();
                if (!releaseProbe.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                if (earlierFails) throw new InvalidDataException("Earlier video probe failed.");
                return TimeSpan.FromSeconds(8);
            }
            return TimeSpan.FromSeconds(4);
        };
        video.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>([newer]);
        var view = new AiVideoEditingView { DataContext = tasks };
        var window = ShowFileDropView(view);
        Control target = FindFileDropTarget(view, AiFileDropTarget.SourceVideo);
        try
        {
            using var data = FileDropTransfer(earlier);
            RaiseFileDrag(target, DragDrop.DropEvent, data);
            await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(target.Classes, Does.Contain("filedropping"));
            await video.SelectSourceVideo.ExecuteAsync();
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(newer));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(4));
            releaseProbe.Set();
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(newer));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(4));
            Assert.That(video.Error.Value, Is.Null);
        }
        finally
        {
            releaseProbe.Set();
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task FileDrop_OversizedCaptionsKeepTheCurrentCues()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var subtitles = CreateSubtitleDialog(scope.Clients);
        subtitles.SelectedSubtitlePageIndex.Value = 1;
        subtitles.ImportCaptionBytes(Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nExisting caption\n"), Beutl.Editor.Services.Captions.CaptionFormats.Srt);
        string oversized = scope.File("large.srt", [1]);
        using (var stream = System.IO.File.OpenWrite(oversized)) stream.SetLength(AiCaptionHistoryResultParser.MaximumResultBytes + 1L);
        var view = new AiSubtitleView { DataContext = subtitles };
        var window = ShowFileDropView(view);
        try
        {
            using var data = FileDropTransfer(oversized);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.Captions), data);
            Assert.That(subtitles.Cues, Has.Count.EqualTo(1));
            Assert.That(subtitles.Cues[0].Text, Is.EqualTo("Existing caption"));
            Assert.That(subtitles.Error.Value, Is.Not.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [Explicit("Produces headless captures of AI file drop targets.")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task FileDrop_CaptureTargets(bool light)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var generation = CreateImageGenerationDialog(scope.Clients);
        await using var edit = CreateImageEditDialog(scope.Clients);
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        await using var subtitles = CreateSubtitleDialog(scope.Clients);
        await using var editing = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(scope.Clients, sourceMode: mode));
        editing.SelectedTask.Value = editing.Tasks.Single(task => task.Mode == AiSourceVideoMode.Motion);
        subtitles.SelectedSubtitlePageIndex.Value = 1;
        await WaitUntilAsync(() => generation.ModelPicker.IsLoaded.Value && video.ModelPicker.IsLoaded.Value
            && edit.ModelPicker.IsLoaded.Value && editing.ActiveContent.Value!.ModelPicker.IsLoaded.Value);
        var views = new (string Name, Control View)[]
        {
            ("image-generation", new AiImageGenerationView { DataContext = generation }),
            ("image-editing", new AiImageEditView { DataContext = edit }),
            ("video-generation", new AiVideoGenerationView { DataContext = video }),
            ("video-editing", new AiVideoEditingView { DataContext = editing }),
            ("captions", new AiSubtitleView { DataContext = subtitles }),
        };
        foreach (var (name, view) in views)
        {
            var window = ShowFileDropView(view, 480);
            window.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            try
            {
                foreach (var expander in view.GetVisualDescendants().OfType<Expander>())
                    if (!expander.GetVisualDescendants().OfType<Control>().Any(control => AiFileDrop.GetTarget(control) != AiFileDropTarget.None))
                        expander.IsExpanded = false;
                HeadlessTestHelpers.Render(3);
                Control[] targets = view.GetVisualDescendants().OfType<Control>()
                    .Where(control => AiFileDrop.GetTarget(control) != AiFileDropTarget.None && control.IsEffectivelyVisible).ToArray();
                foreach (Control target in targets)
                {
                    string extension = AiFileDrop.GetTarget(target) switch
                    {
                        AiFileDropTarget.Captions => "srt",
                        AiFileDropTarget.SourceVideo => "webm",
                        AiFileDropTarget.VideoReference when target.DataContext is AiVideoInputGroup group => group.Kind == "image" ? "png" : group.Kind == "video" ? "webm" : "wav",
                        _ => "png",
                    };
                    using var data = FileDropTransfer(scope.File($"capture.{extension}", s_png));
                    Assert.That(RaiseFileDrag(target, DragDrop.DragEnterEvent, data).DragEffects, Is.EqualTo(DragDropEffects.Copy), name);
                    Assert.That(((Panel)target).Background,
                        Is.SameAs(target.FindResource(target.ActualThemeVariant, "AiFileDropBackgroundBrush")), name);
                }
                HeadlessTestHelpers.Render(3);
                string directory = Environment.GetEnvironmentVariable("BEUTL_AI_FILE_DROP_CAPTURE_DIR")
                    ?? Path.Combine(Path.GetTempPath(), "beutl-ai-file-drop-captures");
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"{name}-{(light ? "light" : "dark")}.png"), PngBitmapEncoderOptions.Default);
            }
            finally { window.Close(); }
        }
    }

    private static Window ShowFileDropView(Control view, int width = 640)
    {
        var window = new Window { Content = view, Width = width, Height = 1500 };
        window.Show();
        HeadlessTestHelpers.Render(3);
        foreach (var expander in view.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
        HeadlessTestHelpers.Render(3);
        return window;
    }

    private static Control FindFileDropTarget(Control view, AiFileDropTarget kind, string? referenceKind = null)
        => view.GetVisualDescendants().OfType<Control>().Single(control => AiFileDrop.GetTarget(control) == kind
            && (referenceKind is null || control.DataContext is AiVideoInputGroup group && group.Kind == referenceKind));

    private static async Task DropFiles(Window window, Control target, IDataTransfer data)
    {
        target.BringIntoView();
        HeadlessTestHelpers.Render(3);
        Assert.That(RaiseFileDrag(target, DragDrop.DragOverEvent, data).DragEffects,
            Is.EqualTo(DragDropEffects.Copy), $"Direct acceptance for {AiFileDrop.GetTarget(target)}");
        RaiseFileDrag(target, DragDrop.DragLeaveEvent, data);
        // The subtitle preview can finish between drag events and move the input panel.
        Point point = DropPoint();
        window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        HeadlessTestHelpers.Render(3);
        point = DropPoint();
        window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy);
        Assert.That(target.Classes, Does.Contain("dragover"),
            $"{AiFileDrop.GetTarget(target)} at {point}; hit={window.InputHitTest(point)?.GetType().Name}");
        HeadlessTestHelpers.Render(3);
        window.DragDrop(DropPoint(), RawDragEventType.Drop, data, DragDropEffects.Copy);
        await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
        HeadlessTestHelpers.Render();

        Point DropPoint() => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
    }

    private static DragEventArgs RaiseFileDrag(Control target, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data)
    {
        var e = new DragEventArgs(routedEvent, data, target, new Point(10, 10), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        target.RaiseEvent(e);
        return e;
    }

    private static Mock<IStorageFile> FileDropStorageFile(string path)
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(item => item.Path).Returns(new Uri(path));
        file.SetupGet(item => item.Name).Returns(Path.GetFileName(path));
        return file;
    }

    private static DataTransfer FileDropTransfer(params string[] paths)
    {
        var data = new DataTransfer();
        foreach (string path in paths) data.Add(DataTransferItem.CreateFile(FileDropStorageFile(path).Object));
        return data;
    }

    private sealed class FileDropScope : IAsyncDisposable
    {
        private readonly StubHandler _handler;
        private readonly HttpClient _http;
        public string DirectoryPath { get; } = Path.Combine(BeutlHomeIsolation.CurrentHome!, "file-drop-" + Guid.NewGuid().ToString("N"));
        public BeutlApiApplication Clients { get; }

        public FileDropScope()
        {
            Directory.CreateDirectory(DirectoryPath);
            var capabilities = JsonNode.Parse(ProviderVideoCapabilities)!;
            var operations = (JsonObject)capabilities["operations"]!;
            foreach (string json in new[] { ImageCapabilitiesJson(), ImageEditCapabilitiesJson(), CaptionCapabilitiesJson(false) })
                foreach (var operation in (JsonObject)JsonNode.Parse(json)!["operations"]!)
                    operations[operation.Key] = operation.Value!.DeepClone();
            operations["video.generate"]!["models"]![0]!["firstFrame"] = true;
            operations["video.generate"]!["models"]![0]!["lastFrame"] = true;
            _handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
            {
                "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
                "/api/v3/ai/capabilities" => JsonResponse(HttpStatusCode.OK, capabilities.ToJsonString()),
                "/api/v3/user/ai-availability" => JsonResponse(HttpStatusCode.OK, "{\"available\":true}"),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
            });
            _http = new HttpClient(_handler);
            Clients = new BeutlApiApplication(_http, new ExtensionProvider());
            SetAuthenticatedUser(Clients, _http);
        }

        public string File(string name, byte[] bytes)
        {
            string path = Path.Combine(DirectoryPath, name);
            System.IO.File.WriteAllBytes(path, bytes);
            return path;
        }

        public async ValueTask DisposeAsync()
        {
            await Clients.DisposeAsync();
            _http.Dispose();
            _handler.Dispose();
            foreach (string path in Directory.GetFiles(DirectoryPath)) System.IO.File.Delete(path);
            Directory.Delete(DirectoryPath);
        }
    }
}
