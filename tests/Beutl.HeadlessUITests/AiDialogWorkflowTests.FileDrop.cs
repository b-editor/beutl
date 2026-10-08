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
using Beutl.Media.Source;
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
    [TestCase("corrupt", false)]
    [TestCase("corrupt", true)]
    [TestCase("encoded-size", true)]
    [TestCase("decoded-size", true)]
    public async Task FileDrop_InvalidSourceImageKeepsThePreviousSelectionAndPreview(string kind, bool hasSource)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var edit = CreateImageEditDialog(scope.Clients);
        await WaitUntilAsync(() => edit.ModelPicker.IsLoaded.Value);
        string? previous = hasSource ? scope.File("previous.png", s_png) : null;
        edit.SourceFilePath.Value = previous;
        if (hasSource)
            edit.ResultImage.Value = Ref<Beutl.Media.Bitmap>.Create(new Beutl.Media.Bitmap(1, 1));
        var previousPreview = edit.OriginalImage.Value;
        var previousBitmap = previousPreview?.Value;
        var previousResult = edit.ResultImage.Value;
        var previousResultBitmap = previousResult?.Value;
        var previousComparison = edit.SelectedComparisonMode.Value;
        Assert.That(edit.CanEdit.Value, Is.EqualTo(hasSource));

        string invalid = scope.File("invalid.png", kind == "corrupt" ? [1, 2, 3] : s_png);
        if (kind == "encoded-size")
        {
            using var stream = System.IO.File.OpenWrite(invalid);
            stream.SetLength(AiRequestLimits.MaxImageUploadBytes + 1);
        }
        else if (kind == "decoded-size")
        {
            using var bitmap = new Beutl.Media.Bitmap(8192, 2049);
            using var stream = System.IO.File.Create(invalid);
            bitmap.Save(stream, Beutl.Graphics.EncodedImageFormat.Png);
        }

        var view = new AiImageEditView { DataContext = edit };
        var window = ShowFileDropView(view);
        try
        {
            using var data = FileDropTransfer(invalid);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.SourceImage), data);
            Assert.That(edit.SourceFilePath.Value, Is.EqualTo(previous));
            Assert.That(edit.OriginalImage.Value, Is.SameAs(previousPreview));
            Assert.That(previousPreview?.Value, Is.SameAs(previousBitmap));
            Assert.That(edit.ResultImage.Value, Is.SameAs(previousResult));
            Assert.That(previousResult?.Value, Is.SameAs(previousResultBitmap));
            Assert.That(edit.SelectedComparisonMode.Value, Is.SameAs(previousComparison));
            Assert.That(edit.CanEdit.Value, Is.EqualTo(hasSource));
            Assert.That(edit.Error.Value, Is.Not.Null);

            string valid = scope.File("valid.png", s_png);
            using var validData = FileDropTransfer(valid);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.SourceImage), validData);
            Assert.That(edit.SourceFilePath.Value, Is.EqualTo(valid));
            Assert.That(edit.OriginalImage.Value, Is.Not.Null);
            Assert.That(edit.CanEdit.Value, Is.True);
            Assert.That(edit.Error.Value, Is.Null);
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
            Assert.That(group.Files.Select(file => file.Path), Is.EqualTo(new[] { first, second }.Take(group.MaximumCount)));
            Assert.That(video.ReferenceGroups.Where(other => other != group).All(other => other.Files.Count == 0), Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("image", "png")]
    [TestCase("video", "webm")]
    [TestCase("audio", "wave")]
    public async Task FileDrop_VideoReferencesRespectRemainingCapacityAndRejectFullGroups(string kind, string extension)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        video.Prompt.Value = "Animate the references";
        await WaitUntilAsync(() => video.ReferenceGroups.All(group => group.IsSupported.Value) && video.CanGenerate.Value);
        var group = video.ReferenceGroups.Single(group => group.Kind == kind);
        for (int i = 0; i < group.MaximumCount - 1; i++)
            group.Add(scope.File($"existing-{i}.{extension}", s_png));
        string[] previous = group.Files.Select(file => file.Path).ToArray();
        string[] dropped = Enumerable.Range(0, 3).Select(i => scope.File($"dropped-{i}.{extension}", s_png)).ToArray();
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.VideoReference, kind);
            using var data = FileDropTransfer(dropped);
            await DropFiles(window, target, data);
            string[] expected = [.. previous, dropped[0]];
            Assert.That(group.Files.Select(file => file.Path), Is.EqualTo(expected));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);

            Assert.That(RaiseFileDrag(target, DragDrop.DragEnterEvent, data).DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(target.Classes, Does.Not.Contain("dragover"));
            Assert.That(RaiseFileDrag(target, DragDrop.DropEvent, data).DragEffects, Is.EqualTo(DragDropEffects.None));
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            Assert.That(group.Files.Select(file => file.Path), Is.EqualTo(expected));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);
            Assert.That(video.ReferenceGroups.Where(other => other != group).All(other => other.Files.Count == 0), Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("image", "png", true)]
    [TestCase("video", "webm", true)]
    [TestCase("image", "png", false)]
    [TestCase("video", "webm", false)]
    public async Task FileDrop_VideoReferenceDuplicatesDoNotConsumeRemainingCapacity(string kind, string extension, bool existing)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        video.Prompt.Value = "Animate the references";
        await WaitUntilAsync(() => video.ReferenceGroups.All(group => group.IsSupported.Value) && video.CanGenerate.Value);
        var group = video.ReferenceGroups.Single(group => group.Kind == kind);
        int remaining = existing ? 1 : 2;
        for (int i = 0; i < group.MaximumCount - remaining; i++)
            group.Add(scope.File($"existing-{i}.{extension}", s_png));
        string[] previous = group.Files.Select(file => file.Path).ToArray();
        string first = scope.File($"first.{extension}", s_png);
        string second = scope.File($"second.{extension}", s_png);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.VideoReference, kind);
            using var data = FileDropTransfer(existing ? [previous[0], first] : [first, first, second]);
            await DropFiles(window, target, data);
            string[] expected = existing ? [.. previous, first] : [.. previous, first, second];
            Assert.That(group.Files.Select(file => file.Path), Is.EqualTo(expected));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("image", "png")]
    [TestCase("video", "webm")]
    [TestCase("audio", "wave")]
    public async Task FileDrop_VideoReferencesRejectFilesAboveTheModelByteLimit(string kind, string extension)
    {
        await TestReset.ResetShellAsync();
        const long maximumBytes = 128;
        await using var scope = new FileDropScope(maximumBytes);
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        video.Prompt.Value = "Animate the references";
        await WaitUntilAsync(() => video.ReferenceGroups.All(group => group.IsSupported.Value) && video.CanGenerate.Value);
        var group = video.ReferenceGroups.Single(group => group.Kind == kind);
        Assert.That(group.MaximumBytes, Is.EqualTo(maximumBytes));
        var previousGroup = video.ReferenceGroups.First(other => other != group);
        string previousExtension = previousGroup.Kind == "image" ? "png" : "webm";
        string previous = scope.File($"previous.{previousExtension}", s_png);
        previousGroup.Add(previous);
        string large = scope.File($"large.{extension}", s_png);
        using (var stream = System.IO.File.OpenWrite(large)) stream.SetLength(maximumBytes + 1);
        string valid = scope.File($"valid.{extension}", s_png);
        using (var stream = System.IO.File.OpenWrite(valid)) stream.SetLength(maximumBytes);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.VideoReference, kind);
            using var largeData = FileDropTransfer(large);
            Assert.That(RaiseFileDrag(target, DragDrop.DragEnterEvent, largeData).DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(target.Classes, Does.Not.Contain("dragover"));
            Assert.That(RaiseFileDrag(target, DragDrop.DropEvent, largeData).DragEffects, Is.EqualTo(DragDropEffects.None));
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            Assert.That(group.Files, Is.Empty);
            Assert.That(previousGroup.Files.Single().Path, Is.EqualTo(previous));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);

            using var mixedData = FileDropTransfer(large, valid);
            await DropFiles(window, target, mixedData);
            Assert.That(group.Files.Single().Path, Is.EqualTo(valid));
            Assert.That(previousGroup.Files.Single().Path, Is.EqualTo(previous));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task FileDrop_VideoReferencesRespectAggregateByteBudgets(bool imageBudget)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        video.Prompt.Value = "Animate the references";
        await WaitUntilAsync(() => video.ReferenceGroups.All(group => group.IsSupported.Value) && video.CanGenerate.Value);
        (string Kind, int MiB)[] inputs = imageBudget
            ? [("image", 5), ("image", 5), ("image", 5), ("image", 4)]
            : [("video", 20), ("image", 5), ("image", 5), ("image", 1)];
        for (int i = 0; i < inputs.Length; i++)
        {
            var (kind, size) = inputs[i];
            string extension = kind == "image" ? "png" : "webm";
            video.ReferenceGroups.Single(group => group.Kind == kind)
                .Add(scope.File($"existing-{i}.{extension}", s_png, size * 1048576L));
        }
        string[] previous = video.ReferenceGroups.SelectMany(group => group.Files).Select(file => file.Path).ToArray();
        Assert.That(video.InputError.Value, Is.Null);
        Assert.That(video.CanGenerate.Value, Is.True);
        var group = video.ReferenceGroups.Single(group => group.Kind == (imageBudget ? "image" : "audio"));
        string droppedExtension = imageBudget ? "png" : "wave";
        string large = scope.File($"large.{droppedExtension}", s_png, 2 * 1048576L);
        string valid = scope.File($"valid.{droppedExtension}", s_png, 1048576L);
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.VideoReference, group.Kind);
            using var largeData = FileDropTransfer(large);
            Assert.That(RaiseFileDrag(target, DragDrop.DragOverEvent, largeData).DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(RaiseFileDrag(target, DragDrop.DropEvent, largeData).DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(video.ReferenceGroups.SelectMany(group => group.Files).Select(file => file.Path), Is.EqualTo(previous));
            using var mixedData = FileDropTransfer(large, valid);
            await DropFiles(window, target, mixedData);
            Assert.That(group.Files.Any(file => file.Path == valid), Is.True);
            Assert.That(video.ReferenceGroups.SelectMany(group => group.Files).Select(file => file.Path).Where(path => path != valid), Is.EqualTo(previous));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileDrop_VideoReferencesAccountForTheWholeBatchByteBudget()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var video = CreateVideoGenerationDialog(scope.Clients);
        video.Prompt.Value = "Animate the references";
        await WaitUntilAsync(() => video.ReferenceGroups.All(group => group.IsSupported.Value) && video.CanGenerate.Value);
        string[] paths = Enumerable.Range(0, 3).Select(i => scope.File($"video-{i}.webm", s_png, 16 * 1048576L)).ToArray();
        var view = new AiVideoGenerationView { DataContext = video };
        var window = ShowFileDropView(view);
        try
        {
            using var data = FileDropTransfer(paths);
            await DropFiles(window, FindFileDropTarget(view, AiFileDropTarget.VideoReference, "video"), data);
            Assert.That(video.ReferenceGroups.Single(group => group.Kind == "video").Files.Select(file => file.Path), Is.EqualTo(paths.Take(2)));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.CanGenerate.Value, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("size")]
    [TestCase("too-short")]
    [TestCase("too-long")]
    public async Task FileDrop_UnsupportedSourceVideoKeepsThePreviousSelection(string kind)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope(maximumSourceVideoBytes: 128);
        await using var tasks = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(scope.Clients, sourceMode: mode));
        var video = tasks.ActiveContent.Value!;
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        string previous = scope.File("previous.webm", s_png);
        string candidate = scope.File("candidate.webm", s_png, kind == "size" ? 129 : 128);
        video.VideoDurationReader = path => TimeSpan.FromSeconds(path == candidate ? kind == "too-short" ? 1 : kind == "too-long" ? 11 : 4 : 4);
        await video.PickInputAsync("source", [previous]);
        Assert.That(video.InputError.Value, Is.Null);
        var view = new AiVideoEditingView { DataContext = tasks };
        var window = ShowFileDropView(view);
        try
        {
            Control target = FindFileDropTarget(view, AiFileDropTarget.SourceVideo);
            using var data = FileDropTransfer(candidate);
            await DropFiles(window, target, data);
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(previous));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(4));
            Assert.That(video.InputError.Value, Is.Null);
            Assert.That(video.Error.Value, Is.Not.Null);
            using var previousData = FileDropTransfer(previous);
            await DropFiles(window, target, previousData);
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(previous));
            Assert.That(video.Error.Value, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task FileDrop_PendingSourceProbeCannotPublishAfterGenerationStarts(bool probeFails)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var tasks = new AiVideoEditingViewModel(mode => CreateVideoGenerationDialog(scope.Clients, sourceMode: mode));
        var video = tasks.ActiveContent.Value!;
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        string previous = scope.File("previous.webm", s_png);
        string candidate = scope.File("candidate.webm", s_png);
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseProbe = new ManualResetEventSlim();
        video.VideoDurationReader = path =>
        {
            if (path == candidate)
            {
                probeStarted.TrySetResult();
                if (!releaseProbe.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                if (probeFails) throw new InvalidDataException("Pending source probe failed.");
                return TimeSpan.FromSeconds(8);
            }
            return TimeSpan.FromSeconds(4);
        };
        await video.PickInputAsync("source", [previous]);
        var view = new AiVideoEditingView { DataContext = tasks };
        var window = ShowFileDropView(view);
        Control target = FindFileDropTarget(view, AiFileDropTarget.SourceVideo);
        try
        {
            using var data = FileDropTransfer(candidate);
            RaiseFileDrag(target, DragDrop.DropEvent, data);
            await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            video.IsGenerating.Value = true;
            video.Error.Value = "Generation status";
            releaseProbe.Set();
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(previous));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(4));
            Assert.That(video.Error.Value, Is.EqualTo("Generation status"));
        }
        finally
        {
            releaseProbe.Set();
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            video.IsGenerating.Value = false;
            window.Close();
        }
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
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task FileDrop_EarlierVideoProbeCannotReplaceANewerBrowseSelection(bool earlierFails, bool browseCanceled)
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
        video.InputPicker = (_, _) => Task.FromResult<IReadOnlyList<string>>(browseCanceled ? [] : [newer]);
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
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(browseCanceled ? null : newer));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(browseCanceled ? null : (double?)4));
            releaseProbe.Set();
            await WaitUntilAsync(() => !target.Classes.Contains("filedropping"));
            Assert.That(video.SourceVideoPath.Value, Is.EqualTo(browseCanceled ? earlier : newer));
            Assert.That(video.SourceDuration.Value, Is.EqualTo(browseCanceled ? 8 : 4));
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
    public async Task FileDrop_RejectedCaptureRemovesItsUnpublishedTemporaryFile()
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        var editor = await OpenEditor("file-drop-rejected-capture");
        await using var video = CreateVideoGenerationDialog(scope.Clients, editor);
        await WaitUntilAsync(() => video.ModelPicker.IsLoaded.Value);
        video.CurrentFrameRenderer = _ => Task.FromResult(new Beutl.Media.Bitmap(2, 2));
        await video.CaptureCurrentFrame.ExecuteAsync();
        string previous = video.FirstFramePath.Value!;
        var previousPreview = video.FirstFramePreview.Value;
        string directory = Path.GetDirectoryName(previous)!;
        string[] before = Directory.GetFiles(directory, "frame-*.png");
        // Just above the decoded-pixel limit, with a compressible PNG below the upload-size limit.
        video.CurrentFrameRenderer = _ => Task.FromResult(new Beutl.Media.Bitmap(8192, 2049));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await video.CaptureCurrentFrame.ExecuteAsync();
            Assert.That(video.FirstFramePath.Value, Is.EqualTo(previous));
            Assert.That(video.FirstFramePreview.Value, Is.SameAs(previousPreview));
            Assert.That(video.Error.Value, Is.Not.Null);
            Assert.That(Directory.GetFiles(directory, "frame-*.png"), Is.EquivalentTo(before));
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task FileDrop_EarlierCaptionReadCannotReplaceANewerImport(bool earlierFails)
    {
        await TestReset.ResetShellAsync();
        await using var scope = new FileDropScope();
        await using var subtitles = CreateSubtitleDialog(scope.Clients);
        byte[] earlierBytes = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nEarlier caption\n");
        string earlier = scope.File("earlier.srt", earlierBytes);
        string newer = scope.File("newer.srt", Encoding.UTF8.GetBytes("1\n00:00:03,000 --> 00:00:04,000\nNewer caption\n"));
        using var blocked = new BlockedCaptionReadStream(earlierBytes, earlierFails);
        Task earlierImport = subtitles.ImportCaptionsCore(earlier, () => blocked);
        try
        {
            await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await subtitles.ImportCaptionsCore(newer);
            Assert.That(subtitles.Cues.Single().Text, Is.EqualTo("Newer caption"));
            blocked.Release.TrySetResult();
            await earlierImport;
            Assert.That(subtitles.Cues.Single().Text, Is.EqualTo("Newer caption"));
            Assert.That(subtitles.Error.Value, Is.Null);
        }
        finally
        {
            blocked.Release.TrySetResult();
            await earlierImport;
        }
    }

    private sealed class BlockedCaptionReadStream(byte[] bytes, bool fails) : MemoryStream(bytes)
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (fails) throw new InvalidDataException("Earlier caption read failed.");
            await base.CopyToAsync(destination, bufferSize, cancellationToken);
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

        public FileDropScope(long? maximumReferenceBytes = null, long? maximumSourceVideoBytes = null)
        {
            Directory.CreateDirectory(DirectoryPath);
            var capabilities = JsonNode.Parse(ProviderVideoCapabilities)!;
            var operations = (JsonObject)capabilities["operations"]!;
            foreach (string json in new[] { ImageCapabilitiesJson(), ImageEditCapabilitiesJson(), CaptionCapabilitiesJson(false) })
                foreach (var operation in (JsonObject)JsonNode.Parse(json)!["operations"]!)
                    operations[operation.Key] = operation.Value!.DeepClone();
            operations["video.generate"]!["models"]![0]!["firstFrame"] = true;
            operations["video.generate"]!["models"]![0]!["lastFrame"] = true;
            if (maximumReferenceBytes is { } maximum)
                foreach (string field in new[] { "maxInputReferenceBytes", "maxVideoReferenceBytes", "maxAudioReferenceBytes" })
                    operations["video.generate"]!["models"]![0]![field] = maximum;
            if (maximumSourceVideoBytes is { } sourceMaximum)
                operations["video.edit"]!["models"]![0]!["maxSourceVideoBytes"] = sourceMaximum;
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

        public string File(string name, byte[] bytes, long? length = null)
        {
            string path = Path.Combine(DirectoryPath, name);
            System.IO.File.WriteAllBytes(path, bytes);
            if (length is { } size)
            {
                using var stream = System.IO.File.OpenWrite(path);
                stream.SetLength(size);
            }
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
