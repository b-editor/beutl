using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.Services;

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
    public void Existing_directory_changed_is_ignored_while_child_editor_writes_still_update_entries()
    {
        string child = Path.Combine(_projectRoot, "child");
        Directory.CreateDirectory(child);
        string document = Path.Combine(child, "main.scene");
        File.WriteAllText(document, "content");
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        FileSystemWatcher? registeredWatcher = null;
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue,
            watcher => registeredWatcher = watcher);
        service.Watch(_projectRoot);
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
    public void Directory_lifecycle_events_continue_to_refresh_the_tree(WatcherChangeTypes changeType)
    {
        const string name = "child";
        if (changeType != WatcherChangeTypes.Deleted)
            Directory.CreateDirectory(Path.Combine(_projectRoot, name));
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        FileSystemWatcher? registeredWatcher = null;
        using var service = new DirectoryWatcherService(TimeSpan.Zero, posted.Enqueue,
            watcher => registeredWatcher = watcher);
        service.Watch(_projectRoot);
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
    public void Changing_folders_discards_pending_editor_entry_checks()
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
        service.Watch(next);
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
    public void Changing_the_watched_path_invalidates_an_already_queued_delivery()
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
        service.Watch(nextDirectory);

        staleDelivery();

        Assert.That(deliveries, Is.Zero);
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
    public void Watch_accepts_empty_and_null_paths_when_stopping()
    {
        using var service = new DirectoryWatcherService();
        service.Watch(_projectRoot);
        Assert.That(service.IsWatching, Is.True);

        Assert.DoesNotThrow(() => service.Watch(string.Empty));
        Assert.That(service.IsWatching, Is.False);
        Assert.DoesNotThrow(() => service.Watch(null));
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
    public void Template_thumbnail_scope_is_cached_per_containing_directory()
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
                Assert.That(service.TemplateDirectoryMembershipCount, Is.EqualTo(1));
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
            CreateDirectorySymlinkOrIgnore(templateAlias, firstTarget);
            string candidate = Path.Combine(secondTarget, "template.json");
            Assert.That(
                service.IsObjectTemplateFile(candidate, templateAlias),
                Is.False);

            Directory.Delete(templateAlias);
            CreateDirectorySymlinkOrIgnore(templateAlias, secondTarget);

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
            CreateDirectorySymlinkOrIgnore(templateAlias, firstTarget);
            string candidate = Path.Combine(secondTarget, "template.bep");
            using var service = new DirectoryWatcherService();
            Assert.That(
                service.ShouldExcludePath(candidate, templateAlias, materialsRoot),
                Is.True);

            Directory.Delete(templateAlias);
            CreateDirectorySymlinkOrIgnore(templateAlias, secondTarget);

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
            CreateDirectorySymlinkOrIgnore(templateAlias, target);
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
        CreateDirectorySymlinkOrIgnore(alias, templateTarget);
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
            CreateDirectorySymlinkOrIgnore(alias, outsideTarget);

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
    public void Same_target_navigation_refreshes_the_alias_used_for_error_rearm()
    {
        string firstTarget = Path.Combine(_projectRoot, "first-target");
        string secondTarget = Path.Combine(_projectRoot, "second-target");
        string firstAlias = Path.Combine(_projectRoot, "first-alias");
        string secondAlias = Path.Combine(_projectRoot, "second-alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        CreateDirectorySymlinkOrIgnore(firstAlias, firstTarget);
        CreateDirectorySymlinkOrIgnore(secondAlias, firstTarget);
        var startedPaths = new List<string>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            static action => action(),
            watcher => startedPaths.Add(watcher.Path));

        service.Watch(firstAlias);
        service.Watch(secondAlias);
        Directory.Delete(secondAlias);
        CreateDirectorySymlinkOrIgnore(secondAlias, secondTarget);

        Assert.That(service.TryRearmAfterError(), Is.True);
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
            CreateDirectorySymlinkOrIgnore(intermediate, templateTarget);
            CreateDirectorySymlinkOrIgnore(alias, intermediate);
            using var service = new DirectoryWatcherService();
            Assert.That(service.ShouldExcludePath(Path.Combine(alias, "item.bep")), Is.False);

            Directory.Delete(intermediate);
            CreateDirectorySymlinkOrIgnore(intermediate, outsideTarget);

            Assert.That(service.ShouldExcludePath(Path.Combine(alias, "item.bep")), Is.True);
        }
        finally
        {
            if (Directory.Exists(alias)) Directory.Delete(alias);
            if (Directory.Exists(intermediate)) Directory.Delete(intermediate);
            if (Directory.Exists(templateTarget)) Directory.Delete(templateTarget, recursive: true);
        }
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

    private static void CreateDirectorySymlinkOrIgnore(string alias, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(alias, target);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            Assert.Ignore($"Directory symbolic links are unavailable: {ex.Message}");
        }
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
