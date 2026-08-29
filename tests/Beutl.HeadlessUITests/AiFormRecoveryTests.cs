using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiFormRecoveryTests
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Test]
    public void RecoveryRowsCarryCanonicalScalarsAndSourcesForEachForm()
    {
        string root = Path.Combine(Path.GetTempPath(), "Beutl.HeadlessUITests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = [11, 22, 33, 44];
            string sourcePath = Path.Combine(root, "input.png");
            File.WriteAllBytes(sourcePath, bytes);
            var store = new FileAiRequestRecoveryStore(root);
            AiRequestRecoverySource imageSource = FileAiRequestRecoveryStore.CreateExternalSource(
                "reference-0", sourcePath, "input.png", bytes);
            AiRequestRecoverySource editSource = FileAiRequestRecoveryStore.CreateExternalSource(
                "image", sourcePath, "input.png", bytes);
            AiRequestRecoverySource frameSource = store.CreateDurableSource(
                "first-frame", "frame.png", bytes);
            store.WriteOrGet(new AiPendingAttempt(
                "account",
                "image.generate",
                "image-fp",
                "image-key",
                "image-model",
                new AiRequestFormSnapshot(
                    Prompt: "image\nline",
                    AspectRatio: "3:2",
                    Background: "transparent",
                    Seed: 17),
                [imageSource]));
            store.WriteOrGet(new AiPendingAttempt(
                "account",
                "image.edit.upscale",
                "edit-fp",
                "edit-key",
                "edit-model",
                new AiRequestFormSnapshot(
                    Prompt: "edit\tline",
                    Task: "upscale",
                    SourceName: "input.png"),
                [editSource]));
            store.WriteOrGet(new AiPendingAttempt(
                "account",
                "video.generate",
                "video-fp",
                "video-key",
                null,
                new AiRequestFormSnapshot(
                    Prompt: "video\nline",
                    DurationSeconds: 8,
                    Resolution: "1080p",
                    AspectRatio: "16:9",
                    GenerateAudio: false,
                    Seed: 9),
                [frameSource]));

            var restarted = new FileAiRequestRecoveryStore(root);
            AiPendingAttempt image = restarted.Find("account", "image.generate", "image-fp")!;
            AiPendingAttempt edit = restarted.Find("account", "image.edit.upscale", "edit-fp")!;
            AiPendingAttempt video = restarted.Find("account", "video.generate", "video-fp")!;
            Assert.Multiple(() =>
            {
                Assert.That(image.Form!.Prompt, Is.EqualTo("image\nline"));
                Assert.That(image.Form.AspectRatio, Is.EqualTo("3:2"));
                Assert.That(image.Model, Is.EqualTo("image-model"));
                Assert.That(edit.Form!.Prompt, Is.EqualTo("edit\tline"));
                Assert.That(edit.EffectiveSources.Single().Path, Is.EqualTo(sourcePath));
                Assert.That(video.Form!.DurationSeconds, Is.EqualTo(8));
                Assert.That(video.Form.GenerateAudio, Is.False);
                Assert.That(restarted.TryResolveSource(video.EffectiveSources.Single(), out string? resolved), Is.True);
                Assert.That(resolved, Is.EqualTo(Path.Combine(restarted.SourceDirectory, frameSource.DurableFile!)));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task AccountSwitchClearsBoundRecoveryAndReturningRehydratesAllForms()
    {
        string root = Path.Combine(Path.GetTempPath(), "Beutl.HeadlessUITests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string sourcePath = Path.Combine(root, "edit.png");
        byte[] sourceBytes = [1, 2, 3];
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        string? account = "account-a";
        var store = new FileAiRequestRecoveryStore(root);
        AiRequestRecoverySource source = FileAiRequestRecoveryStore.CreateExternalSource(
            "image", sourcePath, "edit.png", sourceBytes);
        store.WriteOrGet(new AiPendingAttempt(
            "account-a",
            "image.generate",
            "gen-fingerprint",
            "gen-key",
            "gen-model",
            new AiRequestFormSnapshot(Prompt: "generation")));
        store.WriteOrGet(new AiPendingAttempt(
            "account-a",
            "image.edit.upscale",
            "edit-fingerprint",
            "edit-key",
            "edit-model",
            new AiRequestFormSnapshot(Prompt: "edit", Task: "upscale", SourceName: "edit.png"),
            [source]));
        store.WriteOrGet(new AiPendingAttempt(
            "account-a",
            "video.generate",
            "video-fingerprint",
            "video-key",
            null,
            new AiRequestFormSnapshot(Prompt: "video", DurationSeconds: 4, Resolution: "720p", AspectRatio: "16:9")));

        using var handler = new NotFoundHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://beutl.beditor.net") };
        await using var app = new BeutlApiApplication(client, new ExtensionProvider());
        using var context = new AiRequestRecoveryContext(
            store,
            () => account is { } value
                ? new AiAuthenticatedRequestIdentity(value, User: null)
                : null);

        await using var generation = CreateGeneration(app, context);
        await using var edit = CreateEdit(app, context);
        await using var video = CreateVideo(app, context);
        Assert.Multiple(() =>
        {
            Assert.That(generation.Prompt.Value, Is.EqualTo("generation"));
            Assert.That(edit.Prompt.Value, Is.EqualTo("edit"));
            Assert.That(edit.SourceFilePath.Value, Is.EqualTo(sourcePath));
            Assert.That(video.Prompt.Value, Is.EqualTo("video"));
        });

        account = "account-b";
        context.RefreshIdentity();
        Assert.Multiple(() =>
        {
            Assert.That(generation.SelectedRecoveryAttempt.Value, Is.Null);
            Assert.That(generation.Prompt.Value, Is.Empty);
            Assert.That(edit.SelectedRecoveryAttempt.Value, Is.Null);
            Assert.That(edit.SourceFilePath.Value, Is.Null);
            Assert.That(video.SelectedRecoveryAttempt.Value, Is.Null);
            Assert.That(video.Prompt.Value, Is.Empty);
        });

        account = "account-a";
        context.RefreshIdentity();
        Assert.Multiple(() =>
        {
            Assert.That(generation.SelectedRecoveryAttempt.Value?.Key, Is.EqualTo("gen-key"));
            Assert.That(edit.SelectedRecoveryAttempt.Value?.Key, Is.EqualTo("edit-key"));
            Assert.That(video.SelectedRecoveryAttempt.Value?.Key, Is.EqualTo("video-key"));
        });

        Directory.Delete(root, recursive: true);
    }

    [AvaloniaTest]
    public async Task RestartedFormsDispatchTheirPersistedKeyModelScalarsAndMultilinePrompt()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Beutl.HeadlessUITests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string sourcePath = Path.Combine(root, "input.png");
        byte[] sourceBytes = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        string imageKey;
        string editKey;
        string videoKey;
        var initialStore = new FileAiRequestRecoveryStore(root);
        using (var initialContext = NewContext(initialStore, "test-user"))
        {
            var imageKeyBuilder = new AiRequestKey(
                seed: "image-seed",
                recoveryContext: initialContext,
                operation: "image.generate");
            imageKey = imageKeyBuilder.NameFor(
                "generation line one line two",
                "3:2",
                "transparent",
                "17",
                "old-image").Key;
            AiPendingAttempt imageAttempt = initialStore.PendingFor("test-user", "image.generate").Single();
            initialStore.TryUpdateForm(
                "test-user",
                imageAttempt.Operation,
                imageAttempt.Fingerprint,
                imageAttempt.Key,
                new AiRequestFormSnapshot(
                    Prompt: "generation line one line two",
                    AspectRatio: "3:2",
                    Background: "transparent",
                    Seed: 17,
                    SupportsSeed: true,
                    MaxReferenceImages: 1,
                    MaxReferenceTotalBytes: 1024,
                    SupportsReferenceImage: false,
                    HasBackgroundChoice: true),
                []);

            var editKeyBuilder = new AiRequestKey(
                seed: "edit-seed",
                recoveryContext: initialContext,
                operation: "image.edit");
            editKey = editKeyBuilder.NameFor(
                [
                    "restyle",
                    "edit\tline",
                    "old-edit",
                    AiRequestKey.FileStamp("input.png", sourceBytes),
                ]).Key;
            AiPendingAttempt editAttempt = initialStore.PendingFor("test-user", "image.edit.restyle").Single();
            initialStore.TryUpdateForm(
                "test-user",
                editAttempt.Operation,
                editAttempt.Fingerprint,
                editAttempt.Key,
                new AiRequestFormSnapshot(
                    Prompt: "edit\tline",
                    Task: "restyle",
                    SourceName: "input.png"),
                [FileAiRequestRecoveryStore.CreateExternalSource(
                    "image",
                    sourcePath,
                    "input.png",
                    sourceBytes)]);

            var videoKeyBuilder = new AiRequestKey(
                seed: "video-seed",
                recoveryContext: initialContext,
                operation: "video.generate");
            videoKey = videoKeyBuilder.NameFor(
                "video line one line two",
                "8",
                "1080p",
                "9:16",
                "audio",
                "9",
                "old-video",
                "",
                "").Key;
            AiPendingAttempt videoAttempt = initialStore.PendingFor("test-user", "video.generate").Single();
            initialStore.TryUpdateForm(
                "test-user",
                videoAttempt.Operation,
                videoAttempt.Fingerprint,
                videoAttempt.Key,
                new AiRequestFormSnapshot(
                    Prompt: "video line one line two",
                    DurationSeconds: 8,
                    Resolution: "1080p",
                    AspectRatio: "9:16",
                    GenerateAudio: true,
                    Seed: 9,
                    SupportsAudio: true,
                    SupportsSeed: true,
                    SupportsFirstFrame: false,
                    SupportsLastFrame: false),
                []);
        }

        var requests = new List<(string Path, string? Key, string Body)>();
        using var handler = new RecoveryDispatchHandler(requests, s_png);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://beutl.beditor.net") };
        await using var app = new BeutlApiApplication(client, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var context = NewContext(new FileAiRequestRecoveryStore(root), "test-user");
        await using var generation = CreateGeneration(app, context);
        await using var edit = CreateEdit(app, context);
        await using var video = CreateVideo(app, context);
        edit.SourceFilePath.Value = sourcePath;
        edit.SelectedTask.Value = edit.Tasks.Single(task => task.Value == "restyle");

        await WaitUntilAsync(() => generation.Usage.HasSnapshot.Value
            && edit.Usage.HasSnapshot.Value
            && video.Usage.HasSnapshot.Value
            && generation.ModelPicker.IsLoaded.Value
            && edit.ModelPicker.IsLoaded.Value
            && video.ModelPicker.IsLoaded.Value);
        Assert.Multiple(() =>
        {
            Assert.That(generation.CanGenerate.Value, Is.True, $"generation Error={generation.Error.Value}");
            Assert.That(edit.CanEdit.Value, Is.True, $"edit Error={edit.Error.Value}");
            Assert.That(video.CanGenerate.Value, Is.True, $"video Error={video.Error.Value}");
        });
        await generation.Generate.ExecuteAsync();
        await edit.Edit.ExecuteAsync();
        await video.Generate.ExecuteAsync();
        await WaitUntilAsync(() => requests.Count >= 3
            || generation.Error.Value is not null
            || edit.Error.Value is not null
            || video.Error.Value is not null);
        await WaitUntilAsync(() => generation.SelectedRecoveryAttempt.Value is null
            || generation.Error.Value is not null);
        await WaitUntilAsync(() => video.SelectedRecoveryAttempt.Value is null
            || video.Error.Value is not null);
        Assert.Multiple(() =>
        {
            Assert.That(generation.SelectedRecoveryAttempt.Value, Is.Null,
                $"generation Error={generation.Error.Value}");
            Assert.That(video.SelectedRecoveryAttempt.Value, Is.Null,
                $"video Error={video.Error.Value}");
        });

        Assert.That(
            requests,
            Is.Not.Empty,
            $"generation={generation.Error.Value}; edit={edit.Error.Value}; video={video.Error.Value}; "
                + string.Join("\n", requests.Select(request => $"{request.Path} key={request.Key} body={request.Body}")));
        Assert.Multiple(() =>
        {
            Assert.That(requests.Any(request => request.Path == "/api/v3/ai/images"
                && request.Key == imageKey
                && request.Body.Contains("generation line one line two", StringComparison.Ordinal)
                && request.Body.Contains("3:2", StringComparison.Ordinal)
                && request.Body.Contains("transparent", StringComparison.Ordinal)
                && request.Body.Contains("old-image", StringComparison.Ordinal)),
                Is.True,
                string.Join("\n", requests.Select(request => $"{request.Path} key={request.Key} body={request.Body}")));
            Assert.That(requests.Any(request => request.Path == "/api/v3/ai/images/edit"
                && request.Key == editKey
                && request.Body.Contains("edit\tline", StringComparison.Ordinal)
                && request.Body.Contains("old-edit", StringComparison.Ordinal)),
                Is.True,
                string.Join("\n", requests.Select(request => $"{request.Path} key={request.Key} body={request.Body}")));
            Assert.That(requests.Any(request => request.Path == "/api/v3/ai/videos"
                && request.Key == videoKey
                && request.Body.Contains("video line one line two", StringComparison.Ordinal)
                && request.Body.Contains("1080p", StringComparison.Ordinal)
                && request.Body.Contains("8", StringComparison.Ordinal)
                && request.Body.Contains("9:16", StringComparison.Ordinal)
                && request.Body.Contains("old-video", StringComparison.Ordinal)),
                Is.True,
                string.Join("\n", requests.Select(request => $"{request.Path} key={request.Key} body={request.Body}")));
            Assert.That(generation.AspectRatioOptions.Any(option => option.Value == "3:2"), Is.False);
            Assert.That(generation.BackgroundOptions.Any(option => option.Value == "transparent"), Is.False);
            Assert.That(video.DurationOptions.Any(option => option.Seconds == 8), Is.False);
            Assert.That(video.ResolutionOptions.Any(option => option.Value == "1080p"), Is.False);
            Assert.That(video.AspectRatioOptions.Any(option => option.Value == "9:16"), Is.False);
        });

        Directory.Delete(root, recursive: true);
    }

    [AvaloniaTest]
    public async Task AbandonRemovesUnsupportedRecoveredOptionsFromNewPurchases()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Beutl.HeadlessUITests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileAiRequestRecoveryStore(root);
        store.WriteOrGet(new AiPendingAttempt(
            "test-user",
            "image.generate",
            "image-abandon",
            "image-abandon-key",
            "old-image",
            new AiRequestFormSnapshot(
                Prompt: "image",
                AspectRatio: "3:2",
                Background: "transparent")));
        store.WriteOrGet(new AiPendingAttempt(
            "test-user",
            "video.generate",
            "video-abandon",
            "video-abandon-key",
            "old-video",
            new AiRequestFormSnapshot(
                Prompt: "video",
                DurationSeconds: 8,
                Resolution: "1080p",
                AspectRatio: "9:16")));

        var requests = new List<(string Path, string? Key, string Body)>();
        using var handler = new RecoveryDispatchHandler(requests, s_png);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://beutl.beditor.net") };
        await using var app = new BeutlApiApplication(client, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var context = NewContext(store, "test-user");
        await using var generation = CreateGeneration(app, context);
        await using var video = CreateVideo(app, context);

        await WaitUntilAsync(() => generation.ModelPicker.IsLoaded.Value
            && video.ModelPicker.IsLoaded.Value
            && generation.SelectedRecoveryAttempt.Value is not null
            && video.SelectedRecoveryAttempt.Value is not null);
        Assert.Multiple(() =>
        {
            Assert.That(generation.AspectRatioOptions.Any(option => option.Value == "3:2"), Is.True);
            Assert.That(generation.BackgroundOptions.Any(option => option.Value == "transparent"), Is.True);
            Assert.That(video.DurationOptions.Any(option => option.Seconds == 8), Is.True);
            Assert.That(video.ResolutionOptions.Any(option => option.Value == "1080p"), Is.True);
            Assert.That(video.AspectRatioOptions.Any(option => option.Value == "9:16"), Is.True);
        });

        generation.AbandonPendingAttempt(generation.SelectedRecoveryAttempt.Value!);
        video.AbandonPendingAttempt(video.SelectedRecoveryAttempt.Value!);

        Assert.Multiple(() =>
        {
            Assert.That(generation.AspectRatioOptions.Any(option => option.Value == "3:2"), Is.False);
            Assert.That(generation.BackgroundOptions.Any(option => option.Value == "transparent"), Is.False);
            Assert.That(video.DurationOptions.Any(option => option.Seconds == 8), Is.False);
            Assert.That(video.ResolutionOptions.Any(option => option.Value == "1080p"), Is.False);
            Assert.That(video.AspectRatioOptions.Any(option => option.Value == "9:16"), Is.False);
            Assert.That(store.PendingFor("test-user", "image.generate"), Is.Empty);
            Assert.That(store.PendingFor("test-user", "video.generate"), Is.Empty);
        });

        Directory.Delete(root, recursive: true);
    }

    private static AiImageGenerationDialogViewModel CreateGeneration(
        BeutlApiApplication app,
        AiRequestRecoveryContext context)
        => new(
            app.GetResource<IAiEntitlementService>(),
            app.GetResource<IAiOperationAvailabilityService>(),
            app.GetResource<IAiModelCatalogService>(),
            new TestPlanCoordinator(),
            app.GetResource<IAiImageGenerationService>(),
            app.GetResource<IAuthenticatedContentService>(),
            editViewModel: null,
            context);

    private static AiImageEditDialogViewModel CreateEdit(
        BeutlApiApplication app,
        AiRequestRecoveryContext context)
        => new(
            app.GetResource<IAiEntitlementService>(),
            app.GetResource<IAiOperationAvailabilityService>(),
            app.GetResource<IAiModelCatalogService>(),
            new TestPlanCoordinator(),
            app.GetResource<IAiImageEditingService>(),
            app.GetResource<IAuthenticatedContentService>(),
            editViewModel: null,
            context);

    private static AiVideoGenerationDialogViewModel CreateVideo(
        BeutlApiApplication app,
        AiRequestRecoveryContext context)
        => new(
            app.GetResource<IAiEntitlementService>(),
            app.GetResource<IAiOperationAvailabilityService>(),
            app.GetResource<IAiModelCatalogService>(),
            new TestPlanCoordinator(),
            app.GetResource<IAiVideoService>(),
            app.GetResource<IAuthenticatedContentService>(),
            app.GetResource<IAiJobKindRegistry>(),
            app.GetResource<IAiJobMonitor>(),
            editViewModel: null,
            context);

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent("{}"),
            });
    }

    private static AiRequestRecoveryContext NewContext(FileAiRequestRecoveryStore store, string account)
        => new(store, () => new AiAuthenticatedRequestIdentity(account, User: null));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
            HeadlessTestHelpers.Settle();
        }
    }

    private static void SetAuthenticatedUser(BeutlApiApplication app)
    {
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
        ((ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
    }

    private static string RecoveryCapabilitiesJson() => """
        {
          "operations": {
            "image.generate": {
              "models": [{ "id": "current-image", "displayName": "Current image", "costTier": "low", "isDefault": true, "aspectRatios": ["1:1"], "backgrounds": ["auto"], "seed": false, "maxReferenceImages": 0 }],
              "aspectRatios": ["1:1"], "backgrounds": ["auto"]
            },
            "image.edit.upscale": {
              "models": [{ "id": "current-edit", "displayName": "Current edit", "costTier": "low", "isDefault": true }]
            },
            "video.generate": {
              "models": [{ "id": "current-video", "displayName": "Current video", "costTier": "low", "isDefault": true, "durationsSeconds": [4], "resolutions": ["720p"], "aspectRatios": ["16:9"], "audio": false, "seed": false, "firstFrame": false, "lastFrame": false }],
              "durationsSeconds": [4], "resolutions": ["720p"], "aspectRatios": ["16:9"]
            }
          }
        }
        """;

    private static string RecoveryEntitlementsJson() => """
        {
          "plan":"pro","subscriptionStatus":"active","currentPeriodStart":"2026-08-01T00:00:00Z","currentPeriodEnd":"2026-09-01T00:00:00Z","canUseAi":true,
          "balance":{"monthlyUsage":{"usedPercent":0,"remainingPercent":100,"isExhausted":false},"additionalCredits":10,"hasAdditionalCreditDebt":false},
          "availability":{"image.generate":true,"image.edit.upscale":true,"video.generate":true}
        }
        """;

    private sealed class RecoveryDispatchHandler(
        List<(string Path, string? Key, string Body)> requests,
        byte[] content) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path is "/api/v3/user/entitlements")
                return Json(RecoveryEntitlementsJson());
            if (path is "/api/v3/ai/capabilities")
                return Json(RecoveryCapabilitiesJson());
            if (path is "/api/v3/ai/images" or "/api/v3/ai/images/edit" or "/api/v3/ai/videos")
            {
                string? key = request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values)
                    ? values.SingleOrDefault()
                    : null;
                requests.Add((path, key, body));
                if (path == "/api/v3/ai/videos")
                    return Json("{\"jobId\":\"recovered-video-job\",\"status\":\"queued\"}");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(
                        "{\"error_code\":\"aiProviderError\",\"message\":\"Provider failed.\"}",
                        Encoding.UTF8,
                        "application/json"),
                };
            }
            if (path == "/api/v3/ai/videos/recovered-video-job")
                return Json("{\"jobId\":\"recovered-video-job\",\"status\":\"failed\",\"error\":\"aiProviderError\"}");
            if (path == "/api/contents/recovered-file")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("image/png") },
                    },
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}"),
            };
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class TestPlanCoordinator : IAiPlanCoordinator
    {
        public void OpenAccountSettings() { }

        public void OpenAiPlan() { }

        public Task RefreshIfPendingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
