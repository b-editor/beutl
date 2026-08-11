using System.Text.Json;

using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class PersistentPromptLibraryTests
{
    private string _tempDirectory = null!;
    private string _storagePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-prompt-library-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _storagePath = Path.Combine(_tempDirectory, "prompts.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Test]
    public void DefaultRetention_KeepsUnpinnedRecentPromptTextInMemoryOnly()
    {
        const string SensitivePrompt = "private storyboard concept";
        var library = new PersistentPromptLibrary(_storagePath);

        PromptHistoryEntry entry = library.Record(PromptTaskKind.Image, SensitivePrompt);

        Assert.Multiple(() =>
        {
            Assert.That(library.RetainRecentPromptText, Is.False);
            Assert.That(library.History, Is.EqualTo(new[] { entry }));
            Assert.That(File.ReadAllText(_storagePath), Does.Not.Contain(SensitivePrompt));
        });

        var reloaded = new PersistentPromptLibrary(_storagePath);
        Assert.That(reloaded.History, Is.Empty);
    }

    [Test]
    public void RetainedHistory_IsBoundedAndCoalescesNormalizedDuplicates()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero));
        var options = new PromptLibraryOptions
        {
            MaxRecentItems = 2,
            RetainRecentPromptText = true,
        };
        var library = new PersistentPromptLibrary(_storagePath, options, timeProvider);

        PromptHistoryEntry first = library.Record(PromptTaskKind.Image, "  first\r\nprompt  ");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        PromptHistoryEntry duplicate = library.Record(PromptTaskKind.Image, "first\nprompt");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        PromptHistoryEntry second = library.Record(PromptTaskKind.ImageEdit, "second");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        PromptHistoryEntry third = library.Record(PromptTaskKind.Video, "third");

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.Id, Is.EqualTo(first.Id));
            Assert.That(duplicate.UseCount, Is.EqualTo(2));
            Assert.That(library.History.Select(item => item.Id), Is.EqualTo(new[] { third.Id, second.Id }));
        });

        var reloaded = new PersistentPromptLibrary(_storagePath, options);
        Assert.That(reloaded.History.Select(item => item.Prompt), Is.EqualTo(new[] { "third", "second" }));
    }

    [Test]
    public void SamePrompt_ForDifferentTaskKinds_RemainsDistinct()
    {
        var library = new PersistentPromptLibrary(
            _storagePath,
            new PromptLibraryOptions { RetainRecentPromptText = true });

        library.Record(PromptTaskKind.Image, "shared prompt");
        library.Record(PromptTaskKind.Video, "shared prompt");

        Assert.That(library.History, Has.Count.EqualTo(2));
    }

    [Test]
    public void Pinning_IsAnExplicitPersistenceActionAndProtectsHistoryFromTrimming()
    {
        var options = new PromptLibraryOptions { MaxRecentItems = 1 };
        var library = new PersistentPromptLibrary(_storagePath, options);
        PromptHistoryEntry pinned = library.Record(PromptTaskKind.Image, "keep me");

        Assert.That(library.SetHistoryPinned(pinned.Id, true), Is.True);
        library.Record(PromptTaskKind.Video, "discard me");
        PromptHistoryEntry latest = library.Record(PromptTaskKind.ImageEdit, "session only");

        Assert.Multiple(() =>
        {
            Assert.That(library.History.Select(item => item.Id), Is.EqualTo(new[] { latest.Id, pinned.Id }));
            Assert.That(File.ReadAllText(_storagePath), Does.Contain("keep me"));
            Assert.That(File.ReadAllText(_storagePath), Does.Not.Contain("session only"));
        });

        var reloaded = new PersistentPromptLibrary(_storagePath, options);
        Assert.That(reloaded.History.Single().Id, Is.EqualTo(pinned.Id));

        Assert.That(reloaded.SetHistoryPinned(pinned.Id, false), Is.True);
        Assert.That(new PersistentPromptLibrary(_storagePath, options).History, Is.Empty);
    }

    [Test]
    public void NamedTemplates_CoalesceByTaskAndNameAndPersistPinState()
    {
        var library = new PersistentPromptLibrary(_storagePath);
        PromptTemplate original = library.SaveTemplate(" Hero shot ", PromptTaskKind.Image, "first version");
        PromptTemplate updated = library.SaveTemplate("hero shot", PromptTaskKind.Image, "second version");
        PromptTemplate video = library.SaveTemplate("Hero shot", PromptTaskKind.Video, "video version");

        Assert.That(library.SetTemplatePinned(updated.Id, true), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Id, Is.EqualTo(original.Id));
            Assert.That(updated.CreatedAtUtc, Is.EqualTo(original.CreatedAtUtc));
            Assert.That(library.Templates, Has.Count.EqualTo(2));
            Assert.That(video.Id, Is.Not.EqualTo(updated.Id));
        });

        var reloaded = new PersistentPromptLibrary(_storagePath);
        PromptTemplate reloadedImage = reloaded.Templates.Single(item => item.TaskKind == PromptTaskKind.Image);
        Assert.Multiple(() =>
        {
            Assert.That(reloadedImage.Name, Is.EqualTo("hero shot"));
            Assert.That(reloadedImage.Prompt, Is.EqualTo("second version"));
            Assert.That(reloadedImage.IsPinned, Is.True);
        });
    }

    [Test]
    public void DeleteAndClearOperationsPersistTheResult()
    {
        var options = new PromptLibraryOptions { RetainRecentPromptText = true };
        var library = new PersistentPromptLibrary(_storagePath, options);
        PromptHistoryEntry firstHistory = library.Record(PromptTaskKind.Image, "first history");
        library.Record(PromptTaskKind.Video, "second history");
        PromptTemplate firstTemplate = library.SaveTemplate("First", PromptTaskKind.Image, "first template");
        library.SaveTemplate("Second", PromptTaskKind.Video, "second template");

        Assert.Multiple(() =>
        {
            Assert.That(library.DeleteHistory(firstHistory.Id), Is.True);
            Assert.That(library.DeleteHistory(firstHistory.Id), Is.False);
            Assert.That(library.DeleteTemplate(firstTemplate.Id), Is.True);
            Assert.That(library.DeleteTemplate(firstTemplate.Id), Is.False);
        });

        library.ClearHistory();
        Assert.Multiple(() =>
        {
            Assert.That(library.History, Is.Empty);
            Assert.That(library.Templates, Has.Count.EqualTo(1));
        });

        library.ClearAll();
        var reloaded = new PersistentPromptLibrary(_storagePath, options);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.History, Is.Empty);
            Assert.That(reloaded.Templates, Is.Empty);
        });
    }

    [Test]
    public void CorruptJson_IsQuarantinedAndReplacedWithCurrentVersion()
    {
        const string CorruptContents = "{ definitely not json";
        File.WriteAllText(_storagePath, CorruptContents);

        var library = new PersistentPromptLibrary(_storagePath);

        Assert.Multiple(() =>
        {
            Assert.That(library.History, Is.Empty);
            Assert.That(library.Templates, Is.Empty);
            Assert.That(library.RecoveredCorruptFilePath, Is.Not.Null);
            Assert.That(File.Exists(library.RecoveredCorruptFilePath), Is.True);
            Assert.That(File.ReadAllText(library.RecoveredCorruptFilePath!), Is.EqualTo(CorruptContents));
        });

        using JsonDocument replacement = JsonDocument.Parse(File.ReadAllText(_storagePath));
        Assert.That(
            replacement.RootElement.GetProperty("version").GetInt32(),
            Is.EqualTo(PersistentPromptLibrary.CurrentStorageVersion));
    }

    [Test]
    public void FutureVersion_IsRejectedWithoutChangingOrQuarantiningTheFile()
    {
        string contents = $$"""
            {
              "version": {{PersistentPromptLibrary.CurrentStorageVersion + 1}},
              "history": [],
              "templates": []
            }
            """;
        File.WriteAllText(_storagePath, contents);

        Assert.Throws<NotSupportedException>(() => new PersistentPromptLibrary(_storagePath));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(_storagePath), Is.EqualTo(contents));
            Assert.That(Directory.EnumerateFiles(_tempDirectory, "*.corrupt-*"), Is.Empty);
        });
    }

    [Test]
    public void JsonSchema_ContainsNoAuthenticationOrGeneratedAssetFieldsAndLeavesNoTempFiles()
    {
        var library = new PersistentPromptLibrary(
            _storagePath,
            new PromptLibraryOptions { RetainRecentPromptText = true });
        library.Record(PromptTaskKind.Image, "safe prompt");
        library.SaveTemplate("Safe", PromptTaskKind.Video, "safe template");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_storagePath));
        string[] rootProperties = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rootProperties, Is.EquivalentTo(new[] { "version", "history", "templates" }));
            Assert.That(File.ReadAllText(_storagePath), Does.Not.Contain("auth").IgnoreCase);
            Assert.That(File.ReadAllText(_storagePath), Does.Not.Contain("generatedUrl").IgnoreCase);
            Assert.That(Directory.EnumerateFiles(_tempDirectory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void PersistedFile_OnUnix_HasUserReadWritePermissionsOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes are not available on Windows.");
            return;
        }

        UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var library = new PersistentPromptLibrary(_storagePath);
        library.SaveTemplate("First", PromptTaskKind.Image, "first prompt");

        Assert.That(File.GetUnixFileMode(_storagePath), Is.EqualTo(expectedMode));

        File.SetUnixFileMode(
            _storagePath,
            expectedMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        library.SaveTemplate("Second", PromptTaskKind.Video, "second prompt");

        Assert.That(File.GetUnixFileMode(_storagePath), Is.EqualTo(expectedMode));
    }

    [Test]
    public void ReplacementFailure_PreservesDiskAndMemoryStateAndDeletesTemporaryFile()
    {
        var options = new PromptLibraryOptions { RetainRecentPromptText = true };
        var seedLibrary = new PersistentPromptLibrary(_storagePath, options);
        seedLibrary.Record(PromptTaskKind.Image, "existing history");
        seedLibrary.SaveTemplate("Existing", PromptTaskKind.Video, "existing template");
        byte[] diskBefore = File.ReadAllBytes(_storagePath);

        string? observedTempPath = null;
        string? observedDestinationPath = null;
        bool tempExistedDuringReplacement = false;
        var library = new PersistentPromptLibrary(
            _storagePath,
            options,
            replaceFile: (tempPath, destinationPath) =>
            {
                observedTempPath = tempPath;
                observedDestinationPath = destinationPath;
                tempExistedDuringReplacement = File.Exists(tempPath);
                throw new IOException("Injected replacement failure.");
            });
        PromptHistoryEntry[] historyBefore = library.History.ToArray();
        PromptTemplate[] templatesBefore = library.Templates.ToArray();

        Assert.Throws<IOException>(() => library.Record(PromptTaskKind.ImageEdit, "must not commit"));

        var reloaded = new PersistentPromptLibrary(_storagePath, options);
        Assert.Multiple(() =>
        {
            Assert.That(observedDestinationPath, Is.EqualTo(_storagePath));
            Assert.That(tempExistedDuringReplacement, Is.True);
            Assert.That(File.ReadAllBytes(_storagePath), Is.EqualTo(diskBefore));
            Assert.That(library.History, Is.EqualTo(historyBefore));
            Assert.That(library.Templates, Is.EqualTo(templatesBefore));
            Assert.That(reloaded.History, Is.EqualTo(historyBefore));
            Assert.That(reloaded.Templates, Is.EqualTo(templatesBefore));
            Assert.That(File.Exists(observedTempPath ?? string.Empty), Is.False);
            Assert.That(Directory.EnumerateFiles(_tempDirectory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void InvalidInputs_AreRejectedBeforeWriting()
    {
        var library = new PersistentPromptLibrary(_storagePath);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => library.Record(PromptTaskKind.Image, "  "));
            Assert.Throws<ArgumentException>(() => library.SaveTemplate("  ", PromptTaskKind.Image, "prompt"));
            Assert.Throws<ArgumentException>(() =>
                library.SaveTemplate("line\nbreak", PromptTaskKind.Image, "prompt"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                library.Record((PromptTaskKind)int.MaxValue, "prompt"));
        });

        Assert.That(File.Exists(_storagePath), Is.False);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
