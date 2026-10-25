using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.FileBrowser;

[TestFixture]
public class DirectoryWatcherServiceTests
{
    private string _projectRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"directory-watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_projectRoot, recursive: true);
    }

    [TestCase(".git", true)]
    [TestCase(".git/index", true)]
    [TestCase("assets/.git/objects/ab/cdef", true)]
    [TestCase(".gitkeep", false)]
    [TestCase("assets/.gitkeep", false)]
    [TestCase(".github/workflows/ci.yml", false)]
    [TestCase("assets/.git-cache/file.bin", false)]
    [TestCase(".beutl", true)]
    [TestCase(".beutl/view-state.json", true)]
    [TestCase("assets/.beutl/autosave.json", true)]
    [TestCase(".beutl-cache/file.bin", false)]
    [TestCase("assets/project.beutl-cache/file.bin", false)]
    public void Path_filter_excludes_only_exact_reserved_metadata_segments(
        string relativePath,
        bool expected)
    {
        string path = Path.Combine(
            _projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        using var service = new DirectoryWatcherService();

        Assert.That(service.ShouldExcludePath(path), Is.EqualTo(expected));
    }

    [TestCase("project.bep.0123456789abcdef0123456789abcdef.tmp", true)]
    [TestCase("main.scene.0123456789abcdef0123456789abcdef.tmp", true)]
    [TestCase("clip.belm.0123456789abcdef0123456789abcdef.tmp", true)]
    [TestCase("clip.BELM.0123456789ABCDEF0123456789ABCDEF.tmp", true)]
    [TestCase("clip.belm.\u00a00123456789abcdef0123456789abcdef.tmp", false)]
    [TestCase("clip.belm.0123456789abcdef0123456789abcdef .tmp", false)]
    [TestCase("clip.belm.\u00a00123456789abcdef0123456789abcdef.tmp", false)]
    [TestCase("clip.belm.0123456789abcdef0123456789abcde.tmp", false)]
    [TestCase("clip.belm.0123456789abcdef0123456789abcdef0.tmp", false)]
    [TestCase("clip.belm.0123456789abcdef0123456789abcdeg.tmp", false)]
    [TestCase("clip.belm.0123456789abcdef0123456789abcde .tmp", false)]
    [TestCase("clip.belm.01234567-89ab-cdef-0123-456789abcdef.tmp", false)]
    [TestCase("clip.belm.backup.tmp", false)]
    [TestCase("clip.belm.tmp", false)]
    [TestCase("clip.png.0123456789abcdef0123456789abcdef.tmp", false)]
    [TestCase("notes.tmp", false)]
    public void Atomic_editor_save_sidecars_do_not_refresh_the_browser(string name, bool excluded)
    {
        using var service = new DirectoryWatcherService();

        Assert.That(service.ShouldExcludePath(Path.Combine(_projectRoot, name)), Is.EqualTo(excluded));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Special_directories_ignore_editor_save_sidecars_but_keep_the_completed_file(bool template)
    {
        string directory = template
            ? BeutlEnvironment.GetTemplatesDirectoryPath()
            : BeutlEnvironment.GetMaterialsDirectoryPath();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "item.belm");
        using var service = new DirectoryWatcherService();

        Assert.Multiple(() =>
        {
            Assert.That(service.ShouldExcludePath($"{path}.{Guid.NewGuid():N}.tmp"), Is.True);
            Assert.That(service.ShouldExcludePath(path), Is.False);
        });
    }

    [TestCase(".GIT", ".git")]
    [TestCase("assets/.GIT/objects/ab/cdef", ".git")]
    [TestCase(".BEUTL", ".beutl")]
    [TestCase("assets/.BEUTL/view-state.json", ".beutl")]
    public void Alternate_case_reserved_segments_follow_the_actual_volume_case_semantics(
        string relativePath,
        string reservedName)
    {
        string parent = relativePath.StartsWith("assets/", StringComparison.Ordinal)
            ? Path.Combine(_projectRoot, "assets")
            : _projectRoot;
        Directory.CreateDirectory(Path.Combine(parent, reservedName));
        string path = Path.Combine(
            _projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        using var service = new DirectoryWatcherService();
        Assert.That(
            FilePathComparison.TryAreSameChildPath(
                parent,
                relativePath.Contains(".GIT", StringComparison.Ordinal) ? ".GIT" : ".BEUTL",
                reservedName,
                out bool areSame),
            Is.True);

        Assert.That(
            service.ShouldExcludePath(path),
            Is.EqualTo(areSame));
    }

    [Test]
    public void Concurrent_path_notifications_do_not_race_debounce_disposal()
    {
        string path = Path.Combine(_projectRoot, "assets", "clip.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
        var service = new DirectoryWatcherService();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Parallel.For(0, 512, _ =>
        {
            try
            {
                service.NotifyPathChanged(path);
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });
        service.Dispose();
        Parallel.For(0, 32, _ =>
        {
            try
            {
                service.NotifyPathChanged(path);
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        Assert.That(failures, Is.Empty);
    }

    [Test]
    public async Task Existing_directory_changed_is_ignored_while_child_editor_writes_still_update_entries()
    {
        string child = Path.Combine(_projectRoot, "child");
        Directory.CreateDirectory(child);
        string document = Path.Combine(child, "main.scene");
        File.WriteAllText(document, "content");
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        FileSystemWatcher? registeredWatcher = null;
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue,
            watcher => registeredWatcher = watcher);
        await service.WatchAsync(_projectRoot);
        FileSystemWatcher watcher = registeredWatcher ?? throw new AssertionException("A watcher must be registered.");
        int reloads = 0;
        IReadOnlyCollection<string>? entries = null;
        service.Changed += () => reloads++;
        service.EntriesChanged += paths => entries = paths;

        // Feed the Windows parent last-write event into the actual registered handler.
        service.OnFileSystemEvent(watcher, new FileSystemEventArgs(WatcherChangeTypes.Changed, watcher.Path, "child"));
        Assert.That(posted, Is.Empty, "a parent directory change must not queue a tree refresh");
        service.OnFileSystemEvent(watcher, new FileSystemEventArgs(WatcherChangeTypes.Changed, watcher.Path,
            Path.Combine("child", "main.scene")));
        TakePostedAction(posted)();

        Assert.Multiple(() =>
        {
            Assert.That(reloads, Is.Zero);
            Assert.That(entries, Is.EquivalentTo(new[] { document }));
            Assert.That(posted, Is.Empty);
        });
    }

    [TestCase(WatcherChangeTypes.Created)]
    [TestCase(WatcherChangeTypes.Deleted)]
    [TestCase(WatcherChangeTypes.Renamed)]
    public async Task Directory_lifecycle_events_continue_to_refresh_the_tree(WatcherChangeTypes changeType)
    {
        const string name = "child";
        if (changeType != WatcherChangeTypes.Deleted)
            Directory.CreateDirectory(Path.Combine(_projectRoot, name));
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        FileSystemWatcher? registeredWatcher = null;
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue,
            watcher => registeredWatcher = watcher);
        await service.WatchAsync(_projectRoot);
        FileSystemWatcher watcher = registeredWatcher ?? throw new AssertionException("A watcher must be registered.");
        int reloads = 0;
        int entryChecks = 0;
        service.Changed += () => reloads++;
        service.EntriesChanged += _ => entryChecks++;
        FileSystemEventArgs args = changeType == WatcherChangeTypes.Renamed
            ? new RenamedEventArgs(changeType, watcher.Path, name, "previous-child")
            : new FileSystemEventArgs(changeType, watcher.Path, name);

        service.OnFileSystemEvent(watcher, args);

        Assert.That(posted.Count, Is.EqualTo(changeType == WatcherChangeTypes.Renamed ? 2 : 1),
            "renaming must route both the new and old paths through the debounce");
        while (posted.TryDequeue(out Action? callback))
            callback();
        Assert.Multiple(() =>
        {
            Assert.That(reloads, Is.EqualTo(1));
            Assert.That(entryChecks, Is.Zero);
        });
    }

    [Test]
    public void Editor_entry_checks_are_coalesced_without_reloading_content()
    {
        string document = CreateFile("main.scene");
        string temporary = $"{document}.{Guid.NewGuid():N}.tmp";
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        int reloads = 0;
        IReadOnlyCollection<string>? entries = null;
        service.Changed += () => reloads++;
        service.EntriesChanged += paths => entries = paths;

        service.NotifyPathChanged(document);
        Action stale = TakePostedAction(posted);
        service.NotifyPathChanged(temporary);
        Action current = TakePostedAction(posted);
        stale();
        Assert.That(entries, Is.Null);
        current();

        Assert.That(reloads, Is.Zero);
        Assert.That(entries, Is.EquivalentTo(new[] { document, temporary }));
    }

    [Test]
    public void Entry_checks_do_not_downgrade_a_pending_content_change()
    {
        string document = CreateFile("main.scene");
        string asset = CreateFile("clip.png");
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        int reloads = 0;
        int checks = 0;
        service.Changed += () => reloads++;
        service.EntriesChanged += _ => checks++;

        service.NotifyPathChanged(asset);
        Action stale = TakePostedAction(posted);
        service.NotifyPathChanged(document);
        Action current = TakePostedAction(posted);
        stale();
        current();
        Assert.That(reloads, Is.EqualTo(1));
        Assert.That(checks, Is.Zero);

        service.NotifyPathChanged(document);
        TakePostedAction(posted)();
        Assert.That(reloads, Is.EqualTo(1));
        Assert.That(checks, Is.EqualTo(1));
    }

    [Test]
    public async Task Changing_folders_discards_pending_editor_entry_checks()
    {
        string document = CreateFile("main.scene");
        string next = Path.Combine(_projectRoot, "next");
        Directory.CreateDirectory(next);
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        IReadOnlyCollection<string>? entries = null;
        service.EntriesChanged += paths => entries = paths;
        service.NotifyPathChanged(document);
        Action stale = TakePostedAction(posted);
        await service.WatchAsync(next);
        string nextDocument = Path.Combine(next, "next.scene");
        service.NotifyPathChanged(nextDocument);
        Action current = TakePostedAction(posted);
        stale();
        current();
        Assert.That(entries, Is.EquivalentTo(new[] { nextDocument }));
    }

    [Test]
    public void Reserved_metadata_documents_do_not_schedule_entry_checks()
    {
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        string document = CreateFile(".beutl/hidden.scene");
        service.NotifyPathChanged(document);
        service.NotifyPathChanged($"{document}.{Guid.NewGuid():N}.tmp");
        Assert.That(posted, Is.Empty);
    }

    [Test]
    public void Newer_notification_invalidates_an_already_queued_delivery()
    {
        string path = CreateFile("assets/clip.png");
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        int deliveries = 0;
        service.Changed += () => deliveries++;

        service.NotifyPathChanged(path);
        Action staleDelivery = TakePostedAction(posted);
        service.NotifyPathChanged(path);
        Action currentDelivery = TakePostedAction(posted);

        staleDelivery();
        Assert.That(deliveries, Is.Zero);

        currentDelivery();
        Assert.That(deliveries, Is.EqualTo(1));
    }

    [Test]
    public async Task Changing_the_watched_path_invalidates_an_already_queued_delivery()
    {
        string path = CreateFile("assets/clip.png");
        string nextDirectory = Path.Combine(_projectRoot, "next");
        Directory.CreateDirectory(nextDirectory);
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        int deliveries = 0;
        service.Changed += () => deliveries++;

        service.NotifyPathChanged(path);
        Action staleDelivery = TakePostedAction(posted);
        await service.WatchAsync(nextDirectory);

        staleDelivery();

        Assert.That(deliveries, Is.Zero);
    }

    [Test]
    public async Task Navigation_invalidates_old_events_before_background_setup_can_finish()
    {
        string next = Directory.CreateDirectory(Path.Combine(_projectRoot, "next")).FullName;
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var started = new System.Collections.Concurrent.ConcurrentQueue<FileSystemWatcher>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue, watcher =>
        {
            started.Enqueue(watcher);
            if (started.Count == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release watcher startup.");
            }
        });
        int deliveries = 0;
        service.Changed += () => deliveries++;
        Task first = service.WatchAsync(_projectRoot);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            FileSystemWatcher old = started.First();
            var change = new FileSystemEventArgs(WatcherChangeTypes.Changed, _projectRoot, "old.txt");
            service.OnFileSystemEvent(old, change);
            Action queued = TakePostedAction(posted);

            Task navigation = service.WatchAsync(next);
            queued();
            service.OnFileSystemEvent(old, change);
            Assert.That(deliveries, Is.Zero);
            Assert.That(posted, Is.Empty, "Events from the previous request must also be rejected.");

            release.Set();
            await Task.WhenAll(first, navigation);
            service.OnFileSystemEvent(started.Last(), new FileSystemEventArgs(WatcherChangeTypes.Changed, next, "new.txt"));
            TakePostedAction(posted)();
            Assert.That(deliveries, Is.EqualTo(1), "The destination watcher must still deliver changes.");
        }
        finally
        {
            release.Set();
            await first;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Same_path_navigation_does_not_reuse_a_failed_watcher(bool errorBeforeNavigation)
    {
        var started = new System.Collections.Concurrent.ConcurrentQueue<FileSystemWatcher>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, _ => { }, watcher =>
        {
            started.Enqueue(watcher);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release watcher startup.");
        });
        Task first = service.WatchAsync(_projectRoot, includeSubdirectories: false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            FileSystemWatcher failed = started.First();
            var error = new ErrorEventArgs(new InternalBufferOverflowException());
            if (errorBeforeNavigation) service.OnWatcherError(failed, error);
            Task navigation = service.WatchAsync(_projectRoot, includeSubdirectories: false);
            if (!errorBeforeNavigation) service.OnWatcherError(failed, error);
            release.Set();
            await Task.WhenAll(first, navigation);

            Assert.That(started, Has.Count.EqualTo(2));
            Assert.That(started.Last(), Is.Not.SameAs(failed));
            Assert.That(started.Last().IncludeSubdirectories, Is.False);
        }
        finally { release.Set(); await first; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Error_recovery_only_refreshes_its_own_navigation_request(bool navigate)
    {
        string next = Directory.CreateDirectory(Path.Combine(_projectRoot, "next")).FullName;
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var started = new System.Collections.Concurrent.ConcurrentQueue<FileSystemWatcher>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue, started.Enqueue);
        int deliveries = 0;
        service.Changed += () => deliveries++;
        await service.WatchAsync(_projectRoot);
        service.OnWatcherError(started.Last(), new ErrorEventArgs(new InternalBufferOverflowException()));
        Action recovered = TakePostedAction(posted);
        Assert.That(started, Has.Count.EqualTo(2));
        if (navigate) await service.WatchAsync(next);

        recovered();

        Assert.That(deliveries, Is.EqualTo(navigate ? 0 : 1));
        Assert.That(service.IsWatching, Is.True);
    }

    [Test]
    public void Dispose_invalidates_an_already_queued_delivery()
    {
        string path = CreateFile("assets/clip.png");
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        int deliveries = 0;
        service.Changed += () => deliveries++;

        service.NotifyPathChanged(path);
        Action staleDelivery = TakePostedAction(posted);
        service.Dispose();

        staleDelivery();

        Assert.That(deliveries, Is.Zero);
    }

    [Test]
    public async Task Watch_accepts_empty_and_null_paths_when_stopping()
    {
        using var service = new DirectoryWatcherService();
        await service.WatchAsync(_projectRoot);
        Assert.That(service.IsWatching, Is.True);

        await service.WatchAsync(string.Empty);
        Assert.That(service.IsWatching, Is.False);
        await service.WatchAsync(null);
        Assert.That(service.IsWatching, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Built_in_template_and_material_paths_override_the_reserved_metadata_filter(
        bool template)
    {
        string directory = template
            ? BeutlEnvironment.GetTemplatesDirectoryPath()
            : BeutlEnvironment.GetMaterialsDirectoryPath();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "watched.bep");
        using var service = new DirectoryWatcherService();

        Assert.That(service.ShouldExcludePath(path), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    [NonParallelizable]
    public void Deleted_template_and_material_roots_remain_explicit_filter_exceptions(
        bool template)
    {
        string directory = template
            ? BeutlEnvironment.GetTemplatesDirectoryPath()
            : BeutlEnvironment.GetMaterialsDirectoryPath();
        Directory.CreateDirectory(directory);
        Directory.Delete(directory, recursive: true);
        try
        {
            using var service = new DirectoryWatcherService();
            Assert.That(service.ShouldExcludePath(directory), Is.False);
        }
        finally
        {
            Directory.CreateDirectory(directory);
        }
    }

    [Test]
    public void Template_thumbnail_scope_includes_both_files_in_the_same_directory()
    {
        string directory = BeutlEnvironment.GetTemplatesDirectoryPath();
        Directory.CreateDirectory(directory);
        FileThumbnailService service = FileThumbnailService.Instance;
        service.ClearCache();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(service.IsObjectTemplateFile(Path.Combine(directory, "one.json")), Is.True);
                Assert.That(service.IsObjectTemplateFile(Path.Combine(directory, "two.json")), Is.True);
            });
        }
        finally
        {
            service.ClearCache();
        }
    }

    // A file listed while it was still being copied is probed partway. The result is kept only while
    // the file is unchanged, so the next listing after the copy shows the finished file.
    [Test]
    public async Task Media_info_is_probed_again_once_the_file_changes()
    {
        string clip = Path.Combine(_projectRoot, "clip.mp4");
        File.WriteAllBytes(clip, [0]);
        FileThumbnailService service = FileThumbnailService.Instance;
        service.ClearCache();
        try
        {
            MediaFileInfo? partlyWritten = await service.GetMediaInfoAsync(clip);
            File.WriteAllBytes(clip, new byte[4096]);
            MediaFileInfo? finished = await service.GetMediaInfoAsync(clip);

            Assert.Multiple(() =>
            {
                Assert.That(partlyWritten?.FileSize, Is.EqualTo(1));
                Assert.That(finished?.FileSize, Is.EqualTo(4096));
            });
        }
        finally
        {
            service.ClearCache();
        }
    }

    [Test]
    [NonParallelizable]
    public void Template_thumbnail_scope_resolves_a_bare_relative_file_path()
    {
        string previousDirectory = Environment.CurrentDirectory;
        FileThumbnailService service = FileThumbnailService.Instance;
        service.ClearCache();
        try
        {
            Environment.CurrentDirectory = _projectRoot;
            Assert.That(
                service.IsObjectTemplateFile("template.json", _projectRoot),
                Is.True);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            service.ClearCache();
        }
    }

    [Test]
    public void Template_root_retarget_invalidates_cached_directory_membership()
    {
        string firstTarget = Path.Combine(_projectRoot, "template-root-first");
        string secondTarget = Path.Combine(_projectRoot, "template-root-second");
        string templateAlias = Path.Combine(_projectRoot, "template-root-alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        FileThumbnailService service = FileThumbnailService.Instance;
        service.ClearCache();
        try
        {
            SymbolicLinks.CreateOrIgnore(templateAlias, firstTarget, directory: true);
            string candidate = Path.Combine(secondTarget, "template.json");
            Assert.That(
                service.IsObjectTemplateFile(candidate, templateAlias),
                Is.False);

            Directory.Delete(templateAlias);
            SymbolicLinks.CreateOrIgnore(templateAlias, secondTarget, directory: true);

            Assert.That(
                service.IsObjectTemplateFile(candidate, templateAlias),
                Is.True);
        }
        finally
        {
            service.ClearCache();
            if (Directory.Exists(templateAlias)) Directory.Delete(templateAlias);
        }
    }

    [Test]
    public void Watcher_template_root_retarget_invalidates_cached_directory_membership()
    {
        string firstTarget = Path.Combine(_projectRoot, "watcher-template-root-first");
        string secondTarget = Path.Combine(_projectRoot, "watcher-template-root-second");
        string templateAlias = Path.Combine(_projectRoot, "watcher-template-root-alias");
        string materialsRoot = Path.Combine(_projectRoot, "watcher-materials-root");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        Directory.CreateDirectory(materialsRoot);
        try
        {
            SymbolicLinks.CreateOrIgnore(templateAlias, firstTarget, directory: true);
            string candidate = Path.Combine(secondTarget, "template.bep");
            using var service = new DirectoryWatcherService();
            Assert.That(
                service.ShouldExcludePath(candidate, templateAlias, materialsRoot),
                Is.True);

            Directory.Delete(templateAlias);
            SymbolicLinks.CreateOrIgnore(templateAlias, secondTarget, directory: true);

            Assert.That(
                service.ShouldExcludePath(candidate, templateAlias, materialsRoot),
                Is.False);
        }
        finally
        {
            if (Directory.Exists(templateAlias)) Directory.Delete(templateAlias);
        }
    }

    [Test]
    public void Deleted_special_root_keeps_its_last_live_filesystem_identity()
    {
        string target = Path.Combine(_projectRoot, "configured-template-target");
        string templateAlias = Path.Combine(_projectRoot, "configured-template-alias");
        string materialsRoot = Path.Combine(_projectRoot, "configured-materials-root");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(materialsRoot);
        try
        {
            SymbolicLinks.CreateOrIgnore(templateAlias, target, directory: true);
            using var service = new DirectoryWatcherService();
            Assert.That(
                service.ShouldExcludePath(
                    Path.Combine(templateAlias, "template.bep"),
                    templateAlias,
                    materialsRoot),
                Is.False);

            Directory.Delete(templateAlias);

            Assert.That(
                service.ShouldExcludePath(target, templateAlias, materialsRoot),
                Is.False);
        }
        finally
        {
            if (Directory.Exists(templateAlias)) Directory.Delete(templateAlias);
        }
    }

    [Test]
    public void Retargeted_directory_alias_recomputes_template_and_watcher_membership()
    {
        string templatesRoot = BeutlEnvironment.GetTemplatesDirectoryPath();
        string templateTarget = Path.Combine(templatesRoot, $"retarget-{Guid.NewGuid():N}");
        string outsideTarget = Path.Combine(_projectRoot, "outside");
        string alias = Path.Combine(_projectRoot, "alias");
        Directory.CreateDirectory(templateTarget);
        Directory.CreateDirectory(outsideTarget);
        SymbolicLinks.CreateOrIgnore(alias, templateTarget, directory: true);
        FileThumbnailService thumbnails = FileThumbnailService.Instance;
        thumbnails.ClearCache();
        using var watcher = new DirectoryWatcherService();

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(watcher.ShouldExcludePath(Path.Combine(alias, "item.bep")), Is.False);
                Assert.That(thumbnails.IsObjectTemplateFile(Path.Combine(alias, "item.json")), Is.True);
            });

            Directory.Delete(alias);
            SymbolicLinks.CreateOrIgnore(alias, outsideTarget, directory: true);

            Assert.Multiple(() =>
            {
                Assert.That(watcher.ShouldExcludePath(Path.Combine(alias, "item.bep")), Is.True);
                Assert.That(thumbnails.IsObjectTemplateFile(Path.Combine(alias, "item.json")), Is.False);
            });
        }
        finally
        {
            thumbnails.ClearCache();
            if (Directory.Exists(alias)) Directory.Delete(alias);
            Directory.Delete(templateTarget, recursive: true);
        }
    }

    [Test]
    public async Task Same_target_navigation_refreshes_the_alias_used_for_error_rearm()
    {
        string firstTarget = Path.Combine(_projectRoot, "first-target");
        string secondTarget = Path.Combine(_projectRoot, "second-target");
        string firstAlias = Path.Combine(_projectRoot, "first-alias");
        string secondAlias = Path.Combine(_projectRoot, "second-alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        SymbolicLinks.CreateOrIgnore(firstAlias, firstTarget, directory: true);
        SymbolicLinks.CreateOrIgnore(secondAlias, firstTarget, directory: true);
        var startedPaths = new List<string>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            static action => action(),
            watcher => startedPaths.Add(watcher.Path));

        await service.WatchAsync(firstAlias);
        await service.WatchAsync(secondAlias);
        Directory.Delete(secondAlias);
        SymbolicLinks.CreateOrIgnore(secondAlias, secondTarget, directory: true);

        Assert.That(await service.TryRearmAfterErrorAsync(), Is.True);
        Assert.That(startedPaths.Last(), Is.EqualTo(FilePathComparison.ResolveCanonicalPath(secondTarget)));
    }

    [Test]
    public void Nested_directory_link_retarget_invalidates_the_cached_identity()
    {
        string templatesRoot = BeutlEnvironment.GetTemplatesDirectoryPath();
        string templateTarget = Path.Combine(templatesRoot, $"nested-{Guid.NewGuid():N}");
        string outsideTarget = Path.Combine(_projectRoot, "nested-outside");
        string intermediate = Path.Combine(_projectRoot, "intermediate");
        string alias = Path.Combine(_projectRoot, "nested-alias");
        Directory.CreateDirectory(templateTarget);
        Directory.CreateDirectory(outsideTarget);

        try
        {
            SymbolicLinks.CreateOrIgnore(intermediate, templateTarget, directory: true);
            SymbolicLinks.CreateOrIgnore(alias, intermediate, directory: true);
            using var service = new DirectoryWatcherService();
            Assert.That(service.ShouldExcludePath(Path.Combine(alias, "item.bep")), Is.False);

            Directory.Delete(intermediate);
            SymbolicLinks.CreateOrIgnore(intermediate, outsideTarget, directory: true);

            Assert.That(service.ShouldExcludePath(Path.Combine(alias, "item.bep")), Is.True);
        }
        finally
        {
            if (Directory.Exists(alias)) Directory.Delete(alias);
            if (Directory.Exists(intermediate)) Directory.Delete(intermediate);
            if (Directory.Exists(templateTarget)) Directory.Delete(templateTarget, recursive: true);
        }
    }

    [Test]
    public async Task Rearms_the_watcher_after_an_error()
    {
        using var service = new DirectoryWatcherService();
        await service.WatchAsync(_projectRoot);

        bool rearmed = await service.TryRearmAfterErrorAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rearmed, Is.True);
            Assert.That(service.IsWatching, Is.True);
        });
    }

    [Test]
    public async Task Error_rearm_preserves_nonrecursive_watching()
    {
        var scopes = new List<bool>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, _ => { },
            watcher => scopes.Add(watcher.IncludeSubdirectories));

        await service.WatchAsync(_projectRoot, includeSubdirectories: false);
        Assert.That(await service.TryRearmAfterErrorAsync(), Is.True);

        Assert.That(scopes, Is.EqualTo(new[] { false, false }));
    }

    [Test]
    public async Task Stops_rearming_once_the_retry_budget_is_spent()
    {
        using var service = new DirectoryWatcherService();
        await service.WatchAsync(_projectRoot);

        var results = new List<bool>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(await service.TryRearmAfterErrorAsync());
        }

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.EqualTo(new[] { true, true, true, false, false }));
            Assert.That(service.IsWatching, Is.False);
        });
    }

    [Test]
    public async Task Navigating_to_another_folder_restores_the_retry_budget()
    {
        using var service = new DirectoryWatcherService();
        string other = Path.Combine(_projectRoot, "other");
        Directory.CreateDirectory(other);
        await service.WatchAsync(_projectRoot);
        while (await service.TryRearmAfterErrorAsync())
        {
        }

        await service.WatchAsync(other);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.IsWatching, Is.True);
            Assert.That(await service.TryRearmAfterErrorAsync(), Is.True);
        }
    }

    [Test]
    public async Task Watching_the_same_path_twice_keeps_the_original_watcher()
    {
        int starts = 0;
        int deliveries = 0;
        FileSystemWatcher? active = null;
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            posted.Enqueue,
            watcher => { active = watcher; starts++; });
        service.Changed += () => deliveries++;
        await service.WatchAsync(_projectRoot);
        await service.WatchAsync(_projectRoot);
        service.OnFileSystemEvent(active!, new FileSystemEventArgs(WatcherChangeTypes.Changed, _projectRoot, "file.txt"));
        TakePostedAction(posted)();

        Assert.Multiple(() =>
        {
            Assert.That(service.IsWatching, Is.True);
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(deliveries, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Watching_the_same_path_does_not_restore_spent_retries()
    {
        using var service = new DirectoryWatcherService();
        await service.WatchAsync(_projectRoot);
        while (await service.TryRearmAfterErrorAsync())
        {
        }

        await service.WatchAsync(_projectRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.IsWatching, Is.True);
            Assert.That(await service.TryRearmAfterErrorAsync(), Is.False);
            Assert.That(service.IsWatching, Is.False);
        }
    }

    [Test]
    public async Task A_watcher_that_delivers_again_gets_its_retries_back()
    {
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue);
        await service.WatchAsync(_projectRoot);
        await service.TryRearmAfterErrorAsync();
        await service.TryRearmAfterErrorAsync();

        service.NotifyPathChanged(Path.Combine(_projectRoot, "clip.png"));
        TakePostedAction(posted)();

        var results = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            results.Add(await service.TryRearmAfterErrorAsync());
        }

        Assert.That(results, Is.EqualTo(new[] { true, true, true, false }));
    }

    [Test]
    public async Task A_rebuild_that_cannot_construct_a_watcher_spends_the_whole_budget()
    {
        using var service = new DirectoryWatcherService();
        string doomed = Path.Combine(_projectRoot, "doomed");
        Directory.CreateDirectory(doomed);
        await service.WatchAsync(doomed);
        Directory.Delete(doomed);

        bool rearmed = await service.TryRearmAfterErrorAsync();

        // Re-Watching does not restore the budget — the path is still the failing one.
        Directory.CreateDirectory(doomed);
        await service.WatchAsync(doomed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rearmed, Is.False);
            Assert.That(service.IsWatching, Is.True);
            Assert.That(await service.TryRearmAfterErrorAsync(), Is.False);
        }
    }

    [Test]
    public async Task Watching_after_disposal_cannot_rearm_the_watcher()
    {
        var service = new DirectoryWatcherService();
        await service.WatchAsync(_projectRoot);
        service.Dispose();
        await service.WatchAsync(_projectRoot);
        Assert.That(service.IsWatching, Is.False);
        Assert.That(await service.TryRearmAfterErrorAsync(), Is.False);
    }

    [Test]
    public async Task Events_watch_and_disposal_can_contend_without_rearming_or_throwing()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var service = new DirectoryWatcherService();
            await service.WatchAsync(_projectRoot);
            service.NotifyPathChanged(Path.Combine(_projectRoot, "before.txt"));
            using var start = new ManualResetEventSlim();
            Task callbacks = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 10; i++)
                    service.NotifyPathChanged(Path.Combine(_projectRoot, "event.txt"));
            });
            Task watch = Task.Run(async () => { start.Wait(); await service.WatchAsync(_projectRoot); });
            Task dispose = Task.Run(() => { start.Wait(); service.Dispose(); });
            start.Set();
            await Task.WhenAll(callbacks, watch, dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(service.IsWatching, Is.False);
        }
    }

    [Test]
    public async Task Watching_a_missing_path_leaves_nothing_armed()
    {
        using var service = new DirectoryWatcherService();

        await service.WatchAsync(Path.Combine(_projectRoot, "does-not-exist"));

        Assert.That(service.IsWatching, Is.False);
    }

    [Test]
    public async Task Failed_watcher_start_does_not_publish_a_disposed_instance()
    {
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            _ => throw new IOException("Injected watcher start failure."));

        await service.WatchAsync(_projectRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.IsWatching, Is.False);
            Assert.That(await service.TryRearmAfterErrorAsync(), Is.False);
        }
    }

    [Test]
    public async Task Retargeted_symbolic_link_rebuilds_the_watcher_for_the_new_identity()
    {
        string firstTarget = Path.Combine(_projectRoot, "first-target");
        string secondTarget = Path.Combine(_projectRoot, "second-target");
        string alias = Path.Combine(_projectRoot, "alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        SymbolicLinks.CreateOrIgnore(alias, firstTarget, directory: true);

        var startedPaths = new List<string>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            watcher =>
            {
                startedPaths.Add(watcher.Path);
                watcher.EnableRaisingEvents = true;
            });

        await service.WatchAsync(alias);
        Directory.Delete(alias);
        Directory.CreateSymbolicLink(alias, secondTarget);
        await service.WatchAsync(alias);

        Assert.That(
            startedPaths,
            Is.EqualTo(new[]
            {
                FilePathComparison.ResolveCanonicalPath(firstTarget),
                FilePathComparison.ResolveCanonicalPath(secondTarget),
            }));
    }

    [Test]
    public async Task Error_rearm_resolves_the_original_symbolic_link_again()
    {
        string firstTarget = Path.Combine(_projectRoot, "first-error-target");
        string secondTarget = Path.Combine(_projectRoot, "second-error-target");
        string alias = Path.Combine(_projectRoot, "error-alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        SymbolicLinks.CreateOrIgnore(alias, firstTarget, directory: true);

        var startedPaths = new List<string>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            watcher => startedPaths.Add(watcher.Path));
        await service.WatchAsync(alias);
        Directory.Delete(alias);
        Directory.CreateSymbolicLink(alias, secondTarget);

        bool rearmed = await service.TryRearmAfterErrorAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rearmed, Is.True);
            Assert.That(startedPaths, Has.Count.EqualTo(2));
            Assert.That(startedPaths[1],
                Is.EqualTo(FilePathComparison.ResolveCanonicalPath(secondTarget)));
        });
    }

    [Test]
    public async Task Concurrent_rearms_cannot_exceed_the_retry_budget()
    {
        int starts = 0;
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            _ => Interlocked.Increment(ref starts));
        await service.WatchAsync(_projectRoot);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        await Task.WhenAll(Enumerable.Range(0, 128).Select(async _ =>
        {
            try
            {
                await service.TryRearmAfterErrorAsync();
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        }));

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);
            Assert.That(Volatile.Read(ref starts), Is.LessThanOrEqualTo(4));
        });
    }

    [Test]
    public async Task Navigating_to_a_symbolic_link_cycle_does_not_escape_path_comparison()
    {
        string cycle = Path.Combine(_projectRoot, "cycle");
        SymbolicLinks.CreateOrIgnore(cycle, cycle, directory: true);

        using var service = new DirectoryWatcherService();
        await service.WatchAsync(_projectRoot);
        Assert.That(service.IsWatching, Is.True);

        await service.WatchAsync(cycle);
        Assert.That(service.IsWatching, Is.False);
    }

    private string CreateFile(string relativePath)
    {
        string path = Path.Combine(
            _projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
        return path;
    }

    private static Action TakePostedAction(
        System.Collections.Concurrent.ConcurrentQueue<Action> posted)
    {
        Assert.That(
            SpinWait.SpinUntil(() => !posted.IsEmpty, TimeSpan.FromSeconds(5)),
            Is.True,
            "A debounce delivery was not posted.");
        Assert.That(posted.TryDequeue(out Action? callback), Is.True);
        return callback!;
    }
}
