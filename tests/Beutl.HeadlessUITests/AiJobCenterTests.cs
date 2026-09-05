using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiJobCenterTests
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private AiJobKindRegistry _jobKinds = null!;
    private AiJobResultHandlerRegistry _resultHandlers = null!;

    [SetUp]
    public void SetUp()
    {
        _jobKinds = AiJobKindRegistry.CreateBuiltIn(
            new UnusedImageGenerationService(),
            new UnusedVideoService(),
            new UnusedEntitlementService(),
            new UnusedAvailabilityService(),
            new UnusedModelCatalogService(),
            AiRetryTestContext.Create());
        _resultHandlers = new AiJobResultHandlerRegistry(BuiltInAiJobResultHandlers.Create());
    }

    [TearDown]
    public async Task TearDown()
    {
        await _resultHandlers.DisposeAsync();
        await _jobKinds.DisposeAsync();
    }

    [Test]
    public void Item_ParsesRetainedInputAndNormalizesServerTokens()
    {
        using var item = CreateItem(CreateJob(
            kind: " IMAGE ",
            status: " FAILED ",
            inputParams: ParseInput("""
                {
                  "prompt": "  A moonlit lake  ",
                  "size": "1024x1536"
                }
                """),
            error: "  Provider failed  ",
            canRetry: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.Kind, Is.EqualTo("image"));
            Assert.That(item.Status, Is.EqualTo("failed"));
            Assert.That(item.Prompt, Is.EqualTo("A moonlit lake"));
            Assert.That(item.ImageSize, Is.EqualTo("1024x1536"));
            Assert.That(item.Summary, Is.EqualTo("A moonlit lake"));
            Assert.That(item.Error, Is.EqualTo("Provider failed"));
            Assert.That(item.IsFailed, Is.True);
            Assert.That(item.ShouldPoll, Is.False);
            Assert.That(item.IsTerminal, Is.True);
            Assert.That(item.CanRetry, Is.False,
                "A displayed prompt may be normalized, but a paid retry cannot change its retained body.");
            Assert.That(item.CanDelete, Is.True);
            Assert.That(item.CanAddToScene, Is.False);
        }
    }

    [Test]
    public async Task Item_ThrowingResultHandlerFallsBackToGenericPresentation()
    {
        var descriptor = new AiJobKindDescriptor(
            new AiJobKindId("vendor.throwing"),
            new AiJobStatusMap([]));
        await using IAiJobKindRegistration kindRegistration = _jobKinds.Register(descriptor);
        await using IAiJobResultHandlerRegistration handlerRegistration = _resultHandlers.Register(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                descriptor.Kind,
                new ThrowingResultHandler())));

        using var item = new AiJobItemViewModel(
            CreateJob("vendor.throwing", "unknown", ParseInput("{ \"prompt\": \"safe\" }")),
            _jobKinds,
            _resultHandlers);

        Assert.That(item.Summary, Is.EqualTo("safe"));
        Assert.That(item.CanAddToScene, Is.False);
    }

    [Test]
    public void Item_CanceledUsesDistinctLocalizedPresentation()
    {
        using var item = CreateItem(CreateJob(
            kind: "video",
            status: "canceled",
            inputParams: ParseInput("""{"prompt":"Canceled render"}"""),
            canRetry: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.StatusDisplayName, Is.EqualTo(Strings.AiJobCenter_StatusCanceled));
            Assert.That(item.IsTerminal, Is.True);
            Assert.That(item.ShouldPoll, Is.False);
            Assert.That(item.IsFailed, Is.False);
            Assert.That(item.CanRetry, Is.False);
        }
    }

    [Test]
    public void Item_SucceededReplayableJobHonorsAuthoritativeCanRetry()
    {
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "succeeded",
            inputParams: ParseInput("{\"prompt\":\"Completed image\",\"aspectRatio\":\"1:1\"}"),
            canRetry: true));

        Assert.That(item.CanRetry, Is.True);
    }

    [Test]
    public void Item_MalformedRetainedInputFallsBackWithoutThrowing()
    {
        AiJobItemViewModel? item = null;

        Assert.DoesNotThrow(() => item = CreateItem(CreateJob(
            kind: "video",
            status: "failed",
            inputParams: ParseInput("""
                {
                  "prompt": "   ",
                  "durationSeconds": "six",
                  "resolution": 720
                }
                """),
            url: "   ",
            canRetry: true)));

        using (item!)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(item!.Prompt, Is.Null);
                Assert.That(item.DurationSeconds, Is.Null);
                Assert.That(item.Resolution, Is.Null);
                Assert.That(item.ContentUri, Is.Null);
                Assert.That(item.Summary, Is.EqualTo(Strings.AiJobCenter_NoDescription));
                Assert.That(item.CanRetry, Is.False);
                Assert.That(item.CanAddToScene, Is.False);
            }
        }
    }

    [Test]
    public void Item_DetailsOmitTheUsageCostOfTheOperation()
    {
        using var item = CreateItem(CreateJob(
            kind: "video",
            status: "succeeded",
            inputParams: ParseInput("""
                {
                  "prompt": "Orbiting camera",
                  "durationSeconds": 6,
                  "resolution": "1080p"
                }
                """),
            url: "https://beutl.beditor.net/api/contents/video-1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                item.Details,
                Does.Not.Contain("20"),
                "The history must not disclose what the operation consumed.");
            Assert.That(item.Details, Does.Contain("1080p"));
        }
    }

    [Test]
    public void Item_KnownServerErrorCodeUsesLocalizedUserMessage()
    {
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "failed",
            error: "aiProviderError"));

        Assert.That(item.Error, Is.EqualTo(Strings.AiProviderError));
    }

    [Test]
    public void Item_UpdateTransitionsActiveJobWithoutReplacingBusyState()
    {
        using var item = CreateItem(CreateJob(
            kind: "video",
            status: "running",
            inputParams: ParseInput("""
                {
                  "prompt": "Orbiting camera",
                  "durationSeconds": 6,
                  "resolution": "1080p"
                }
                """)));
        int propertyChangedCount = 0;
        item.PropertyChanged += OnPropertyChanged;
        using IDisposable? operation = item.TryBeginOperation();

        item.Update(CreateJob(
            kind: "video",
            status: "succeeded",
            inputParams: ParseInput("""
                {
                  "prompt": "Orbiting camera",
                  "durationSeconds": 6,
                  "resolution": "1080p"
                }
                """),
            url: "https://beutl.beditor.net/api/contents/video-1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation, Is.Not.Null);
            Assert.That(item.IsBusy.Value, Is.True);
            Assert.That(item.ShouldPoll, Is.False);
            Assert.That(item.IsTerminal, Is.True);
            Assert.That(item.CanDelete, Is.True);
            Assert.That(item.CanAddToScene, Is.True);
            Assert.That(item.DurationSeconds, Is.EqualTo(6));
            Assert.That(item.Resolution, Is.EqualTo("1080p"));
            Assert.That(propertyChangedCount, Is.EqualTo(1));
        }

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => propertyChangedCount++;
    }

    [Test]
    public void Item_SucceededCaptionResultCanBeReopenedFromPrivateHistory()
    {
        using var item = CreateItem(CreateJob(
            kind: "translation",
            status: "succeeded",
            inputParams: ParseInput("""{ "targetLanguage": "ja" }"""),
            url: "https://beutl.beditor.net/api/contents/caption-1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.CanAddToScene, Is.True);
            Assert.That(item.Language, Is.EqualTo("ja"));
        }
    }

    [Test]
    public void Item_DisposeDuringOperationDefersBusyPropertyDisposalUntilLeaseEnds()
    {
        var item = CreateItem(CreateJob(kind: "image", status: "succeeded"));
        IDisposable? operation = item.TryBeginOperation();
        Assert.That(operation, Is.Not.Null);
        Assert.That(item.IsBusy.Value, Is.True);

        item.Dispose();

        Assert.DoesNotThrow(() => operation!.Dispose());
        Assert.That(item.TryBeginOperation(), Is.Null);
        Assert.DoesNotThrow(item.Dispose);
    }

    [Test]
    public void Item_PreviewClaimCanBeRetriedAfterTransientFailure()
    {
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "succeeded",
            url: "https://beutl.beditor.net/api/contents/preview"));
        Assert.That(item.TryClaimPreviewLoad(), Is.True);
        item.ResetPreviewLoadClaim();
        Assert.That(item.TryClaimPreviewLoad(), Is.True);
    }

    [AvaloniaTest]
    public void Item_DisposeLinearizesPreviewNotificationAndReleasesBitmap()
    {
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "succeeded",
            url: "https://beutl.beditor.net/api/contents/preview"));
        int notifications = 0;
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(item.Preview))
                notifications++;
        };
        var bitmap = Ref<Bitmap>.Create(Bitmap.FromStream(new MemoryStream(s_png)));
        item.SetPreview(bitmap);
        item.Dispose();
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(item.Preview, Is.Null);
        Assert.That(bitmap.Value, Is.Null,
            "Disposal must release the native bitmap owned by the item.");
    }

    [Test]
    public void Item_SetPreviewAfterDisposeDisposesIncomingBitmapWithoutNotification()
    {
        using var item = CreateItem(CreateJob(kind: "image", status: "succeeded", url: "https://beutl.beditor.net/api/contents/preview"));
        int notifications = 0;
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(item.Preview)) notifications++;
        };
        item.Dispose();
        var incoming = Ref<Bitmap>.Create(Bitmap.FromStream(new MemoryStream(s_png)));
        item.SetPreview(incoming);
        Assert.That(notifications, Is.Zero);
        Assert.That(item.Preview, Is.Null);
        Assert.That(incoming.Value, Is.Null,
            "A preview delivered after disposal must be released immediately.");
    }

    [AvaloniaTest]
    public void View_UsesItemsControlWithVirtualizingPanelAndLabelsEveryAction()
    {
        var view = new AiJobCenterView();
        var viewWindow = new Window { Content = view, Width = 360, Height = 640 };
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "failed",
            inputParams: ParseInput(
                """{ "prompt": "Accessible action", "aspectRatio": "1:1" }"""),
            canRetry: true));

        try
        {
            viewWindow.Show();
            HeadlessTestHelpers.Render();

            ItemsControl jobList = view.FindControl<ItemsControl>("JobList")!;
            Assert.That(jobList, Is.Not.Null);
            Assert.That(AutomationProperties.GetName(jobList), Is.EqualTo(Strings.AiJobCenter));
            Assert.That(jobList, Is.TypeOf<ItemsControl>(),
                "Job cards must not inherit ListBox selection backgrounds.");
            ScrollViewer scrollViewer = jobList.FindAncestorOfType<ScrollViewer>()!;
            Assert.Multiple(() =>
            {
                Assert.That(scrollViewer, Is.Not.Null,
                    "The ItemsControl must remain inside an explicit scrolling container.");
                Assert.That(scrollViewer.HorizontalScrollBarVisibility,
                    Is.EqualTo(ScrollBarVisibility.Disabled));
                Assert.That(scrollViewer.VerticalScrollBarVisibility,
                    Is.EqualTo(ScrollBarVisibility.Auto));
            });
            Assert.That(
                jobList.GetVisualDescendants().OfType<VirtualizingStackPanel>(),
                Is.Not.Empty,
                "The job list must keep its explicitly virtualizing items panel.");

            AssertAction(view.FindControl<Button>("RefreshButton"), Strings.Refresh);
            AssertAction(view.FindControl<Button>("LoadMoreButton"), Strings.AiJobCenter_LoadMore);

            jobList.ItemsSource = new[] { item };
            HeadlessTestHelpers.Render();

            ContentPresenter itemContainer = (ContentPresenter)jobList.ContainerFromIndex(0)!;
            Assert.That(itemContainer.HorizontalContentAlignment,
                Is.EqualTo(HorizontalAlignment.Stretch));
            AiJobCard card = itemContainer.GetVisualDescendants().OfType<AiJobCard>().Single();
            AutomationPeer cardPeer = ControlAutomationPeer.CreatePeerForElement(card);
            Assert.Multiple(() =>
            {
                Assert.That(cardPeer.GetAutomationControlType(),
                    Is.EqualTo(AutomationControlType.ListItem));
                Assert.That(cardPeer.GetName(), Is.EqualTo(item.Summary));
                Assert.That(cardPeer.IsControlElement(), Is.True);
            });
            List<Button> buttons = itemContainer.GetVisualDescendants().OfType<Button>().ToList();
            Button addButton = buttons.Single(button => Equals(button.Content, Strings.AiAddToScene));
            Button retryButton = buttons.Single(button => Equals(button.Content, Strings.AiJobCenter_Retry));
            Button deleteButton = buttons.Single(button =>
                AutomationProperties.GetName(button) == Strings.Delete);
            AssertAction(addButton, Strings.AiAddToScene);
            AssertAction(
                retryButton,
                Strings.AiJobCenter_Retry);
            AssertAction(deleteButton, Strings.Delete);
            Assert.That(
                new[] { addButton.Parent, retryButton.Parent, deleteButton.Parent },
                Is.All.TypeOf<WrapPanel>(),
                "Job actions must wrap instead of clipping in a narrow dock pane.");
            var actionPanel = (WrapPanel)addButton.Parent!;
            Assert.That(
                new[] { addButton, retryButton, deleteButton }.Select(button => button.Bounds.Right),
                Is.All.LessThanOrEqualTo(actionPanel.Bounds.Width + 1),
                "Every wrapped job action must remain inside the narrow card.");
        }
        finally
        {
            viewWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ViewModel_RefreshRetryAddAndDeleteUsePersistentJobWorkflow()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-operations");
        int retryRequests = 0;
        int deleteRequests = 0;
        bool deleted = false;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/user/ai-availability" => JsonResponse(
                HttpStatusCode.OK,
                """{ "available": true }"""),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, JobsJson(deleted)),
            "/api/v3/ai/images" => RetryImage(),
            "/api/v3/ai/jobs/job-failed" when request.Method == HttpMethod.Delete => DeleteJob(),
            "/api/contents/file-success" => ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);

        await WaitUntilAsync(() => viewModel.Jobs.Count == 2);
        AiJobItemViewModel completed = viewModel.Jobs.Single(item => item.Id == "job-success");
        AiJobItemViewModel failed = viewModel.Jobs.Single(item => item.Id == "job-failed");

        await viewModel.AddToSceneAsync(completed);
        await viewModel.RequestRetryConfirmationAsync(failed);
        await viewModel.ConfirmPendingActionAsync();
        viewModel.RequestDeleteConfirmation(failed);
        Assert.That(viewModel.ConfirmationTitle.Value, Does.Contain("Retry me"),
                "The confirmation names the job, which may have scrolled out of sight below it.");
        await viewModel.ConfirmPendingActionAsync();
        await WaitUntilAsync(() => viewModel.Jobs.Count == 1);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(retryRequests, Is.EqualTo(1));
            Assert.That(deleteRequests, Is.EqualTo(1));
            Assert.That(viewModel.Jobs.Single().Id, Is.EqualTo("job-success"));
            Assert.That(viewModel.Error.Value, Is.Null);
        }

        HttpResponseMessage RetryImage()
        {
            Interlocked.Increment(ref retryRequests);
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "retry-job",
                  "fileId": "retry-file",
                  "url": "https://beutl.beditor.net/api/contents/retry-file"
                }
                """);
        }

        HttpResponseMessage DeleteJob()
        {
            Interlocked.Increment(ref deleteRequests);
            deleted = true;
            return JsonResponse(HttpStatusCode.OK, """{ "deleted": true }""");
        }
    }

    [AvaloniaTest]
    public async Task DeleteJob_NotFoundRaceRefreshesAsAlreadyDeleted()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-delete-race");
        bool deleted = false;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, JobsJson(deleted)),
            "/api/v3/ai/jobs/job-failed" when request.Method == HttpMethod.Delete => MissingDelete(),
            "/api/contents/file-success" => ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        await WaitUntilAsync(() => viewModel.Jobs.Count == 2);
        AiJobItemViewModel failed = viewModel.Jobs.Single(item => item.Id == "job-failed");

        await viewModel.DeleteJobAsync(failed);
        await WaitUntilAsync(() => viewModel.Jobs.Count == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Jobs.Single().Id, Is.EqualTo("job-success"));
            Assert.That(viewModel.Error.Value, Is.Null);
        }

        HttpResponseMessage MissingDelete()
        {
            deleted = true;
            return JsonResponse(HttpStatusCode.NotFound, """
                {
                  "error_code": "aiJobNotFound",
                  "message": "The job no longer exists.",
                  "documentation_url": null
                }
                """);
        }
    }

    [AvaloniaTest]
    public async Task AddToScene_RestoresTranscriptionHistory()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-caption-history");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/jobs" => JsonResponse(
                HttpStatusCode.OK,
                """{ "jobs": [], "nextCursor": null }"""),
            "/api/contents/caption-1" => JsonResponse(HttpStatusCode.OK, """
                {
                  "version": 1,
                  "kind": "stt",
                  "language": "en",
                  "segments": [
                    { "start": 0, "end": 1.5, "text": "First" },
                    { "start": 2, "end": 3, "text": "Second" }
                  ]
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        using var item = CreateItem(CreateJob(
            kind: "stt",
            status: "succeeded",
            url: "https://beutl.beditor.net/api/contents/caption-1"));

        await viewModel.AddToSceneAsync(item);

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(2), viewModel.Error.Value);
        Assert.That(viewModel.Error.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task Retry_UsesCurrentPricingAndRechecksCurrentBalanceBeforeSubmission()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-retry-pricing");
        int entitlementRequests = 0;
        int retryRequests = 0;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(
                HttpStatusCode.OK,
                Interlocked.Increment(ref entitlementRequests) < 3
                    ? EntitlementsJson(canStartImage: true, usedPercent: 0)
                    : EntitlementsJson(canStartImage: false, usedPercent: 50)),
            "/api/v3/user/ai-availability" => JsonResponse(
                HttpStatusCode.OK,
                """{ "available": true }"""),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, """{ "jobs": [], "nextCursor": null }"""),
            "/api/v3/ai/images" => RetryImage(),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "failed",
            inputParams: ParseInput(
                """{ "prompt": "Retry at the current price", "aspectRatio": "1:1" }"""),
            canRetry: true));
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);

        await viewModel.RequestRetryConfirmationAsync(item);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.IsConfirmationOpen.Value, Is.True);
            Assert.That(viewModel.CanConfirm.Value, Is.True);
            Assert.That(
                viewModel.ConfirmationMessage.Value,
                Does.Contain(Strings.AiJobCenter_RetryConfirmation));
            Assert.That(
                viewModel.ConfirmationMessage.Value,
                Does.Not.Contain("75"),
                "The confirmation must not disclose the per-operation usage cost.");
        }

        await viewModel.ConfirmPendingActionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.IsConfirmationOpen.Value, Is.False);
            Assert.That(retryRequests, Is.Zero, "The request must be blocked after the balance recheck.");
            Assert.That(
                viewModel.Error.Value,
                Is.EqualTo(Strings.AiEstimatedUsageInsufficient));
        }

        HttpResponseMessage RetryImage()
        {
            Interlocked.Increment(ref retryRequests);
            return JsonResponse(HttpStatusCode.OK, ImageResponseJson());
        }
    }

    [AvaloniaTest]
    public async Task VideoRetry_RechecksDurationSpecificAvailabilityBeforeSubmission()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-retry-availability");
        int availabilityRequests = 0;
        int retryRequests = 0;
        var availabilityBodies = new List<string>();
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/user/ai-availability":
                    availabilityRequests++;
                    availabilityBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    return JsonResponse(
                        HttpStatusCode.OK,
                            availabilityRequests < 2
                            ? """{ "available": true }"""
                            : """{ "available": false }""");
                case "/api/v3/ai/jobs":
                    return JsonResponse(
                        HttpStatusCode.OK,
                        """{ "jobs": [], "nextCursor": null }""");
                case "/api/v3/ai/videos":
                    retryRequests++;
                    return JsonResponse(
                        HttpStatusCode.OK,
                        """{ "jobId": "retry-video", "status": "queued" }""");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        using var item = CreateItem(CreateJob(
            kind: "video",
            status: "failed",
            inputParams: ParseInput(
                """{ "prompt": "Retry video", "durationSeconds": 8, "resolution": "1080p" }"""),
            canRetry: true));
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);

        await viewModel.RequestRetryConfirmationAsync(item);
        Assert.That(viewModel.CanConfirm.Value, Is.True);

        await viewModel.ConfirmPendingActionAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(availabilityRequests, Is.EqualTo(2));
            Assert.That(retryRequests, Is.Zero);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Strings.AiEstimatedUsageInsufficient));
            Assert.That(
                availabilityBodies,
                Is.All.EqualTo("""{"operation":"video.generate","durationSeconds":8}"""));
        }
    }

    [AvaloniaTest]
    public async Task CustomKind_ControlsPresentationRetryAndResultHandling()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-custom-kind");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(
                HttpStatusCode.OK,
                """{ "jobs": [], "nextCursor": null }"""),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);

        IAiJobKindRegistry jobKinds = clients.GetResource<IAiJobKindRegistry>();
        var retryHandler = new CustomRetryHandler();
        var resultHandler = new CustomResultHandler(editor);
        var descriptor = new AiJobKindDescriptor(
            new AiJobKindId("vendor.upscale"),
            new AiJobStatusMap(
            [
                KeyValuePair.Create(
                    new AiJobStatusId("retryable"),
                    new AiJobStatusSemantics(
                        true,
                        false,
                        new AiJobOutcomeId("vendor.retryable"))),
                KeyValuePair.Create(
                    new AiJobStatusId("ready"),
                    new AiJobStatusSemantics(
                        true,
                        false,
                        new AiJobOutcomeId("vendor.ready"))),
            ]))
        {
            RetryHandler = retryHandler,
        };
        await using IAiJobKindRegistration registration = jobKinds.Register(descriptor);
        await using IAiJobResultHandlerRegistration resultRegistration = _resultHandlers.Register(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("vendor.upscale"),
                resultHandler)));
        using var viewModel = CreateJobCenter(editor, clients);
        using var item = new AiJobItemViewModel(
            CreateJob(
                kind: "vendor.upscale",
                status: "retryable",
                inputParams: ParseInput("""{ "prompt": "Upscale this" }"""),
                canRetry: true),
            jobKinds,
            _resultHandlers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.KindDisplayName, Is.EqualTo("Vendor upscale"));
            Assert.That(item.StatusDisplayName, Is.EqualTo("Ready to retry"));
            Assert.That(item.IsFailed, Is.True);
            Assert.That(item.CanRetry, Is.True);
        }

        await viewModel.RetryJobAsync(item);
        item.Update(CreateJob(
            kind: "vendor.upscale",
            status: "ready",
            inputParams: ParseInput("""{ "prompt": "Upscale this" }"""),
            url: "https://beutl.beditor.net/api/contents/vendor-result"));
        await viewModel.AddToSceneAsync(item);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(retryHandler.RetryCount, Is.EqualTo(1));
            Assert.That(resultHandler.HandleCount, Is.EqualTo(1));
            Assert.That(resultHandler.ResolvedSceneEditingContext, Is.True);
            Assert.That(item.StatusDisplayName, Is.EqualTo("Ready to open"));
            Assert.That(item.CanAddToScene, Is.True);
            Assert.That(viewModel.Error.Value, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task RetryConfirmationPinsOriginatingHandlerAcrossReplacementAndCancel()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-retry-lifetime");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(
                HttpStatusCode.OK,
                """{ "jobs": [], "nextCursor": null }"""),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        IAiJobKindRegistry registry = clients.GetResource<IAiJobKindRegistry>();
        using var viewModel = CreateJobCenter(editor, clients);

        var original = new CustomRetryHandler();
        var replacement = new CustomRetryHandler();
        IAiJobKindRegistration originalRegistration = registry.Register(
            RetryDescriptor("vendor.replace", original));
        using var item = new AiJobItemViewModel(
            CreateJob(
                kind: "vendor.replace",
                status: "retryable",
                inputParams: ParseInput("""{ "prompt": "Pinned retry" }"""),
                canRetry: true),
            registry,
            _resultHandlers);

        await viewModel.RequestRetryConfirmationAsync(item);
        Assert.That(viewModel.CanConfirm.Value, Is.True);
        Task retirement = originalRegistration.DisposeAsync().AsTask();
        Assert.That(retirement.IsCompleted, Is.False);
        await using IAiJobKindRegistration replacementRegistration = registry.Register(
            RetryDescriptor("vendor.replace", replacement));

        await viewModel.ConfirmPendingActionAsync();
        await retirement.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(original.RetryCount, Is.EqualTo(1));
            Assert.That(replacement.RetryCount, Is.Zero);
        });

        var canceledOriginal = new CustomRetryHandler();
        var canceledReplacement = new CustomRetryHandler();
        IAiJobKindRegistration canceledRegistration = registry.Register(
            RetryDescriptor("vendor.cancel", canceledOriginal));
        using var canceledItem = new AiJobItemViewModel(
            CreateJob(
                kind: "vendor.cancel",
                status: "retryable",
                inputParams: ParseInput("""{ "prompt": "Canceled retry" }"""),
                canRetry: true),
            registry,
            _resultHandlers);

        await viewModel.RequestRetryConfirmationAsync(canceledItem);
        Assert.That(viewModel.CanConfirm.Value, Is.True);
        Task canceledRetirement = canceledRegistration.DisposeAsync().AsTask();
        Assert.That(canceledRetirement.IsCompleted, Is.False);
        await using IAiJobKindRegistration canceledReplacementRegistration = registry.Register(
            RetryDescriptor("vendor.cancel", canceledReplacement));

        viewModel.CancelConfirmation();
        await canceledRetirement.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(canceledOriginal.RetryCount, Is.Zero);
            Assert.That(canceledReplacement.RetryCount, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task RetryConfirmationWithoutDescriptorStopsLoading()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-missing-descriptor");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        using var item = new AiJobItemViewModel(
            CreateJob(
                "vendor.missing",
                "retryable",
                ParseInput("{\"prompt\":\"missing\"}"),
                canRetry: true),
            clients.GetResource<IAiJobKindRegistry>(),
            _resultHandlers);

        await viewModel.RequestRetryConfirmationAsync(item);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsConfirmationLoading.Value, Is.False);
            Assert.That(viewModel.CanConfirm.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task RetryConfirmationCancellationDrainsSlowPreflightBeforeUnloading()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-slow-preflight");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        IAiJobKindRegistry registry = clients.GetResource<IAiJobKindRegistry>();
        using var viewModel = CreateJobCenter(editor, clients);
        var slow = new SlowRetryHandler();
        await using IAiJobKindRegistration registration = registry.Register(
            RetryDescriptor("vendor.slow", slow));
        using var item = new AiJobItemViewModel(
            CreateJob("vendor.slow", "retryable", ParseInput("{\"prompt\":\"slow\"}"), canRetry: true),
            registry,
            _resultHandlers);

        Task confirmation = viewModel.RequestRetryConfirmationAsync(item);
        await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task unload = registration.DisposeAsync().AsTask();
        Assert.That(unload.IsCompleted, Is.False);

        viewModel.CancelConfirmation();
        Assert.That(unload.IsCompleted, Is.False,
            "Cancellation must not unload an extension while its preflight is still running.");
        slow.Release.TrySetResult();
        await confirmation;
        await unload.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(slow.CancellationObserved, Is.True);
    }

    [AvaloniaTest]
    public async Task DisposeAsyncReturnsSamePendingTaskWhilePreflightDrains()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-dispose-idempotent");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        IAiJobKindRegistry registry = clients.GetResource<IAiJobKindRegistry>();
        using var viewModel = CreateJobCenter(editor, clients);
        var slow = new SlowRetryHandler();
        await using IAiJobKindRegistration registration = registry.Register(RetryDescriptor("vendor.dispose", slow));
        using var item = new AiJobItemViewModel(
            CreateJob("vendor.dispose", "retryable", ParseInput("{\"prompt\":\"slow\"}"), canRetry: true),
            registry,
            _resultHandlers);

        Task confirmation = viewModel.RequestRetryConfirmationAsync(item);
        await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task unload = registration.DisposeAsync().AsTask();
        viewModel.Dispose();
        Task first = viewModel.DisposeAsync().AsTask();
        Task second = viewModel.DisposeAsync().AsTask();
        Assert.That(second, Is.SameAs(first));
        Assert.That(first.IsCompleted, Is.False);
        slow.Release.TrySetResult();
        await confirmation;
        await first;
        await unload;
    }

    [AvaloniaTest]
    public async Task DisposeAsyncDrainsNonCooperativeResultHandler()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-result-drain");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        IAiJobKindRegistry registry = clients.GetResource<IAiJobKindRegistry>();
        using var viewModel = CreateJobCenter(editor, clients);
        var blocking = new BlockingResultHandler();
        await using IAiJobKindRegistration registration = registry.Register(
            new AiJobKindDescriptor(new AiJobKindId("vendor.blocking-result"),
                new AiJobStatusMap([KeyValuePair.Create(new AiJobStatusId("ready"), new AiJobStatusSemantics(true, false, new AiJobOutcomeId("ready")))])));
        await using IAiJobResultHandlerRegistration resultRegistration = _resultHandlers.Register(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(new AiJobKindId("vendor.blocking-result"), blocking)));
        using var item = new AiJobItemViewModel(CreateJob("vendor.blocking-result", "ready", ParseInput("{\"prompt\":\"x\"}"), url: "https://beutl.beditor.net/api/contents/x"), registry, _resultHandlers);
        Task operation = viewModel.AddToSceneAsync(item);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Dispose();
        Task dispose = viewModel.DisposeAsync().AsTask();
        Assert.That(dispose.IsCompleted, Is.False);
        blocking.Release.TrySetResult();
        await operation;
        await dispose;
        await registration.DisposeAsync();
    }

    [AvaloniaTest]
    public async Task RetryConfirmationCancelAndDisposeSwallowSynchronousCancellationCallbackErrors()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-throwing-preflight");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        IAiJobKindRegistry registry = clients.GetResource<IAiJobKindRegistry>();
        using var viewModel = CreateJobCenter(editor, clients);
        var throwing = new ThrowingCancelRetryHandler();
        await using IAiJobKindRegistration registration = registry.Register(
            RetryDescriptor("vendor.throwing-cancel", throwing));
        using var item = new AiJobItemViewModel(
            CreateJob("vendor.throwing-cancel", "retryable", ParseInput("{\"prompt\":\"throw\"}"), canRetry: true),
            registry,
            _resultHandlers);

        Task confirmation = viewModel.RequestRetryConfirmationAsync(item);
        await throwing.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotThrow(viewModel.CancelConfirmation);
        throwing.Release.TrySetResult();
        await confirmation;
        Assert.DoesNotThrow(viewModel.Dispose);
        await registration.DisposeAsync();
    }


    [AvaloniaTest]
    public async Task RetryConfirmationStalePreflightCannotOverwriteReplacement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-stale-preflight");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.InternalServerError, "{}"),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        IAiJobKindRegistry registry = clients.GetResource<IAiJobKindRegistry>();
        using var viewModel = CreateJobCenter(editor, clients);
        var slow = new SlowRetryHandler();
        var replacement = new CustomRetryHandler();
        await using IAiJobKindRegistration slowRegistration = registry.Register(RetryDescriptor("vendor.stale.slow", slow));
        await using IAiJobKindRegistration replacementRegistration = registry.Register(RetryDescriptor("vendor.stale.fast", replacement));
        using var first = new AiJobItemViewModel(CreateJob("vendor.stale.slow", "retryable", ParseInput("{\"prompt\":\"first\"}"), canRetry: true), registry, _resultHandlers);
        using var second = new AiJobItemViewModel(CreateJob("vendor.stale.fast", "retryable", ParseInput("{\"prompt\":\"second\"}"), canRetry: true), registry, _resultHandlers);

        Task firstConfirmation = viewModel.RequestRetryConfirmationAsync(first);
        await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task secondConfirmation = viewModel.RequestRetryConfirmationAsync(second);
        await secondConfirmation;
        Assert.That(viewModel.CanConfirm.Value, Is.True);
        Assert.That(viewModel.ConfirmationMessage.Value, Does.Contain("No additional charge"));
        slow.Release.TrySetResult();
        await firstConfirmation;
        Assert.That(viewModel.CanConfirm.Value, Is.True);
        Assert.That(viewModel.ConfirmationMessage.Value, Does.Contain("No additional charge"));
        viewModel.CancelConfirmation();
    }

    private static void AssertAction(Button? button, string accessibleName)
    {
        Assert.That(button, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AutomationProperties.GetName(button!), Is.EqualTo(accessibleName));
            Assert.That(ToolTip.GetTip(button!), Is.EqualTo(accessibleName));
        }
    }

    private AiJobItemViewModel CreateItem(AiJob job)
        => new(job, _jobKinds, _resultHandlers);

    private static AiJobKindDescriptor RetryDescriptor(
        string kind,
        IAiJobRetryHandler retryHandler)
        => new(
            new AiJobKindId(kind),
            new AiJobStatusMap(
            [
                KeyValuePair.Create(
                    new AiJobStatusId("retryable"),
                    new AiJobStatusSemantics(
                        true,
                        false,
                        new AiJobOutcomeId("vendor.retryable"))),
            ]))
        {
            RetryHandler = retryHandler,
        };

    [Test]
    public void DecodePreview_DownsamplesWithinTheBoundedDecodeSurface()
    {
        using var source = new SKBitmap(2_048, 1_024, SKColorType.Rgba8888, SKAlphaType.Premul);
        source.Erase(SKColors.CornflowerBlue);
        using SKImage image = SKImage.FromBitmap(source);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var encodedStream = new MemoryStream();
        encoded.SaveTo(encodedStream);
        encodedStream.Position = 0;

        using Bitmap preview = AiJobCenterViewModel.DecodePreview(encodedStream);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview.Width, Is.EqualTo(512));
            Assert.That(preview.Height, Is.EqualTo(256));
        }
    }

    [AvaloniaTest]
    public async Task VisiblePreviewDownloadsUseBoundedConcurrency()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-preview-concurrency");
        var releaseDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fourDownloadsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        object concurrencyGate = new();
        int activeDownloads = 0;
        int maximumDownloads = 0;
        int contentRequests = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/jobs")
                return JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}");
            if (request.RequestUri?.AbsolutePath.StartsWith("/api/contents/preview-", StringComparison.Ordinal) == true)
            {
                lock (concurrencyGate)
                {
                    contentRequests++;
                    activeDownloads++;
                    maximumDownloads = Math.Max(maximumDownloads, activeDownloads);
                    if (activeDownloads == 4)
                        fourDownloadsStarted.TrySetResult();
                }
                try
                {
                    await releaseDownloads.Task.WaitAsync(cancellationToken);
                    return ByteResponse(s_png, "image/png");
                }
                finally
                {
                    lock (concurrencyGate)
                        activeDownloads--;
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        AiJob[] jobs = Enumerable.Range(0, 10)
            .Select(index => CreateJob(
                "image",
                "succeeded",
                ParseInput($"{{\"prompt\":\"preview {index}\"}}"),
                $"https://beutl.beditor.net/api/contents/preview-{index}") with
            {
                Id = new AiJobId($"preview-{index}"),
                FileId = new AiContentId($"preview-{index}"),
            })
            .ToArray();
        viewModel.ApplySnapshot(new AiJobMonitorSnapshot([.. jobs], null, false, null));
        foreach (AiJobItemViewModel item in viewModel.Jobs)
            viewModel.SetPreviewVisibility(item, true);

        await fourDownloadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        foreach (AiJobItemViewModel item in viewModel.Jobs.Skip(4))
            viewModel.SetPreviewVisibility(item, false);
        lock (concurrencyGate)
        {
            Assert.That(activeDownloads, Is.EqualTo(4));
            Assert.That(maximumDownloads, Is.EqualTo(4));
        }

        releaseDownloads.TrySetResult();
        await WaitUntilAsync(() => viewModel.Jobs.Take(4).All(item => item.Preview is not null));
        await Task.Delay(50);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(maximumDownloads, Is.EqualTo(4));
            Assert.That(contentRequests, Is.EqualTo(4));
            Assert.That(viewModel.Jobs.Skip(4).All(item => item.Preview is null), Is.True);
        }
    }

    [AvaloniaTest]
    public async Task ViewRequestsPreviewsOnlyForRealizedRowsAndLoadsMoreAfterScrolling()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-preview-viewport");
        int contentRequests = 0;
        int pageRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/jobs")
            {
                Interlocked.Increment(ref pageRequests);
                return JsonResponse(HttpStatusCode.OK, "{\"jobs\":[],\"nextCursor\":null}");
            }
            if (request.RequestUri?.AbsolutePath.StartsWith("/api/contents/viewport-", StringComparison.Ordinal) == true)
            {
                Interlocked.Increment(ref contentRequests);
                return ByteResponse(s_png, "image/png");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        await WaitUntilAsync(() => Volatile.Read(ref pageRequests) > 0 && !viewModel.IsLoading.Value);
        AiJob[] jobs = Enumerable.Range(0, 50)
            .Select(index => CreateJob(
                "image",
                "succeeded",
                ParseInput($"{{\"prompt\":\"viewport {index}\"}}"),
                $"https://beutl.beditor.net/api/contents/viewport-{index}") with
            {
                Id = new AiJobId($"viewport-{index}"),
                FileId = new AiContentId($"viewport-{index}"),
            })
            .ToArray();
        viewModel.ApplySnapshot(new AiJobMonitorSnapshot([.. jobs], null, false, null));
        var view = new AiJobCenterView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 360, Height = 320 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            await WaitUntilAsync(() => Volatile.Read(ref contentRequests) > 0);
            await Task.Delay(50);
            HeadlessTestHelpers.Render();
            int initialRequests = Volatile.Read(ref contentRequests);
            Assert.That(initialRequests, Is.LessThan(50));

            ItemsControl jobList = view.FindControl<ItemsControl>("JobList")!;
            ScrollViewer scrollViewer = jobList.FindAncestorOfType<ScrollViewer>()!;
            scrollViewer.Offset = new Avalonia.Vector(0, scrollViewer.Extent.Height);
            HeadlessTestHelpers.Render();
            await WaitUntilAsync(() => Volatile.Read(ref contentRequests) > initialRequests);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ViewModel_ShowsTheGeneratedPictureBesideItsPrompt()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-previews");
        int contentRequests = 0;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/user/ai-availability" => JsonResponse(
                HttpStatusCode.OK,
                """{ "available": true }"""),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, JobsJson(deleted: false)),
            "/api/contents/file-success" => CountedPng(ref contentRequests),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);

        await WaitUntilAsync(() => viewModel.Jobs.Count == 2);
        AiJobItemViewModel completed = viewModel.Jobs.Single(item => item.Id == "job-success");
        AiJobItemViewModel failed = viewModel.Jobs.Single(item => item.Id == "job-failed");
        Assert.That(contentRequests, Is.Zero, "Unrealized history rows must not download previews.");
        viewModel.SetPreviewVisibility(completed, true);
        await WaitUntilAsync(() => completed.Preview is not null);

        int afterFirstLoad = contentRequests;
        viewModel.ApplySnapshot(new AiJobMonitorSnapshot(
            [completed.Job, failed.Job],
            NextCursor: null,
            IsLoading: false,
            Error: null));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completed.HasImagePreview, Is.True);
            Assert.That(completed.Preview, Is.Not.Null,
                "A history of prompts is far slower to search than a history of pictures.");
            Assert.That(completed.Preview!.Value.Width, Is.LessThanOrEqualTo(512));
            Assert.That(completed.Preview!.Value.Height, Is.LessThanOrEqualTo(512));
            Assert.That(failed.Preview, Is.Null, "A job that failed produced nothing to show.");
            Assert.That(contentRequests, Is.EqualTo(afterFirstLoad),
                "Polling must not fetch the same picture again.");
        }
    }

    [AvaloniaTest]
    public async Task ViewModel_DiscardsOutOfOrderSnapshots()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-snapshot-order");
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        AiJob job = CreateJob("video", "succeeded");

        viewModel.ApplySnapshot(new AiJobMonitorSnapshot([job], null, false, null), sequence: 100);
        viewModel.ApplySnapshot(new AiJobMonitorSnapshot([], null, false, null), sequence: 99);

        Assert.That(viewModel.Jobs.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "job-1" }));
    }

    [AvaloniaTest]
    public async Task ViewModel_RetriesPreviewAfterTransientDecodeFailure()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-preview-retry");
        int contentRequests = 0;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/user/ai-availability" => JsonResponse(
                HttpStatusCode.OK,
                """{ "available": true }"""),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, JobsJson(deleted: false)),
            "/api/contents/file-success" => ++contentRequests == 1
                ? ByteResponse([0, 1, 2], "image/png")
                : ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);

        await WaitUntilAsync(() => viewModel.Jobs.Count == 2);
        AiJobItemViewModel completed = viewModel.Jobs.Single(item => item.Id == "job-success");
        viewModel.SetPreviewVisibility(completed, true);
        await WaitUntilAsync(() => contentRequests > 0);
        await WaitUntilAsync(() => !completed.IsPreviewLoadRequested);
        // A subsequent authoritative snapshot may retry a transiently failed
        // preview because the failed load released its claim.
        viewModel.ApplySnapshot(new AiJobMonitorSnapshot(
            [completed.Job, viewModel.Jobs.Single(item => item.Id == "job-failed").Job],
            NextCursor: null,
            IsLoading: false,
            Error: null));
        await WaitUntilAsync(() => completed.Preview is not null);

        Assert.That(contentRequests, Is.GreaterThanOrEqualTo(2));
    }

    [AvaloniaTest]
    public async Task ViewModel_DatesAJobByHowLongAgoItRan()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-job-center-dates");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/user/ai-availability" => JsonResponse(
                HttpStatusCode.OK,
                """{ "available": true }"""),
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, JobsJson(deleted: false)),
            "/api/contents/file-success" => ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);

        await WaitUntilAsync(() => viewModel.Jobs.Count == 2);
        AiJobItemViewModel completed = viewModel.Jobs.Single(item => item.Id == "job-success");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completed.CreatedAtText, Is.Not.Empty);
            Assert.That(completed.CreatedAtTooltip, Is.Not.Empty,
                "The exact time is still one hover away.");
            Assert.That(completed.CreatedAtText, Is.Not.EqualTo(completed.CreatedAtTooltip));
        }
    }

    [Test]
    public void RelativeTime_ReadsAsHowLongAgoUntilTheDateIsWhatIdentifiesIt()
    {
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RelativeTimeText.Format(now, now), Is.EqualTo(Strings.TimeAgoJustNow));
            Assert.That(RelativeTimeText.Format(now.AddSeconds(30), now),
                Is.EqualTo(Strings.TimeAgoJustNow));
            Assert.That(RelativeTimeText.Format(now.AddMinutes(-5), now),
                Is.EqualTo(string.Format(Strings.TimeAgoMinutesFormat, 5)));
            Assert.That(RelativeTimeText.Format(now.AddHours(-3), now),
                Is.EqualTo(string.Format(Strings.TimeAgoHoursFormat, 3)));
            Assert.That(RelativeTimeText.Format(now.AddDays(-2), now),
                Is.EqualTo(string.Format(Strings.TimeAgoDaysFormat, 2)));
            Assert.That(RelativeTimeText.Format(now.AddDays(-30), now),
                Is.EqualTo(now.AddDays(-30).ToLocalTime().ToString("d")),
                "Past a week the calendar date is what identifies it.");
            Assert.That(RelativeTimeText.Format(now.AddMinutes(5), now),
                Is.EqualTo(Strings.TimeAgoJustNow),
                "A clock that is ahead of the server must not read as a negative age.");
        }
    }

    private static HttpResponseMessage CountedPng(ref int requests)
    {
        requests++;
        return ByteResponse(s_png, "image/png");
    }

    private AiJobCenterViewModel CreateJobCenter(
        EditViewModel editor,
        BeutlApiApplication clients)
        => new(
            editor,
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobClient>(),
            clients.GetResource<IAiJobMonitor>(),
            clients.GetResource<IAiJobKindRegistry>(),
            _resultHandlers,
            null);

    private static AiJob CreateJob(
        string kind,
        string status,
        JsonElement? inputParams = null,
        string? url = null,
        string? error = null,
        bool canRetry = false)
    {
        return new AiJob(
            new AiJobId("job-1"),
            new AiJobKindId(kind),
            new AiJobStatusId(status),
            inputParams,
            url is null ? null : new AiContentId("file-1"),
            string.IsNullOrWhiteSpace(url) ? null : new Uri(url),
            error,
            canRetry,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 1, 0, TimeSpan.Zero));
    }

    private static JsonElement ParseInput(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
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
        var user = new AuthenticatedUser(
            profile,
            new AuthResponse
            {
                Token = "token",
                RefreshToken = "refresh-token",
                Expiration = DateTime.UtcNow.AddHours(1),
            },
            app,
            DateTime.UtcNow);
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((global::Reactive.Bindings.ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
    }

    private static string EntitlementsJson(bool canStartImage = true, int usedPercent = 4) => $$"""
        {
          "plan": "pro",
          "subscriptionStatus": "active",
          "currentPeriodStart": "2026-08-01T00:00:00Z",
          "currentPeriodEnd": "2026-09-01T00:00:00Z",
          "canUseAi": true,
          "balance": {
            "monthlyUsage": {
              "usedPercent": {{usedPercent}},
              "remainingPercent": {{100 - usedPercent}},
              "isExhausted": {{(usedPercent >= 100 ? "true" : "false")}}
            },
            "additionalCredits": 0,
            "hasAdditionalCreditDebt": false
          },
          "availability": {
            "image.generate": {{(canStartImage ? "true" : "false")}},
            "video.generate": {{(canStartImage ? "true" : "false")}}
          }
        }
        """;

    private static string ImageResponseJson() => """
        {
          "jobId": "retry-job",
          "fileId": "retry-file",
          "url": "https://beutl.beditor.net/api/contents/retry-file"
        }
        """;

    private static string JobsJson(bool deleted)
    {
        var jobs = new List<object>
        {
            new
            {
                id = "job-success",
                kind = "image",
                status = "succeeded",
                inputParams = new { size = "1024x1024" },
                fileId = "file-success",
                url = "https://beutl.beditor.net/api/contents/file-success",
                usageUnits = 20,
                error = (string?)null,
                canRetry = false,
                createdAt = "2026-08-01T00:00:00Z",
                updatedAt = "2026-08-01T00:01:00Z",
            },
        };
        if (!deleted)
        {
            jobs.Add(new
            {
                id = "job-failed",
                kind = "image",
                status = "failed",
                inputParams = new { prompt = "Retry me", size = "1024x1024" },
                fileId = (string?)null,
                url = (string?)null,
                usageUnits = 20,
                error = "Provider failed",
                canRetry = true,
                createdAt = "2026-08-01T00:02:00Z",
                updatedAt = "2026-08-01T00:03:00Z",
            });
        }

        return JsonSerializer.Serialize(new { jobs, nextCursor = (string?)null });
    }

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
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = await responder(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class UnusedImageGenerationService : IAiImageGenerationService
    {
        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            IProgress<AiImagePreview>? progress,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedVideoService : IAiVideoService
    {
        public Task<AiVideoGenerationResult> CreateAsync(
            AiVideoGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AiVideoJob> GetAsync(AiJobId jobId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedEntitlementService : IAiEntitlementService
    {
        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; } =
            new ReactivePropertySlim<AiEntitlements?>();

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedAvailabilityService : IAiOperationAvailabilityService
    {
        public Task<bool> CheckAsync(
            AiOperationAvailabilityRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedModelCatalogService : IAiModelCatalogService
    {
        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(AiModelCatalog.Empty);

        public void Invalidate()
        {
        }
    }

    private sealed class CustomRetryHandler : IAiJobRetryHandler
    {
        public int RetryCount { get; private set; }

        public bool CanRetry(AiJob job, AiJobStatusSemantics status)
            => job.CanRetry && status.Outcome == new AiJobOutcomeId("vendor.retryable");

        public ValueTask<AiJobRetryPreflight> GetPreflightAsync(
            AiJob job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new AiJobRetryPreflight(
                true,
                true,
                "No additional charge"));
        }

        public ValueTask<AiJobRetryPreparationResult> PrepareAsync(
            AiJob job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<AiJobRetryPreparationResult>(
                AiJobRetryPreparationResult.Ready(new CustomRetryPreparation(this)));
        }

        private sealed class CustomRetryPreparation(CustomRetryHandler owner)
            : IAiJobRetryPreparation
        {
            private int _executed;

            public Task ExecuteAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref _executed, 1) != 0)
                    throw new InvalidOperationException("The retry preparation was already executed.");
                owner.RetryCount++;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private class SlowRetryHandler : IAiJobRetryHandler
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        private readonly bool _throwOnCancellation;

        public SlowRetryHandler(bool throwOnCancellation = false)
        {
            _throwOnCancellation = throwOnCancellation;
        }

        public bool CanRetry(AiJob job, AiJobStatusSemantics status) => true;

        public async ValueTask<AiJobRetryPreflight> GetPreflightAsync(
            AiJob job,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                CancellationObserved = true;
                if (_throwOnCancellation)
                    throw new InvalidOperationException("synchronous cancellation callback failure");
            });
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                // Stay in-flight until the test releases the handler. This models a provider
                // callback that observes cancellation but must still drain before its extension
                // descriptor can be unloaded.
                await Release.Task;
                throw;
            }

            return new AiJobRetryPreflight(true, true, "slow");
        }

        public ValueTask<AiJobRetryPreparationResult> PrepareAsync(
            AiJob job,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(AiJobRetryPreparationResult.Blocked("unused"));
    }

    private sealed class ThrowingCancelRetryHandler : SlowRetryHandler
    {
        public ThrowingCancelRetryHandler() : base(throwOnCancellation: true) { }
    }

    private sealed class BlockingResultHandler : IAiJobResultHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status) => new("Blocking", "Ready", "Open", "1×", false);
        public AiJobCompletionPresentation? CreateCompletion(AiJob job, AiJobStatusSemantics status, AiJobPresentation presentation) => null;
        public bool CanHandle(AiJob job, AiJobStatusSemantics status) => true;
        public async Task HandleAsync(AiJob job, IAiJobResultContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class ThrowingResultHandler : IAiJobResultHandler
    {
        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
            => throw new InvalidOperationException("presentation failure");

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status,
            AiJobPresentation presentation)
            => throw new InvalidOperationException("completion failure");

        public bool CanHandle(AiJob job, AiJobStatusSemantics status) => true;

        public Task HandleAsync(AiJob job, IAiJobResultContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CustomResultHandler(EditViewModel expectedEditor) : IAiJobResultHandler
    {
        public int HandleCount { get; private set; }

        public bool ResolvedSceneEditingContext { get; private set; }

        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
        {
            bool retryable = status.Outcome == new AiJobOutcomeId("vendor.retryable");
            return new AiJobPresentation(
                "Vendor upscale",
                retryable ? "Ready to retry" : "Ready to open",
                "Upscale this",
                "2×",
                retryable);
        }

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status,
            AiJobPresentation presentation)
            => null;

        public bool CanHandle(AiJob job, AiJobStatusSemantics status)
            => status.Outcome == new AiJobOutcomeId("vendor.ready");

        public Task HandleAsync(
            AiJob job,
            IAiJobResultContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HandleCount++;
            ResolvedSceneEditingContext =
                ReferenceEquals(context.Editor, expectedEditor)
                && ReferenceEquals(context.Editor.Scene, expectedEditor.Scene)
                && ReferenceEquals(
                    context.Editor.ElementAdder,
                    expectedEditor.GetService(typeof(IElementAdder)));
            return Task.CompletedTask;
        }
    }
}
