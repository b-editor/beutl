using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Text;
using System.Windows.Input;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiSubtitleAdvancedTests
{
    [Test]
    public async Task DisposeAsync_DrainsAdmittedSubtitleOperationAndRejectsLatePublication()
    {
        string directory = CreateDraftDirectory();
        try
        {
            var viewModel = CreateTeardownViewModel(directory);
            await Task.Delay(25);
            var lifetime = GetOperations(viewModel);
            AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;

            Task first = viewModel.DisposeAsync().AsTask();
            Task second = viewModel.DisposeAsync().AsTask();
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.IsCompleted, Is.False);

            operation.Dispose();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(operation.TryPublish(static () => { }), Is.False);
            await viewModel.DisposeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task TranslateCore_NonCooperativeServiceDrainsBeforeSubtitleCleanup()
    {
        string directory = CreateDraftDirectory();
        var translation = new BlockingSubtitleTranslation();
        try
        {
            var viewModel = CreateTeardownViewModel(directory, translation);
            Assert.That(
                viewModel.ImportCaptionBytes(
                    Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\nhello\n"),
                    CaptionFormats.Srt),
                Is.True);
            Task translate = (Task)typeof(AiSubtitleDialogViewModel)
                .GetMethod("TranslateCore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(viewModel, null)!;
            await translation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposal = viewModel.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False);
            translation.Release.TrySetResult();
            await translate.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DisposeAsync_CancellationCallbackFailureStillCleansUp()
    {
        string directory = CreateDraftDirectory();
        try
        {
            var viewModel = CreateTeardownViewModel(directory);
            await Task.Delay(25);
            AsyncOperationLifetime.Operation operation = GetOperations(viewModel).TryEnter()!;
            using CancellationTokenRegistration registration = operation.CancellationToken.Register(
                static () => throw new InvalidOperationException("subtitle cancellation callback failed"));

            Task disposal = viewModel.DisposeAsync().AsTask();
            operation.Dispose();
            Assert.That(
                async () => await disposal.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(viewModel.DisposeAsync().AsTask(), Is.SameAs(disposal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void SpeechWave_DownmixesAndResamplesToCompactPcm16()
    {
        const int sampleRate = 48_000;
        float[] stereo = Enumerable.Repeat(new[] { 0.5f, -0.5f }, sampleRate)
            .SelectMany(value => value)
            .ToArray();
        using var stream = new MemoryStream();

        var writer = new AiSubtitleDialogViewModel.SpeechWaveWriter(stream);
        writer.Append(new AudioFrameSnapshot(stereo, sampleRate, 2, TimeSpan.Zero), CancellationToken.None);
        writer.Complete();

        byte[] bytes = stream.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(BitConverter.ToInt32(bytes, 4), Is.EqualTo(bytes.Length - 8));
            Assert.That(BitConverter.ToInt32(bytes, 24), Is.EqualTo(16_000));
            Assert.That(BitConverter.ToInt16(bytes, 22), Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(bytes, 40), Is.EqualTo(16_000 * sizeof(short)));
            Assert.That(bytes.Length, Is.EqualTo(44 + (16_000 * sizeof(short))));
        });
    }

    [Test]
    public void SpeechWave_JoinsSlicesWithoutDroppingOrShiftingSamples()
    {
        const int sampleRate = 48_000;
        using var stream = new MemoryStream();
        var writer = new AiSubtitleDialogViewModel.SpeechWaveWriter(stream);

        float[] levels = [0.5f, -0.5f, 0.25f];
        for (int slice = 0; slice < levels.Length; slice++)
        {
            float[] mono = Enumerable.Repeat(levels[slice], sampleRate).ToArray();
            writer.Append(
                new AudioFrameSnapshot(mono, sampleRate, 1, TimeSpan.FromSeconds(slice)),
                CancellationToken.None);
        }

        writer.Complete();

        byte[] bytes = stream.ToArray();
        short[] samples = new short[(bytes.Length - 44) / sizeof(short)];
        Buffer.BlockCopy(bytes, 44, samples, 0, bytes.Length - 44);
        Assert.Multiple(() =>
        {
            // Three one-second slices are one three-second wave: a boundary that
            // dropped or repeated a sample would move every word after it.
            Assert.That(samples, Has.Length.EqualTo(3 * 16_000));
            Assert.That(BitConverter.ToInt32(bytes, 40), Is.EqualTo(3 * 16_000 * sizeof(short)));
            for (int slice = 0; slice < levels.Length; slice++)
            {
                short expected = (short)Math.Round(levels[slice] * short.MaxValue);
                Assert.That(
                    samples.Skip(slice * 16_000).Take(16_000),
                    Is.All.EqualTo(expected),
                    $"Slice {slice} lands whole in the wave.");
            }
        });
    }

    [Test]
    public void SpeechWave_PreCanceledRequestWritesNothing()
    {
        using var stream = new MemoryStream();
        var writer = new AiSubtitleDialogViewModel.SpeechWaveWriter(stream);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            writer.Append(
                new AudioFrameSnapshot(new float[32_000], 16_000, 1, TimeSpan.Zero),
                cancellationTokenSource.Token));
        Assert.That(stream.Length, Is.Zero);
    }

    [Test]
    public void SpeechWave_MediaReaderStreamsBlocksAndUsesDecodedSampleCount()
    {
        const int sampleRate = 48_000;
        const int decodedSamples = 72_000;
        using var reader = new StreamingAudioReader(sampleRate, decodedSamples);
        using var stream = new MemoryStream();

        SpeechWaveChunkResult result = AiSubtitleDialogViewModel.WriteSpeechWave(
                reader,
                startSample: 0,
                requestedSamples: 120_000,
                stream,
                CancellationToken.None);

        byte[] bytes = stream.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(result.SourceSampleCount, Is.EqualTo(decodedSamples));
            Assert.That(result.OutputSampleCount, Is.EqualTo(24_000));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(reader.MaximumRequestedSamples, Is.EqualTo(sampleRate));
            Assert.That(reader.ReadCount, Is.EqualTo(2));
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(BitConverter.ToInt32(bytes, 4), Is.EqualTo(bytes.Length - 8));
            Assert.That(BitConverter.ToInt32(bytes, 40), Is.EqualTo(24_000 * sizeof(short)));
            Assert.That(bytes.Length, Is.EqualTo(44 + 24_000 * sizeof(short)));
        });
    }

    [Test]
    public void SpeechWave_MediaReaderKeepsItsPlaceAcrossAWholeChunk()
    {
        const int sampleRate = 48_000;
        const int decodedSamples = sampleRate * 4;
        using var reader = new StreamingAudioReader(sampleRate, decodedSamples);
        using var stream = new MemoryStream();

        SpeechWaveChunkResult result = AiSubtitleDialogViewModel.WriteSpeechWave(
            reader,
            startSample: 0,
            requestedSamples: decodedSamples,
            stream,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            // The resample cursor is written samples times the source rate, which
            // leaves int after about three seconds of 48 kHz audio. Wrapped
            // negative, the cursor never reaches the end of the block and the
            // loop writes until the disk is full.
            Assert.That(result.OutputSampleCount, Is.EqualTo(4 * 16_000));
            Assert.That(stream.Length, Is.EqualTo(44 + (4 * 16_000 * sizeof(short))));
        });
    }

    [Test]
    public void SourceAudioFingerprint_DetectsSamePathReplacement()
    {
        string path = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var original = new SourceAudioFingerprint(
                new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path).Ticks);
            Assert.That(
                AiSubtitleDialogViewModel.HasMatchingSourceAudioFingerprint(path, original),
                Is.True);

            File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

            Assert.That(
                AiSubtitleDialogViewModel.HasMatchingSourceAudioFingerprint(path, original),
                Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaTest]
    public async Task CueCommands_ReflectSelectionCaretTimingAndNeighborState()
    {
        using var httpClient = new HttpClient();
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = CreateViewModel(clients);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.DeleteCue.CanExecute(), Is.False);
            Assert.That(viewModel.SplitCue.CanExecute(), Is.False);
            Assert.That(viewModel.MergeCue.CanExecute(), Is.False);
        });

        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 4, Text = "hello world" },
            new AiTranscriptionSegment { Start = 5, End = 7, Text = "again" },
        ];
        HeadlessTestHelpers.Settle();

        EditableCaptionCueViewModel first = viewModel.Cues[0];
        Assert.Multiple(() =>
        {
            Assert.That(first.CaretIndex, Is.EqualTo(first.Text.Length));
            Assert.That(viewModel.DeleteCue.CanExecute(), Is.True);
            Assert.That(viewModel.SplitCue.CanExecute(), Is.False);
            Assert.That(viewModel.MergeCue.CanExecute(), Is.True);
        });

        first.CaretIndex = 5;
        HeadlessTestHelpers.Settle();
        Assert.That(viewModel.SplitCue.CanExecute(), Is.True);
        viewModel.Cues[1].StartText = "invalid";
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SplitCue.CanExecute(), Is.False);
            Assert.That(viewModel.MergeCue.CanExecute(), Is.False);
        });
        viewModel.Cues[1].StartText = "00:00:05.000";
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SplitCue.CanExecute(), Is.True);
            Assert.That(viewModel.MergeCue.CanExecute(), Is.True);
        });

        ((ICommand)viewModel.SplitCue).Execute(null);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(3));
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("hello"));
            Assert.That(viewModel.Cues[1].Text, Is.EqualTo(" world"));
            Assert.That(viewModel.Cues[0].EndText, Is.EqualTo("00:00:01.818"));
        });

        viewModel.SelectedCue.Value = viewModel.Cues[^1];
        HeadlessTestHelpers.Settle();
        Assert.That(viewModel.MergeCue.CanExecute(), Is.False);
        viewModel.SelectedCue.Value = viewModel.Cues[0];
        HeadlessTestHelpers.Settle();
        ((ICommand)viewModel.MergeCue).Execute(null);
        HeadlessTestHelpers.Settle();
        viewModel.SelectedCue.Value!.StartText = "invalid";
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(2));
            Assert.That(viewModel.DeleteCue.CanExecute(), Is.True);
            Assert.That(viewModel.SplitCue.CanExecute(), Is.False);
            Assert.That(viewModel.MergeCue.CanExecute(), Is.False);
        });

        viewModel.SelectedCue.Value = null;
        HeadlessTestHelpers.Settle();
        Assert.That(viewModel.DeleteCue.CanExecute(), Is.False);
    }

    [Test]
    public async Task WrapCues_WrapsEditableDocument()
    {
        using var httpClient = new HttpClient();
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = CreateViewModel(clients);
        viewModel.MaximumLineLength.Value = 5;
        viewModel.MaximumLineCount.Value = 10;
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 4, Text = "hello world" },
        ];

        ((ICommand)viewModel.WrapCues).Execute(null);

        Assert.That(viewModel.Cues[0].Text, Does.Contain("\n"));
    }

    [Test]
    public async Task CaptionBytes_ImportExportAndMalformedInputAreHandled()
    {
        using var httpClient = new HttpClient();
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = CreateViewModel(clients);
        byte[] srt = Encoding.UTF8.GetBytes("""
            1
            00:00:01,000 --> 00:00:02,500
            Imported caption

            """);

        bool imported = viewModel.ImportCaptionBytes(srt, CaptionFormats.Srt);
        byte[] exported = viewModel.ExportCaptionBytes(CaptionFormats.WebVtt);
        bool mixedImported = viewModel.ImportCaptionBytes(Encoding.UTF8.GetBytes("""
            1
            00:00:01,000 --> 00:00:02,500
            Preserved caption

            2
            invalid timing
            Rejected caption

            """), CaptionFormats.Srt);
        bool malformed = viewModel.ImportCaptionBytes([0xff, 0xfe], CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(imported, Is.True);
            Assert.That(Encoding.UTF8.GetString(exported), Does.StartWith("WEBVTT"));
            Assert.That(mixedImported, Is.True);
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "Preserved caption" }));
            Assert.That(malformed, Is.False);
            Assert.That(viewModel.Error.Value, Is.Not.Null);
        });
    }

    [Test]
    public async Task JobOwnedDraft_IsRestoredByRecreatedViewModelFromBaseScope()
    {
        string directory = CreateDraftDirectory();
        try
        {
            CaptionDraftScope scope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
            var firstStore = new FileCaptionDraftStore(directory);
            Assert.That(firstStore.TryOpen(scope, out ICaptionDraftSession? writer), Is.True);
            using (writer)
            {
                writer!.Save(new CaptionDraftEntry("paid-job-1", CreateDraft("restored result")));
            }

            using var httpClient = new HttpClient();
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            var recreatedStore = new FileCaptionDraftStore(directory);
            using AiSubtitleDialogViewModel viewModel = CreateViewModel(
                clients,
                recreatedStore,
                Observable.Return<CaptionDraftScope?>(scope));

            Assert.That(viewModel.HasPartialResult.Value, Is.True);
            ((ICommand)viewModel.ApplyPartialResult).Execute(null);

            Assert.That(viewModel.Cues.Select(cue => cue.Text),
                Is.EqualTo(new[] { "restored result" }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AccountSwitch_ClearsDisplayedUserStateBeforeRestoringNewUsersDraft()
    {
        string directory = CreateDraftDirectory();
        try
        {
            Guid projectId = Guid.NewGuid();
            Guid sceneId = Guid.NewGuid();
            CaptionDraftScope userA = new("user-a", projectId, sceneId);
            CaptionDraftScope userB = new("user-b", projectId, sceneId);
            var store = new FileCaptionDraftStore(directory);
            SaveDraft(store, userA, "job-a", "A paid caption");
            SaveDraft(store, userB, "job-b", "B paid caption");

            using var httpClient = new HttpClient();
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            using var scopes = new ReactivePropertySlim<CaptionDraftScope?>(userA);
            using AiSubtitleDialogViewModel viewModel = CreateViewModel(clients, store, scopes);
            viewModel.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 1, Text = "A displayed caption" },
            ];
            viewModel.SelectedSourceLanguage.Value = viewModel.SourceLanguages.Last();

            scopes.Value = userB;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.Cues, Is.Empty,
                    "The previous user's cues must be cleared synchronously with the scope change.");
                Assert.That(viewModel.SelectedCue.Value, Is.Null);
                Assert.That(viewModel.SelectedSourceLanguage.Value, Is.SameAs(viewModel.SourceLanguages[0]));
                Assert.That(viewModel.HasPartialResult.Value, Is.True);
            }

            ((ICommand)viewModel.ApplyPartialResult).Execute(null);
            Assert.That(viewModel.Cues.Select(cue => cue.Text),
                Is.EqualTo(new[] { "B paid caption" }));

            viewModel.Dispose();
            Assert.That(store.TryOpen(userA, out ICaptionDraftSession? userASession), Is.True);
            using (userASession)
            {
                CaptionDraftEntry? untouched = userASession!.Read().Entry;
                Assert.Multiple(() =>
                {
                    Assert.That(untouched?.JobId, Is.EqualTo("job-a"));
                    Assert.That(untouched?.Draft.Cues.Single().Text, Is.EqualTo("A paid caption"));
                });
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AiSubtitleDialogViewModel CreateViewModel(
        BeutlApiApplication clients,
        ICaptionDraftStore? draftStore = null,
        IObservable<CaptionDraftScope?>? scopes = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            CaptionCatalog.CreateDefault("Default"),
            draftStore ?? CaptionDraftStoreProvider.Current,
            scopes ?? Observable.Return<CaptionDraftScope?>(null));

    private static AiSubtitleDialogViewModel CreateTeardownViewModel(
        string directory,
        IAiCaptionTranslationService? translation = null)
        => new(
            new SubtitleStubEntitlements(),
            new SubtitleStubAvailability(),
            new SubtitleStubModelCatalog(),
            new SubtitleStubPlanCoordinator(),
            new SubtitleStubTranscription(),
            translation ?? new SubtitleStubTranslation(),
            CaptionCatalog.CreateDefault("Default"),
            new FileCaptionDraftStore(directory),
            Observable.Return<CaptionDraftScope?>(null));

    private static AsyncOperationLifetime GetOperations(AiSubtitleDialogViewModel viewModel)
        => (AsyncOperationLifetime)typeof(AiSubtitleDialogViewModel)
            .GetField("_operations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(viewModel)!;

    private sealed class SubtitleStubEntitlements : IAiEntitlementService
    {
        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; }
            = new ReactivePropertySlim<AiEntitlements?>();

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => Task.FromResult<AiEntitlements?>(null);
    }

    private sealed class SubtitleStubAvailability : IAiOperationAvailabilityService
    {
        public Task<bool> CheckAsync(
            AiOperationAvailabilityRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class SubtitleStubModelCatalog : IAiModelCatalogService
    {
        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(AiModelCatalog.Empty);

        public void Invalidate()
        {
        }
    }

    private sealed class SubtitleStubPlanCoordinator : IAiPlanCoordinator
    {
        public void OpenAccountSettings()
        {
        }

        public void OpenAiPlan()
        {
        }

        public Task RefreshIfPendingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class SubtitleStubTranscription : IAiTranscriptionService
    {
        public Task<AiTranscriptionResponse> TranscribeAsync(
            AiTranscriptionRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<AiTranscriptionResponse>(
                new InvalidOperationException("not used"));
    }

    private sealed class SubtitleStubTranslation : IAiCaptionTranslationService
    {
        public Task<AiCaptionTranslationResponse> TranslateAsync(
            AiCaptionTranslationRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<AiCaptionTranslationResponse>(
                new InvalidOperationException("not used"));

        public Task<AiCaptionTranslationResponse> TranslateAsync(
            AiCaptionTranslationRequest request,
            IProgress<AiCaptionTranslationSegment>? progress,
            CancellationToken cancellationToken)
            => Task.FromException<AiCaptionTranslationResponse>(
                new InvalidOperationException("not used"));
    }

    private sealed class BlockingSubtitleTranslation : IAiCaptionTranslationService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiCaptionTranslationResponse> TranslateAsync(
            AiCaptionTranslationRequest request,
            CancellationToken cancellationToken)
            => TranslateAsync(request, progress: null, cancellationToken);

        public async Task<AiCaptionTranslationResponse> TranslateAsync(
            AiCaptionTranslationRequest request,
            IProgress<AiCaptionTranslationSegment>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            return new AiCaptionTranslationResponse(null, []);
        }
    }

    private static void SaveDraft(
        ICaptionDraftStore store,
        CaptionDraftScope scope,
        string jobId,
        string text)
    {
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry(jobId, CreateDraft(text)));
        }
    }

    private static CaptionDraft CreateDraft(string text)
    {
        var segment = new AiTranscriptionSegment { Start = 0, End = 1, Text = text };
        return new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            [new StoredCaptionCue(0, TimeSpan.FromSeconds(1).Ticks, text, null, "en", [])],
            "en",
            [segment],
            CaptionDraftKind.Transcription,
            1,
            1,
            null,
            null);
    }

    private static string CreateDraftDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Beutl.HeadlessUITests",
            nameof(AiSubtitleAdvancedTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StreamingAudioReader(int sampleRate, int totalSamples) : MediaReader
    {
        public int MaximumRequestedSamples { get; private set; }

        public int ReadCount { get; private set; }

        public override VideoStreamInfo VideoInfo
            => throw new InvalidOperationException("The test reader has no video stream.");

        public override AudioStreamInfo AudioInfo { get; } = new(
            "test",
            new Rational(totalSamples, sampleRate),
            sampleRate,
            2);

        public override bool HasVideo => false;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(
            int start,
            int length,
            [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            MaximumRequestedSamples = Math.Max(MaximumRequestedSamples, length);
            int decoded = Math.Clamp(totalSamples - start, 0, length);
            var pcm = new Pcm<Stereo32BitFloat>(sampleRate, decoded);
            pcm.DataSpan.Fill(new Stereo32BitFloat(0.25f, -0.25f));
            sound = Ref<IPcm>.Create(pcm);
            ReadCount++;
            return true;
        }
    }
}
