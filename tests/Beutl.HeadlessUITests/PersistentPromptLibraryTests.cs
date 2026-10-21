using System.Text.Json;

using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Time.Testing;

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
            Assert.That(library.History, Is.EqualTo(new[] { entry }));
            Assert.That(File.ReadAllText(_storagePath), Does.Not.Contain(SensitivePrompt));
        });

        var reloaded = new PersistentPromptLibrary(_storagePath);
        Assert.That(reloaded.History, Is.Empty);
    }

    [Test]
    public void RecentHistory_IsBoundedAndCoalescesNormalizedDuplicates()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero));
        var options = new PromptLibraryOptions { MaxRecentItems = 2 };
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
    }

    [Test]
    public void SamePrompt_ForDifferentTaskKinds_RemainsDistinct()
    {
        var library = new PersistentPromptLibrary(_storagePath);

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
    public void SeparateInstancesReloadBeforeWritingSoMutationsAreMerged()
    {
        var first = new PersistentPromptLibrary(_storagePath);
        var second = new PersistentPromptLibrary(_storagePath);

        first.SetHistoryPinned(first.Record(PromptTaskKind.Image, "first process").Id, true);
        second.SetHistoryPinned(second.Record(PromptTaskKind.Video, "second process").Id, true);

        var reloaded = new PersistentPromptLibrary(_storagePath);
        Assert.That(
            reloaded.History.Select(item => item.Prompt),
            Is.EquivalentTo(new[] { "first process", "second process" }));
    }

    [Test]
    public async Task SeparateInstanceWaitsForTheCrossProcessWriteLock()
    {
        var library = new PersistentPromptLibrary(_storagePath);
        using var heldLock = new FileStream(
            _storagePath + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        Task<PromptHistoryEntry> write = Task.Run(() =>
            library.Record(PromptTaskKind.Image, "wait for the other process"));
        await Task.Delay(50);
        Assert.That(write.IsCompleted, Is.False);

        heldLock.Dispose();
        PromptHistoryEntry entry = await write.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(entry.Prompt, Is.EqualTo("wait for the other process"));
    }

    [Test]
    public void DeleteAndClearOperationsPersistTheResult()
    {
        var library = new PersistentPromptLibrary(_storagePath);
        PromptHistoryEntry firstHistory = library.Record(PromptTaskKind.Image, "first history");
        library.SetHistoryPinned(firstHistory.Id, true);
        library.SetHistoryPinned(library.Record(PromptTaskKind.Video, "second history").Id, true);
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
        var reloaded = new PersistentPromptLibrary(_storagePath);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.History, Is.Empty);
            Assert.That(reloaded.Templates, Is.Empty);
        });
    }

    // Version 1 was never written by a release, so it is read like any other unusable document.
    [TestCase("{ definitely not json")]
    [TestCase("""{ "version": 1, "history": [], "templates": [] }""")]
    public void UnusableDocument_IsQuarantinedAndReplacedWithCurrentVersion(string contents)
    {
        File.WriteAllText(_storagePath, contents);

        var library = new PersistentPromptLibrary(_storagePath);
        string[] recoveryFiles = Directory.GetFiles(_tempDirectory, "*.corrupt-*");

        Assert.Multiple(() =>
        {
            Assert.That(library.History, Is.Empty);
            Assert.That(library.Templates, Is.Empty);
            Assert.That(recoveryFiles, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(recoveryFiles[0]), Is.EqualTo(contents));
        });

        using JsonDocument replacement = JsonDocument.Parse(File.ReadAllText(_storagePath));
        Assert.That(
            replacement.RootElement.GetProperty("version").GetInt32(),
            Is.EqualTo(PersistentPromptLibrary.CurrentStorageVersion));
    }

    [Test]
    public async Task CorruptRecoveryReloadsTheWinnerAfterTakingTheCrossProcessWriteLock()
    {
        const string CorruptContents = "{ definitely not json";
        File.WriteAllText(_storagePath, CorruptContents);
        using var initialReadCompleted = new ManualResetEventSlim();
        using var releaseRecovery = new ManualResetEventSlim();
        Task<PersistentPromptLibrary> delayedLoad = Task.Run(() =>
            new PersistentPromptLibrary(
                _storagePath,
                beforeCorruptionWriteLock: () =>
                {
                    initialReadCompleted.Set();
                    if (!releaseRecovery.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The delayed corruption recovery was not released.");
                }));

        try
        {
            Assert.That(initialReadCompleted.Wait(TimeSpan.FromSeconds(5)), Is.True);
            var concurrent = new PersistentPromptLibrary(_storagePath);
            concurrent.SaveTemplate(
                "Concurrent",
                PromptTaskKind.Video,
                "written after the corrupt file was recovered");

            releaseRecovery.Set();
            PersistentPromptLibrary loaded = await delayedLoad.WaitAsync(TimeSpan.FromSeconds(5));
            var reloaded = new PersistentPromptLibrary(_storagePath);
            string[] recoveryFiles = Directory.GetFiles(_tempDirectory, "*.corrupt-*");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(loaded.Templates.Select(item => item.Name), Is.EqualTo(new[] { "Concurrent" }));
                Assert.That(reloaded.Templates.Select(item => item.Name), Is.EqualTo(new[] { "Concurrent" }));
                Assert.That(recoveryFiles, Has.Length.EqualTo(1));
                Assert.That(File.ReadAllText(recoveryFiles[0]), Is.EqualTo(CorruptContents));
            }
        }
        finally
        {
            releaseRecovery.Set();
            await delayedLoad.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void LoadingManyDistinctTemplatesPreservesEveryIndexedEntry()
    {
        const int TemplateCount = 512;
        DateTimeOffset timestamp = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var templates = Enumerable.Range(0, TemplateCount)
            .Select(index => new
            {
                id = Guid.NewGuid(),
                name = $"Template {index}",
                taskKind = "image",
                prompt = $"Prompt {index}",
                createdAtUtc = timestamp,
                updatedAtUtc = timestamp.AddSeconds(index),
                isPinned = false,
            })
            .ToArray();
        File.WriteAllText(_storagePath, JsonSerializer.Serialize(new
        {
            version = PersistentPromptLibrary.CurrentStorageVersion,
            history = Array.Empty<object>(),
            templates,
        }));

        var library = new PersistentPromptLibrary(_storagePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(library.Templates, Has.Count.EqualTo(TemplateCount));
            Assert.That(library.Templates[0].Name, Is.EqualTo($"Template {TemplateCount - 1}"));
            Assert.That(library.Templates[^1].Name, Is.EqualTo("Template 0"));
        }
    }

    [Test]
    public void Loading_many_pinned_history_items_preserves_each_entry()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        var history = Enumerable.Range(0, 1024).Select(index => new
        {
            id = Guid.NewGuid(),
            taskKind = "image",
            prompt = $"Pinned {index % 512}",
            lastUsedAtUtc = timestamp.AddSeconds(index),
            useCount = 1,
            isPinned = true,
        });
        File.WriteAllText(_storagePath, JsonSerializer.Serialize(new
        {
            version = PersistentPromptLibrary.CurrentStorageVersion,
            history,
            templates = Array.Empty<object>(),
        }));
        string stored = File.ReadAllText(_storagePath);
        var library = new PersistentPromptLibrary(_storagePath);
        // Entries that repeat a prompt are kept as they are, and loading writes nothing.
        Assert.That(library.History, Has.Count.EqualTo(1024));
        Assert.That(library.History.All(entry => entry.UseCount == 1 && entry.IsPinned), Is.True);
        Assert.That(File.ReadAllText(_storagePath), Is.EqualTo(stored));
    }

    [Test]
    public void Loading_normalizes_in_memory_and_the_next_change_writes_it()
    {
        // Only hand edits leave text or timestamps unnormalized; loading does not rewrite the file for them.
        string contents = $$"""
            {
              "version": {{PersistentPromptLibrary.CurrentStorageVersion}},
              "history": [],
              "templates": [
                {
                  "id": "{{Guid.NewGuid()}}",
                  "name": "  Hero shot  ",
                  "taskKind": "image",
                  "prompt": "first line\r\nsecond line",
                  "createdAtUtc": "2026-08-09T09:00:00+09:00",
                  "updatedAtUtc": "2026-08-09T09:00:00+09:00",
                  "isPinned": false
                }
              ]
            }
            """;
        File.WriteAllText(_storagePath, contents);

        var library = new PersistentPromptLibrary(_storagePath);
        PromptTemplate loaded = library.Templates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Name, Is.EqualTo("Hero shot"));
            Assert.That(loaded.Prompt, Is.EqualTo("first line\nsecond line"));
            Assert.That(loaded.UpdatedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(File.ReadAllText(_storagePath), Is.EqualTo(contents));
        });

        library.SaveTemplate("Other", PromptTaskKind.Video, "other prompt");
        string written = File.ReadAllText(_storagePath);
        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("\"name\": \"Hero shot\""));
            Assert.That(written, Does.Not.Contain("\\r"));
            Assert.That(written, Does.Not.Contain("+09:00"));
        });
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
        var library = new PersistentPromptLibrary(_storagePath);
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
        var seedLibrary = new PersistentPromptLibrary(_storagePath);
        seedLibrary.SetHistoryPinned(seedLibrary.Record(PromptTaskKind.Image, "existing history").Id, true);
        seedLibrary.SaveTemplate("Existing", PromptTaskKind.Video, "existing template");
        byte[] diskBefore = File.ReadAllBytes(_storagePath);

        string? observedTempPath = null;
        string? observedDestinationPath = null;
        bool tempExistedDuringReplacement = false;
        var library = new PersistentPromptLibrary(
            _storagePath,
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

        var reloaded = new PersistentPromptLibrary(_storagePath);
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
            Assert.Throws<ArgumentException>(() => library.SaveTemplate(
                "Oversized",
                PromptTaskKind.Video,
                new string('x', 4_001)));
        });

        Assert.That(File.Exists(_storagePath), Is.False);
    }
}
