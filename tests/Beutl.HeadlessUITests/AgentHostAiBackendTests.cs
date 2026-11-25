using System.Diagnostics.CodeAnalysis;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AgentHostAiBackendTests
{
    private const string Extension = ".agentaudio";
    private string _directory = null!;

    private DecoderRegistry _decoders = null!;

    [SetUp]
    public void SetUp()
    {
        _decoders = new DecoderRegistry(new());
        _directory = Path.Combine(Path.GetTempPath(), "beutl-agent-backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaTest]
    public async Task TheSignedInAccountAndItsPlanDecideWhetherAgentsCanGenerate()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://beutl.beditor.net") };
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider(new Beutl.Configuration.ExtensionConfig()), new Beutl.Configuration.ViewConfig(), new Beutl.Configuration.UpdateConfig());
        AuthenticatedUser user = CreateUser(app);
        var entitlements = new StubEntitlements();

        bool knownBeforeRead = CreateBackend(() => user, entitlements).KnowsModelAvailability;
        string? signedOut = await CreateBackend(() => null, entitlements).GetUnavailableReasonAsync(CancellationToken.None);
        entitlements.Current.Value = Entitlements(canUseAi: false);
        bool knownAfterRead = CreateBackend(() => user, entitlements).KnowsModelAvailability;
        string? noPlan = await CreateBackend(() => user, entitlements).GetUnavailableReasonAsync(CancellationToken.None);
        entitlements.Current.Value = null;
        entitlements.Refreshed = Entitlements(canUseAi: true);
        string? refreshedPlan = await CreateBackend(() => user, entitlements).GetUnavailableReasonAsync(CancellationToken.None);
        entitlements.Refreshed = null;
        entitlements.RefreshError = new HttpRequestException("offline");
        string? offline = await CreateBackend(() => user, entitlements).GetUnavailableReasonAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(knownBeforeRead, Is.False, "the plan has not been read");
            Assert.That(knownAfterRead, Is.True);
            Assert.That(signedOut, Does.Contain("Sign in"));
            Assert.That(noPlan, Does.Contain("Pro plan"));
            Assert.That(refreshedPlan, Is.Null);
            Assert.That(offline, Is.Null, "the service gives its own reason when the request is sent");
        });
    }

    [Test]
    public async Task ModelsAndExecutorsComeFromTheApp()
    {
        GenerativeModelInfo model = new("m", "M", IsDefault: true, IsAvailable: true, Image: null);
        var catalog = new StubCatalog([model]);
        var executor = new StubExecutor();
        Scene? seen = null;
        var backend = new AgentHostAiBackend(
            CreateRecoveryContext(() => null), new StubEntitlements(), new StubAvailability(), new StubTranscription(),
            () => catalog, scene =>
            {
                seen = scene;
                return executor;
            },
            new StubModelCatalog(), _decoders);
        var scene = new Scene();

        IReadOnlyList<GenerativeModelInfo> models = await backend.GetModelsAsync("image.generate", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models, Is.EqualTo(new[] { model }));
            Assert.That(catalog.Operations, Is.EqualTo(new[] { "image.generate" }));
            Assert.That(backend.CreateExecutor(scene), Is.SameAs(executor));
            Assert.That(seen, Is.SameAs(scene));
        });
    }

    [Test]
    public async Task ALongRecordingIsTranscribedInPartsAndKeepsItsTimes()
    {
        const int sampleRate = 8_000;
        // Ten minutes and two seconds: a full part and a short one.
        string path = WriteSource("long" + Extension);
        var decoder = new StubDecoder(() => new StubAudioReader(sampleRate, sampleRate * 602L));
        var availability = new StubAvailability();
        var transcription = new StubTranscription();
        var progress = new RecordingProgress();
        _decoders.Register(decoder);
        AgentTranscript transcript = (await CreateBackend(availability: availability, transcription: transcription)
            .TranscribeAsync(path, "ja", "whisper", progress, CancellationToken.None)).Transcript;

        Assert.Multiple(() =>
        {
            Assert.That(transcription.Requests, Has.Count.EqualTo(2));
            Assert.That(transcription.Requests.Select(request => request.Language), Is.All.EqualTo("ja"));
            Assert.That(transcription.Requests.Select(request => request.Model?.Value), Is.All.EqualTo("whisper"));
            Assert.That(availability.Requests, Has.Count.EqualTo(2));
            Assert.That(transcript.Language, Is.EqualTo("ja"));
            Assert.That(transcript.Segments.Select(segment => (segment.Start, segment.Text)),
                Is.EqualTo(new[] { (1d, "part 1"), (601d, "part 2") }), "the second part is offset by the first's ten minutes");
            Assert.That(transcript.Words!.Select(word => word.Start), Is.EqualTo(new[] { 1d, 601d }));
            Assert.That(progress.Values, Does.Contain("Transcribing part 2 of 2"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetryingATranscriptionSkipsCompletedPartsAndRecoversThePendingPart(bool settledFailure)
    {
        const int sampleRate = 8_000;
        string path = WriteSource("retry" + Extension);
        _decoders.Register(new StubDecoder(() => new StubAudioReader(sampleRate, sampleRate * 602L)));
        var transcription = new StubTranscription();
        transcription.OnRequest = () =>
        {
            if (transcription.Requests.Count == 2)
            {
                throw settledFailure
                    ? new AiProviderErrorException()
                    : new HttpRequestException("The response was lost.");
            }
        };
        var progress = new RecordingProgress();
        var retry = new AgentTranscriptionRetry("transcribe-1", RequestedModel: null);
        AgentHostAiBackend first = CreateBackend(transcription: transcription);
        await Assert.ThrowsAsync(settledFailure ? typeof(AgentAiException) : typeof(HttpRequestException),
            () => first.TranscribeAsync(path, "ja", "whisper", progress, CancellationToken.None, retry));

        // Reopen the recovery files with a new backend. An already submitted part must be
        // retrievable even when there are no credits left, with the originally resolved model.
        var availability = new StubAvailability { Allowed = settledFailure };
        AgentHostAiBackend second = CreateBackend(availability: availability, transcription: transcription);
        AgentTranscriptionResult resumed = await second.TranscribeAsync(
            path, "ja", "new-default", progress, CancellationToken.None, retry);
        AgentTranscriptionResult completed = await second.TranscribeAsync(
            path, "ja", "new-default", progress, CancellationToken.None, retry);
        AgentAiException? changed = await Assert.ThrowsAsync<AgentAiException>(() => second.TranscribeAsync(
            path, "en", "new-default", progress, CancellationToken.None, retry));

        Assert.Multiple(() =>
        {
            Assert.That(transcription.Requests, Has.Count.EqualTo(3));
            Assert.That(availability.Requests, Has.Count.EqualTo(settledFailure ? 1 : 0));
            Assert.That(transcription.Requests[2].IdempotencyKey, settledFailure
                ? Is.Not.EqualTo(transcription.Requests[1].IdempotencyKey)
                : Is.EqualTo(transcription.Requests[1].IdempotencyKey));
            Assert.That(transcription.Requests.Select(request => request.Model?.Value), Is.All.EqualTo("whisper"));
            Assert.That(resumed.ModelId, Is.EqualTo("whisper"));
            Assert.That(resumed.Transcript.Segments.Select(segment => (segment.Start, segment.Text)),
                Is.EqualTo(new[] { (1d, "part 1"), (601d, "part 3") }));
            Assert.That(resumed.Transcript.Words!.Select(word => word.Start), Is.EqualTo(new[] { 1d, 601d }));
            Assert.That(completed.Transcript.Segments, Is.EqualTo(resumed.Transcript.Segments));
            Assert.That(changed?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [AvaloniaTest]
    [TestCase(null)]
    [TestCase("whisper")]
    public async Task ATranscriptionRetryRecoversThroughTheToolWhenNoModelIsAffordable(string? requestedModel)
    {
        const int sampleRate = 8_000;
        string path = WriteSource("paid" + Extension);
        _decoders.Register(new StubDecoder(() => new StubAudioReader(sampleRate, sampleRate * 2L)));
        var model = new GenerativeModelInfo("whisper", "Whisper", IsDefault: true, IsAvailable: true, Image: null);
        var transcription = new StubTranscription();
        transcription.OnRequest = () =>
        {
            if (transcription.Requests.Count == 1)
                throw new AiResultUnavailableException();
        };
        using var originalJobs = new AgentAiJobManager();
        var originalTools = new AgentHostAiTools(TestShell.Editor, originalJobs,
            CreateBackend(transcription: transcription, catalog: new StubCatalog([model])), new AgentSessionManager());
        var first = await originalTools.TranscribeAudio(path, model: requestedModel, waitSeconds: 10, requestKey: "paid");
        Assert.That(first.Value?.Status, Is.EqualTo(AgentAiJobStatus.Failed), first.Error?.Message);

        var entitlements = new StubEntitlements();
        entitlements.Current.Value = Entitlements(canUseAi: true);
        var availability = new StubAvailability { Allowed = false };
        var catalog = new StubCatalog([model with { IsAvailable = false }]);
        AgentHostAiBackend backend = CreateBackend(entitlements: entitlements, availability: availability,
            transcription: transcription, catalog: catalog);
        using var retryJobs = new AgentAiJobManager();
        var retryTools = new AgentHostAiTools(TestShell.Editor, retryJobs, backend, new AgentSessionManager());
        var recovered = await retryTools.TranscribeAudio(path, model: requestedModel, waitSeconds: 10, requestKey: "paid");
        using var reopenedJobs = new AgentAiJobManager();
        var reopenedTools = new AgentHostAiTools(TestShell.Editor, reopenedJobs, backend, new AgentSessionManager());
        var completed = await reopenedTools.TranscribeAudio(path, model: requestedModel, waitSeconds: 10, requestKey: "paid");

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Value?.Status, Is.EqualTo(AgentAiJobStatus.Succeeded), recovered.Error?.Message);
            Assert.That(recovered.Value?.Output?.ModelId, Is.EqualTo(model.Id));
            Assert.That(completed.Value?.Status, Is.EqualTo(AgentAiJobStatus.Succeeded), completed.Error?.Message);
            Assert.That(catalog.Operations, Is.Empty);
            Assert.That(availability.Requests, Is.Empty);
            Assert.That(transcription.Requests, Has.Count.EqualTo(2));
            Assert.That(transcription.Requests[1].IdempotencyKey, Is.EqualTo(transcription.Requests[0].IdempotencyKey));
        });

        var fresh = await retryTools.TranscribeAudio(path, model: requestedModel, waitSeconds: 10, requestKey: "fresh");
        Assert.That(fresh.IsSuccess, Is.False, "a new request still checks whether the model is available");
    }

    [Test]
    public async Task CompletedTranscriptsExpireWithoutRechargingOrRemovingPendingProgress()
    {
        const int sampleRate = 8_000;
        string path = WriteSource("retention" + Extension);
        _decoders.Register(new StubDecoder(() => new StubAudioReader(sampleRate, sampleRate * 2L)));
        var completedKey = new AgentTranscriptionRetry("completed", RequestedModel: null);
        var pendingKey = new AgentTranscriptionRetry("pending", RequestedModel: null);
        var pendingService = new StubTranscription { OnRequest = () => throw new AiResultUnavailableException() };
        var transcription = new StubTranscription();
        var progress = new RecordingProgress();
        await CreateBackend(transcription: transcription).TranscribeAsync(
            path, null, null, progress, CancellationToken.None, completedKey);
        await Assert.ThrowsAsync<AgentAiException>(() => CreateBackend(transcription: pendingService).TranscribeAsync(
            path, null, null, progress, CancellationToken.None, pendingKey));

        string directory = Path.Combine(_directory, "agent-transcriptions");
        string completedPath = Directory.EnumerateFiles(directory, "*.completed").Single();
        foreach (string savedPath in Directory.EnumerateFiles(directory))
            File.SetLastWriteTimeUtc(savedPath, DateTime.UtcNow.AddDays(-2));

        var availability = new StubAvailability { Allowed = false };
        AgentHostAiBackend backend = CreateBackend(availability: availability, transcription: transcription);
        AgentAiException? expired = await Assert.ThrowsAsync<AgentAiException>(() => backend.TranscribeAsync(
            path, null, null, progress, CancellationToken.None, completedKey));
        Assert.That(expired?.Code, Is.EqualTo(ErrorCode.AiJobNotFound));
        Assert.That(File.Exists(completedPath), Is.False);
        Assert.That(transcription.Requests, Has.Count.EqualTo(1), "an expired key cannot buy another transcript");

        AgentTranscriptionResult recovered = await backend.TranscribeAsync(
            path, null, null, progress, CancellationToken.None, pendingKey);
        Assert.Multiple(() =>
        {
            Assert.That(availability.Requests, Is.Empty);
            Assert.That(transcription.Requests, Has.Count.EqualTo(2));
            Assert.That(transcription.Requests[1].IdempotencyKey, Is.EqualTo(pendingService.Requests.Single().IdempotencyKey));
            Assert.That(recovered.Transcript.Segments, Has.Count.EqualTo(1));
        });
    }

    [TestCase("account-b")]
    [TestCase(null)]
    public async Task IdentityChangeAfterTheFirstPartStopsTranscription(string? nextAccount)
    {
        const int sampleRate = 8_000;
        string path = WriteSource("long" + Extension);
        _decoders.Register(new StubDecoder(() => new StubAudioReader(sampleRate, sampleRate * 602L)));
        using var identity = new ReactivePropertySlim<AiAuthenticatedRequestIdentity?>(new("account-a", User: null));
        using var recovery = new AiRequestRecoveryContext(
            new FileAiRequestKeyMap(_directory), () => identity.Value, identity);
        var availability = new StubAvailability();
        var transcription = new StubTranscription
        {
            OnRequest = () =>
            {
                Assert.That(AiAuthenticatedRequestScope.Identity?.AccountId, Is.EqualTo("account-a"));
                identity.Value = nextAccount is null ? null : new(nextAccount, User: null);
            },
        };
        AgentAiException? error = await CatchAsync(
            CreateBackend(availability: availability, transcription: transcription, recoveryContext: recovery), path);

        Assert.Multiple(() =>
        {
            Assert.That(error?.Code, Is.EqualTo(ErrorCode.AiUnavailable));
            Assert.That(availability.Requests, Has.Count.EqualTo(1));
            Assert.That(transcription.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AShortReadMidFileDoesNotEndTheTranscript()
    {
        const int sampleRate = 8_000;
        // Ten minutes and two seconds, with the decoder returning half a block at 100 seconds.
        string path = WriteSource("short" + Extension);
        var decoder = new StubDecoder(() => new StubAudioReader(sampleRate, sampleRate * 602L) { ShortReadAt = sampleRate * 100L });
        var transcription = new StubTranscription();
        _decoders.Register(decoder);
        AgentTranscript transcript = (await CreateBackend(transcription: transcription)
            .TranscribeAsync(path, null, null, new Progress<string>(), CancellationToken.None)).Transcript;

        Assert.Multiple(() =>
        {
            Assert.That(transcription.Requests, Has.Count.EqualTo(2), "the part cut short at 100.5 s, then the rest");
            Assert.That(transcript.Segments.Select(segment => segment.Start), Is.EqualTo(new[] { 1d, 101.5d }));
        });
    }

    [Test]
    public async Task AReadThatFailsLaterIsAnErrorButTheEndOfTheAudioIsNot()
    {
        const int sampleRate = 8_000;
        string path = WriteSource("late" + Extension);
        StubAudioReader? next = null;
        var decoder = new StubDecoder(() => next!);
        var transcription = new StubTranscription();
        _decoders.Register(decoder);
        // Reports 10:02 but its audio stops at 10:00: the first part is the whole transcript.
        next = new StubAudioReader(sampleRate, sampleRate * 602L) { DataEnd = sampleRate * 600L };
        AgentTranscript shorter = (await CreateBackend(transcription: transcription)
            .TranscribeAsync(path, null, null, new Progress<string>(), CancellationToken.None)).Transcript;
        // Reports 10:02 but its audio stops at 10:00.2: the 0.2 s tail is the real end, so it is
        // sent with the first part rather than taken for a broken read.
        next = new StubAudioReader(sampleRate, sampleRate * 602L) { DataEnd = sampleRate * 6002L / 10 };
        var shortSegments = new StubTranscription { Segments = [new AiTranscriptionSegment { Start = 0, End = 0.1, Text = "a" }] };
        AgentTranscript withTail = (await CreateBackend(transcription: shortSegments)
            .TranscribeAsync(path, null, null, new Progress<string>(), CancellationToken.None)).Transcript;
        // Every read from one second on comes back half full, so parts keep ending early: the
        // recording is never read to the end, and a part of it is not a success.
        next = new StubAudioReader(sampleRate, sampleRate * 60L) { ShortReadsFrom = sampleRate };
        AgentAiException? pieces = await CatchAsync(
            CreateBackend(transcription: new StubTranscription { Segments = [new AiTranscriptionSegment { Start = 0, End = 1, Text = "a" }] }),
            path);
        // Reads fail from 10:00: not the end of the recording, and not a success.
        next = new StubAudioReader(sampleRate, sampleRate * 602L) { FailFrom = sampleRate * 600L };
        AgentAiException? failed = await CatchAsync(CreateBackend(transcription: transcription), path);

        Assert.Multiple(() =>
        {
            Assert.That(shorter.Segments, Has.Count.EqualTo(1));
            Assert.That(shortSegments.Requests, Has.Count.EqualTo(2), "the tail is transcribed too");
            Assert.That(withTail.Segments.Select(segment => segment.Start), Is.EqualTo(new[] { 0d, 600d }));
            Assert.That(failed?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
            Assert.That(pieces?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
        });
    }

    [Test]
    public async Task WhatCannotBeTranscribedIsRefusedWithItsReason()
    {
        string path = WriteSource("refused" + Extension);
        var transcription = new StubTranscription();
        StubAudioReader? next = null;
        var decoder = new StubDecoder(() => next!);
        _decoders.Register(decoder);
        next = new StubAudioReader(48_000, 48_000) { Audio = false };
        AgentAiException? silent = await CatchAsync(CreateBackend(transcription: transcription), path);
        // Thirteen hours at 48 kHz is more samples than an int position reaches.
        next = new StubAudioReader(48_000, 48_000L * 3_600 * 13);
        AgentAiException? tooLong = await CatchAsync(CreateBackend(transcription: transcription), path);
        next = new StubAudioReader(16_000, 16_000);
        AgentAiException? noCredits = await CatchAsync(
            CreateBackend(availability: new StubAvailability { Allowed = false }, transcription: transcription), path);
        next = new StubAudioReader(16_000, 16_000);
        AgentAiException? refused = await CatchAsync(
            CreateBackend(transcription: new StubTranscription { Error = new AiUsageLimitExceededException() }), path);
        next = new StubAudioReader(16_000, 16_000);
        // The check refuses outright when the plan lapsed after the call was accepted.
        AgentAiException? planLapsed = await CatchAsync(
            CreateBackend(availability: new StubAvailability { Error = new AiPlanRequiredException() }, transcription: transcription), path);
        // A decoder that returns one sample at a time would otherwise make every sample a paid part.
        next = new StubAudioReader(16_000, 16_000 * 60L) { Trickle = true };
        AgentAiException? trickle = await CatchAsync(CreateBackend(transcription: transcription), path);
        next = new StubAudioReader(16_000, 16_000) { Readable = false };
        AgentAiException? undecodable = await CatchAsync(CreateBackend(transcription: transcription), path);
        next = new StubAudioReader(16_000, 16_000);
        // A one-second part cannot have speech ending at five seconds.
        AgentAiException? badTimes = await CatchAsync(
            CreateBackend(transcription: new StubTranscription { Segments = [new AiTranscriptionSegment { Start = 0, End = 5, Text = "late" }] }), path);

        Assert.Multiple(() =>
        {
            Assert.That(silent?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
            Assert.That(tooLong?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
            Assert.That(noCredits?.Code, Is.EqualTo(ErrorCode.AiUnavailable));
            Assert.That(refused?.Code, Is.EqualTo(ErrorCode.AiUnavailable), "out of credits is the account's state, not a failed request");
            Assert.That(badTimes?.Code, Is.EqualTo(ErrorCode.AiGenerationFailed));
            Assert.That(undecodable?.Code, Is.EqualTo(ErrorCode.MediaUnsupported), "it opened, but its audio does not decode");
            Assert.That(planLapsed?.Code, Is.EqualTo(ErrorCode.AiUnavailable));
            Assert.That(trickle?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
            Assert.That(transcription.Requests, Is.Empty, "nothing was sent for a refused file");
        });
    }

    // Transcription opens the audio alone, and a file that then opens only as video is refused for its missing audio.
    [Test]
    public async Task AFileWithoutAudioIsRefusedForItsMissingAudio()
    {
        string path = WriteSource("silent-video" + Extension);
        var requested = new List<MediaMode>();
        var decoder = new StubDecoder(options =>
        {
            requested.Add(options.StreamsToLoad);
            return options.StreamsToLoad.HasFlag(MediaMode.Video)
                ? new StubAudioReader(48_000, 48_000) { Audio = false }
                : null;
        });
        _decoders.Register(decoder);
        AgentAiException? refused = await CatchAsync(CreateBackend(transcription: new StubTranscription()), path);

        Assert.Multiple(() =>
        {
            Assert.That(refused?.Message, Is.EqualTo("The file has no audio to transcribe."));
            Assert.That(requested, Is.EqualTo(new[] { MediaMode.Audio, MediaMode.Video }));
        });
    }

    private static async Task<AgentAiException?> CatchAsync(AgentHostAiBackend backend, string path)
    {
        try
        {
            await backend.TranscribeAsync(path, null, null, new Progress<string>(), CancellationToken.None);
            return null;
        }
        catch (AgentAiException ex)
        {
            return ex;
        }
    }

    private string WriteSource(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private AgentHostAiBackend CreateBackend(
        Func<AuthenticatedUser?>? user = null,
        IAiEntitlementService? entitlements = null,
        IAiOperationAvailabilityService? availability = null,
        IAiTranscriptionService? transcription = null,
        AiRequestRecoveryContext? recoveryContext = null,
        IGenerativeModelCatalog? catalog = null)
        => new(
            recoveryContext ?? (user is null
                ? CreateRecoveryContext(() => new("test-user", User: null))
                : CreateRecoveryContext(() => user() is { } current ? new(current.Profile.Id, current) : null)),
            entitlements ?? new StubEntitlements(),
            availability ?? new StubAvailability(),
            transcription ?? new StubTranscription(),
            () => catalog ?? new StubCatalog([]),
            _ => new StubExecutor(),
            new StubModelCatalog(), _decoders);

    private AiRequestRecoveryContext CreateRecoveryContext(Func<AiAuthenticatedRequestIdentity?> identity)
        => new(new FileAiRequestKeyMap(_directory), identity);

    private static AiEntitlements Entitlements(bool canUseAi)
        => new(
            canUseAi ? "pro" : null,
            canUseAi ? "active" : null,
            null,
            null,
            false,
            canUseAi,
            new AiBalance(new AiMonthlyUsage(0, 100, false), 0, false),
            new AiOperationAvailability([]));

    private sealed class StubEntitlements : IAiEntitlementService
    {
        public ReactivePropertySlim<AiEntitlements?> Current { get; } = new();

        public AiEntitlements? Refreshed { get; set; }

        public Exception? RefreshError { get; set; }

        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements => Current;

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => RefreshError is { } error ? Task.FromException<AiEntitlements?>(error) : Task.FromResult(Refreshed);
    }

    private sealed class StubAvailability : IAiOperationAvailabilityService
    {
        public bool Allowed { get; init; } = true;

        public Exception? Error { get; init; }

        public List<AiOperationAvailabilityRequest> Requests { get; } = [];

        public Task<bool> CheckAsync(AiOperationAvailabilityRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Error is { } error ? Task.FromException<bool>(error) : Task.FromResult(Allowed);
        }
    }

    private sealed class StubTranscription : IAiTranscriptionService
    {
        public Exception? Error { get; init; }

        public AiTranscriptionSegment[]? Segments { get; init; }

        public Action? OnRequest { get; set; }

        public List<AiTranscriptionRequest> Requests { get; } = [];

        public Task<AiTranscriptionResponse> TranscribeAsync(AiTranscriptionRequest request, CancellationToken cancellationToken)
        {
            if (Error is { } error)
                return Task.FromException<AiTranscriptionResponse>(error);
            Requests.Add(request);
            OnRequest?.Invoke();
            string text = $"part {Requests.Count}";
            return Task.FromResult(new AiTranscriptionResponse(
                null,
                Segments ?? [new AiTranscriptionSegment { Start = 1, End = 2, Text = text }],
                "ja",
                // Only the first word is usable: the second has no time, the third goes back before
                // it and the fourth has no text.
                [
                    new AiTranscriptionWord { Start = 1, End = 2, Word = text },
                    new AiTranscriptionWord { Start = -1, End = 0, Word = "?" },
                    new AiTranscriptionWord { Start = 0.5, End = 0.8, Word = "earlier" },
                    new AiTranscriptionWord { Start = 2, End = 2.5, Word = " " },
                ]));
        }
    }

    private sealed class StubCatalog(IReadOnlyList<GenerativeModelInfo> models) : IGenerativeModelCatalog
    {
        public List<string> Operations { get; } = [];

        public Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(string operationId, CancellationToken cancellationToken)
        {
            Operations.Add(operationId);
            return Task.FromResult(models);
        }
    }

    private sealed class StubModelCatalog : IAiModelCatalogService
    {
        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(AiModelCatalog.Empty);

        public void Invalidate()
        {
        }
    }

    private sealed class StubExecutor : IGenerativeNodeExecutor
    {
        public Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Values { get; } = [];

        public void Report(string value) => Values.Add(value);
    }

    private sealed class StubDecoder(Func<MediaOptions, MediaReader?> open) : IDecoderInfo
    {
        public StubDecoder(Func<MediaReader> open)
            : this(_ => open())
        {
        }

        public string Name => "Agent transcription test decoder";

        public IEnumerable<string> VideoExtensions() => [];

        public IEnumerable<string> AudioExtensions() => [Extension];

        public MediaReader? Open(string file, MediaOptions options) => open(options);
    }

    private sealed class StubAudioReader(int sampleRate, long totalSamples) : MediaReader
    {
        public bool Audio { get; init; } = true;

        public bool Readable { get; init; } = true;

        public long? ShortReadAt { get; init; }

        public bool Trickle { get; init; }

        public long? DataEnd { get; init; }

        public long? FailFrom { get; init; }

        public long? ShortReadsFrom { get; init; }

        public override VideoStreamInfo VideoInfo
            => throw new InvalidOperationException("The test reader has no video stream.");

        public override AudioStreamInfo AudioInfo { get; } = new("test", new Rational(totalSamples, sampleRate), sampleRate, 1);

        public override bool HasVideo => false;

        public override bool HasAudio => Audio;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (!Readable || start >= FailFrom)
            {
                sound = null;
                return false;
            }

            int decoded = (int)Math.Clamp((DataEnd ?? totalSamples) - start, 0, length);
            if (start == ShortReadAt)
                decoded /= 2;
            if (Trickle)
                decoded = Math.Min(decoded, 1);
            if (start >= ShortReadsFrom && decoded > 0)
                decoded = Math.Max(1, decoded / 2);
            sound = new AudioBuffer(sampleRate, 2, decoded);
            sound.GetChannelData(0).Fill(0.25f);
            sound.GetChannelData(1).Fill(-0.25f);
            return true;
        }
    }
}
