using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CaptionDraftStoreTests
{
    [Test]
    public void SaveCreatesReadableDraftWithOwnerOnlyUnixPermissions()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-caption-draft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileCaptionDraftStore(directory);
            var scope = new CaptionDraftScope("user", Guid.NewGuid(), Guid.NewGuid());
            Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
            using (session)
            {
                var cue = new StoredCaptionCue(
                    0,
                    TimeSpan.TicksPerSecond,
                    "source",
                    null,
                    null,
                    new Dictionary<string, string>());
                session!.Save(new CaptionDraftEntry(
                    null,
                    new CaptionDraft(
                        FileCaptionDraftStore.CurrentVersion,
                        [cue],
                        "en",
                        null,
                        CaptionDraftKind.Translation,
                        1,
                        1,
                        new CaptionTranslationResume(
                            [cue],
                            "en",
                            "en",
                            "ja",
                            new Dictionary<string, string> { ["piece"] = "translated" },
                            1),
                        null),
                    []));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(store.GetStoragePath(scope)), Is.True);
                Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
                using (reopened)
                    Assert.That(reopened!.Read().Outcome, Is.EqualTo(CaptionDraftReadOutcome.Read));
            }

            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode mode = File.GetUnixFileMode(store.GetStoragePath(scope));
                Assert.That(
                    mode & (UnixFileMode.GroupRead
                        | UnixFileMode.GroupWrite
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherWrite),
                    Is.EqualTo(UnixFileMode.None));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
