using System.Net;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Stubbed API clients, a signed-in user and the AI dialog view models for the AI tests.
internal static class AiTestSupport
{
    private static readonly string[] s_aiOperations =
    [
        "image.generate",
        "image.edit.remove_background",
        "image.edit.upscale",
        "image.edit.restyle",
        "image.edit.remove_object",
        "image.edit.outpaint",
        "audio.transcribe",
        "subtitle.translate",
        "video.generate",
    ];

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    // The server's error body. withDocumentationUrl: false leaves the field out, which is not the same as
    // null: the client's ApiErrorResponse requires it.
    public static HttpResponseMessage ErrorResponse(
        HttpStatusCode status, string code, string message, bool withDocumentationUrl = true)
    {
        var body = new Dictionary<string, string?> { ["error_code"] = code, ["message"] = message };
        if (withDocumentationUrl) body["documentation_url"] = null;
        return JsonResponse(status, System.Text.Json.JsonSerializer.Serialize(body));
    }

    public static string? IdempotencyKeyOf(HttpRequestMessage request)
        => request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values)
            ? values.SingleOrDefault()
            : null;

    public static HttpResponseMessage ByteResponse(byte[] bytes, string mediaType)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue(mediaType) },
            },
        };

    // Signs the clients in as id, or out when id is null. BeutlApiApplication has no way in for tests, so
    // this sets the user it holds.
    public static void SetAuthenticatedUser(BeutlApiApplication clients, string? id = "test-user")
    {
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var state = (ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(clients)!;
        state.Value = id is null ? null : CreateUser(clients, id);
    }

    // A user of the clients whose names and tokens come from id, signed in for the next hour.
    public static AuthenticatedUser CreateUser(BeutlApiApplication clients, string id = "test-user")
    {
        var profile = new Profile(new ProfileResponse
        {
            Id = id,
            Name = id,
            DisplayName = id,
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, clients);
        return new AuthenticatedUser(profile, new AuthResponse
        {
            Token = $"token-{id}",
            RefreshToken = $"refresh-{id}",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, clients, DateTime.UtcNow);
    }

    // An entitlements response for an active pro plan from 2026-08-01 to 2026-09-01 that can use AI for
    // operations, or for every AI operation unless given.
    public static string EntitlementsJson(
        string[]? operations = null, int usedPercent = 0, int additionalCredits = 0, bool cancelAtPeriodEnd = false)
    {
        string availability = string.Join(
            ",\n",
            (operations ?? s_aiOperations).Select(operation => $"    \"{operation}\": true"));
        return $$"""
            {
              "plan": "pro",
              "subscriptionStatus": "active",
              "currentPeriodStart": "2026-08-01T00:00:00Z",
              "currentPeriodEnd": "2026-09-01T00:00:00Z",
              "cancelAtPeriodEnd": {{(cancelAtPeriodEnd ? "true" : "false")}},
              "canUseAi": true,
              "balance": {
                "monthlyUsage": {
                  "usedPercent": {{usedPercent}},
                  "remainingPercent": {{100 - usedPercent}},
                  "isExhausted": false
                },
                "additionalCredits": {{additionalCredits}},
                "hasAdditionalCreditDebt": false
              },
              "availability": {
            {{availability}}
              }
            }
            """;
    }

    // The AI dialog view models over a test's API clients. Unless a test passes its own, each gets a fresh
    // form recovery context and a plan coordinator over the clients' entitlements.
    public static AiImageGenerationDialogViewModel CreateImageGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        AiRequestRecoveryContext? context = null,
        IAiPlanCoordinator? planCoordinator = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            planCoordinator ?? CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobClient>(),
            clients.GetResource<IAiJobKindRegistry>(),
            editor,
            context ?? AiRetryTestContext.CreateForm());

    public static AiImageEditDialogViewModel CreateImageEditDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        AiRequestRecoveryContext? context = null,
        IAiPlanCoordinator? planCoordinator = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            planCoordinator ?? CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageEditingService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobClient>(),
            clients.GetResource<IAiJobKindRegistry>(),
            editor,
            context ?? AiRetryTestContext.CreateForm());

    public static AiVideoGenerationDialogViewModel CreateVideoGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        AiRequestRecoveryContext? context = null,
        AiSourceVideoMode? sourceMode = null,
        IAiOperationAvailabilityService? availability = null,
        IAiPlanCoordinator? planCoordinator = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            availability ?? clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            planCoordinator ?? CreatePlanCoordinator(clients),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>(),
            clients.GetResource<IAiJobMonitor>(),
            clients.GetResource<IAiJobClient>(),
            editor,
            context ?? AiRetryTestContext.CreateForm(), sourceMode);

    public static AiSubtitleDialogViewModel CreateSubtitleDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        ICaptionDraftStore? draftStore = null,
        IObservable<CaptionDraftScope?>? draftScopes = null,
        IAiOperationAvailabilityService? availabilityService = null,
        CaptionCatalog? captionCatalog = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            availabilityService ?? clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            captionCatalog ?? CreateCaptionCatalog(),
            draftStore ?? CaptionDraftStoreProvider.Current,
            draftScopes ?? Observable.Return<CaptionDraftScope?>(null),
            editor);

    public static CaptionCatalog CreateCaptionCatalog()
        => CaptionCatalog.ComposeWithDefaultElementFactory(
            "Default",
            [],
            new DefaultTextCaptionElementFactory(Beutl.Media.FontFamily.Default));

    private static IAiPlanCoordinator CreatePlanCoordinator(BeutlApiApplication clients)
        => new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>());

    // Answers each request through responder. Unless handleAvailability is false, it answers the
    // availability check itself. With throwIfCanceled, a request whose token is already canceled throws
    // before it reaches responder, so it is not counted as an answered request.
    public sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        bool handleAvailability = true,
        bool throwIfCanceled = false) : HttpMessageHandler
    {
        public StubHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            bool handleAvailability = true,
            bool throwIfCanceled = false)
            : this((request, _) => Task.FromResult(responder(request)), handleAvailability, throwIfCanceled)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (throwIfCanceled)
                cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = handleAvailability
                && request.RequestUri?.AbsolutePath
                == "/api/v3/user/ai-availability"
                ? JsonResponse(HttpStatusCode.OK, "{ \"available\": true }")
                : await responder(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }

    // The handler, its HttpClient and the clients over them, signed in unless signIn is false; disposed in
    // reverse order of creation.
    public sealed class ApiScope : IAsyncDisposable
    {
        private readonly StubHandler _handler;
        private readonly HttpClient _http;

        public ApiScope(StubHandler handler, bool signIn = true)
        {
            _handler = handler;
            _http = new HttpClient(handler);
            Clients = new BeutlApiApplication(_http, new ExtensionProvider());
            if (signIn) SetAuthenticatedUser(Clients);
        }

        public BeutlApiApplication Clients { get; }

        public async ValueTask DisposeAsync()
        {
            await Clients.DisposeAsync();
            _http.Dispose();
            _handler.Dispose();
        }
    }
}
