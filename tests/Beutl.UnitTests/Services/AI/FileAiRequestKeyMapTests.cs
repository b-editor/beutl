using System.Text.Json;
using Beutl.Language;
using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

[TestFixture]
public sealed class FileAiRequestKeyMapTests
{
    private string _directory = null!;

    private string MapPath => Path.Combine(_directory, "ai-request-recovery.json");

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Beutl.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void PendingAttemptsAreNamedWithTheTranslatedOperation()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Attempt("video.extend").DisplayName,
                Is.EqualTo(string.Format(Strings.AiPendingRequestDefaultModel, Strings.AiVideoExtend)));
            Assert.That(Attempt("video.edit", "model-a").DisplayName,
                Is.EqualTo($"{Strings.AiVideoEditing} (model-a)"));
            Assert.That(Attempt("image.edit.upscale").DisplayName,
                Is.EqualTo(string.Format(Strings.AiPendingRequestDefaultModel, Strings.AiImageEdit)));
        }

        static AiPendingAttempt Attempt(string operation, string? model = null)
            => new("account", operation, "fingerprint", "key", model);
    }

    [Test]
    public void TwoInstancesAndARestartShareOneKeyPerRequest()
    {
        var first = new FileAiRequestKeyMap(_directory);
        var second = new FileAiRequestKeyMap(_directory);

        (AiPendingAttempt added, bool addedExisted) = first.GetOrAdd("account", "image.generate", "same", "model", () => "first-key");
        (AiPendingAttempt raced, bool racedExisted) = second.GetOrAdd("account", "image.generate", "same", "model", () => "second-key");
        second.GetOrAdd("account", "video.generate", "same", null, () => "video-key");

        var restarted = new FileAiRequestKeyMap(_directory);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((added.Key, addedExisted), Is.EqualTo(("first-key", false)));
            Assert.That((raced.Key, racedExisted), Is.EqualTo(("first-key", true)));
            Assert.That(restarted.PendingFor("account", "image.generate").Single().Model, Is.EqualTo("model"));
            Assert.That(restarted.PendingFor("account", "video.generate").Single().Key, Is.EqualTo("video-key"));
            Assert.That(restarted.PendingFor("other", "image.generate"), Is.Empty);
        }
    }

    [Test]
    public void RemoveDeletesOnlyTheRowThatStillHoldsTheKey()
    {
        var map = new FileAiRequestKeyMap(_directory);
        AiPendingAttempt first = map.GetOrAdd("account", "image.generate", "first", null, () => "key-first").Attempt;
        map.GetOrAdd("account", "image.generate", "second", null, () => "key-second");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.Remove(first with { Key = "stale-key" }), Is.False);
            Assert.That(map.Remove(first), Is.True);
            Assert.That(map.PendingFor("account", "image.generate").Select(row => row.Key), Is.EqualTo(new[] { "key-second" }));
        }
    }

    [Test]
    public void AnOperationListsTheOperationsUnderIt()
    {
        var map = new FileAiRequestKeyMap(_directory);
        map.GetOrAdd("account", "image.edit.upscale", "a", null, () => "key-a");
        map.GetOrAdd("account", "image.edit.restyle", "b", null, () => "key-b");
        map.GetOrAdd("account", "image.editor", "c", null, () => "key-c");

        Assert.That(map.PendingFor("account", "image.edit").Select(row => row.Key), Is.EquivalentTo(new[] { "key-a", "key-b" }));
    }

    [Test]
    public void RowsAnOlderVersionWroteAreReadAsKeysAndRewrittenInTheirFirstShape()
    {
        File.WriteAllText(MapPath, """
            {"version":1,"records":[
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"with-form","Key":"key-form","Model":"model-a",
               "Form":{"Prompt":"a cat","VideoQuality":"pro"},"Sources":[{"Role":"reference-0","Path":"/tmp/a.png","Name":"a.png","ContentHash":"00","Length":1,"DurableFile":"copy","ElementId":null}]},
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"key-only","Key":"key-plain","Model":null}
            ]}
            """);
        var map = new FileAiRequestKeyMap(_directory);

        IReadOnlyList<AiPendingAttempt> rows = map.PendingFor("account", "image.generate");
        map.GetOrAdd("account", "image.generate", "new", null, () => "key-new");

        using JsonDocument written = JsonDocument.Parse(File.ReadAllText(MapPath));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(row => (row.Key, row.Model)), Is.EqualTo(new[] { ("key-form", (string?)"model-a"), ("key-plain", null) }));
            Assert.That(written.RootElement.EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "version", "records" }));
            Assert.That(
                written.RootElement.GetProperty("records").EnumerateArray()
                    .Select(row => string.Join(",", row.EnumerateObject().Select(property => property.Name))),
                Is.All.EqualTo("AccountId,Operation,Fingerprint,Key,Model"));
        }
    }

    [Test]
    public void AnUnreadableRowIsSkippedAndTheOthersStay()
    {
        File.WriteAllText(MapPath, """
            {"version":1,"records":[
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"no-key"},
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"bad-key","Key":"has space","Model":null},
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"bad-model","Key":"key-x","Model":7},
              "not a row",
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"good","Key":"key-good","Model":null,"Added":"later"},
              {"AccountId":"account","Operation":"image.generate","Fingerprint":"good","Key":"key-duplicate","Model":null}
            ]}
            """);

        var map = new FileAiRequestKeyMap(_directory);

        Assert.That(map.PendingFor("account", "image.generate").Select(row => row.Key), Is.EqualTo(new[] { "key-good" }));
    }

    [TestCase("{")]
    [TestCase("[]")]
    [TestCase("{\"version\":2,\"records\":[]}")]
    [TestCase("{\"version\":1}")]
    public void AFileThisVersionCannotReadIsEmptyUntilTheNextWriteReplacesIt(string contents)
    {
        File.WriteAllText(MapPath, contents);
        var map = new FileAiRequestKeyMap(_directory);

        IReadOnlyList<AiPendingAttempt> before = map.PendingFor("account", "image.generate");
        map.GetOrAdd("account", "image.generate", "fingerprint", null, () => "key");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.Empty);
            Assert.That(new FileAiRequestKeyMap(_directory).PendingFor("account", "image.generate").Single().Key, Is.EqualTo("key"));
        }
    }

    [Test]
    public void ABusyMapFailsInsteadOfLosingAKey()
    {
        var map = new FileAiRequestKeyMap(_directory) { LockTimeout = TimeSpan.Zero };
        using var lease = new FileStream(MapPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<InvalidDataException>(() => map.GetOrAdd("account", "image.generate", "fingerprint", null, () => "key"));
    }

    [Test]
    public void AFullMapRefusesANewRowAndKeepsTheOthers()
    {
        var map = new FileAiRequestKeyMap(_directory);
        for (int index = 0; index < 256; index++)
            map.GetOrAdd("account", "image.generate", $"fingerprint-{index:000}", null, () => $"key-{index:000}");

        Assert.Throws<InvalidDataException>(() =>
            map.GetOrAdd("account", "image.generate", "overflow", null, () => "overflow-key"));
        Assert.That(new FileAiRequestKeyMap(_directory).PendingFor("account", "image.generate"), Has.Count.EqualTo(256));
    }

    [Test]
    public void WhatTheReplacedStoresKeptBesideTheKeysIsDeleted()
    {
        string sources = Path.Combine(_directory, "ai-request-recovery-sources");
        Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources, "copy"), "bytes");
        foreach (string name in new[] { "ai-request-recovery-generations.json", "ai-request-recovery-claims.json", "retry-keys.json" })
            File.WriteAllText(Path.Combine(_directory, name), "{}");
        File.WriteAllText(Path.Combine(_directory, "unrelated.json"), "{}");

        _ = new FileAiRequestKeyMap(_directory);

        Assert.That(
            Directory.EnumerateFileSystemEntries(_directory).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "unrelated.json" }));
    }

    [Test]
    public void TheMapIsPrivateOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix modes are not available on Windows.");
            return;
        }

        new FileAiRequestKeyMap(_directory).GetOrAdd("account", "image.generate", "fingerprint", null, () => "key");

#pragma warning disable CA1416 // Guarded above; these APIs are unavailable only on Windows.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.GetUnixFileMode(_directory), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(File.GetUnixFileMode(MapPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
#pragma warning restore CA1416
    }
}
