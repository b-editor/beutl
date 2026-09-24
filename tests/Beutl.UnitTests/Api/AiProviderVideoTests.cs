using System.Net;
using System.Text.Json;
using Beutl.Api;
using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

public sealed partial class AiCapabilityServiceTests
{
    private static AiUploadSource VideoInput(string name, string type, long length = 3)
        => new(name, type, _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])), length);

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    public async Task ProviderVideo_DisposesEveryOpenedReferenceWhenCleanupFails(int failures, bool failLastOpen)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"jobId":"job","status":"queued"}"""));
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var first = new ReferenceDisposalProbe(throws: true);
        using var second = new ReferenceDisposalProbe(throws: failures == 2);
        using var last = new ReferenceDisposalProbe(throws: false);
        AiUploadSource Upload(string name, string mediaType, ReferenceDisposalProbe stream) => new(
            name, mediaType,
            _ => failLastOpen && ReferenceEquals(stream, last)
                ? ValueTask.FromException<Stream>(new IOException("Injected open failure."))
                : ValueTask.FromResult<Stream>(stream), 3);
        var request = new AiVideoGenerationRequest("scene", 5, new("720p"), new("16:9"), inputReferences:
        [
            Upload("a.png", "image/png", first),
            Upload("b.mp4", "video/mp4", second),
            Upload("c.wav", "audio/wav", last),
        ]);
        if (failLastOpen)
        {
            var error = Assert.ThrowsAsync<IOException>(async () =>
                await app.GetResource<IAiVideoService>().CreateAsync(request, CancellationToken.None));
            Assert.That(error!.Message, Is.EqualTo("Injected open failure."));
        }
        else
        {
            var result = await app.GetResource<IAiVideoService>().CreateAsync(request, CancellationToken.None);
            Assert.That(result.JobId.Value, Is.EqualTo("job"));
        }
        Assert.That(first.AsyncDisposals, Is.EqualTo(1));
        Assert.That(second.AsyncDisposals, Is.EqualTo(1));
        Assert.That(last.AsyncDisposals, Is.EqualTo(failLastOpen ? 0 : 1));
        Assert.That(handler.Requests.Count, Is.EqualTo(failLastOpen ? 0 : 1));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ReferenceCleanup_PreservesApiErrorsAndCancellation(bool image, bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler(_ =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return JsonResponse(HttpStatusCode.BadRequest, """{"error_code":"aiModelUnavailable","message":"Withdrawn model","documentation_url":null}""");
        });
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var first = new ReferenceDisposalProbe(throws: true);
        using var trailing = new ReferenceDisposalProbe(throws: false);
        AiUploadSource Upload(string name, Stream stream) => new(name, "image/png", _ => ValueTask.FromResult(stream), 3);
        var references = new[] { Upload("first.png", first), Upload("trailing.png", trailing) };
        async Task Send()
        {
            if (image)
                await app.GetResource<IAiImageGenerationService>().GenerateAsync(
                    new AiImageGenerationRequest("scene", new("1:1"), references: references), cancellation.Token);
            else
                await app.GetResource<IAiVideoService>().CreateAsync(
                    new AiVideoGenerationRequest("scene", 5, new("720p"), new("16:9"), inputReferences: references), cancellation.Token);
        }
        var error = Assert.CatchAsync<Exception>(Send);
        if (cancel) Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        else Assert.That(error, Is.TypeOf<AiModelUnavailableException>());
        Assert.That(first.AsyncDisposals, Is.EqualTo(1));
        Assert.That(trailing.AsyncDisposals, Is.EqualTo(1));
    }

    private sealed class ReferenceDisposalProbe(bool throws) : MemoryStream([1, 2, 3])
    {
        public int AsyncDisposals { get; private set; }
        public override ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            base.Dispose(disposing: true);
            return throws ? ValueTask.FromException(new IOException("Injected cleanup failure.")) : ValueTask.CompletedTask;
        }
    }

    [TestCase(AiSourceVideoMode.Edit)]
    [TestCase(AiSourceVideoMode.Extend)]
    [TestCase(AiSourceVideoMode.Motion)]
    public async Task SourceVideoCleanup_PreservesCreatedJob(AiSourceVideoMode mode)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"jobId":"job","status":"queued"}"""));
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var video = new ReferenceDisposalProbe(throws: true);
        using var character = new ReferenceDisposalProbe(throws: true);
        var request = new AiSourceVideoRequest(mode, "edit the scene",
            new("source.mp4", "video/mp4", _ => ValueTask.FromResult<Stream>(video), 3),
            durationSeconds: mode == AiSourceVideoMode.Edit ? null : 5,
            characterImage: mode == AiSourceVideoMode.Motion
                ? new("character.png", "image/png", _ => ValueTask.FromResult<Stream>(character), 3) : null);

        var result = await app.GetResource<IAiVideoService>().CreateFromSourceAsync(request, CancellationToken.None);

        Assert.That(result.JobId.Value, Is.EqualTo("job"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(video.AsyncDisposals, Is.EqualTo(1));
        Assert.That(character.AsyncDisposals, Is.EqualTo(mode == AiSourceVideoMode.Motion ? 1 : 0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceVideoCleanup_PreservesApiErrorsAndCancellation(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler(_ =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return JsonResponse(HttpStatusCode.BadRequest, """{"error_code":"aiModelUnavailable","message":"Withdrawn model","documentation_url":null}""");
        });
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var video = new ReferenceDisposalProbe(throws: true);
        using var character = new ReferenceDisposalProbe(throws: true);
        var request = new AiSourceVideoRequest(AiSourceVideoMode.Motion, "move the character",
            new("source.mp4", "video/mp4", _ => ValueTask.FromResult<Stream>(video), 3), durationSeconds: 5,
            characterImage: new("character.png", "image/png", _ => ValueTask.FromResult<Stream>(character), 3));

        var error = Assert.CatchAsync<Exception>(async () =>
            await app.GetResource<IAiVideoService>().CreateFromSourceAsync(request, cancellation.Token));

        if (cancel) Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        else Assert.That(error, Is.TypeOf<AiModelUnavailableException>());
        Assert.That(video.AsyncDisposals, Is.EqualTo(1));
        Assert.That(character.AsyncDisposals, Is.EqualTo(1));
    }

    [Test]
    public async Task SourceVideoCleanup_PreservesCharacterOpenFailure()
    {
        using var handler = new RecordingHandler(_ => throw new AssertionException("No request should be sent."));
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var video = new ReferenceDisposalProbe(throws: true);
        var openFailure = new IOException("Injected character open failure.");
        var request = new AiSourceVideoRequest(AiSourceVideoMode.Motion, "move the character",
            new("source.mp4", "video/mp4", _ => ValueTask.FromResult<Stream>(video), 3), durationSeconds: 5,
            characterImage: new("character.png", "image/png", _ => ValueTask.FromException<Stream>(openFailure), 3));

        var error = Assert.ThrowsAsync<IOException>(async () =>
            await app.GetResource<IAiVideoService>().CreateFromSourceAsync(request, CancellationToken.None));

        Assert.That(error, Is.SameAs(openFailure));
        Assert.That(video.AsyncDisposals, Is.EqualTo(1));
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(AiSourceVideoMode.Edit)]
    [TestCase(AiSourceVideoMode.Extend)]
    [TestCase(AiSourceVideoMode.Motion)]
    public async Task ProviderVideo_SourceUsesTheWebContractAndTheSameRetryKey(AiSourceVideoMode mode)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"jobId":"job","status":"queued"}"""));
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        string key = Guid.NewGuid().ToString();
        var request = new AiSourceVideoRequest(mode, "change the sky",
            sourceVideo: VideoInput("source.webm", "video/webm"),
            durationSeconds: mode == AiSourceVideoMode.Edit ? null : 5,
            characterImage: mode == AiSourceVideoMode.Motion ? VideoInput("character.png", "image/png") : null,
            orientation: "image", quality: "pro", model: new("gateway/model"), idempotencyKey: key);
        var service = app.GetResource<IAiVideoService>();
        await service.CreateFromSourceAsync(request, CancellationToken.None);
        await service.CreateFromSourceAsync(request, CancellationToken.None);
        var sent = handler.Requests.First();
        Assert.That(handler.Requests.Select(item => item.IdempotencyKey), Is.All.EqualTo(key));
        Assert.That(sent.Path, Is.EqualTo("/api/v3/ai/videos/" + mode.ToString().ToLowerInvariant()));
        Assert.That(sent.ContentType, Does.StartWith("multipart/form-data"));
        AssertQuotedMultipartNames(sent);
        Assert.That(sent.Body, Does.Contain("source.webm").And.Not.Contain("sourceJobId"));
        Assert.That(sent.Body.Contains("character.png"), Is.EqualTo(mode == AiSourceVideoMode.Motion));
    }

    [Test]
    public async Task ProviderVideo_ReferencesUseTypedMultipartWithoutAFirstFrame()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """{"jobId":"job","status":"queued"}"""));
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        await app.GetResource<IAiVideoService>().CreateAsync(new("scene", 5, new("720p"), new("16:9"),
            inputReferences: [VideoInput("a.png", "image/png"), VideoInput("b.mp4", "video/mp4"), VideoInput("c.wav", "audio/wav")]), CancellationToken.None);
        var sent = handler.Requests.Single();
        Assert.That(sent.Path, Is.EqualTo("/api/v3/ai/videos/frames"));
        AssertQuotedMultipartNames(sent);
        Assert.That(sent.Body, Does.Contain("reference[]").And.Contain("audio/wav").And.Not.Contain("firstFrame"));
        Assert.That(sent.Body.IndexOf("a.png"), Is.LessThan(sent.Body.IndexOf("b.mp4")));
        Assert.That(sent.Body.IndexOf("b.mp4"), Is.LessThan(sent.Body.IndexOf("c.wav")));
    }

    [Test]
    public void ProviderVideo_RejectsConflictingSourcesAndAggregateReferenceOverflow()
    {
        var video = VideoInput("clip.mp4", "video/mp4");
        Assert.Throws<ArgumentNullException>(() => new AiSourceVideoRequest(AiSourceVideoMode.Edit, "edit", null!));
        Assert.Throws<ArgumentException>(() => new AiSourceVideoRequest(AiSourceVideoMode.Motion, "move", video, durationSeconds: 5));
        Assert.Throws<ArgumentException>(() => new AiVideoGenerationRequest("scene", 5, new("720p"), new("16:9"), firstFrame: VideoInput("a.png", "image/png"), inputReferences: [video]));
        Assert.Throws<AiFileTooLargeException>(() => new AiVideoGenerationRequest("scene", 5, new("720p"), new("16:9"),
            inputReferences: [VideoInput("a.mp4", "video/mp4", 20 * 1024 * 1024), VideoInput("b.mp4", "video/mp4", 20 * 1024 * 1024)]));
    }

    [Test]
    public async Task ProviderVideo_MapsNewCapabilitiesAndKeepsOldModelDefaults()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"operations":{"video.generate":{"models":[{"id":"gateway/model","promptToVideo":false,"inputReferences":true,"maxInputReferences":9,"maxInputReferenceBytes":5242880,"maxVideoReferences":3,"maxVideoReferenceBytes":33554432,"maxAudioReferences":0,"maxAudioReferenceBytes":0,"maxPromptLength":80},{"id":"old/model"}]},"video.edit":{"models":[{"id":"gateway/editor","maxSourceVideoBytes":1024,"minSourceVideoSeconds":2,"maxSourceVideoSeconds":10}]}}}
            """));
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(app);
        var catalog = await app.GetResource<IAiModelCatalogService>().GetAsync(CancellationToken.None);
        var model = catalog.ModelsFor(AiOperations.VideoGeneration).Single(item => item.Id.Value == "gateway/model").Video!;
        Assert.That(model.SupportsPromptToVideo, Is.False);
        Assert.That(model.MaxInputReferences, Is.EqualTo(9));
        Assert.That(model.MaxVideoReferences, Is.EqualTo(3));
        Assert.That(model.MaxAudioReferences, Is.Zero);
        Assert.That(model.MaxPromptLength, Is.EqualTo(80));
        var old = catalog.ModelsFor(AiOperations.VideoGeneration).Single(item => item.Id.Value == "old/model").Video!;
        Assert.That(old.SupportsPromptToVideo, Is.True);
        Assert.That(old.SupportsInputReferences, Is.False);
        var editor = catalog.ModelsFor(AiOperations.VideoEditing).Single().Video!;
        Assert.That(editor.MaxSourceVideoBytes, Is.EqualTo(1024));
        Assert.That(editor.MinSourceVideoSeconds, Is.EqualTo(2));
        Assert.That(editor.MaxSourceVideoSeconds, Is.EqualTo(10));
    }
}
