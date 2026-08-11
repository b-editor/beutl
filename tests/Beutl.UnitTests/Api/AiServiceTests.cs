using System.Net;
using System.Reflection;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiCapabilityServiceTests
{
    // Monthly usage is proportional; the exact purchased balance appears only
    // in this account snapshot, not in ordinary operation responses.
    private const string EntitlementBalanceJson = """
        {
          "monthlyUsage": {
            "usedPercent": 12,
            "remainingPercent": 88,
            "isExhausted": false
          },
          "additionalCredits": 7,
          "hasAdditionalCreditDebt": false
        }
        """;

    [Test]
    public void Application_RegistersEachCapabilityIndependently()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));

        object[] services =
        [
            app.GetResource<IAiEntitlementService>(),
            app.GetResource<IAiImageGenerationService>(),
            app.GetResource<IAiImageEditingService>(),
            app.GetResource<IAiTranscriptionService>(),
            app.GetResource<IAiCaptionTranslationService>(),
            app.GetResource<IAiVideoService>(),
            app.GetResource<IAuthenticatedContentService>(),
            app.GetResource<IAiJobClient>(),
            app.GetResource<IAiJobMonitor>(),
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Distinct().Count(), Is.EqualTo(services.Length));
            Assert.That(
                typeof(IAiImageGenerationService).GetMethods()
                    .Concat(typeof(IAiImageEditingService).GetMethods())
                    .Select(method => method.ReturnType.ToString()),
                Has.None.Contains("Beutl.Api.Clients"));
            Assert.That(
                typeof(IAiTranscriptionService).GetMethods()
                    .Concat(typeof(IAiCaptionTranslationService).GetMethods())
                    .SelectMany(method => method.GetParameters())
                    .Select(parameter => parameter.ParameterType.Namespace),
                Has.None.EqualTo("Beutl.Api.Clients"));
        }
    }

    [Test]
    public async Task Entitlements_WhenSignedOut_ReturnsNullWithoutTransportRequest()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));

        AiEntitlements? result = await app.GetResource<IAiEntitlementService>()
            .RefreshAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(handler.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Entitlements_MapsServerDtoIntoDomainModelAndPublishesIt()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, $$"""
            {
              "plan": "pro",
              "subscriptionStatus": "active",
              "currentPeriodStart": "2026-08-01T00:00:00Z",
              "currentPeriodEnd": "2026-09-01T00:00:00Z",
              "canUseAi": true,
              "balance": {{EntitlementBalanceJson}},
              "availability": {
                "image.generate": true,
                "vendor.custom": false
              }
            }
            """));
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        IAiEntitlementService service = app.GetResource<IAiEntitlementService>();

        AiEntitlements? result = await service.RefreshAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.CanUseAi, Is.True);
            Assert.That(result.Balance.MonthlyUsage.RemainingPercent, Is.EqualTo(88));
            Assert.That(result.Availability.CanStart(AiOperations.ImageGeneration), Is.True);
            Assert.That(
                AiOperations.ImageEdit(new AiImageEditTaskId("remove_background")),
                Is.EqualTo(new AiOperationId("image.edit.remove_background")));
            Assert.That(result.Availability.CanStart(new AiOperationId("vendor.custom")), Is.False);
            Assert.That(service.Entitlements.Value, Is.SameAs(result));
            Assert.That(handler.Requests.Single().Authorization, Is.EqualTo("Bearer token"));
        }
    }

    [Test]
    public async Task ImageGeneration_UsesOperationRequestAndReturnsDomainIdentifiers()
    {
        using var handler = new RecordingHandler(request => request.Path switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/images" => JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "jobId": "job-image-1",
                  "fileId": "file-image-1",
                  "url": "https://beutl.beditor.net/api/contents/file-image-1"
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        IAiEntitlementService entitlements = app.GetResource<IAiEntitlementService>();
        await entitlements.RefreshAsync(CancellationToken.None);

        AiImageResult result = await app.GetResource<IAiImageGenerationService>().GenerateAsync(
            new AiImageGenerationRequest("A moonlit harbor", new AiImageSizeId("1536x1024")),
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Single(item => item.Path == "/api/v3/ai/images");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Body, Does.Contain("A moonlit harbor"));
            Assert.That(request.Body, Does.Contain("1536x1024"));
            Assert.That(result.JobId, Is.EqualTo(new AiJobId("job-image-1")));
            Assert.That(result.FileId, Is.EqualTo(new AiContentId("file-image-1")));
            Assert.That(
                entitlements.Entitlements.Value!.Balance.MonthlyUsage.UsedPercent,
                Is.EqualTo(12));
            Assert.That(
                entitlements.Entitlements.Value.Balance.AdditionalCredits,
                Is.EqualTo(7));
        }
    }

    [Test]
    public async Task OperationWithoutBalance_PreservesLastEntitlementSnapshot()
    {
        using var handler = new RecordingHandler(request => request.Path switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/images" => JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "job-image-2",
                  "fileId": "file-image-2",
                  "url": "https://beutl.beditor.net/api/contents/file-image-2"
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        IAiEntitlementService entitlements = app.GetResource<IAiEntitlementService>();
        await entitlements.RefreshAsync(CancellationToken.None);

        await app.GetResource<IAiImageGenerationService>().GenerateAsync(
            new AiImageGenerationRequest("Exhaust credits", new AiImageSizeId("1024x1024")),
            CancellationToken.None);

        AiBalance balance = entitlements.Entitlements.Value!.Balance;
        Assert.Multiple(() =>
        {
            Assert.That(balance.AdditionalCredits, Is.EqualTo(7));
            Assert.That(balance.MonthlyUsage.UsedPercent, Is.EqualTo(12));
            Assert.That(balance.HasAdditionalCreditDebt, Is.False);
        });
    }

    [Test]
    public async Task Translation_MapsTypedSegmentsWithoutExposingWireDtos()
    {
        using var handler = new RecordingHandler(request => request.Path switch
        {
            "/api/v3/ai/translations" => JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "jobId": "job-translate-1",
                  "segments": [
                    { "id": "cue-1", "text": "Bonjour" },
                    { "id": "cue-2", "text": "Monde" }
                  ]
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);

        AiCaptionTranslationResponse result = await app.GetResource<IAiCaptionTranslationService>()
            .TranslateAsync(
                new AiCaptionTranslationRequest(
                    [
                        new AiCaptionTranslationSegment
                        {
                            Id = "cue-1",
                            Text = "Hello",
                            Context = new AiCaptionTranslationSegmentContext(
                                "cue-1",
                                0,
                                TimeSpan.FromSeconds(1.5),
                                TimeSpan.FromSeconds(3)),
                        },
                        new AiCaptionTranslationSegment { Id = "cue-2", Text = "World" },
                    ],
                    " FR ",
                    " EN "),
                CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Body, Does.Contain("\"targetLanguage\":\"fr\""));
            Assert.That(request.Body, Does.Contain("\"sourceLanguage\":\"en\""));
            Assert.That(request.Body, Does.Contain(
                "\"context\":{\"groupId\":\"cue-1\",\"partIndex\":0,\"start\":1.5,\"end\":3}"));
            Assert.That(result.JobId, Is.EqualTo(new AiJobId("job-translate-1")));
            Assert.That(result.Segments.Select(segment => segment.Text),
                Is.EqualTo(new[] { "Bonjour", "Monde" }));
        }
    }

    [Test]
    public async Task Transcription_UsesMediaRequestAndReturnsDomainSegments()
    {
        using var handler = new RecordingHandler(request => request.Path switch
        {
            "/api/v3/ai/transcriptions" => JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "jobId": "job-stt-1",
                  "segments": [ { "start": 0.5, "end": 1.75, "text": "Hello" } ],
                  "language": "en",
                  "words": [ { "start": 0.5, "end": 1.0, "word": "Hello" } ]
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        var stream = new TrackingMemoryStream([1, 2, 3]);
        var source = new AiUploadSource(
            "speech.custom",
            "audio/x-beutl-test",
            _ => ValueTask.FromResult<Stream>(stream));

        AiTranscriptionResponse result = await app.GetResource<IAiTranscriptionService>()
            .TranscribeAsync(new AiTranscriptionRequest(source, "en"), CancellationToken.None);
        RecordedRequest request = handler.Requests.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.JobId, Is.EqualTo(new AiJobId("job-stt-1")));
            Assert.That(result.Segments.Single().Text, Is.EqualTo("Hello"));
            Assert.That(result.Words!.Single().Word, Is.EqualTo("Hello"));
            Assert.That(request.ContentType, Does.StartWith("multipart/form-data"));
            Assert.That(request.Body, Does.Contain("speech.custom"));
            Assert.That(request.Body, Does.Contain("audio/x-beutl-test"));
            Assert.That(stream.IsDisposed, Is.True);
        }
    }

    [Test]
    public async Task VideoCapability_OwnsCreationAndLookup()
    {
        using var handler = new RecordingHandler(request => request.Path switch
        {
            "/api/v3/ai/videos" => JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "jobId": "video-job-1",
                  "status": "queued"
                }
                """),
            "/api/v3/ai/videos/video-job-1" => JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "jobId": "video-job-1",
                  "status": "succeeded",
                  "fileId": "video-file-1",
                  "url": "https://beutl.beditor.net/api/contents/video-file-1",
                  "error": null
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        IAiVideoService service = app.GetResource<IAiVideoService>();

        AiVideoGenerationResult created = await service.CreateAsync(
            new AiVideoGenerationRequest("Ocean waves", 6, new AiVideoResolutionId("720p")),
            CancellationToken.None);
        AiVideoJob completed = await service.GetAsync(created.JobId, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Status, Is.EqualTo(AiJobStatuses.Queued));
            Assert.That(completed.Status, Is.EqualTo(AiJobStatuses.Succeeded));
            Assert.That(completed.FileId, Is.EqualTo(new AiContentId("video-file-1")));
            Assert.That(handler.Requests.Select(request => request.Path),
                Does.Contain("/api/v3/ai/videos/video-job-1"));
        }
    }

    [Test]
    public async Task JobClient_IsPureAsyncHistoryAndDeleteClient()
    {
        using var handler = new RecordingHandler(request => request.Method == HttpMethod.Delete
            ? JsonResponse(HttpStatusCode.OK, "{ \"deleted\": true }")
            : JsonResponse(HttpStatusCode.OK, """
                {
                  "jobs": [
                    {
                      "id": "job-1",
                      "kind": "vendor-kind",
                      "status": "vendor-status",
                      "inputParams": { "prompt": "Hello" },
                      "fileId": null,
                      "url": null,
                      "error": null,
                      "canRetry": false,
                      "createdAt": "2026-08-01T00:00:00Z",
                      "updatedAt": "2026-08-01T00:01:00Z"
                    }
                  ],
                  "nextCursor": "next"
                }
                """));
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        IAiJobClient client = app.GetResource<IAiJobClient>();

        AiJobPage page = await client.GetPageAsync(new AiJobPageRequest("cursor-value", 25), CancellationToken.None);
        await client.DeleteAsync(page.Jobs.Single().Id, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.NextCursor, Is.EqualTo("next"));
            Assert.That(page.Jobs.Single().Kind, Is.EqualTo(new AiJobKindId("vendor-kind")));
            Assert.That(page.Jobs.Single().Status, Is.EqualTo(new AiJobStatusId("vendor-status")));
            Assert.That(handler.Requests[0].Query, Does.Contain("cursor=cursor-value"));
            Assert.That(handler.Requests[0].Query, Does.Contain("limit=25"));
            Assert.That(handler.Requests[1].Method, Is.EqualTo(HttpMethod.Delete));
        }
    }

    [Test]
    public async Task AuthenticatedContent_RejectsForeignOriginsAndUsesCapturedBearer()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([4, 5, 6]),
        });
        using var httpClient = new HttpClient(handler);
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        SetAuthenticatedUser(app);
        IAuthenticatedContentService service = app.GetResource<IAuthenticatedContentService>();

        using var destination = new MemoryStream();
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CopyToAsync(
                new Uri("https://example.com/api/contents/file-1"),
                destination,
                CancellationToken.None));
        // Derive the accepted origin from the client so the assertion holds for
        // both the local and production API base addresses.
        await service.CopyToAsync(
            new Uri(app.HttpClient.BaseAddress!, "/api/contents/file-1"),
            destination,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(handler.Requests.Single().Authorization, Is.EqualTo("Bearer token"));
        }
    }

    private static string EntitlementsJson() => $$"""
        {
          "plan": "pro",
          "subscriptionStatus": "active",
          "currentPeriodStart": null,
          "currentPeriodEnd": null,
          "canUseAi": true,
          "balance": {{EntitlementBalanceJson}},
          "availability": {}
        }
        """;

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

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string? Authorization,
        string? ContentType,
        string Body);

    private sealed class RecordingHandler(
        Func<RecordedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.RequestUri?.Query ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));
            _requests.Add(recorded);
            HttpResponseMessage response = responder(recorded);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class TrackingMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
