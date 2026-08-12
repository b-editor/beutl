using System.Net;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiDialogWorkflowTests
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [AvaloniaTest]
    public void ImageSaveLease_SurvivesResultReplacement()
    {
        var owner = Ref<Bitmap>.Create(new Bitmap(2, 2));
        using Ref<Bitmap>? saveLease = AiResultImageLease.Acquire(owner);

        owner.Dispose();

        using var output = new MemoryStream();
        Assert.DoesNotThrow(() => saveLease!.Value.Save(output, EncodedImageFormat.Png));
        Assert.That(output.Length, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    public async Task ImageGeneration_GeneratesAndImportsResult()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-image-dialog");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/images" => JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file")),
            "/api/contents/image-file" => ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        await viewModel.AddToScene.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        string persistedElement = CoreSerializer.SerializeToJsonObject(editor.Scene.Children.Single()).ToJsonString();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
            Assert.That(persistedElement, Does.Not.Contain("image-job"));
            Assert.That(persistedElement, Does.Not.Contain("image-file"));
            Assert.That(persistedElement, Does.Not.Contain("A calm blue sky"));
        });
    }

    [AvaloniaTest]
    public async Task ImageEdit_UsesResultSnapshotWhenTaskChangesBeforeImport()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-image-edit-dialog");
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, s_png);
        try
        {
            using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
            {
                "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
                "/api/v3/ai/images/edit" => JsonResponse(HttpStatusCode.OK, ImageResponseJson("edit-job", "edit-file")),
                "/api/contents/edit-file" => ByteResponse(s_png, "image/png"),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
            });
            using var httpClient = new HttpClient(handler);
            using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
            SetAuthenticatedUser(clients, httpClient);
            using var viewModel = CreateImageEditDialog(clients, editor);
            viewModel.SourceFilePath.Value = sourcePath;
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value && viewModel.CanEdit.Value);

            await viewModel.Edit.ExecuteAsync();
            viewModel.SelectedTask.Value = viewModel.Tasks.Single(task => task.Value == "upscale");
            await viewModel.AddToScene.ExecuteAsync();
            HeadlessTestHelpers.Settle();

            string persistedElement = CoreSerializer.SerializeToJsonObject(editor.Scene.Children.Single()).ToJsonString();
            Assert.Multiple(() =>
            {
                Assert.That(persistedElement, Does.Not.Contain("edit-job"));
                Assert.That(persistedElement, Does.Not.Contain("edit-file"));
            });
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_PollsResultAndDisposeRemovesPreviewFile()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-dialog");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/videos" => JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "video-job",
                  "status": "queued"
                }
                """),
            "/api/v3/ai/videos/video-job" => JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "video-job",
                  "status": "succeeded",
                  "fileId": "video-file",
                  "url": "https://beutl.beditor.net/api/contents/video-file",
                  "error": null
                }
                """),
            "/api/contents/video-file" => ByteResponse([1, 2, 3, 4], "video/mp4"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A slow camera pan";

        await viewModel.Generate.ExecuteAsync();

        string resultPath = viewModel.ResultVideoPath.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(resultPath, Is.Not.Null);
            Assert.That(File.Exists(resultPath), Is.True);
            Assert.That(viewModel.StatusText.Value, Is.EqualTo(Beutl.Language.Strings.AiVideoCompleted));
        });

        await viewModel.DisposeAsync();
        Assert.That(File.Exists(resultPath), Is.False);
    }

    [AvaloniaTest]
    public async Task VideoGeneration_ReplacingResultRemovesSupersededPreviewFile()
    {
        await TestReset.ResetShellAsync();
        int submissions = 0;
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (path == "/api/v3/ai/videos" && request.Method == HttpMethod.Post)
            {
                int number = Interlocked.Increment(ref submissions);
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    jobId = $"video-job-{number}",
                    status = "queued",
                }));
            }
            if (path.StartsWith("/api/v3/ai/videos/video-job-", StringComparison.Ordinal))
            {
                string number = path[(path.LastIndexOf('-') + 1)..];
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    jobId = $"video-job-{number}",
                    status = "succeeded",
                    fileId = $"video-file-{number}",
                    url = $"https://beutl.beditor.net/api/contents/video-file-{number}",
                    error = (string?)null,
                }));
            }
            if (path.StartsWith("/api/contents/video-file-", StringComparison.Ordinal))
                return ByteResponse([1, 2, 3, 4], "video/mp4");
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "Replace this preview";

        await viewModel.Generate.ExecuteAsync();
        string firstPath = viewModel.ResultVideoPath.Value!;
        await viewModel.Generate.ExecuteAsync();
        string secondPath = viewModel.ResultVideoPath.Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
            Assert.That(File.Exists(firstPath), Is.False);
            Assert.That(File.Exists(secondPath), Is.True);
        }

        await viewModel.DisposeAsync();
        Assert.That(File.Exists(secondPath), Is.False);
    }

    [AvaloniaTest]
    public async Task ProviderFailure_ShowsRefundSafeRetryMessage()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/images" => JsonResponse(HttpStatusCode.InternalServerError, """
                { "error_code": "aiProviderError", "message": "Provider failed.", "documentation_url": null }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A prompt";

        await viewModel.Generate.ExecuteAsync();

        Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));
    }

    [AvaloniaTest]
    public async Task ClosingImageTool_CancelsInFlightRequestWithoutPostDisposeMutation()
    {
        await TestReset.ResetShellAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/images")
            {
                requestStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "Cancel this request";

        Task operation = viewModel.Generate.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Dispose();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await operation;
        await viewModel.DisposeAsync();
    }

    [AvaloniaTest]
    public async Task DisposingImageDialog_AwaitsCancellationIgnoringRequestAndRejectsLateResult()
    {
        await TestReset.ResetShellAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int contentRequests = 0;
        using var handler = new StubHandler(async (request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/images")
            {
                requestStarted.TrySetResult();
                await releaseRequest.Task;
                return JsonResponse(HttpStatusCode.OK, ImageResponseJson("late-job", "late-file"));
            }
            if (request.RequestUri?.AbsolutePath == "/api/contents/late-file")
            {
                Interlocked.Increment(ref contentRequests);
                return ByteResponse(s_png, "image/png");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "Do not publish";
        int resultPublications = 0;
        using IDisposable publication = viewModel.ResultImage
            .Where(image => image is not null)
            .Subscribe(_ => resultPublications++);

        Task operation = viewModel.Generate.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = viewModel.DisposeAsync().AsTask();

        Assert.That(disposal.IsCompleted, Is.False);
        releaseRequest.TrySetResult();
        await operation;
        await disposal;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultPublications, Is.Zero);
            Assert.That(contentRequests, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task DisposingImageEditDialog_AwaitsOperationAndRejectsLateResult()
    {
        await TestReset.ResetShellAsync();
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, s_png);
        try
        {
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int contentRequests = 0;
            using var handler = new StubHandler(async (request, _) =>
            {
                if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                if (request.RequestUri?.AbsolutePath == "/api/v3/ai/images/edit")
                {
                    requestStarted.TrySetResult();
                    await releaseRequest.Task;
                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("late-edit", "late-edit-file"));
                }
                if (request.RequestUri?.AbsolutePath == "/api/contents/late-edit-file")
                {
                    Interlocked.Increment(ref contentRequests);
                    return ByteResponse(s_png, "image/png");
                }

                return JsonResponse(HttpStatusCode.NotFound, "{}");
            });
            using var httpClient = new HttpClient(handler);
            using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
            SetAuthenticatedUser(clients, httpClient);
            var viewModel = CreateImageEditDialog(clients);
            viewModel.SourceFilePath.Value = sourcePath;
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value && viewModel.CanEdit.Value);
            int resultPublications = 0;
            using IDisposable publication = viewModel.ResultImage
                .Where(image => image is not null)
                .Subscribe(_ => resultPublications++);

            Task operation = viewModel.Edit.ExecuteAsync();
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disposal = viewModel.DisposeAsync().AsTask();

            Assert.That(disposal.IsCompleted, Is.False);
            releaseRequest.TrySetResult();
            await operation;
            await disposal;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resultPublications, Is.Zero);
                Assert.That(contentRequests, Is.Zero);
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task DisposingVideoDialog_CancelsSubmissionAndLeavesNoPreviewFile()
    {
        await TestReset.ResetShellAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string resultDirectory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Results");
        string[] filesBefore = Directory.Exists(resultDirectory)
            ? Directory.GetFiles(resultDirectory, "ai-video-*.mp4")
            : [];
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/videos")
            {
                requestStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "Cancel this video";

        Task operation = viewModel.Generate.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = viewModel.DisposeAsync().AsTask();

        await operation;
        await disposal;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            Directory.Exists(resultDirectory)
                ? Directory.GetFiles(resultDirectory, "ai-video-*.mp4")
                : [],
            Is.EqualTo(filesBefore));
    }

    [AvaloniaTest]
    public async Task DisposingVideoDialog_DuringBlockedFrameRenderPublishesNoFileOrPreview()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-frame-capture-disposal");
        var renderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRender = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        string inputDirectory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Inputs");
        string[] filesBefore = Directory.Exists(inputDirectory)
            ? Directory.GetFiles(inputDirectory, "frame-*.png")
            : [];
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients, editor);
        viewModel.CurrentFrameRenderer = async _ =>
        {
            renderStarted.TrySetResult();
            return await releaseRender.Task;
        };

        Task operation = viewModel.CaptureCurrentFrame.ExecuteAsync();
        await renderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int publishedFramePaths = 0;
        using IDisposable publication = viewModel.FirstFramePath
            .Where(path => path is not null)
            .Subscribe(_ => publishedFramePaths++);
        Task disposal = viewModel.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        releaseRender.TrySetResult(new Bitmap(2, 2));
        await operation;
        await disposal;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(publishedFramePaths, Is.Zero);
            Assert.That(
                Directory.Exists(inputDirectory)
                    ? Directory.GetFiles(inputDirectory, "frame-*.png")
                    : [],
                Is.EqualTo(filesBefore));
        }
    }

    [AvaloniaTest]
    public async Task CapturingOrClearingFrame_RemovesSupersededTemporaryInput()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-frame-capture-replacement");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        await using var viewModel = CreateVideoGenerationDialog(clients, editor);
        viewModel.CurrentFrameRenderer = _ => Task.FromResult(new Bitmap(2, 2));

        await viewModel.CaptureCurrentFrame.ExecuteAsync();
        string firstPath = viewModel.FirstFramePath.Value!;
        await viewModel.CaptureCurrentFrame.ExecuteAsync();
        string secondPath = viewModel.FirstFramePath.Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
            Assert.That(File.Exists(firstPath), Is.False);
            Assert.That(File.Exists(secondPath), Is.True);
        }

        viewModel.ClearFirstFrame.Execute();
        Assert.That(File.Exists(secondPath), Is.False);
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_BatchesAtomicallyAndImportsTranslatedCaptions()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation");
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using JsonDocument json = JsonDocument.Parse(body);
                object[] segments = json.RootElement.GetProperty("segments")
                    .EnumerateArray()
                    .Select(segment => (object)new
                    {
                        id = segment.GetProperty("id").GetString(),
                        text = "T-" + segment.GetProperty("text").GetString(),
                    })
                    .ToArray();
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    jobId = $"translation-{requestNumber}",
                    segments,
                }));
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value = Enumerable.Range(0, 201)
            .Select(index => new AiTranscriptionSegment
            {
                Start = index * 2,
                End = index * 2 + 1,
                Text = $"line-{index}",
            })
            .ToArray();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);

        await viewModel.Translate.ExecuteAsync();
        await viewModel.AddToScene.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues, Has.Count.EqualTo(201));
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-line-0"));
            Assert.That(editor.Scene.Children, Has.Count.EqualTo(201));
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_SecondBatchFailureKeepsPaidPartialAndResumesWithoutRebilling()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation-failure");
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                return requestNumber is 1 or 3
                    ? CreateTranslationResponse(request, "translation-first")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value = CreateTranslationBatchSegments();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);
        string[] originalTexts = viewModel.Cues.Select(cue => cue.Text).ToArray();

        await viewModel.Translate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(originalTexts));
            Assert.That(viewModel.IsTranslating.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
            Assert.That(viewModel.PartialResultMessage.Value, Does.Contain("1"));
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-line-0"));
            Assert.That(viewModel.Cues[^1].Text, Is.EqualTo("line-200"));
        });

        await viewModel.Translate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(3),
                "The completed first batch must not be submitted and billed again.");
            Assert.That(viewModel.Cues.All(cue => cue.Text.StartsWith("T-", StringComparison.Ordinal)), Is.True);
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_SecondBatchCancellationPreservesOriginalCues()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation-cancel");
        int translationRequests = 0;
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                if (requestNumber == 1)
                    return CreateTranslationResponse(request, "translation-first");

                secondRequestStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value = CreateTranslationBatchSegments();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);
        string[] originalTexts = viewModel.Cues.Select(cue => cue.Text).ToArray();

        Task operation = viewModel.Translate.ExecuteAsync();
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        GetLifetimeCancellationSource(viewModel).Cancel();
        await operation;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(originalTexts));
            Assert.That(viewModel.IsTranslating.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-line-0"));
            Assert.That(viewModel.Cues[^1].Text, Is.EqualTo("line-200"));
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_PaidPartialSurvivesDialogCloseAndStillResumes()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "translation-restart-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        IObservable<CaptionDraftScope?> draftScopes =
            Observable.Return<CaptionDraftScope?>(draftScope);
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                return requestNumber is 1 or 3
                    ? CreateTranslationResponse(request, $"translation-{requestNumber}")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);

        using (var firstDialog = CreateSubtitleDialog(
                   clients,
                   draftStore: draftStore,
                   draftScopes: draftScopes))
        {
            await WaitUntilAsync(() => firstDialog.Usage.HasSnapshot.Value);
            firstDialog.ResultSegments.Value = CreateTranslationBatchSegments();
            await WaitUntilAsync(() => firstDialog.CanTranslate.Value);
            await firstDialog.Translate.ExecuteAsync();
            Assert.That(firstDialog.HasPartialResult.Value, Is.True);
        }
        AssertStoredCaptionDraftJob(draftStore, draftScope, "translation-1");

        using var restoredDialog = CreateSubtitleDialog(
            clients,
            draftStore: draftStore,
            draftScopes: draftScopes);
        await WaitUntilAsync(() => restoredDialog.Usage.HasSnapshot.Value);
        Assert.That(restoredDialog.HasPartialResult.Value, Is.True);
        restoredDialog.ApplyPartialResult.Execute();
        await WaitUntilAsync(() => restoredDialog.CanTranslate.Value);
        await restoredDialog.Translate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(3));
            Assert.That(restoredDialog.Cues, Has.Count.EqualTo(201));
            Assert.That(
                restoredDialog.Cues.All(cue => cue.Text.StartsWith("T-", StringComparison.Ordinal)),
                Is.True);
            Assert.That(restoredDialog.HasPartialResult.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_EditDuringDeferredResponsePreservesUserRevisionAndJobMetadata()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation-stale-response");
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "translation-stale-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        var translationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                translationStarted.TrySetResult();
                await releaseTranslation.Task.WaitAsync(cancellationToken);
                return CreateTranslationResponse(request, "translation-stale");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(
            clients,
            editor,
            draftStore,
            Observable.Return<CaptionDraftScope?>(draftScope));
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 1, Text = "original caption" },
        ];
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);

        Task operation = viewModel.Translate.ExecuteAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Cues[0].Text = "user edit while translating";
        releaseTranslation.TrySetResult();
        await operation;
        AssertStoredCaptionDraftJob(draftStore, draftScope, "translation-stale");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(1));
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("user edit while translating"));
            Assert.That(viewModel.IsTranslating.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.HasPartialResult.Value, Is.True,
                "A paid response must remain recoverable when the editor revision changes.");
        }

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-original caption"));
        });
    }

    [AvaloniaTest]
    public async Task SourceFileTranscription_DeferredPaidResultPersistsResponseJobMetadata()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "source-transcription-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        var transcriptionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranscription = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                transcriptionStarted.TrySetResult();
                await releaseTranscription.Task.WaitAsync(cancellationToken);
                return CreateTranscriptionResponse("source-transcription-job");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(
            clients,
            draftStore: draftStore,
            draftScopes: Observable.Return<CaptionDraftScope?>(draftScope));
        string sourcePath = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "source-transcription.wav");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        try
        {
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
            viewModel.SelectedAudioSource.Value = new AudioSourceItem(
                "Source audio",
                sourcePath,
                TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

            Task operation = viewModel.Transcribe.ExecuteAsync();
            await transcriptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 1, Text = "user edit" },
            ];
            releaseTranscription.TrySetResult();
            await operation;

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Cues.Single().Text, Is.EqualTo("user edit"));
                Assert.That(viewModel.HasPartialResult.Value, Is.True);
            });
            AssertStoredCaptionDraftJob(
                draftStore,
                draftScope,
                "source-transcription-job");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_SecondChunkFailureKeepsPaidPartialAndResumesWithoutRebilling()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-failure");
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "scene-mix-failure-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        int transcriptionRequests = 0;
        string audioDirectory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Audio");
        string[] filesBefore = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                int requestNumber = Interlocked.Increment(ref transcriptionRequests);
                return requestNumber is 1 or 3
                    ? CreateTranscriptionResponse("transcription-first")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(
            clients,
            editor,
            draftStore,
            Observable.Return<CaptionDraftScope?>(draftScope));
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        viewModel.SceneRangeStartText.Value = "00:00:00.000";
        viewModel.SceneRangeEndText.Value = "00:00:00.100";
        var originalSegments = new[]
        {
            new AiTranscriptionSegment { Start = 0, End = 0.04, Text = "existing caption" },
        };
        viewModel.ResultSegments.Value = originalSegments;
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value && viewModel.Cues.Count == 1);

        await viewModel.Transcribe.ExecuteAsync();
        AssertStoredCaptionDraftJob(draftStore, draftScope, "transcription-first");

        string[] filesAfter = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(2));
            Assert.That(viewModel.ResultSegments.Value, Is.SameAs(originalSegments));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "existing caption" }));
            Assert.That(viewModel.IsTranscribing.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));
            Assert.That(filesAfter, Is.EqualTo(filesBefore));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "new caption" }));
        });

        await viewModel.Transcribe.ExecuteAsync();
        string[] filesAfterResume = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(3),
                "The completed first chunk must not be submitted and billed again.");
            Assert.That(viewModel.Cues, Has.Count.EqualTo(2));
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
            Assert.That(filesAfterResume, Is.EqualTo(filesBefore));
        });
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_SecondChunkCancellationPreservesResultAndDeletesTemporaryFiles()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-cancel");
        int transcriptionRequests = 0;
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string audioDirectory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Audio");
        string[] filesBefore = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                int requestNumber = Interlocked.Increment(ref transcriptionRequests);
                if (requestNumber == 1)
                    return CreateTranscriptionResponse("transcription-first");

                secondRequestStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        viewModel.SceneRangeStartText.Value = "00:00:00.000";
        viewModel.SceneRangeEndText.Value = "00:00:00.100";
        var originalSegments = new[]
        {
            new AiTranscriptionSegment { Start = 0, End = 0.04, Text = "existing caption" },
        };
        viewModel.ResultSegments.Value = originalSegments;
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value && viewModel.Cues.Count == 1);

        Task operation = viewModel.Transcribe.ExecuteAsync();
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        GetLifetimeCancellationSource(viewModel).Cancel();
        await operation;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        string[] filesAfter = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(2));
            Assert.That(viewModel.ResultSegments.Value, Is.SameAs(originalSegments));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "existing caption" }));
            Assert.That(viewModel.IsTranscribing.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(filesAfter, Is.EqualTo(filesBefore));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "new caption" }));
        });
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_EditDuringFinalChunkKeepsPaidResultRecoverable()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-stale-response");
        int transcriptionRequests = 0;
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                int requestNumber = Interlocked.Increment(ref transcriptionRequests);
                if (requestNumber == 2)
                {
                    secondRequestStarted.TrySetResult();
                    await releaseSecondRequest.Task.WaitAsync(cancellationToken);
                }
                return CreateTranscriptionResponse($"transcription-{requestNumber}");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        viewModel.SceneRangeStartText.Value = "00:00:00.000";
        viewModel.SceneRangeEndText.Value = "00:00:00.100";
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 0.04, Text = "existing caption" },
        ];
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value && viewModel.Cues.Count == 1);

        Task operation = viewModel.Transcribe.ExecuteAsync();
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Cues[0].Text = "user edit while transcribing";
        releaseSecondRequest.TrySetResult();
        await operation;

        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues.Select(cue => cue.Text),
                Is.EqualTo(new[] { "user edit while transcribing" }));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(2));
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
        });
    }

    private static AiTranscriptionSegment[] CreateTranslationBatchSegments()
        => Enumerable.Range(0, 201)
            .Select(index => new AiTranscriptionSegment
            {
                Start = index * 2,
                End = index * 2 + 1,
                Text = $"line-{index}",
            })
            .ToArray();

    private static HttpResponseMessage CreateTranslationResponse(HttpRequestMessage request, string jobId)
    {
        string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using JsonDocument json = JsonDocument.Parse(body);
        object[] segments = json.RootElement.GetProperty("segments")
            .EnumerateArray()
            .Select(segment => (object)new
            {
                id = segment.GetProperty("id").GetString(),
                text = "T-" + segment.GetProperty("text").GetString(),
            })
            .ToArray();
        return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            jobId,
            segments,
        }));
    }

    private static HttpResponseMessage CreateTranscriptionResponse(string jobId)
        => JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            jobId,
            language = "en",
            segments = new[] { new { start = 0.0, end = 0.04, text = "new caption" } },
        }));

    private static AiImageGenerationDialogViewModel CreateImageGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editor);

    private static AiImageEditDialogViewModel CreateImageEditDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageEditingService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editor);

    private static AiVideoGenerationDialogViewModel CreateVideoGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>(),
            editor);

    private static AiSubtitleDialogViewModel CreateSubtitleDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        ICaptionDraftStore? draftStore = null,
        IObservable<CaptionDraftScope?>? draftScopes = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            CaptionCatalog.CreateDefault("Default"),
            draftStore ?? CaptionDraftStoreProvider.Current,
            draftScopes ?? Observable.Return<CaptionDraftScope?>(null),
            editor);

    private static IAiPlanCoordinator CreatePlanCoordinator(BeutlApiApplication clients)
        => new AiPlanCoordinator(clients, clients.GetResource<IAiEntitlementService>());

    private static void AssertStoredCaptionDraftJob(
        FileCaptionDraftStore store,
        CaptionDraftScope scope,
        string expectedJobId)
    {
        using JsonDocument envelope = JsonDocument.Parse(File.ReadAllBytes(store.GetStoragePath(scope)));
        Assert.That(envelope.RootElement.GetProperty("jobId").GetString(), Is.EqualTo(expectedJobId));
    }

    private static LifetimeCancellationSource GetLifetimeCancellationSource(
        AiSubtitleDialogViewModel viewModel)
        => (LifetimeCancellationSource)typeof(AiSubtitleDialogViewModel)
            .GetField("_lifetimeCts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(viewModel)!;

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10, cancellationTokenSource.Token);
        }
    }

    private static void SetAuthenticatedUser(BeutlApiApplication app, HttpClient httpClient)
    {
        httpClient.BaseAddress = new Uri("https://beutl.beditor.net");
        var profile = new Profile(new ProfileResponse
        {
            Id = "test-user",
            Name = "test",
            DisplayName = "Test User",
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, app);
        var user = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, app, DateTime.UtcNow);
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
    }

    private static string EntitlementsJson() => """
        {
          "plan": "pro",
          "subscriptionStatus": "active",
          "currentPeriodStart": "2026-08-01T00:00:00Z",
          "currentPeriodEnd": "2026-09-01T00:00:00Z",
          "canUseAi": true,
          "balance": {
            "monthlyUsage": {
              "usedPercent": 0,
              "remainingPercent": 100,
              "isExhausted": false
            },
            "additionalCredits": 0,
            "hasAdditionalCreditDebt": false
          },
          "availability": {
            "image.generate": true,
            "image.edit.remove_background": true,
            "image.edit.upscale": true,
            "image.edit.restyle": true,
            "image.edit.remove_object": true,
            "image.edit.outpaint": true,
            "audio.transcribe": true,
            "subtitle.translate": true,
            "video.generate": true
          }
        }
        """;

    private static string ImageResponseJson(string jobId, string fileId) => $$"""
        {
          "jobId": "{{jobId}}",
          "fileId": "{{fileId}}",
          "url": "https://beutl.beditor.net/api/contents/{{fileId}}"
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ByteResponse(byte[] bytes, string mediaType)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) },
            },
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await responder(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
