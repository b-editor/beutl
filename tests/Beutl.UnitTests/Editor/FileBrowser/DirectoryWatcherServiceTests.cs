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

    [Test]
    public void Path_scope_fails_closed_when_canonical_inspection_is_invalid()
    {
        bool result = true;

        Assert.DoesNotThrow(() =>
            result = PathScope.IsUnderDirectory("\0", _projectRoot));
        Assert.That(result, Is.False);
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
