using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;

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
            new UnusedEntitlementService());
        _resultHandlers = new AiJobResultHandlerRegistry(BuiltInAiJobResultHandlers.Create());
    }

    [TearDown]
    public void TearDown()
    {
        _resultHandlers.Dispose();
        _jobKinds.Dispose();
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
            Assert.That(item.CanRetry, Is.True);
            Assert.That(item.CanDelete, Is.True);
            Assert.That(item.CanAddToScene, Is.False);
        }
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

    [AvaloniaTest]
    public void View_UsesVirtualizingPanelAndLabelsEveryAction()
    {
        var view = new AiJobCenterView();
        var viewWindow = new Window { Content = view, Width = 360, Height = 640 };
        Window? itemWindow = null;
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "failed",
            inputParams: ParseInput("""{ "prompt": "Accessible action" }"""),
            canRetry: true));

        try
        {
            viewWindow.Show();
            HeadlessTestHelpers.Render();

            ListBox jobList = view.FindControl<ListBox>("JobList")!;
            Assert.That(jobList, Is.Not.Null);
            Assert.That(
                jobList.GetVisualDescendants().OfType<VirtualizingStackPanel>(),
                Is.Not.Empty,
                "The job list must keep its explicitly virtualizing items panel.");

            AssertAction(view.FindControl<Button>("RefreshButton"), Strings.Refresh);
            AssertAction(view.FindControl<Button>("LoadMoreButton"), Strings.AiJobCenter_LoadMore);

            IDataTemplate template = jobList.ItemTemplate!;
            Control content = new ContentPresenter
            {
                Content = item,
                ContentTemplate = template,
            };
            itemWindow = new Window { Content = content, Width = 340, Height = 320 };
            itemWindow.Show();
            HeadlessTestHelpers.Render();

            List<Button> buttons = content.GetVisualDescendants().OfType<Button>().ToList();
            AssertAction(buttons.Single(button => Equals(button.Content, Strings.AiAddToScene)), Strings.AiAddToScene);
            AssertAction(
                buttons.Single(button => Equals(button.Content, Strings.AiJobCenter_Retry)),
                Strings.AiJobCenter_Retry);
            AssertAction(buttons.Single(button => Equals(button.Content, Strings.Delete)), Strings.Delete);
        }
        finally
        {
            itemWindow?.Close();
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
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, JobsJson(deleted)),
            "/api/v3/ai/images" => RetryImage(),
            "/api/v3/ai/jobs/job-failed" when request.Method == HttpMethod.Delete => DeleteJob(),
            "/api/contents/file-success" => ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);

        await WaitUntilAsync(() => viewModel.Jobs.Count == 2);
        AiJobItemViewModel completed = viewModel.Jobs.Single(item => item.Id == "job-success");
        AiJobItemViewModel failed = viewModel.Jobs.Single(item => item.Id == "job-failed");

        await viewModel.AddToSceneAsync(completed);
        await viewModel.RequestRetryConfirmationAsync(failed);
        await viewModel.ConfirmPendingActionAsync();
        viewModel.RequestDeleteConfirmation(failed);
        Assert.That(viewModel.ConfirmationTitle.Value, Is.EqualTo(Strings.AiJobCenter_DeleteTitle));
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
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
            "/api/v3/ai/jobs" => JsonResponse(HttpStatusCode.OK, """{ "jobs": [], "nextCursor": null }"""),
            "/api/v3/ai/images" => RetryImage(),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateJobCenter(editor, clients);
        using var item = CreateItem(CreateJob(
            kind: "image",
            status: "failed",
            inputParams: ParseInput("""{ "prompt": "Retry at the current price" }"""),
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
        using var clients = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
        using IAiJobKindRegistration registration = jobKinds.Register(descriptor);
        using IDisposable resultRegistration = _resultHandlers.Register(
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class UnusedImageGenerationService : IAiImageGenerationService
    {
        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
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
            return ValueTask.FromResult(new AiJobRetryPreflight(true, true, "No additional charge"));
        }

        public Task RetryAsync(
            AiJob job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RetryCount++;
            return Task.CompletedTask;
        }
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
