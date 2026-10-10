using System.Diagnostics.CodeAnalysis;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AgentHostAiBackendTests
{
    private const string Extension = ".agentaudio";
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
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
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
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
            () => null, new StubEntitlements(), new StubAvailability(), new StubTranscription(),
            () => catalog, scene =>
            {
                seen = scene;
                return executor;
            },
            new StubModelCatalog());
        var scene = new Scene();

        IReadOnlyList<GenerativeModelInfo> models = await backend.GetModelsAsync("image.generate", CancellationToken.None);
        long budget = await backend.GetImageReferenceBudgetAsync(CancellationToken.None);
        long offlineBudget = await CreateBackend(modelCatalog: new StubModelCatalog { Error = new HttpRequestException("offline") })
            .GetImageReferenceBudgetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models, Is.EqualTo(new[] { model }));
            Assert.That(budget, Is.EqualTo(AiRequestLimits.MaxImageReferencesTotalBytes), "an empty catalog publishes no budget of its own");
            Assert.That(offlineBudget, Is.EqualTo(AiRequestLimits.MaxImageReferencesTotalBytes));
            Assert.That(catalog.Operations, Is.EqualTo(new[] { "image.generate" }));
            Assert.That(backend.CreateExecutor(scene), Is.SameAs(executor));
            Assert.That(seen, Is.SameAs(scene));
        });
    }

    [Test]
    public async Task WithoutApiClientsEveryAiToolIsUnavailable()
    {
        UnavailableAgentAiBackend backend = UnavailableAgentAiBackend.Instance;
        string? reason = await backend.GetUnavailableReasonAsync(CancellationToken.None);
        IReadOnlyList<GenerativeModelInfo> models = await backend.GetModelsAsync("image.generate", CancellationToken.None);
        long budget = await backend.GetImageReferenceBudgetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(budget, Is.EqualTo(AiRequestLimits.MaxImageReferencesTotalBytes));
            Assert.That(backend.KnowsModelAvailability, Is.False);
            Assert.That(reason, Is.Not.Null);
            Assert.That(models, Is.Empty);
            Assert.That(() => backend.CreateExecutor(new Scene()), Throws.InvalidOperationException);
            Assert.That(
                () => backend.TranscribeAsync("a.wav", null, null, new Progress<string>(), CancellationToken.None),
                Throws.InvalidOperationException);
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
        DecoderRegistry.Register(decoder);
        try
        {
            AgentTranscript transcript = await CreateBackend(availability: availability, transcription: transcription)
                .TranscribeAsync(path, "ja", "whisper", progress, CancellationToken.None);

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
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    [Test]
    public async Task WhatCannotBeTranscribedIsRefusedWithItsReason()
    {
        string path = WriteSource("refused" + Extension);
        var transcription = new StubTranscription();
        StubAudioReader? next = null;
        var decoder = new StubDecoder(() => next!);
        DecoderRegistry.Register(decoder);
        try
        {
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
                Assert.That(transcription.Requests, Is.Empty, "nothing was sent for a refused file");
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
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

    private static AgentHostAiBackend CreateBackend(
        Func<AuthenticatedUser?>? user = null,
        IAiEntitlementService? entitlements = null,
        IAiOperationAvailabilityService? availability = null,
        IAiTranscriptionService? transcription = null,
        IAiModelCatalogService? modelCatalog = null)
        => new(
            user ?? (() => null),
            entitlements ?? new StubEntitlements(),
            availability ?? new StubAvailability(),
            transcription ?? new StubTranscription(),
            () => new StubCatalog([]),
            _ => new StubExecutor(),
            modelCatalog ?? new StubModelCatalog());

    private static AuthenticatedUser CreateUser(BeutlApiApplication app)
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
        return new AuthenticatedUser(profile, new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, app, DateTime.UtcNow);
    }

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

        public List<AiOperationAvailabilityRequest> Requests { get; } = [];

        public Task<bool> CheckAsync(AiOperationAvailabilityRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Allowed);
        }
    }

    private sealed class StubTranscription : IAiTranscriptionService
    {
        public Exception? Error { get; init; }

        public AiTranscriptionSegment[]? Segments { get; init; }

        public List<AiTranscriptionRequest> Requests { get; } = [];

        public Task<AiTranscriptionResponse> TranscribeAsync(AiTranscriptionRequest request, CancellationToken cancellationToken)
        {
            if (Error is { } error)
                return Task.FromException<AiTranscriptionResponse>(error);
            Requests.Add(request);
            string text = $"part {Requests.Count}";
            return Task.FromResult(new AiTranscriptionResponse(
                null,
                Segments ?? [new AiTranscriptionSegment { Start = 1, End = 2, Text = text }],
                "ja",
                // The second word has no usable time and is left out of the transcript.
                [new AiTranscriptionWord { Start = 1, End = 2, Word = text }, new AiTranscriptionWord { Start = -1, End = 0, Word = "?" }]));
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
        public Exception? Error { get; init; }

        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
            => Error is { } error ? Task.FromException<AiModelCatalog>(error) : Task.FromResult(AiModelCatalog.Empty);

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

    private sealed class StubDecoder(Func<MediaReader> open) : IDecoderInfo
    {
        public string Name => "Agent transcription test decoder";

        public IEnumerable<string> VideoExtensions() => [];

        public IEnumerable<string> AudioExtensions() => [Extension];

        public MediaReader Open(string file, MediaOptions options) => open();
    }

    private sealed class StubAudioReader(int sampleRate, long totalSamples) : MediaReader
    {
        public bool Audio { get; init; } = true;

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

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            int decoded = (int)Math.Clamp(totalSamples - start, 0, length);
            var pcm = new Pcm<Stereo32BitFloat>(sampleRate, decoded);
            pcm.DataSpan.Fill(new Stereo32BitFloat(0.25f, -0.25f));
            sound = Ref<IPcm>.Create(pcm);
            return true;
        }
    }
}
