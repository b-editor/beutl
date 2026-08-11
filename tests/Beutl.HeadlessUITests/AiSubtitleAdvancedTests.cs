using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiSubtitleAdvancedTests
{
    [Test]
    public void TranslationEstimate_AccountsForProviderBatchBoundaries()
    {
        string[] captions =
        [
            new('a', 19_500),
            new('b', 1_000),
        ];

        int units = AiSubtitleDialogViewModel.CalculateTranslationUnits(captions, rate: 5);

        Assert.That(units, Is.EqualTo(105));
    }

    [Test]
    public void SpeechWave_DownmixesAndResamplesToCompactPcm16()
    {
        string path = Path.Combine(Path.GetTempPath(), $"speech-{Guid.NewGuid():N}.wav");
        try
        {
            const int sampleRate = 48_000;
            float[] stereo = Enumerable.Repeat(new[] { 0.5f, -0.5f }, sampleRate)
                .SelectMany(value => value)
                .ToArray();
            var snapshot = new AudioFrameSnapshot(stereo, sampleRate, 2, TimeSpan.Zero);

            AiSubtitleDialogViewModel.WriteSpeechWave(snapshot, path, CancellationToken.None);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Multiple(() =>
            {
                Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
                Assert.That(BitConverter.ToInt32(bytes, 24), Is.EqualTo(16_000));
                Assert.That(BitConverter.ToInt16(bytes, 22), Is.EqualTo(1));
                Assert.That(bytes.Length, Is.EqualTo(44 + 16_000 * sizeof(short)));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void SpeechWave_PreCanceledRequestDoesNotCreateTemporaryFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"speech-{Guid.NewGuid():N}.wav");
        var snapshot = new AudioFrameSnapshot(new float[32_000], 16_000, 1, TimeSpan.Zero);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AiSubtitleDialogViewModel.WriteSpeechWave(
                snapshot,
                path,
                cancellationTokenSource.Token));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void CueCommands_SplitMergeAndWrapEditableDocument()
    {
        using var httpClient = new HttpClient();
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        using var viewModel = CreateViewModel(clients);
        viewModel.MaximumLineLength.Value = 5;
        viewModel.MaximumLineCount.Value = 10;
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 4, Text = "hello world" },
        ];

        ((ICommand)viewModel.SplitCue).Execute(null);
        Assert.That(viewModel.Cues, Has.Count.EqualTo(2));

        viewModel.SelectedCue.Value = viewModel.Cues[0];
        ((ICommand)viewModel.MergeCue).Execute(null);
        ((ICommand)viewModel.WrapCues).Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(1));
            Assert.That(viewModel.Cues[0].Text, Does.Contain("\n"));
            Assert.That(viewModel.Cues[0].TryCreateCue(out CaptionCue? cue), Is.True);
            Assert.That(cue!.Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(cue.End, Is.EqualTo(TimeSpan.FromSeconds(4)));
        });
    }

    [Test]
    public void CaptionBytes_ImportExportAndMalformedInputAreHandled()
    {
        using var httpClient = new HttpClient();
        using var clients = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
    public void JobOwnedDraft_IsRestoredByRecreatedViewModelFromBaseScope()
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
            using var clients = BeutlApiApplication.Create(
                new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
    public void AccountSwitch_ClearsDisplayedUserStateBeforeRestoringNewUsersDraft()
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
            using var clients = BeutlApiApplication.Create(
                new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
                CaptionDraftEntry? untouched = userASession!.Load();
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
            new AiPlanCoordinator(clients, clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            CaptionCatalog.CreateDefault("Default"),
            draftStore ?? CaptionDraftStoreProvider.Current,
            scopes ?? Observable.Return<CaptionDraftScope?>(null));

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
            new GenerationProvenance(
                "beutl.ai",
                "audio.transcribe",
                1,
                JsonSerializer.SerializeToElement(new { parameters = new { } }),
                DateTimeOffset.UtcNow),
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
}
