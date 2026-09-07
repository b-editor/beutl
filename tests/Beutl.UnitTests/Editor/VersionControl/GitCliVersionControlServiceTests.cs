using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class GitCliVersionControlServiceTests : RealGitTestRepository
{
    // Stable union of the existing policy, Engine built-in decoders, optional decoders,
    // and the still-image formats advertised by SharedFilePickerOptions.OpenImage.
    private static readonly string[] s_expectedSupportedMediaExtensions =
    [
        ".mp4",
        ".mov",
        ".mkv",
        ".avi",
        ".wmv",
        ".flv",
        ".webm",
        ".wav",
        ".mp3",
        ".flac",
        ".aac",
        ".m4a",
        ".ogg",
        ".opus",
        ".wma",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".webp",
        ".tiff",
        ".tif",
        ".wave",
        ".apng",
        ".264",
        ".mpeg",
        ".ts",
        ".mts",
        ".m2ts",
        ".sami",
        ".smi",
        ".m4v",
        ".adts",
        ".asf",
        ".3gp",
        ".3gp2",
        ".3gpp",
        ".ico",
        ".wbmp",
        ".pkm",
        ".ktx",
        ".astc",
        ".dng",
        ".heif",
        ".avif",
    ];

    private static string CreateTestCaseInsensitiveGlob(string extension)
    {
        return string.Concat(extension.Select(static character =>
            character is >= 'a' and <= 'z'
                ? $"[{character}{char.ToUpperInvariant(character)}]"
                : character.ToString()));
    }

    [Test]
    public async Task InitializeAsync_creates_repository_files_and_initial_snapshot()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitIdentityRequiredException>(
            async () => await service.InitializeAsync(
                new InitOptions(
                    new RepositoryInfo(projectRoot, projectRoot),
                    UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Null);
            Assert.That(Directory.Exists(Path.Combine(projectRoot, ".git")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(projectRoot, ".gitattributes")), Is.False);
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.SetLocalIdentityAsync(
                    new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
                    CancellationToken.None));
        });
        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: false)
            {
                Identity = new GitIdentity(
                    "Beutl Test",
                    "beutl-test@example.invalid"),
            },
            CancellationToken.None);

        var projectRepository = new RepositoryInfo(projectRoot, projectRoot);
        GitCommandResult branch = await Runner.RunAsync(
            projectRepository,
            ["branch", "--show-current"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult log = await Runner.RunAsync(
            projectRepository,
            ["log", "-1", "--format=%s%n%b"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult count = await Runner.RunAsync(
            projectRepository,
            ["rev-list", "--count", "HEAD"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult topLevel = await Runner.RunAsync(
            projectRepository,
            ["rev-parse", "--show-toplevel"],
            GitCommandOptions.Local,
            CancellationToken.None);
        string expectedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(topLevel.Stdout.Trim()));

        Assert.Multiple(() =>
        {
            Assert.That(service.Repository!.RepoRoot, Is.EqualTo(expectedRoot));
            Assert.That(service.Repository.ProjectRoot, Is.EqualTo(expectedRoot));
            Assert.That(service.Repository.Pathspec, Is.EqualTo("."));
            Assert.That(branch.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(log.Stdout, Does.StartWith("beutl: initialize version control\n"));
            Assert.That(log.Stdout, Does.Contain("Beutl-Snapshot: init"));
            Assert.That(
                runner.Commands.Single(IsCommitCommand),
                Does.Contain("--no-gpg-sign"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.[tT][mM][pP]\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain(".gitattributes text eol=lf\n"));
        });
    }

    [Test]
    public async Task InitializeAsync_keeps_repository_and_exposes_lock_when_initial_commit_fails()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(projectRoot, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new FailingInitialCommitRunner(
            CreateRunner(),
            expectedLock,
            beforeFailure: _ =>
            {
                // Make the rollback ownership check fail on every platform. The repository-lock
                // failure is then nested inside an AggregateException instead of escaping directly.
                using FileStream index = File.Open(
                    Path.Combine(projectRoot, ".git", "index"),
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                index.WriteByte(0);
            });
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);
        var options = new InitOptions(
            new RepositoryInfo(projectRoot, projectRoot),
            UseLfsWhenAvailable: false)
        {
            Identity = new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
        };

        Exception? exception = Assert.CatchAsync<Exception>(
            async () => await service.InitializeAsync(options, CancellationToken.None));
        IReadOnlyList<Exception> reportedFailures = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception!];
        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<AggregateException>());
            Assert.That(reportedFailures, Has.Some.TypeOf<GitOperationException>());
            Assert.That(service.Repository, Is.Not.Null);
            Assert.That(service.RecoverableLock, Is.SameAs(expectedLock));
            Assert.That(runner.InitialCommitAttempts, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InitializeAsync_restores_the_prior_index_when_initial_commit_stops_after_staging(
        bool cancelCommit)
    {
        string ignorePath = Path.Combine(Root, ".gitignore");
        const string originalIgnoreContents = "custom ignore rule\n";
        await CommitFileAsync(".gitignore", originalIgnoreContents, "baseline ignore");
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        UnixFileMode? originalIgnoreMode = null;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                ignorePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            originalIgnoreMode = File.GetUnixFileMode(ignorePath);
        }

        FileAttributes originalIgnoreAttributes = File.GetAttributes(ignorePath);
        string stagedFile = Path.Combine(Root, "staged.belm");
        await File.WriteAllTextAsync(stagedFile, "staged before initialization\n");
        await RunGitAsync("add", "--", "staged.belm");
        await File.WriteAllTextAsync(stagedFile, "working tree during initialization\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        string tipBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string indexBefore = (await RunGitAsync("write-tree")).Stdout.Trim();
        using var cancellation = new CancellationTokenSource();
        var runner = new FailingInitialCommitRunner(
            CreateRunner(),
            cancellation: cancelCommit ? cancellation : null);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        if (cancelCommit)
        {
            Assert.CatchAsync<OperationCanceledException>(
                async () => await service.InitializeAsync(
                    new InitOptions(Repository, UseLfsWhenAvailable: false),
                    cancellation.Token));
        }
        else
        {
            Assert.ThrowsAsync<GitOperationException>(
                async () => await service.InitializeAsync(
                    new InitOptions(Repository, UseLfsWhenAvailable: false),
                    CancellationToken.None));
        }

        string tipAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        GitCommandResult stagedContents = await RunGitAsync("show", ":staged.belm");
        GitCommandResult stagedNames = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(runner.InitialCommitAttempts, Is.EqualTo(1));
            Assert.That(tipAfter, Is.EqualTo(tipBefore));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(stagedContents.Stdout, Is.EqualTo("staged before initialization\n"));
            Assert.That(stagedNames.Stdout, Is.EqualTo("staged.belm\n"));
            Assert.That(
                File.ReadAllText(stagedFile),
                Is.EqualTo("working tree during initialization\n"));
            Assert.That(
                File.ReadAllText(ignorePath),
                Is.EqualTo(originalIgnoreContents + "**/.beutl/\n*.[tT][mM][pP]\n"));
            Assert.That(File.GetAttributes(ignorePath), Is.EqualTo(originalIgnoreAttributes));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, ".gitattributes")),
                Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
        });
        if (!OperatingSystem.IsWindows() && originalIgnoreMode is { } expectedMode)
        {
            Assert.That(File.GetUnixFileMode(ignorePath), Is.EqualTo(expectedMode));
        }
    }

    [Test]
    public async Task InitializeAsync_preserves_an_external_hygiene_edit_when_the_initial_commit_fails()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        string indexBefore = (await RunGitAsync("write-tree")).Stdout.Trim();
        string ignorePath = Path.Combine(Root, ".gitignore");
        var runner = new FailingInitialCommitRunner(
            CreateRunner(),
            beforeFailure: _ => File.WriteAllText(ignorePath, "external edit\n"));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo("external edit\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, ".gitattributes")),
                Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
        });
    }

    [Test]
    public async Task InitializeAsync_keeps_external_hygiene_replacements_at_the_failed_commit_boundary()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string ignorePath = Path.Combine(Root, ".gitignore");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(ignorePath, "original ignore\n");
        await File.WriteAllTextAsync(attributesPath, "original attributes\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        string indexBefore = (await RunGitAsync("write-tree")).Stdout.Trim();
        const string externalIgnore = "external replacement ignore\n";
        const string externalAttributes = "external replacement attributes\n";
        var runner = new FailingInitialCommitRunner(
            CreateRunner(),
            // This is the last reachable boundary before failed initialization starts rollback.
            beforeFailure: _ =>
            {
                File.WriteAllText(ignorePath, externalIgnore);
                File.WriteAllText(attributesPath, externalAttributes);
            });
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo(externalIgnore));
            Assert.That(File.ReadAllText(attributesPath), Is.EqualTo(externalAttributes));
        });
    }

    [Test]
    public async Task InitializeAsync_accepts_a_durable_initial_commit_when_its_result_is_lost()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        var repository = new RepositoryInfo(projectRoot, projectRoot);
        var runner = new LostInitialCommitResultRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.DoesNotThrowAsync(
            async () => await service.InitializeAsync(
                new InitOptions(repository, UseLfsWhenAvailable: false)
                {
                    Identity = new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
                },
                CancellationToken.None));

        GitCommandResult count = await runner.Inner.RunAsync(
            repository,
            ["rev-list", "--count", "HEAD"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult staged = await runner.Inner.RunAsync(
            repository,
            ["diff", "--cached", "--name-only"],
            GitCommandOptions.Local,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(runner.InitialCommitAttempts, Is.EqualTo(1));
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(staged.Stdout, Is.Empty);
        });
    }

    [Test]
    public async Task InitializeAsync_requires_identity_before_existing_repository_mutation_or_association()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        await RunGitAsync("config", "--unset", "user.name");
        await RunGitAsync("config", "--unset", "user.email");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        Assert.ThrowsAsync<GitIdentityRequiredException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Null);
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(File.Exists(Path.Combine(Root, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task InitializeAsync_rejects_detached_existing_repository_before_mutation()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string detachedTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("checkout", "--detach", detachedTip);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        Assert.ThrowsAsync<DetachedHeadNotSupportedException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Null);
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(File.Exists(Path.Combine(Root, ".gitignore")), Is.False);
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
            Assert.That(File.Exists(Path.Combine(Root, "project.bep")), Is.True);
        });
    }

    [Test]
    public async Task InitializeAsync_does_not_reassociate_a_service_with_another_repository()
    {
        string otherRoot = CreateTemporaryDirectory();
        var otherRepository = new RepositoryInfo(otherRoot, otherRoot);
        await Runner.RunAsync(
            otherRepository,
            ["init"],
            GitCommandOptions.Local,
            CancellationToken.None);
        using GitCliVersionControlService service = CreateService();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(otherRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(service.Repository, Is.SameAs(Repository));
    }

    [Test]
    public void InitializeAsync_rejects_discovery_from_a_different_repository()
    {
        string projectRoot = CreateTemporaryDirectory();
        string discoveredRoot = CreateTemporaryDirectory();
        var runner = new MismatchedDiscoveryRunner(discoveredRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(
                    new RepositoryInfo(projectRoot, projectRoot),
                    UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(runner.Commands, Does.Not.Contain("init"));
    }

    [Test]
    public void DiscoverRepositoryAsync_rejects_control_characters_before_running_Git()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows paths cannot contain the control characters used by this regression.");
        }

        string projectRoot = Path.Combine(Root, "project\nwith\tcontrols");
        var runner = new MismatchedDiscoveryRunner(Root);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<ArgumentException>(
            async () => await service.DiscoverRepositoryAsync(
                projectRoot,
                CancellationToken.None));
        Assert.That(runner.Commands, Is.Empty);
    }

    [Test]
    public void DiscoverRepositoryAsync_rejects_a_prefix_for_another_project_root()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new MismatchedDiscoveryRunner(projectRoot, "another-project/");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DiscoverRepositoryAsync(
                projectRoot,
                CancellationToken.None));
    }

    [Test]
    public async Task DiscoverRepositoryAsync_preserves_literal_backslashes_in_a_Unix_prefix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Backslashes are directory separators on Windows.");
        }

        string projectRoot = Path.Combine(Root, @"project\with-backslash");
        var runner = new MismatchedDiscoveryRunner(Root, @"project\with-backslash/");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            projectRoot,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.ProjectRoot, Is.EqualTo(projectRoot));
            Assert.That(discovered.Pathspec, Is.EqualTo(@"project\with-backslash"));
        });
    }

    [Test]
    public async Task GetDiffAsync_preserves_a_literal_backslash_in_a_Unix_project_pathspec()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Backslashes are directory separators on Windows.");
        }

        string projectRoot = Path.Combine(Root, @"project\with-backslash");
        Directory.CreateDirectory(projectRoot);
        string projectFile = Path.Combine(projectRoot, "project.bep");
        await File.WriteAllTextAsync(projectFile, "project contents\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "add nested project");
        string sha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        string diff = await service.GetDiffAsync(
            sha,
            $"{repository.Pathspec}/project.bep",
            CancellationToken.None);

        Assert.That(diff, Does.Contain("+project contents"));
    }

    [Test]
    public async Task InitializeAsync_installs_local_lfs_and_writes_media_patterns_when_active()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new RecordingInitializationRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: true)
            {
                Identity = new GitIdentity(
                    "Beutl Test",
                    "beutl-test@example.invalid"),
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                runner.Commands,
                Does.Contain("lfs install --local"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain(
                    "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain(
                    "**/*.[pP][nN][gG] filter=lfs diff=lfs merge=lfs -text\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("# BEGIN BEUTL MANAGED LFS\n"));
        });
    }

    [Test]
    public async Task InitializeAsync_writes_each_supported_media_Lfs_pattern_once()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new RecordingInitializationRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: true)
            {
                Identity = new GitIdentity(
                    "Beutl Test",
                    "beutl-test@example.invalid"),
            },
            CancellationToken.None);

        const string lfsAttributes = " filter=lfs diff=lfs merge=lfs -text";
        string[] actualPatterns = File.ReadAllLines(Path.Combine(projectRoot, ".gitattributes"))
            .Where(static line => line.StartsWith("**/*", StringComparison.Ordinal)
                && line.EndsWith(lfsAttributes, StringComparison.Ordinal))
            .Select(static line => line[..^lfsAttributes.Length])
            .ToArray();
        string[] expectedPatterns = s_expectedSupportedMediaExtensions
            .Select(static extension => $"**/*{CreateTestCaseInsensitiveGlob(extension)}")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actualPatterns, Is.Unique);
            Assert.That(actualPatterns, Is.EquivalentTo(expectedPatterns));
        });
    }

    [Test]
    public async Task HasVersionTrackingOptInAsync_only_reports_true_after_hygiene_runs()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        var config = new VersionControlConfig { UseLfsWhenAvailable = false };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false, config),
            Repository,
            watcher: null,
            _ => CreateRunner());

        bool beforeHygiene = await service.HasVersionTrackingOptInAsync(
            Repository,
            CancellationToken.None);
        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        bool afterHygiene = await service.HasVersionTrackingOptInAsync(
            Repository,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(beforeHygiene, Is.False);
            Assert.That(afterHygiene, Is.True);
        });
    }

    [Test]
    public async Task HasVersionTrackingOptInAsync_survives_deleting_the_generated_hygiene_files()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        var config = new VersionControlConfig { UseLfsWhenAvailable = false };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false, config),
            Repository,
            watcher: null,
            _ => CreateRunner());

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        await service.CommitAllAsync(
            "beutl: enable version control",
            SnapshotKind.Init,
            CancellationToken.None);
        File.Delete(Path.Combine(Root, ".gitignore"));
        File.Delete(Path.Combine(Root, ".gitattributes"));

        Assert.That(
            await service.HasVersionTrackingOptInAsync(Repository, CancellationToken.None),
            Is.True);
    }

    [Test]
    public async Task HasVersionTrackingOptInAsync_reports_false_for_a_repository_beutl_never_managed()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        var config = new VersionControlConfig { UseLfsWhenAvailable = false };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false, config),
            Repository,
            watcher: null,
            _ => CreateRunner());

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        File.Delete(Path.Combine(Root, ".gitattributes"));

        Assert.That(
            await service.HasVersionTrackingOptInAsync(Repository, CancellationToken.None),
            Is.False);
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_applies_Lfs_to_mixed_case_media_extensions()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        var config = new VersionControlConfig { UseLfsWhenAvailable = true };
        var runner = new RecordingLfsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true, config),
            Repository,
            watcher: null,
            _ => runner);

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(Root, ".gitattributes")),
            Does.Contain(
                "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n"));
        string[] paths =
        [
            "resources/CLIP.MP4",
            "resources/Clip.Mp4",
            "resources/audio.WaVe",
            "resources/animation.ApNg",
            "resources/raw.DnG",
            "resources/photo.HeIf",
            "resources/photo.AvIf",
            "assets/CLIP.MP4",
            "root-clip.PnG",
        ];
        GitCommandResult attributes = await RunGitAsync(
            ["check-attr", "filter", "--", .. paths]);
        Assert.That(
            attributes.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            Is.EqualTo(paths.Select(static path => $"{path}: filter: lfs").ToArray()));
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_falls_back_to_non_Lfs_when_a_custom_hook_blocks_install()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string hookRecord = (await RunGitAsync("rev-parse", "--git-path", "hooks/pre-push"))
            .Stdout.TrimEnd('\r', '\n');
        string hookPath = Path.GetFullPath(
            Path.IsPathFullyQualified(hookRecord)
                ? hookRecord
                : Path.Combine(Root, hookRecord));
        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        const string hookContents = "#!/bin/sh\nprintf 'custom pre-push hook\\n'\n";
        await File.WriteAllTextAsync(hookPath, hookContents);
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(
            attributesPath,
            "custom text\n# BEGIN BEUTL MANAGED LFS\n"
            + "resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text\n"
            + "# END BEUTL MANAGED LFS\n");
        var notices = new List<VersionControlPolicyNotice>();
        var config = new VersionControlConfig
        {
            UseLfsWhenAvailable = true,
            LargeMediaWarningThresholdMb = 0,
        };
        var runner = new CustomHookRejectingLfsInstallRunner(
            CreateRunner(),
            hookPath,
            hookContents);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true, config),
            Repository,
            watcher: null,
            _ => runner,
            policyNoticeSink: (notice, _) =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, [0]);
        CommitResult commit = await service.CommitAllAsync(
            "non-lfs fallback",
            SnapshotKind.Manual,
            CancellationToken.None);

        string attributes = await File.ReadAllTextAsync(attributesPath);
        Assert.Multiple(() =>
        {
            Assert.That(runner.LfsInstallCalls, Is.EqualTo(1));
            Assert.That(File.Exists(hookPath), Is.True);
            Assert.That(File.ReadAllText(hookPath), Is.EqualTo(hookContents));
            Assert.That(status.HasConflicts, Is.False);
            Assert.That(attributes, Does.Contain("*.[bB][eE][pP] text eol=lf"));
            Assert.That(attributes, Does.Not.Contain("# BEGIN BEUTL MANAGED LFS"));
            Assert.That(attributes, Does.Not.Contain("filter=lfs"));
            Assert.That(commit, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                notices,
                Is.EqualTo(new[]
                {
                    new VersionControlPolicyNotice.LargeMediaWithoutLfs(
                        "resources/large.mp4",
                        1),
                }));
        });
    }

    [TestCase(".WAVE")]
    [TestCase(".APNG")]
    [TestCase(".DNG")]
    [TestCase(".HEIF")]
    [TestCase(".AVIF")]
    public async Task CommitAllAsync_warns_for_supported_large_media_without_Lfs(string extension)
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string relativePath = $"resources/large{extension}";
        string mediaPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, [0]);
        var notices = new List<VersionControlPolicyNotice>();
        var config = new VersionControlConfig { LargeMediaWarningThresholdMb = 0 };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false, config),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            policyNoticeSink: (notice, _) =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                notices,
                Is.EqualTo(new[]
                {
                    new VersionControlPolicyNotice.LargeMediaWithoutLfs(relativePath, 1),
                }));
        });
    }

    [TestCase("assets/large.mp4")]
    [TestCase("root-large.mov")]
    public async Task CommitAllAsync_warns_for_large_media_outside_the_resources_directory(
        string relativePath)
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string mediaPath = Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, [0]);
        var notices = new List<VersionControlPolicyNotice>();
        var config = new VersionControlConfig { LargeMediaWarningThresholdMb = 0 };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false, config),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            policyNoticeSink: (notice, _) =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                notices,
                Is.EqualTo(new[]
                {
                    new VersionControlPolicyNotice.LargeMediaWithoutLfs(relativePath, 1),
                }));
        });
    }

    [Test]
    public async Task CommitAllAsync_detects_large_media_under_a_Unix_backslash_pathspec()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Backslashes are directory separators on Windows.");
        }

        string projectRoot = Path.Combine(Root, @"project\with-backslash");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "initial\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "add nested project");
        string relativePath = "resources/large.MP4";
        string mediaPath = Path.Combine(
            projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, [0]);
        var notices = new List<VersionControlPolicyNotice>();
        var config = new VersionControlConfig { LargeMediaWarningThresholdMb = 0 };
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false, config),
            repository,
            watcher: null,
            _ => CreateRunner(),
            policyNoticeSink: (notice, _) =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                notices,
                Is.EqualTo(new[]
                {
                    new VersionControlPolicyNotice.LargeMediaWithoutLfs(relativePath, 1),
                }));
        });
    }

    [Test]
    public async Task InitializeAsync_rejects_ignored_data_in_a_new_repository_before_mutation()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, ".gitignore"), "*.bep\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(
                    new RepositoryInfo(projectRoot, projectRoot),
                    UseLfsWhenAvailable: false)
                {
                    Identity = new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
                },
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(Directory.Exists(Path.Combine(projectRoot, ".git")), Is.False);
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("*.bep\n"));
        });
    }

    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    public async Task InitializeAsync_rejects_ignored_future_hygiene_paths_in_top_level_repository(
        string fileName)
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), $"/{fileName}\n");
        await RunGitAsync("add", "--force", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore future hygiene path");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task InitializeAsync_rejects_ignored_resource_media_in_top_level_repository()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        Directory.CreateDirectory(Path.Combine(Root, "resources"));
        await File.WriteAllTextAsync(Path.Combine(Root, "resources", "clip.mp4"), "media\n");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "/resources/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore media");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(service.Repository, Is.Null);
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task InitializeAsync_rejects_ignored_media_outside_resources_directory()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        Directory.CreateDirectory(Path.Combine(Root, "assets"));
        await File.WriteAllTextAsync(Path.Combine(Root, "assets", "clip.mp4"), "media\n");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "/assets/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore project media");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("ignore rules"));
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_is_idempotent_and_does_not_stage_or_commit()
    {
        await CommitFileAsync("project.bep", "{}\n", "existing repository");
        string initialTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        GitCommandResult currentTip = await RunGitAsync("rev-parse", "HEAD");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(currentTip.Stdout.Trim(), Is.EqualTo(initialTip));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(
                File.ReadAllLines(Path.Combine(Root, ".gitignore")),
                Is.EqualTo(new[] { "**/.beutl/", "*.[tT][mM][pP]" }));
            Assert.That(
                File.ReadAllLines(Path.Combine(Root, ".gitattributes"))
                    .Count(static line => line == "*.[bB][eE][pP] text eol=lf"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_marks_mixed_case_project_extensions_as_text()
    {
        await CommitFileAsync("project.bep", "{}\n", "existing repository");
        using var service = CreateService();

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        string[] paths = ["Project.BEP", "Scene.ScEnE", "Element.BeLm"];
        GitCommandResult attributes = await RunGitAsync(
            ["check-attr", "text", "eol", "--", .. paths]);
        string[] lines = attributes.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(
            lines,
            Is.EqualTo(paths.SelectMany(static path => new[]
            {
                $"{path}: text: set",
                $"{path}: eol: lf",
            }).ToArray()));
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_preserves_unmanaged_lfs_rules_when_lfs_is_disabled()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        const string unmanagedMatchingRule =
            "resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text";
        const string customRule =
            "assets/**/*.psd filter=lfs diff=lfs merge=lfs -text";
        const string customizedResourceRule =
            "resources/**/*.mov filter=custom diff=custom merge=custom -text";
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            $"{unmanagedMatchingRule}\n{customRule}\n{customizedResourceRule}\n");
        using var service = CreateService();

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        string[] lines = await File.ReadAllLinesAsync(Path.Combine(Root, ".gitattributes"));
        Assert.Multiple(() =>
        {
            Assert.That(lines, Does.Contain(unmanagedMatchingRule));
            Assert.That(lines, Does.Contain(customRule));
            Assert.That(lines, Does.Contain(customizedResourceRule));
            Assert.That(lines, Does.Contain("*.[bB][eE][pP] text eol=lf"));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_removes_only_the_Beutl_managed_lfs_block()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        const string managedRule =
            "resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text";
        const string customRule =
            "assets/**/*.psd filter=lfs diff=lfs merge=lfs -text";
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            $"custom text\n# BEGIN BEUTL MANAGED LFS\n{managedRule}\n"
            + $"# END BEUTL MANAGED LFS\n{customRule}\n");
        using var service = CreateService();

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        string contents = await File.ReadAllTextAsync(Path.Combine(Root, ".gitattributes"));
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Not.Contain("# BEGIN BEUTL MANAGED LFS"));
            Assert.That(contents, Does.Not.Contain(managedRule));
            Assert.That(contents, Does.Contain(customRule));
            Assert.That(contents, Does.Contain("custom text"));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_disabling_Lfs_removes_rules_managed_while_enabled()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        var config = new VersionControlConfig { UseLfsWhenAvailable = true };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true, config),
            Repository,
            watcher: null,
            _ => CreateRunner());

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(Root, ".gitattributes")),
            Does.Contain("# BEGIN BEUTL MANAGED LFS"));

        config.UseLfsWhenAvailable = false;
        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        string contents = await File.ReadAllTextAsync(Path.Combine(Root, ".gitattributes"));
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Not.Contain("# BEGIN BEUTL MANAGED LFS"));
            Assert.That(contents, Does.Not.Contain("filter=lfs"));
            Assert.That(contents, Does.Contain("*.[bB][eE][pP] text eol=lf"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EnsureRepositoryHygieneAsync_keeps_user_Lfs_overrides_after_the_managed_block(
        bool existingManagedBlock)
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        const string overrideRule =
            "resources/**/*.mp4 -filter -diff -merge -text";
        string managedBlock = existingManagedBlock
            ? "# BEGIN BEUTL MANAGED LFS\nlegacy managed contents\n"
              + "# END BEUTL MANAGED LFS\n"
            : string.Empty;
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            $"custom before\n{managedBlock}{overrideRule}\ncustom after\n");
        var runner = new RecordingLfsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: true),
            CancellationToken.None);

        string[] lines = await File.ReadAllLinesAsync(Path.Combine(Root, ".gitattributes"));
        GitCommandResult attribute = await RunGitAsync(
            "check-attr",
            "filter",
            "--",
            "resources/nested/clip.mp4");
        Assert.Multiple(() =>
        {
            Assert.That(
                Array.IndexOf(lines, "# BEGIN BEUTL MANAGED LFS"),
                Is.LessThan(Array.IndexOf(lines, overrideRule)));
            Assert.That(attribute.Stdout, Does.EndWith("filter: unset\n"));
        });
    }

    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    public async Task EnsureRepositoryHygieneAsync_refuses_to_follow_hygiene_file_links(
        string fileName)
    {
        string externalRoot = CreateTemporaryDirectory();
        string externalPath = Path.Combine(externalRoot, fileName);
        const string originalContents = "external contents\n";
        await File.WriteAllTextAsync(externalPath, originalContents);
        string hygienePath = Path.Combine(Root, fileName);
        CreateFileSymbolicLinkOrIgnore(hygienePath, externalPath);
        using var service = CreateService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("regular file"));
            Assert.That(File.ReadAllText(externalPath), Is.EqualTo(originalContents));
            Assert.That(new FileInfo(hygienePath).LinkTarget, Is.Not.Null);
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_retries_after_a_concurrent_regular_file_edit()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(attributesPath, "original custom rule\n");
        int edits = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            beforeHygieneFileReplace: async (path, cancellationToken) =>
            {
                if (path == attributesPath && Interlocked.Exchange(ref edits, 1) == 0)
                {
                    await File.WriteAllTextAsync(
                        path,
                        "concurrent custom rule\n",
                        cancellationToken);
                }
            });

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        string contents = await File.ReadAllTextAsync(attributesPath);
        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(contents, Does.Contain("concurrent custom rule\n"));
            Assert.That(contents, Does.Not.Contain("original custom rule\n"));
            Assert.That(contents, Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_merges_an_edit_at_the_commit_boundary()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(attributesPath, "original custom rule\n");
        int edits = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            beforeFileCommit: async (path, cancellationToken) =>
            {
                if (path == attributesPath && Interlocked.Exchange(ref edits, 1) == 0)
                {
                    await File.WriteAllTextAsync(
                        path,
                        "commit-boundary custom rule\n",
                        cancellationToken);
                }
            });

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        string contents = await File.ReadAllTextAsync(attributesPath);
        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(contents, Does.Contain("commit-boundary custom rule\n"));
            Assert.That(contents, Does.Not.Contain("original custom rule\n"));
            Assert.That(contents, Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
            Assert.That(
                Directory.EnumerateFiles(Root)
                    .Where(path => Path.GetFileName(path).StartsWith(
                        ".gitattributes.",
                        StringComparison.Ordinal)),
                Is.Empty);
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_retains_a_second_edit_during_rollback()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(attributesPath, "original custom rule\n");
        int firstEdits = 0;
        int secondEdits = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            beforeFileCommit: async (path, cancellationToken) =>
            {
                if (path == attributesPath && Interlocked.Exchange(ref firstEdits, 1) == 0)
                {
                    await File.WriteAllTextAsync(
                        path,
                        "first concurrent edit\n",
                        cancellationToken);
                }
            },
            afterFileExchange: async (path, cancellationToken) =>
            {
                if (path == attributesPath && Interlocked.Exchange(ref secondEdits, 1) == 0)
                {
                    await File.WriteAllTextAsync(
                        path,
                        "second concurrent edit\n",
                        cancellationToken);
                }
            });

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        int retainedPathStart = exception!.Message.IndexOf('\'');
        int retainedPathEnd = exception.Message.LastIndexOf('\'');
        Assert.That(retainedPathStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(retainedPathEnd, Is.GreaterThan(retainedPathStart));
        string retainedPath = exception.Message[(retainedPathStart + 1)..retainedPathEnd];
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("retained at"));
            Assert.That(firstEdits, Is.EqualTo(1));
            Assert.That(secondEdits, Is.EqualTo(1));
            Assert.That(
                File.ReadAllText(attributesPath),
                Is.EqualTo("first concurrent edit\n"));
            Assert.That(File.Exists(retainedPath), Is.True);
            Assert.That(
                File.ReadAllText(retainedPath),
                Is.EqualTo("second concurrent edit\n"));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_reports_verified_backup_cleanup_failure()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(attributesPath, "original custom rule\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            deleteVerifiedHygieneFile: _ => false);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        int retainedPathStart = exception!.Message.IndexOf('\'');
        int retainedPathEnd = exception.Message.LastIndexOf('\'');
        Assert.That(retainedPathStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(retainedPathEnd, Is.GreaterThan(retainedPathStart));
        string retainedPath = exception.Message[(retainedPathStart + 1)..retainedPathEnd];
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("could not be removed"));
            Assert.That(retainedPath, Does.EndWith(".tmp"));
            Assert.That(File.Exists(retainedPath), Is.True);
            Assert.That(
                File.ReadAllText(retainedPath),
                Is.EqualTo("original custom rule\n"));
            Assert.That(
                File.ReadAllText(attributesPath),
                Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_preserves_existing_Unix_file_mode()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes are not available on Windows.");
            return;
        }

        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(attributesPath, "custom rule\n");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(attributesPath, expectedMode);
        using var service = CreateService();

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        Assert.That(File.GetUnixFileMode(attributesPath), Is.EqualTo(expectedMode));
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_aborts_when_a_file_becomes_a_link_after_read()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(attributesPath, "original custom rule\n");
        string externalRoot = CreateTemporaryDirectory();
        string externalPath = Path.Combine(externalRoot, ".gitattributes");
        await File.WriteAllTextAsync(externalPath, "external contents\n");
        int replacements = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            beforeHygieneFileReplace: (path, _) =>
            {
                if (path == attributesPath && Interlocked.Exchange(ref replacements, 1) == 0)
                {
                    File.Delete(path);
                    CreateFileSymbolicLinkOrIgnore(path, externalPath);
                }

                return Task.CompletedTask;
            });

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("regular file"));
            Assert.That(File.ReadAllText(externalPath), Is.EqualTo("external contents\n"));
            Assert.That(new FileInfo(attributesPath).LinkTarget, Is.Not.Null);
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_rejects_detached_HEAD_before_file_or_Lfs_mutation()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string detachedTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("checkout", "--detach", detachedTip);
        string ignorePath = Path.Combine(Root, ".gitignore");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(ignorePath, "custom ignore\n");
        await File.WriteAllTextAsync(attributesPath, "custom attributes\n");
        var runner = new RecordingLfsRunner(CreateRunner());
        var config = new VersionControlConfig { UseLfsWhenAvailable = true };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true, config),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<DetachedHeadNotSupportedException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo("custom ignore\n"));
            Assert.That(File.ReadAllText(attributesPath), Is.EqualTo("custom attributes\n"));
            Assert.That(runner.LfsInstallCalls, Is.Zero);
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_rejects_unborn_HEAD_before_mutation()
    {
        string ignorePath = Path.Combine(Root, ".gitignore");
        string attributesPath = Path.Combine(Root, ".gitattributes");
        await File.WriteAllTextAsync(ignorePath, "custom ignore\n");
        await File.WriteAllTextAsync(attributesPath, "custom attributes\n");
        using var service = CreateService();

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo("custom ignore\n"));
            Assert.That(File.ReadAllText(attributesPath), Is.EqualTo("custom attributes\n"));
        });
    }

    [Test]
    public async Task EnsureRepositoryHygieneAsync_rejects_ignored_required_data_before_mutation()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        Directory.CreateDirectory(Path.Combine(Root, "resources"));
        await File.WriteAllTextAsync(Path.Combine(Root, "resources", "clip.mp4"), "media\n");
        string ignorePath = Path.Combine(Root, ".gitignore");
        await File.WriteAllTextAsync(ignorePath, "/resources/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore media");
        using var service = CreateService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.EnsureRepositoryHygieneAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ignore rules"));
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo("/resources/\n"));
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task InitializeAsync_sets_main_without_relying_on_the_init_default_branch()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new RecordingInitializationRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: false)
            {
                Identity = new GitIdentity(
                    "Beutl Test",
                    "beutl-test@example.invalid"),
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runner.Commands, Does.Contain("init"));
            Assert.That(
                runner.Commands,
                Does.Contain("symbolic-ref HEAD refs/heads/main"));
            Assert.That(runner.Commands, Does.Not.Contain("init -b main"));
        });
    }

    [Test]
    public async Task CommitAllAsync_creates_one_snapshot_and_skips_a_clean_tree()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        using var service = CreateService();

        CommitResult first = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        CommitResult second = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        GitCommandResult log = await RunGitAsync("log", "-1", "--format=%s%n%b");
        GitCommandResult count = await RunGitAsync("rev-list", "--count", "HEAD");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<CommitResult.Committed>());
            Assert.That(second, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(log.Stdout, Does.StartWith("beutl: snapshot on save\n"));
            Assert.That(log.Stdout, Does.Contain("Beutl-Snapshot: save"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CommitAllAsync_restores_the_prior_index_when_commit_stops_after_staging(
        bool cancelCommit)
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "staged before snapshot\n");
        await RunGitAsync("add", "--", "project.bep");
        await File.WriteAllTextAsync(projectFile, "working tree at snapshot\n");
        string indexBefore = (await RunGitAsync("write-tree")).Stdout.Trim();
        using var cancellation = new CancellationTokenSource();
        var runner = new FailingSnapshotCommitRunner(
            CreateRunner(),
            cancelCommit ? cancellation : null);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        if (cancelCommit)
        {
            Assert.CatchAsync<OperationCanceledException>(
                async () => await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    cancellation.Token));
        }
        else
        {
            Assert.ThrowsAsync<GitOperationException>(
                async () => await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    CancellationToken.None));
        }

        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        GitCommandResult stagedContents = await RunGitAsync("show", ":project.bep");
        string workingContents = await File.ReadAllTextAsync(projectFile);
        Assert.Multiple(() =>
        {
            Assert.That(runner.CommitAttempts, Is.EqualTo(1));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(stagedContents.Stdout, Is.EqualTo("staged before snapshot\n"));
            Assert.That(workingContents, Is.EqualTo("working tree at snapshot\n"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CommitAllAsync_preserves_live_index_when_temp_index_reconciliation_fails_or_is_cancelled(
        bool cancelAdd)
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "changed\n");
        string tipBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        byte[] indexBefore = await File.ReadAllBytesAsync(Path.Combine(Root, ".git", "index"));
        using var cancellation = new CancellationTokenSource();
        var runner = new TempIndexAddFailureRunner(CreateRunner(), cancelAdd ? cancellation : null);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        if (cancelAdd)
        {
            Assert.CatchAsync<OperationCanceledException>(
                async () => await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    cancellation.Token));
        }
        else
        {
            Assert.ThrowsAsync<GitOperationException>(
                async () => await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    CancellationToken.None));
        }

        byte[] indexAfter = await File.ReadAllBytesAsync(Path.Combine(Root, ".git", "index"));
        string tipAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(runner.AddAttempts, Is.EqualTo(1));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(tipAfter, Is.EqualTo(tipBefore));
            Assert.That(runner.TemporaryIndexPath, Is.Not.Null);
            Assert.That(File.Exists(runner.TemporaryIndexPath!), Is.False);
        });
    }

    [Test]
    public async Task CommitAllAsync_refuses_index_rollback_when_external_stage_lands_after_post_add_snapshot()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        string projectFile = Path.Combine(projectRoot, "project.bep");
        await File.WriteAllTextAsync(projectFile, "baseline\n");
        await RunGitAsync("add", "--", "nested-project/project.bep");
        await RunGitAsync("commit", "-m", "nested baseline");
        var projectRepository = new RepositoryInfo(Root, projectRoot);
        string tipBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await File.WriteAllTextAsync(projectFile, "snapshot\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "external.txt"), "external stage\n");
        var runner = new FailingSnapshotCommitRunner(
            CreateRunner(),
            beforeFailure: repository =>
            {
                CreateRunner().RunAsync(
                        Repository,
                        ["add", "--", "external.txt"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            });
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            projectRepository,
            watcher: null,
            _ => runner);

        Exception? exception = Assert.CatchAsync<Exception>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        string branchTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("changed after staging"));
            Assert.That(staged.Stdout, Does.Contain("external.txt"));
            Assert.That(branchTip, Is.EqualTo(tipBefore));
        });
    }

    [Test]
    public async Task CommitAllAsync_refuses_live_index_publication_when_external_stage_lands_during_temp_staging()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        string projectFile = Path.Combine(projectRoot, "project.bep");
        await File.WriteAllTextAsync(projectFile, "baseline\n");
        await RunGitAsync("add", "--", "nested-project/project.bep");
        await RunGitAsync("commit", "-m", "nested baseline");
        await File.WriteAllTextAsync(projectFile, "snapshot\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "external.txt"), "external stage\n");
        var projectRepository = new RepositoryInfo(Root, projectRoot);
        var runner = new ExternalStageDuringTempIndexRunner(
            CreateRunner(),
            Repository,
            "external.txt");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            projectRepository,
            watcher: null,
            _ => runner);

        Exception? exception = Assert.CatchAsync<Exception>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("changed after the snapshot tree was captured"));
            Assert.That(staged.Stdout, Does.Contain("external.txt"));
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InitializeAsync_refuses_live_index_publication_when_external_stage_lands_during_temp_staging()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "snapshot\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "external.txt"), "external stage\n");
        string tipBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        var projectRepository = new RepositoryInfo(Root, projectRoot);
        var runner = new ExternalStageDuringTempIndexRunner(
            CreateRunner(),
            Repository,
            "external.txt");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Exception? exception = Assert.CatchAsync<Exception>(
            async () => await service.InitializeAsync(
                new InitOptions(projectRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        string tipAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("changed after the snapshot tree was captured"));
            Assert.That(staged.Stdout, Does.Contain("external.txt"));
            Assert.That(tipAfter, Is.EqualTo(tipBefore));
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CommitAllAsync_byte_exact_rollback_preserves_preexisting_intent_to_add_entry()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "snapshot\n");
        string intentFile = Path.Combine(Root, "intent.txt");
        await File.WriteAllTextAsync(intentFile, "intent\n");
        await RunGitAsync("add", "-N", "--", "intent.txt");
        string indexRecord = (await RunGitAsync("rev-parse", "--git-path", "index"))
            .Stdout.TrimEnd('\r', '\n');
        string indexPath = Path.GetFullPath(
            Path.IsPathFullyQualified(indexRecord)
                ? indexRecord
                : Path.Combine(Root, indexRecord));
        byte[] indexBefore = await File.ReadAllBytesAsync(indexPath);
        var runner = new FailingSnapshotCommitRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        byte[] indexAfter = await File.ReadAllBytesAsync(indexPath);
        GitCommandResult status = await RunGitAsync("status", "--porcelain", "--", "intent.txt");
        Assert.Multiple(() =>
        {
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(status.Stdout, Does.Contain("intent.txt"));
            Assert.That(runner.CommitAttempts, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CommitAllAsync_reports_a_durable_commit_when_the_runner_loses_its_result(
        bool startWithCommit)
    {
        if (startWithCommit)
        {
            await CommitFileAsync("project.bep", "baseline\n", "baseline");
        }

        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "snapshot contents\n");
        var runner = new LostSnapshotCommitResultRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        GitCommandResult count = await RunGitAsync("rev-list", "--count", "HEAD");
        GitCommandResult head = await RunGitAsync("rev-parse", "HEAD");
        GitCommandResult committedContents = await RunGitAsync("show", "HEAD:project.bep");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        var committed = (CommitResult.Committed)result;
        var revision = (CommitRevision.Known)committed.Revision;
        Assert.Multiple(() =>
        {
            Assert.That(runner.CommitAttempts, Is.EqualTo(1));
            Assert.That(revision.Sha, Is.EqualTo(head.Stdout.Trim()));
            Assert.That(count.Stdout.Trim(), Is.EqualTo(startWithCommit ? "2" : "1"));
            Assert.That(committedContents.Stdout, Is.EqualTo("snapshot contents\n"));
            Assert.That(staged.Stdout, Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_blocks_a_HEAD_switch_while_publishing_the_captured_ref()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "baseline\n");
        await RunGitAsync("add", "--", "nested-project/project.bep");
        await RunGitAsync("commit", "-m", "project baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("branch", "alternate", baseTip);
        await File.WriteAllTextAsync(Path.Combine(Root, "external.txt"), "external stage\n");
        await RunGitAsync("add", "--", "external.txt");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "snapshot\n");
        var isolatedEnvironment = new Dictionary<string, string?>(IsolatedGitEnvironment)
        {
            ["GIT_OPTIONAL_LOCKS"] = "0",
        };
        var runner = new SwitchBranchBeforeSnapshotPublicationRunner(
            new GitCliRunner(GitPath, TimeSpan.FromSeconds(30), isolatedEnvironment),
            Repository,
            "alternate");
        var projectRepository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            projectRepository,
            watcher: null,
            _ => runner);

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        string alternateTip = (await RunGitAsync("rev-parse", "refs/heads/alternate")).Stdout.Trim();
        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();
        GitCommandResult committedContents = await RunGitAsync(
            "show",
            "refs/heads/main:nested-project/project.bep");
        GitCommandResult cachedProject = await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            "--",
            "nested-project");
        GitCommandResult worktreeProject = await RunGitAsync(
            "diff",
            "--name-only",
            "--",
            "nested-project");
        GitCommandResult stagedExternal = await RunGitAsync("show", ":external.txt");
        Assert.Multiple(() =>
        {
            Assert.That(((CommitRevision.Known)result.Revision).Sha, Is.EqualTo(mainTip));
            Assert.That(mainTip, Is.Not.EqualTo(baseTip));
            Assert.That(alternateTip, Is.EqualTo(baseTip));
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(committedContents.Stdout, Is.EqualTo("snapshot\n"));
            Assert.That(cachedProject.Stdout, Is.Empty);
            Assert.That(worktreeProject.Stdout, Is.Empty);
            Assert.That(stagedExternal.Stdout, Is.EqualTo("external stage\n"));
            Assert.That(runner.SwitchCount, Is.Zero);
            Assert.That(runner.BlockedSwitchCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CommitAllAsync_refuses_a_HEAD_switch_before_snapshot_tree_capture()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("branch", "alternate", baseTip);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        var runner = new SwitchBranchBeforeSnapshotTreeRunner(
            CreateRunner(),
            Repository,
            "alternate");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<ProjectCheckpointStateChangedException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        string alternateTip = (await RunGitAsync("rev-parse", "refs/heads/alternate")).Stdout.Trim();
        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(mainTip, Is.EqualTo(baseTip));
            Assert.That(alternateTip, Is.EqualTo(baseTip));
            Assert.That(currentBranch, Is.EqualTo("alternate"));
            Assert.That(runner.SwitchCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InitializeAsync_blocks_a_HEAD_switch_while_publishing_the_captured_ref()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("branch", "alternate", baseTip);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial project\n");
        var runner = new SwitchBranchBeforeSnapshotPublicationRunner(
            CreateRunner(),
            Repository,
            "alternate");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        string alternateTip = (await RunGitAsync("rev-parse", "refs/heads/alternate")).Stdout.Trim();
        string currentBranch = (await RunGitAsync("branch", "--show-current")).Stdout.Trim();
        GitCommandResult committedContents = await RunGitAsync(
            "show",
            "refs/heads/main:project.bep");
        Assert.Multiple(() =>
        {
            Assert.That(mainTip, Is.Not.EqualTo(baseTip));
            Assert.That(alternateTip, Is.EqualTo(baseTip));
            Assert.That(currentBranch, Is.EqualTo("main"));
            Assert.That(committedContents.Stdout, Is.EqualTo("initial project\n"));
            Assert.That(runner.SwitchCount, Is.Zero);
            Assert.That(runner.BlockedSwitchCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CommitAllAsync_reconciles_the_index_from_the_snapshot_not_a_later_worktree_edit()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "snapshot t1\n");
        var runner = new MutateWorktreeBeforeIndexReconciliationRunner(
            CreateRunner(),
            projectFile,
            "worktree t2\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        GitCommandResult committedContents = await RunGitAsync("show", "HEAD:project.bep");
        GitCommandResult cached = await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            "--",
            "project.bep");
        GitCommandResult unstaged = await RunGitAsync(
            "diff",
            "--name-only",
            "--",
            "project.bep");
        Assert.Multiple(() =>
        {
            Assert.That(((CommitRevision.Known)result.Revision).Sha, Is.EqualTo(head));
            Assert.That(committedContents.Stdout, Is.EqualTo("snapshot t1\n"));
            Assert.That(File.ReadAllText(projectFile), Is.EqualTo("worktree t2\n"));
            Assert.That(cached.Stdout, Is.Empty);
            Assert.That(unstaged.Stdout.Trim(), Is.EqualTo("project.bep"));
            Assert.That(runner.MutationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CommitAllAsync_preserves_an_external_stage_after_snapshot_tree_capture()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "snapshot t1\n");
        var runner = new StageProjectAfterSnapshotTreeRunner(
            CreateRunner(),
            Repository,
            projectFile,
            "externally staged t2\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Exception? exception = Assert.CatchAsync<Exception>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        string mainTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        GitCommandResult stagedContents = await RunGitAsync("show", ":project.bep");
        GitCommandResult staged = await RunGitAsync(
            "diff",
            "--cached",
            "--name-only",
            "--",
            "project.bep");
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Does.Contain("changed after the snapshot tree was captured"));
            Assert.That(mainTip, Is.EqualTo(baseTip));
            Assert.That(stagedContents.Stdout, Is.EqualTo("externally staged t2\n"));
            Assert.That(staged.Stdout.Trim(), Is.EqualTo("project.bep"));
            Assert.That(runner.StageCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CommitAllAsync_rejects_a_captured_ref_CAS_race_and_preserves_the_external_tip()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string baseTree = (await RunGitAsync("rev-parse", "HEAD^{tree}")).Stdout.Trim();
        string externalTip = (await RunGitAsync(
            "commit-tree",
            baseTree,
            "-p",
            baseTip,
            "-m",
            "external ref update")).Stdout.Trim();
        await WriteHookAsync("post-commit", "printf 'post\\n' > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        var runner = new MoveCapturedRefBeforeSnapshotPublicationRunner(
            CreateRunner(),
            externalTip);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Exception? exception = Assert.CatchAsync<Exception>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult committedContents = await RunGitAsync(
            "show",
            "refs/heads/main:project.bep");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("captured branch"));
            Assert.That(mainTip, Is.EqualTo(externalTip));
            Assert.That(committedContents.Stdout, Is.EqualTo("baseline\n"));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(runner.MoveCount, Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(Root, "hook-post.txt")), Is.False);
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_rejects_a_gitlink_created_after_layout_validation_before_publication()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        var runner = new CreateNestedRepositoryDuringSnapshotRunner(
            CreateRunner(),
            Root);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        string mainTip = (await RunGitAsync("rev-parse", "refs/heads/main")).Stdout.Trim();
        GitCommandResult stagedGitlink = await RunGitAsync(
            "ls-files",
            "--stage",
            "--",
            "embedded");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("nested Git repository 'embedded'"));
            Assert.That(mainTip, Is.EqualTo(baseTip));
            Assert.That(stagedGitlink.Stdout, Is.Empty);
            Assert.That(runner.InjectionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CommitAllAsync_cleans_a_temporary_worktree_when_add_result_is_lost()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        var runner = new LostSnapshotWorktreeAddResultRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<IOException>(async () => await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None));

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string worktrees = (await RunGitAsync("worktree", "list", "--porcelain")).Stdout;
        string staged = (await RunGitAsync("diff", "--cached", "--name-only")).Stdout;
        Assert.Multiple(() =>
        {
            Assert.That(runner.AddAttempts, Is.EqualTo(1));
            Assert.That(runner.TemporaryWorktreePath, Is.Not.Null);
            Assert.That(Directory.Exists(runner.TemporaryWorktreePath!), Is.False);
            Assert.That(worktrees, Does.Not.Contain(runner.TemporaryWorktreePath!));
            Assert.That(head, Is.EqualTo(baseTip));
            Assert.That(staged, Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_rejects_detached_HEAD_before_staging_or_committing()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string detachedTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("checkout", "--detach", detachedTip);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        using var service = CreateService();

        Assert.ThrowsAsync<DetachedHeadNotSupportedException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        GitCommandResult currentTip = await RunGitAsync("rev-parse", "HEAD");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(currentTip.Stdout.Trim(), Is.EqualTo(detachedTip));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("changed\n"));
        });
    }

    [Test]
    public async Task CommitAllAsync_skips_unattended_snapshot_without_staging_when_identity_is_missing()
    {
        await RunGitAsync("config", "--unset", "user.name");
        await RunGitAsync("config", "--unset", "user.email");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on close",
            SnapshotKind.Close,
            CancellationToken.None);
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.SkippedNoIdentity>());
            Assert.That(staged.Stdout, Is.Empty);
        });
    }

    [Test]
    public async Task Retirement_reports_missing_identity_without_publishing_the_backend_notice()
    {
        await RunGitAsync("config", "--unset", "user.name");
        await RunGitAsync("config", "--unset", "user.email");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            policyNoticeSink: (notice, _) =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        CommitResult? result = await ((IProjectVersionControlBackend)service).RetireAsync(
            new ProjectVersionControlFinalSnapshot(
                "beutl: snapshot on close",
                SnapshotKind.Close));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.SkippedNoIdentity>());
            Assert.That(notices, Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_ignores_Beutl_temporary_resource_artifacts()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.tmp\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore temporary artifacts");
        string resourceDirectory = Path.Combine(Root, "resources");
        Directory.CreateDirectory(resourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(resourceDirectory, "preview.tmp"), "temporary\n");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
    }

    [Test]
    public async Task CommitAllAsync_ignores_Beutl_state_scene_artifacts()
    {
        await CommitFileAsync("project.bep", "{}\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "**/.beutl/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore Beutl state");
        string stateDirectory = Path.Combine(Root, ".beutl");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(stateDirectory, "recovery.scene"), "temporary\n");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
    }

    [Test]
    public async Task CommitAllAsync_rejects_a_referenced_scene_in_the_Beutl_state_directory()
    {
        string stateDirectory = Path.Combine(Root, ".beutl");
        Directory.CreateDirectory(stateDirectory);
        string projectFile = Path.Combine(Root, "project.bep");
        string sceneFile = Path.Combine(stateDirectory, "linked.scene");
        var project = new Project();
        project.Items.Add(new Scene(1920, 1080, "LinkedScene")
        {
            Uri = new Uri(sceneFile),
        });
        CoreSerializer.StoreToUri(project, new Uri(projectFile));
        await RunGitAsync("add", "--", "project.bep");
        await RunGitAsync("commit", "-m", "baseline");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            isWorktreeMutationAllowed: static () => true,
            projectFile: projectFile);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain(".beutl/linked.scene"));
    }

    [Test]
    public async Task CommitAllAsync_rejects_an_ignored_plugin_project_item_sidecar()
    {
        string projectFile = Path.Combine(Root, "project.bep");
        string sidecarFile = Path.Combine(Root, "plugin-data", "item.custom-sidecar");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecarFile)!);
        var item = new SnapshotTestProjectItem
        {
            Uri = new Uri(sidecarFile),
        };
        CoreSerializer.StoreToUri<ProjectItem>(item, item.Uri);
        var project = new Project();
        project.Items.Add(item);
        CoreSerializer.StoreToUri(project, new Uri(projectFile));
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            "*.custom-sidecar\n");
        await RunGitAsync("add", "--", "project.bep", ".gitignore");
        await RunGitAsync("commit", "-m", "plugin project item");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            isWorktreeMutationAllowed: static () => true,
            projectFile: projectFile);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("plugin-data/item.custom-sidecar"));
    }

    [TestCase(".git:clip.png")]
    [TestCase(".beutl.")]
    [TestCase(".git ")]
    public void Reserved_path_detection_preserves_legal_Unix_filename_characters(
        string fileName)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("These trailing and stream-like names are reserved on Windows.");
        }

        var rootUri = new Uri(Path.TrimEndingDirectorySeparator(Root)
                              + Path.DirectorySeparatorChar);
        var uri = new Uri(rootUri, Uri.EscapeDataString(fileName));

        Assert.That(
            VersionControlSerializationGraph.IsInReservedProjectPath(uri, Root),
            Is.False);
    }

    [TestCase(".beutl", ".beutl")]
    [TestCase(".BeUtL", ".BeUtL")]
    [TestCase(".beutl", ".beutl/child")]
    public async Task CommitAllAsync_allows_an_inaccessible_ignored_Beutl_state_subtree(
        string stateDirectoryName,
        string inaccessibleRelativePath)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix directory permissions are not available on Windows.");
            return;
        }

        await CommitFileAsync("project.bep", "{}\n", "baseline");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            $"**/{stateDirectoryName}/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore Beutl state");
        string stateDirectory = Path.Combine(Root, stateDirectoryName);
        Directory.CreateDirectory(stateDirectory);
        string inaccessibleDirectory = Path.Combine(
            Root,
            inaccessibleRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(inaccessibleDirectory);
        string stateFile = Path.Combine(inaccessibleDirectory, "recovery.scene");
        await File.WriteAllTextAsync(stateFile, "temporary\n");
        File.SetUnixFileMode(inaccessibleDirectory, UnixFileMode.None);
        CommitResult? result = null;
        try
        {
            try
            {
                _ = Directory.EnumerateFileSystemEntries(inaccessibleDirectory).FirstOrDefault();
                Assert.Ignore("The current user can still enumerate a mode-000 directory.");
                return;
            }
            catch (UnauthorizedAccessException)
            {
            }

            using var service = CreateService();
            result = await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None);
        }
        finally
        {
            File.SetUnixFileMode(
                inaccessibleDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(File.Exists(stateFile), Is.True);
        });
    }

    [Test]
    public async Task CommitAllAsync_fails_closed_when_an_ignored_required_path_cannot_be_enumerated()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix directory permissions are not available on Windows.");
            return;
        }

        await CommitFileAsync("project.bep", "{}\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "/opaque/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore unrelated directory");
        string opaqueDirectory = Path.Combine(Root, "opaque");
        Directory.CreateDirectory(opaqueDirectory);
        string requiredPath = Path.Combine(opaqueDirectory, "hidden.scene");
        await File.WriteAllTextAsync(requiredPath, "ignored required data\n");
        File.SetUnixFileMode(opaqueDirectory, UnixFileMode.None);
        InvalidOperationException? exception;
        try
        {
            try
            {
                _ = Directory.EnumerateFileSystemEntries(opaqueDirectory).FirstOrDefault();
                Assert.Ignore("The current user can still enumerate a mode-000 directory.");
                return;
            }
            catch (UnauthorizedAccessException)
            {
            }

            using var service = CreateService();

            exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    CancellationToken.None));
        }
        finally
        {
            File.SetUnixFileMode(
                opaqueDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("safely"));
            Assert.That(File.Exists(requiredPath), Is.True);
        });
    }

    [Test]
    public async Task Identity_is_read_and_written_in_repository_local_config()
    {
        await RunGitAsync("config", "--unset", "user.name");
        await RunGitAsync("config", "--unset", "user.email");
        using var service = CreateService();

        Assert.That(await service.GetIdentityAsync(CancellationToken.None), Is.Null);

        var expected = new GitIdentity("Local User", "local@example.invalid");
        await service.SetLocalIdentityAsync(expected, CancellationToken.None);
        GitIdentity? actual = await service.GetIdentityAsync(CancellationToken.None);
        GitCommandResult localName = await RunGitAsync("config", "--local", "--get", "user.name");
        GitCommandResult localEmail = await RunGitAsync("config", "--local", "--get", "user.email");

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(localName.Stdout.Trim(), Is.EqualTo(expected.Name));
            Assert.That(localEmail.Stdout.Trim(), Is.EqualTo(expected.Email));
        });
    }

    [Test]
    public async Task SetLocalIdentityAsync_preserves_an_ordinary_edit_at_the_commit_boundary()
    {
        await RunGitAsync("config", "--local", "user.name", "Original Name");
        await RunGitAsync("config", "--local", "user.email", "original@example.invalid");
        string configPath = Path.Combine(Root, ".git", "config");
        string concurrentConfig = await File.ReadAllTextAsync(configPath)
                                  + "\n[beutl]\n\tconcurrent = true\n";
        int edits = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            beforeFileCommit: async (path, cancellationToken) =>
            {
                if (path == configPath && Interlocked.Exchange(ref edits, 1) == 0)
                {
                    await File.WriteAllTextAsync(path, concurrentConfig, cancellationToken);
                }
            });

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SetLocalIdentityAsync(
                new GitIdentity("Replacement Name", "replacement@example.invalid"),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("external edit was preserved"));
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(concurrentConfig));
            Assert.That(File.Exists(configPath + ".lock"), Is.False);
            Assert.That(
                Directory.GetFiles(Path.GetDirectoryName(configPath)!, ".beutl-config-*"),
                Is.Empty);
        });
    }

    [Test]
    public async Task SetLocalIdentityAsync_reports_a_verified_displaced_config_cleanup_failure()
    {
        await RunGitAsync("config", "--local", "user.name", "Original Name");
        await RunGitAsync("config", "--local", "user.email", "original@example.invalid");
        string configDirectory = Path.Combine(Root, ".git");
        int deletionAttempts = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            deleteOwnedLocalConfigFile: path =>
            {
                if (Interlocked.Increment(ref deletionAttempts) == 1)
                {
                    return false;
                }

                File.Delete(path);
                return !Path.Exists(path);
            });

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SetLocalIdentityAsync(
                new GitIdentity("Replacement Name", "replacement@example.invalid"),
                CancellationToken.None));
        string[] retainedConfigs = Directory.GetFiles(configDirectory)
            .Where(path => string.Equals(
                               Path.GetFileName(path),
                               "config.lock",
                               StringComparison.Ordinal)
                           || Path.GetFileName(path).StartsWith(
                               ".config.",
                               StringComparison.Ordinal))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("could not be removed"));
            Assert.That(deletionAttempts, Is.GreaterThanOrEqualTo(1));
            Assert.That(retainedConfigs, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(retainedConfigs[0]), Does.Contain("Original Name"));
        });
    }

    [Test]
    public async Task SetLocalIdentityAsync_reports_retained_prior_config_after_a_second_edit()
    {
        await RunGitAsync("config", "--local", "user.name", "Original Name");
        await RunGitAsync("config", "--local", "user.email", "original@example.invalid");
        string configPath = Path.Combine(Root, ".git", "config");
        string configDirectory = Path.GetDirectoryName(configPath)!;
        const string LaterConfig = "[beutl]\n\tlater = true\n";
        int deletionAttempts = 0;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            afterFileExchange: async (path, cancellationToken) =>
            {
                if (path == configPath)
                {
                    await File.WriteAllTextAsync(path, LaterConfig, cancellationToken);
                }
            },
            deleteOwnedLocalConfigFile: path =>
            {
                if (Interlocked.Increment(ref deletionAttempts) == 1)
                {
                    return false;
                }

                File.Delete(path);
                return !Path.Exists(path);
            });

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SetLocalIdentityAsync(
                new GitIdentity("Replacement Name", "replacement@example.invalid"),
                CancellationToken.None));
        string retainedPath = Directory.GetFiles(configDirectory)
            .Single(path => string.Equals(
                                Path.GetFileName(path),
                                "config.lock",
                                StringComparison.Ordinal)
                            || Path.GetFileName(path).StartsWith(
                                ".config.",
                                StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(retainedPath));
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(LaterConfig));
            Assert.That(File.ReadAllText(retainedPath), Does.Contain("Original Name"));
        });
    }

    [Test]
    public async Task SetLocalIdentityAsync_restores_both_local_values_when_the_email_write_fails()
    {
        await RunGitAsync("config", "--local", "user.name", "Original Name");
        await RunGitAsync("config", "--local", "user.email", "original@example.invalid");
        string? liveNameDuringUpdate = null;
        GitOperationException? concurrentWriteFailure = null;
        var runner = new FailingIdentityEmailWriteRunner(
            CreateRunner(),
            new GitOperationException(4, "simulated config write failure"),
            () =>
            {
                liveNameDuringUpdate = RunGitAsync(
                        "config",
                        "--local",
                        "--get",
                        "user.name")
                    .GetAwaiter()
                    .GetResult()
                    .Stdout.Trim();
                try
                {
                    RunGitAsync(
                            "config",
                            "--local",
                            "user.name",
                            "Concurrent Name")
                        .GetAwaiter()
                        .GetResult();
                }
                catch (GitOperationException ex)
                {
                    concurrentWriteFailure = ex;
                }
            });
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.SetLocalIdentityAsync(
                new GitIdentity("Replacement Name", "replacement@example.invalid"),
                CancellationToken.None));

        IReadOnlyList<string> names = await GetLocalConfigValuesAsync("user.name");
        IReadOnlyList<string> emails = await GetLocalConfigValuesAsync("user.email");
        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EqualTo(new[] { "Original Name" }));
            Assert.That(emails, Is.EqualTo(new[] { "original@example.invalid" }));
            Assert.That(liveNameDuringUpdate, Is.EqualTo("Original Name"));
            Assert.That(concurrentWriteFailure, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(Root, ".git", "config.lock")), Is.False);
            Assert.That(
                Directory.GetFiles(Path.Combine(Root, ".git"), ".beutl-config-*"),
                Is.Empty);
        });
    }

    [Test]
    public async Task SetLocalIdentityAsync_restores_absence_and_multiple_values_when_cancelled()
    {
        await RunGitAsync("config", "--local", "--unset-all", "user.name");
        await RunGitAsync("config", "--local", "--unset-all", "user.email");
        await RunGitAsync("config", "--local", "--add", "user.email", "first@example.invalid");
        await RunGitAsync("config", "--local", "--add", "user.email", "second@example.invalid");
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FailingIdentityEmailWriteRunner(
            CreateRunner(),
            new OperationCanceledException(cancellationSource.Token),
            cancellationSource.Cancel);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.SetLocalIdentityAsync(
                new GitIdentity("Replacement Name", "replacement@example.invalid"),
                cancellationSource.Token));

        IReadOnlyList<string> names = await GetLocalConfigValuesAsync("user.name");
        IReadOnlyList<string> emails = await GetLocalConfigValuesAsync("user.email");
        Assert.Multiple(() =>
        {
            Assert.That(names, Is.Empty);
            Assert.That(
                emails,
                Is.EqualTo(new[] { "first@example.invalid", "second@example.invalid" }));
            Assert.That(File.Exists(Path.Combine(Root, ".git", "config.lock")), Is.False);
            Assert.That(
                Directory.GetFiles(Path.Combine(Root, ".git"), ".beutl-config-*"),
                Is.Empty);
        });
    }

    [TestCase("config.lock", false)]
    [TestCase("config.lock.lock", true)]
    public async Task SetLocalIdentityAsync_preserves_a_foreign_configuration_lock(
        string lockFileName,
        bool succeeds)
    {
        await RunGitAsync("config", "--local", "user.name", "Original Name");
        await RunGitAsync("config", "--local", "user.email", "original@example.invalid");
        string lockPath = Path.Combine(Root, ".git", lockFileName);
        await File.WriteAllTextAsync(lockPath, "foreign lock sentinel\n");
        using var service = CreateService();

        var replacement = new GitIdentity("Replacement Name", "replacement@example.invalid");
        if (succeeds)
        {
            Assert.DoesNotThrowAsync(
                async () => await service.SetLocalIdentityAsync(
                    replacement,
                    CancellationToken.None));
        }
        else
        {
            Assert.ThrowsAsync<GitOperationException>(
                async () => await service.SetLocalIdentityAsync(
                    replacement,
                    CancellationToken.None));
        }

        IReadOnlyList<string> names = await GetLocalConfigValuesAsync("user.name");
        IReadOnlyList<string> emails = await GetLocalConfigValuesAsync("user.email");
        Assert.Multiple(() =>
        {
            Assert.That(
                names,
                Is.EqualTo(new[] { succeeds ? replacement.Name : "Original Name" }));
            Assert.That(
                emails,
                Is.EqualTo(new[]
                {
                    succeeds ? replacement.Email : "original@example.invalid",
                }));
            Assert.That(File.ReadAllText(lockPath), Is.EqualTo("foreign lock sentinel\n"));
        });
    }

    [Test]
    public async Task CommitAllAsync_scopes_commit_and_preserves_foreign_staged_changes()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "foreign.txt"), "foreign\n");
        await RunGitAsync("add", "--", "foreign.txt");
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        GitCommandResult committed = await RunGitAsync("show", "--format=", "--name-only", "HEAD");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(committed.Stdout, Does.Contain("nested-project/project.bep"));
            Assert.That(committed.Stdout, Does.Not.Contain("foreign.txt"));
            Assert.That(staged.Stdout.Trim(), Is.EqualTo("foreign.txt"));
        });
    }

    [Test]
    public async Task Stale_lock_failure_is_exposed_and_removed_only_on_request()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        string lockPath = Path.Combine(Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "");
        File.SetLastWriteTimeUtc(
            lockPath,
            DateTime.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        using GitCliVersionControlService service = CreateService();
        var completion = new TaskCompletionSource<RepositoryLockInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, lockInfo) =>
            completion.TrySetResult(lockInfo);

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));
        RepositoryLockInfo lockInfo = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(service.RecoverableLock, Is.EqualTo(lockInfo));
            Assert.That(File.Exists(lockPath), Is.True);
        });

        Assert.That(
            await service.RemoveRecoverableLockAsync(
                new RepositoryLockInfo(lockInfo.LockPath, lockInfo.LastWriteTimeUtc),
                CancellationToken.None),
            Is.False);
        Assert.That(File.Exists(lockPath), Is.True);

        bool removed = await service.RemoveRecoverableLockAsync(
            lockInfo,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(OperatingSystem.IsWindows()));
            Assert.That(File.Exists(lockPath), Is.EqualTo(!OperatingSystem.IsWindows()));
        });
        if (OperatingSystem.IsWindows())
        {
            Assert.That(
                await service.CommitAllAsync(
                    "beutl: snapshot on save",
                    SnapshotKind.Save,
                    CancellationToken.None),
                Is.TypeOf<CommitResult.Committed>());
        }
    }

    [Test]
    public async Task Stale_current_branch_lock_failure_offers_one_click_recovery()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        string lockPath = Path.Combine(Root, ".git", "refs", "heads", "main.lock");
        await File.WriteAllTextAsync(lockPath, "stale");
        File.SetLastWriteTimeUtc(
            lockPath,
            DateTime.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        using GitCliVersionControlService service = CreateService();
        var completion = new TaskCompletionSource<RepositoryLockInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, lockInfo) =>
            completion.TrySetResult(lockInfo);

        GitOperationException? exception = Assert.ThrowsAsync<GitOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));
        RepositoryLockInfo lockInfo = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.IsRepositoryLockFailure, Is.True);
            Assert.That(
                RepositoryPathComparer.AreEquivalent(lockInfo.LockPath, lockPath),
                Is.True);
            Assert.That(service.RecoverableLock, Is.EqualTo(lockInfo));
            Assert.That(File.Exists(lockPath), Is.True);
        });
        bool removed = await service.RemoveRecoverableLockAsync(
            lockInfo,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(OperatingSystem.IsWindows()));
            Assert.That(File.Exists(lockPath), Is.EqualTo(!OperatingSystem.IsWindows()));
        });
    }

    [Test]
    public async Task Mapped_remote_failure_still_exposes_recoverable_lock()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new RemoteLockFailureRunner(CreateRunner(), expectedLock);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        var completion = new TaskCompletionSource<RepositoryLockInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, lockInfo) =>
            completion.TrySetResult(lockInfo);

        RemoteOpResult result = await service.PushAsync(
            progress: null,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(runner.PushCalls, Is.EqualTo(1));
            Assert.That(runner.LockProbeCalls, Is.EqualTo(1));
            Assert.That(service.RecoverableLock, Is.SameAs(expectedLock));
        });
        RepositoryLockInfo actual = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(actual, Is.EqualTo(expectedLock));
            Assert.That(service.RecoverableLock, Is.EqualTo(expectedLock));
        });
    }

    [Test]
    public async Task Exclusive_operation_exposes_lock_in_a_wrapped_aggregate_branch()
    {
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new FailingInitialCommitRunner(CreateRunner(), expectedLock);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        await service.GetStatusAsync(CancellationToken.None);
        var expectedFailure = new InvalidOperationException(
            "recovery wrapper",
            new AggregateException(
                new InvalidOperationException("first recovery failure"),
                new GitOperationException(
                    1,
                    $"fatal: Unable to create '{expectedLock.LockPath}': index.lock exists.")));

        InvalidOperationException? actual = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                _ => Task.FromException<bool>(expectedFailure),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expectedFailure));
            Assert.That(service.RecoverableLock, Is.SameAs(expectedLock));
        });
    }

    [Test]
    public async Task GetHistoryAsync_pages_commits_and_parses_snapshot_trailers()
    {
        await CommitFileAsync("project.bep", "one\n", "manual baseline");
        using var service = CreateService();

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "two\n");
        await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "three\n");
        await service.CommitAllAsync(
            "beutl: snapshot on close",
            SnapshotKind.Close,
            CancellationToken.None);

        IReadOnlyList<CommitInfo> firstPage = await service.GetHistoryAsync(
            0,
            2,
            CancellationToken.None);
        IReadOnlyList<CommitInfo> secondPage = await service.GetHistoryAsync(
            2,
            2,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstPage, Has.Count.EqualTo(2));
            Assert.That(firstPage[0].Kind, Is.EqualTo(SnapshotKind.Close));
            Assert.That(firstPage[0].Subject, Is.EqualTo("beutl: snapshot on close"));
            Assert.That(firstPage[0].AuthorName, Is.EqualTo("Beutl Test"));
            Assert.That(firstPage[0].Sha, Has.Length.EqualTo(40));
            Assert.That(firstPage[1].Kind, Is.EqualTo(SnapshotKind.Save));
            Assert.That(secondPage, Has.Count.EqualTo(1));
            Assert.That(secondPage[0].Kind, Is.EqualTo(SnapshotKind.Manual));
            Assert.That(secondPage[0].Subject, Is.EqualTo("manual baseline"));
        });
    }

    [TestCase("save")]
    [TestCase("close")]
    [TestCase("safety")]
    [TestCase("restore")]
    [TestCase("recovery")]
    [TestCase("init")]
    public async Task Manual_commit_with_reserved_snapshot_trailer_remains_manual(
        string injectedKind)
    {
        await CommitFileAsync("project.bep", "before\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "milestone\n");
        using var service = CreateService();
        string message = $"named milestone\n\nBeutl-Snapshot: {injectedKind}";

        CommitResult result = await service.CommitAllAsync(
            message,
            SnapshotKind.Manual,
            CancellationToken.None);
        CommitInfo history = (await service.GetHistoryAsync(
            skip: 0,
            take: 1,
            CancellationToken.None)).Single();
        string body = (await RunGitAsync("log", "-1", "--format=%B")).Stdout;
        string trailer = (await RunGitAsync(
                "log",
                "-1",
                "--format=%(trailers:key=Beutl-Snapshot,valueonly)"))
            .Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(history.Subject, Is.EqualTo("named milestone"));
            Assert.That(history.Kind, Is.EqualTo(SnapshotKind.Manual));
            Assert.That(body, Does.Contain($"Beutl-Snapshot: {injectedKind}"));
            Assert.That(body, Does.Contain("Beutl-Snapshot: manual"));
            Assert.That(trailer.Trim(), Is.EqualTo("manual"));
        });
    }

    [Test]
    public async Task GetHistoryAsync_parses_recovery_snapshot_trailer()
    {
        await CommitFileAsync("project.bep", "before\n", "baseline");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "recovered\n");

        await service.CommitAllAsync(
            "beutl: recover project state after failed restore",
            SnapshotKind.Recovery,
            CancellationToken.None);
        CommitInfo recovery = (await service.GetHistoryAsync(
            0,
            1,
            CancellationToken.None)).Single();

        Assert.That(recovery.Kind, Is.EqualTo(SnapshotKind.Recovery));
    }

    [Test]
    public async Task History_and_commit_views_disable_signature_output()
    {
        await CommitFileAsync("project.bep", "one\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "two\n");
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        string sha = ((CommitRevision.Known)((CommitResult.Committed)result).Revision).Sha;
        runner.Commands.Clear();

        await service.GetHistoryAsync(0, 10, CancellationToken.None);
        await service.GetCommitFilesAsync(sha, CancellationToken.None);
        await service.GetDiffAsync(sha, "project.bep", CancellationToken.None);

        IReadOnlyList<IReadOnlyList<string>> parsedCommands = runner.Commands
            .Where(static command => command.Contains("log")
                                     || command.FirstOrDefault() == "show")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(parsedCommands, Has.Count.EqualTo(3));
            Assert.That(parsedCommands, Has.All.Contains("--no-show-signature"));
        });
    }

    [Test]
    public async Task Automatic_snapshots_disable_signing_while_manual_commits_preserve_user_configuration()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "automatic\n");
        await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "manual\n");
        await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);

        IReadOnlyList<string>[] commits = runner.Commands
            .Where(IsCommitCommand)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(commits, Has.Length.EqualTo(2));
            Assert.That(commits[0], Does.Contain("--no-gpg-sign"));
            Assert.That(commits[1], Does.Not.Contain("--no-gpg-sign"));
        });
    }

    [Test]
    public async Task Manual_commit_honors_enabled_signing_configuration()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This test uses the Unix false executable as a deterministic failing signer.");
        }

        string falseExecutable = File.Exists("/usr/bin/false") ? "/usr/bin/false" : "/bin/false";
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("config", "commit.gpgSign", "true");
        await RunGitAsync("config", "gpg.program", falseExecutable);
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "automatic\n");
        Assert.That(
            await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None),
            Is.TypeOf<CommitResult.Committed>());
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "manual\n");

        Assert.ThrowsAsync<GitOperationException>(async () => await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None));

        IReadOnlyList<string>[] commits = runner.Commands
            .Where(IsCommitCommand)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(commits, Has.Length.EqualTo(2));
            Assert.That(commits[0], Does.Contain("--no-gpg-sign"));
            Assert.That(commits[1], Does.Contain("-S"));
        });
    }

    [Test]
    public async Task CommitAllAsync_honors_pre_commit_rejection_without_running_post_commit()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string indexPath = Path.Combine(Root, ".git", "index");
        byte[] indexBefore = await File.ReadAllBytesAsync(indexPath);
        await WriteHookAsync("pre-commit", "exit 17\n");
        await WriteHookAsync("post-commit", "printf 'post\\n' > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        Assert.ThrowsAsync<GitOperationException>(async () => await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None));

        string tipAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        byte[] indexAfter = await File.ReadAllBytesAsync(indexPath);
        Assert.Multiple(() =>
        {
            Assert.That(tipAfter, Is.EqualTo(baseTip));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(File.Exists(Path.Combine(Root, "hook-post.txt")), Is.False);
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_runs_message_and_post_hooks_with_the_published_snapshot_context()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await WriteHookAsync(
            "prepare-commit-msg",
            "test \"$2\" = message || exit 31\n"
            + "test \"$#\" -eq 2 || exit 32\n"
            + "test \"$1\" != \"$(git rev-parse --path-format=absolute --git-path COMMIT_EDITMSG)\" || exit 33\n"
            + "printf 'external message\\n' > \"$(git rev-parse --path-format=absolute --git-path COMMIT_EDITMSG)\"\n"
            + "printf '\\nPrepared-By: hook  \\n\\n\\n' >> \"$1\"\n");
        await WriteHookAsync(
            "commit-msg",
            "printf '\\nReviewed-By: hook  \\n\\n\\n' >> \"$1\"\n");
        await WriteHookAsync(
            "post-commit",
            "test -f \"$GIT_COMMIT_EDITMSG\" || exit 34\n"
            + "printf '%s|%s|%s|%s|%s|%s|%s|%s\\n' \"$GIT_EDITOR\" \"$GIT_INDEX_FILE\" "
            + "\"$GIT_AUTHOR_NAME\" \"$GIT_AUTHOR_EMAIL\" \"$GIT_AUTHOR_DATE\" "
            + "\"$(git branch --show-current)\" \"$(git rev-parse HEAD)\" "
            + "\"$(git diff --cached --name-only -- project.bep)\" > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);

        string sha = ((CommitRevision.Known)result.Revision).Sha;
        string message = (await RunGitAsync("show", "-s", "--format=%B", sha)).Stdout;
        string snapshotTrailer = (await RunGitAsync(
                "show",
                "-s",
                "--format=%(trailers:key=Beutl-Snapshot,valueonly)",
                sha))
            .Stdout.Trim();
        string reviewedByTrailer = (await RunGitAsync(
                "show",
                "-s",
                "--format=%(trailers:key=Reviewed-By,valueonly)",
                sha))
            .Stdout.Trim();
        string[] post = (await File.ReadAllTextAsync(Path.Combine(Root, "hook-post.txt")))
            .TrimEnd('\r', '\n')
            .Split('|');
        string[] author = (await RunGitAsync("show", "-s", "--format=%an%x00%ae%x00%at", sha))
            .Stdout.TrimEnd('\r', '\n')
            .Split('\0');
        string hookTimestamp = post[4].TrimStart('@').Split(' ')[0];
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Prepared-By: hook\n"));
            Assert.That(message, Does.Contain("Reviewed-By: hook\n"));
            Assert.That(message, Does.Not.Contain("external message"));
            Assert.That(message, Does.Not.Contain("hook  "));
            Assert.That(snapshotTrailer, Is.EqualTo("manual"));
            Assert.That(reviewedByTrailer, Is.EqualTo("hook"));
            Assert.That(post, Has.Length.EqualTo(8));
            Assert.That(post[0], Is.EqualTo(":"));
            Assert.That(
                Path.GetFullPath(post[1]),
                Is.EqualTo(Path.Combine(Root, ".git", "index")));
            Assert.That(post[2], Is.EqualTo(author[0]));
            Assert.That(post[3], Is.EqualTo(author[1]));
            Assert.That(hookTimestamp, Is.EqualTo(author[2]));
            Assert.That(post[5], Is.EqualTo("main"));
            Assert.That(post[6], Is.EqualTo(sha));
            Assert.That(post[7], Is.Empty);
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_repairs_the_snapshot_trailer_without_demoting_hook_trailers()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("config", "trailer.separators", "=");
        await WriteHookAsync(
            "prepare-commit-msg",
            "printf 'hook subject\\n\\nPrepared body\\n' > \"$1\"\n");
        await WriteHookAsync(
            "commit-msg",
            "printf '\\nHook tail\\n\\nReviewed-By=hook\\nBeutl-Snapshot: close\\n' >> \"$1\"\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "automatic snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        string sha = ((CommitRevision.Known)result.Revision).Sha;
        string message = (await RunGitAsync("show", "-s", "--format=%B", sha)).Stdout;
        string[] snapshotTrailers = (await RunGitAsync(
                "-c",
                "trailer.separators=:=",
                "show",
                "-s",
                "--format=%(trailers:key=Beutl-Snapshot,valueonly)",
                sha))
            .Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string reviewedByTrailer = (await RunGitAsync(
                "-c",
                "trailer.separators=:=",
                "show",
                "-s",
                "--format=%(trailers:key=Reviewed-By,valueonly)",
                sha))
            .Stdout.Trim();
        CommitInfo history = (await service.GetHistoryAsync(
            skip: 0,
            take: 1,
            CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.StartWith("hook subject\n"));
            Assert.That(message, Does.Contain("Prepared body\n"));
            Assert.That(message, Does.Contain("Hook tail\n"));
            Assert.That(snapshotTrailers, Is.EqualTo(new[] { "save" }));
            Assert.That(reviewedByTrailer, Is.EqualTo("hook"));
            Assert.That(history.Kind, Is.EqualTo(SnapshotKind.Save));
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_reconstructs_a_hook_duplicated_snapshot_trailer()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await WriteHookAsync(
            "commit-msg",
            "printf 'BEUTL-SNAPSHOT: close\\n' >> \"$1\"\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);
        string sha = ((CommitRevision.Known)result.Revision).Sha;
        string[] snapshotTrailers = (await RunGitAsync(
                "show",
                "-s",
                "--format=%(trailers:key=Beutl-Snapshot,valueonly)",
                sha))
            .Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        Assert.Multiple(() =>
        {
            Assert.That(sha, Is.Not.EqualTo(baseTip));
            Assert.That(snapshotTrailers, Is.EqualTo(new[] { "manual" }));
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [TestCase("strip", false)]
    [TestCase("verbatim", true)]
    public async Task CommitAllAsync_honors_commit_cleanup_for_hook_edited_messages(
        string cleanup,
        bool preservesWhitespace)
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("config", "commit.cleanup", cleanup);
        await WriteHookAsync(
            "prepare-commit-msg",
            "cp \"$1\" prepare-seen.txt\n"
            + "printf '\\n#hook-comment\\nPrepared  \\n\\n\\n' >> \"$1\"\n");
        await WriteHookAsync(
            "commit-msg",
            "cp \"$1\" commit-msg-seen.txt\n"
            + "printf '\\nReviewed  \\n\\n\\n' >> \"$1\"\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "manual snapshot  \n#input-comment",
            SnapshotKind.Manual,
            CancellationToken.None);
        string sha = ((CommitRevision.Known)result.Revision).Sha;
        string message = (await RunGitAsync("show", "-s", "--format=%B", sha)).Stdout;
        string prepareSeen = await File.ReadAllTextAsync(Path.Combine(Root, "prepare-seen.txt"));
        string commitMsgSeen = await File.ReadAllTextAsync(
            Path.Combine(Root, "commit-msg-seen.txt"));

        Assert.Multiple(() =>
        {
            Assert.That(prepareSeen, Does.Contain("#input-comment"));
            Assert.That(prepareSeen.Contains("manual snapshot  ", StringComparison.Ordinal),
                Is.EqualTo(preservesWhitespace));
            Assert.That(commitMsgSeen, Does.Contain("#hook-comment"));
            Assert.That(commitMsgSeen, Does.Contain("Prepared  "));
            Assert.That(message.Contains("#hook-comment", StringComparison.Ordinal),
                Is.EqualTo(preservesWhitespace));
            Assert.That(message.Contains("Prepared  ", StringComparison.Ordinal),
                Is.EqualTo(preservesWhitespace));
            Assert.That(message.Contains("Reviewed  ", StringComparison.Ordinal),
                Is.EqualTo(preservesWhitespace));
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [TestCase("verbatim", false)]
    [TestCase("whitespace", true)]
    public async Task CommitAllAsync_preserves_non_utf8_bytes_written_by_commit_msg_hook(
        string cleanup,
        bool addsTrailingNewline)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The byte-writing hook uses POSIX printf octal escapes.");
        }

        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("config", "commit.cleanup", cleanup);
        await RunGitAsync("config", "i18n.commitEncoding", "ISO-8859-1");
        await WriteHookAsync("commit-msg", "printf '\\351' >> \"$1\"\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);
        string sha = ((CommitRevision.Known)result.Revision).Sha;
        GitCommandResult commitObject = await Runner.RunAsync(
            Repository,
            ["cat-file", "commit", sha],
            GitCommandOptions.Local with
            {
                MaxStdoutBytes = 1024 * 1024,
                CaptureStdoutBytes = true,
            },
            CancellationToken.None);

        byte[] bytes = commitObject.StdoutBytes!;
        int bodySeparator = bytes.AsSpan().IndexOf("\n\n"u8);
        byte[] messagePrefix = Encoding.UTF8.GetBytes(
            "manual snapshot\n\nBeutl-Snapshot: manual\n");
        byte[] reconstructedTrailer = Encoding.UTF8.GetBytes(
            "\n\nBeutl-Snapshot: manual\n");
        int hookMessageLength = messagePrefix.Length + 1 + (addsTrailingNewline ? 1 : 0);
        var expectedMessage = new byte[hookMessageLength + reconstructedTrailer.Length];
        messagePrefix.CopyTo(expectedMessage, 0);
        expectedMessage[messagePrefix.Length] = 0xe9;
        if (addsTrailingNewline)
        {
            expectedMessage[messagePrefix.Length + 1] = (byte)'\n';
        }

        reconstructedTrailer.CopyTo(expectedMessage, hookMessageLength);
        string snapshotTrailer = (await RunGitAsync(
                "show",
                "-s",
                "--format=%(trailers:key=Beutl-Snapshot,valueonly)",
                sha))
            .Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(commitObject.StdoutTruncated, Is.False);
            Assert.That(bodySeparator, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                bodySeparator < 0 ? [] : bytes[(bodySeparator + 2)..],
                Is.EqualTo(expectedMessage));
            Assert.That(snapshotTrailer, Is.EqualTo("manual"));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task CommitAllAsync_ignores_ambient_identity_and_freezes_it_before_hooks()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await WriteHookAsync(
            "pre-commit",
            "printf '%s|%s|%s|%s|%s|%s\\n' "
            + "\"$GIT_AUTHOR_NAME\" \"$GIT_AUTHOR_EMAIL\" \"$GIT_AUTHOR_DATE\" "
            + "\"$GIT_COMMITTER_NAME\" \"$GIT_COMMITTER_EMAIL\" \"$GIT_COMMITTER_DATE\" "
            + "> pre-ident.txt\n"
            + "git config user.name 'Hook Mutation'\n"
            + "git config user.email hook-mutation@example.invalid\n"
            + "sleep 2\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        string[] ambientVariables =
        [
            "GIT_AUTHOR_DATE",
            "GIT_AUTHOR_EMAIL",
            "GIT_AUTHOR_NAME",
            "GIT_COMMITTER_DATE",
            "GIT_COMMITTER_EMAIL",
            "GIT_COMMITTER_NAME",
        ];
        var originalValues = ambientVariables.ToDictionary(
            static name => name,
            Environment.GetEnvironmentVariable);
        Dictionary<string, string?> isolatedEnvironment = IsolatedGitEnvironment
            .Where(static pair => pair.Key is not "GIT_AUTHOR_DATE" and not "GIT_COMMITTER_DATE")
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        DateTimeOffset commitStartedAt = DateTimeOffset.UtcNow;
        CommitResult.Committed result;
        DateTimeOffset commitFinishedAt;
        try
        {
            Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", "2001-01-01T00:00:00Z");
            Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "ambient-author@example.invalid");
            Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Ambient Author");
            Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", "2099-12-31T23:59:59Z");
            Environment.SetEnvironmentVariable(
                "GIT_COMMITTER_EMAIL",
                "ambient-committer@example.invalid");
            Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Ambient Committer");
            var runner = new GitCliRunner(
                GitPath,
                TimeSpan.FromSeconds(10),
                isolatedEnvironment);
            using var service = new GitCliVersionControlService(
                CreateInstalledLocator(),
                Repository,
                watcher: null,
                _ => runner);

            result = (CommitResult.Committed)await service.CommitAllAsync(
                "manual snapshot",
                SnapshotKind.Manual,
                CancellationToken.None);
            commitFinishedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            foreach ((string name, string? value) in originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        string sha = ((CommitRevision.Known)result.Revision).Sha;
        string[] preIdent = (await File.ReadAllTextAsync(Path.Combine(Root, "pre-ident.txt")))
            .TrimEnd('\r', '\n')
            .Split('|');
        string[] committed = (await RunGitAsync(
                "show",
                "-s",
                "--format=%an%x00%ae%x00%cn%x00%ce%x00%at%x00%ct",
                sha))
            .Stdout.TrimEnd('\r', '\n')
            .Split('\0');
        static string Timestamp(string value) => value.TrimStart('@').Split(' ')[0];
        long earliestExpectedTimestamp = commitStartedAt.AddSeconds(-1).ToUnixTimeSeconds();
        long latestExpectedTimestamp = commitFinishedAt.AddSeconds(1).ToUnixTimeSeconds();
        Assert.Multiple(() =>
        {
            Assert.That(preIdent, Has.Length.EqualTo(6));
            Assert.That(committed, Has.Length.EqualTo(6));
            Assert.That(committed[0], Is.EqualTo("Beutl Test"));
            Assert.That(committed[1], Is.EqualTo("beutl-test@example.invalid"));
            Assert.That(committed[2], Is.EqualTo("Beutl Test"));
            Assert.That(committed[3], Is.EqualTo("beutl-test@example.invalid"));
            Assert.That(committed[0], Is.EqualTo(preIdent[0]));
            Assert.That(committed[1], Is.EqualTo(preIdent[1]));
            Assert.That(committed[2], Is.EqualTo(preIdent[3]));
            Assert.That(committed[3], Is.EqualTo(preIdent[4]));
            Assert.That(committed[4], Is.EqualTo(Timestamp(preIdent[2])));
            Assert.That(committed[5], Is.EqualTo(Timestamp(preIdent[5])));
            Assert.That(
                long.Parse(committed[4], System.Globalization.CultureInfo.InvariantCulture),
                Is.InRange(earliestExpectedTimestamp, latestExpectedTimestamp));
            Assert.That(
                long.Parse(committed[5], System.Globalization.CultureInfo.InvariantCulture),
                Is.InRange(earliestExpectedTimestamp, latestExpectedTimestamp));
            Assert.That(committed, Has.None.EqualTo("Hook Mutation"));
            Assert.That(committed, Has.None.EqualTo("hook-mutation@example.invalid"));
            Assert.That(committed, Has.None.EqualTo("Ambient Author"));
            Assert.That(committed, Has.None.EqualTo("ambient-author@example.invalid"));
            Assert.That(committed, Has.None.EqualTo("Ambient Committer"));
            Assert.That(committed, Has.None.EqualTo("ambient-committer@example.invalid"));
        });
    }

    [Test]
    public async Task CommitAllAsync_rejects_a_hook_created_symlink_entry()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string blob = (await RunGitAsync("rev-parse", "HEAD:project.bep")).Stdout.Trim();
        await WriteHookAsync(
            "pre-commit",
            $"git update-index --add --cacheinfo 120000,{blob},project.bep\n");
        await WriteHookAsync("post-commit", "printf 'post\\n' > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "manual snapshot",
                SnapshotKind.Manual,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("non-regular"));
            Assert.That((RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(baseTip));
            Assert.That(File.Exists(Path.Combine(Root, "hook-post.txt")), Is.False);
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_rejects_a_hook_deleted_project_entry()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await WriteHookAsync(
            "pre-commit",
            "git update-index --force-remove -- project.bep\n");
        await WriteHookAsync("post-commit", "printf 'post\\n' > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "manual snapshot",
                SnapshotKind.Manual,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("removed content"));
            Assert.That((RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(baseTip));
            Assert.That(File.Exists(Path.Combine(Root, "hook-post.txt")), Is.False);
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CommitAllAsync_validates_a_hook_tree_for_a_directory_symlink_alias(
        bool intermediateAlias)
    {
        string projectRoot = intermediateAlias
            ? Path.Combine(Root, "nested", "project")
            : Root;
        Directory.CreateDirectory(projectRoot);
        string projectFile = Path.Combine(projectRoot, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline");

        string aliasedProjectFile;
        if (intermediateAlias)
        {
            string linkedNested = Path.Combine(Root, "linked-nested");
            CreateDirectorySymbolicLinkOrIgnore(linkedNested, Path.Combine(Root, "nested"));
            aliasedProjectFile = Path.Combine(linkedNested, "project", "project.bep");
        }
        else
        {
            string aliasRoot = Path.Combine(CreateTemporaryDirectory(), "repository");
            CreateDirectorySymbolicLinkOrIgnore(aliasRoot, Root);
            aliasedProjectFile = Path.Combine(aliasRoot, "project.bep");
        }

        var repository = new RepositoryInfo(Root, projectRoot);
        string hookFile = repository.Pathspec == "."
            ? "hook-added.txt"
            : $"{repository.Pathspec}/hook-added.txt";
        await WriteHookAsync(
            "pre-commit",
            $"printf 'hooked\\n' > \"{hookFile}\"\n"
            + $"git add -- \"{hookFile}\"\n");
        await File.WriteAllTextAsync(projectFile, "{ }\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner(),
            projectFile: aliasedProjectFile);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        string[] committedPaths = (await RunGitAsync(
                "show",
                "--format=",
                "--name-only",
                "HEAD"))
            .Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(committedPaths, Does.Contain(hookFile));
            Assert.That(committedPaths, Does.Contain(
                repository.Pathspec == "."
                    ? "project.bep"
                    : $"{repository.Pathspec}/project.bep"));
        });
    }

    [Test]
    public async Task CommitAllAsync_honors_automatic_comment_character_selection()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("config", "commit.cleanup", "strip");
        await RunGitAsync("config", "core.commentChar", "auto");
        await WriteHookAsync(
            "prepare-commit-msg",
            "printf '\\n;remove-me\\n#keep-me\\nbody  \\n' >> \"$1\"\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        var result = (CommitResult.Committed)await service.CommitAllAsync(
            "manual snapshot\n#keep-initial",
            SnapshotKind.Manual,
            CancellationToken.None);

        string sha = ((CommitRevision.Known)result.Revision).Sha;
        string message = (await RunGitAsync("show", "-s", "--format=%B", sha)).Stdout;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("#keep-initial"));
            Assert.That(message, Does.Contain("#keep-me"));
            Assert.That(message, Does.Contain("body\n"));
            Assert.That(message, Does.Not.Contain(";remove-me"));
            Assert.That(message, Does.Not.Contain("body  "));
        });
    }

    [Test]
    public async Task Manual_commit_freezes_disabled_signing_before_pre_commit_hook()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("config", "commit.gpgSign", "false");
        await WriteHookAsync(
            "pre-commit",
            "git config commit.gpgSign true\n"
            + "git config gpg.program definitely-not-a-signer\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        CommitResult result = await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);

        IReadOnlyList<string> commit = runner.Commands.Single(IsCommitCommand);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(commit, Does.Not.Contain("-S"));
        });
    }

    [Test]
    public async Task CommitAllAsync_returns_no_changes_when_a_hook_restores_the_parent_tree()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await WriteHookAsync("pre-commit", "git read-tree HEAD\n");
        await WriteHookAsync("post-commit", "printf 'post\\n' > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That((RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()).Stdout.Trim(),
                Is.EqualTo(baseTip));
            Assert.That(File.Exists(Path.Combine(Root, "hook-post.txt")), Is.False);
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task CommitAllAsync_returns_committed_when_post_commit_fails()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await WriteHookAsync("post-commit", "exit 23\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "snapshot\n");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "manual snapshot",
            SnapshotKind.Manual,
            CancellationToken.None);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                ((CommitRevision.Known)((CommitResult.Committed)result).Revision).Sha,
                Is.EqualTo(head));
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task InitializeAsync_runs_commit_hooks_for_an_unborn_branch()
    {
        await WriteHookAsync(
            "prepare-commit-msg",
            "test \"$2\" = message || exit 31\n"
            + "test \"$#\" -eq 2 || exit 32\n"
            + "printf '\\nPrepared-By: init-hook\\n' >> \"$1\"\n");
        await WriteHookAsync(
            "post-commit",
            "printf '%s|%s\\n' \"$(git branch --show-current)\" "
            + "\"$(git rev-parse HEAD)\" > hook-post.txt\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string[] commitAndParents = (await RunGitAsync("rev-list", "--parents", "-n", "1", "HEAD"))
            .Stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string message = (await RunGitAsync("show", "-s", "--format=%B", "HEAD")).Stdout;
        string post = (await File.ReadAllTextAsync(Path.Combine(Root, "hook-post.txt")))
            .TrimEnd('\r', '\n');
        Assert.Multiple(() =>
        {
            Assert.That(commitAndParents, Has.Length.EqualTo(1));
            Assert.That(message, Does.Contain("Prepared-By: init-hook"));
            Assert.That(message, Does.Contain("Beutl-Snapshot: init"));
            Assert.That(post, Is.EqualTo($"main|{head}"));
            Assert.That(FindOwnedCommitMessageFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task RevisionContainsProjectFileAsync_rejects_revisions_without_the_project_file()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string baseline = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "{}\n", "add project");
        string projectRevision = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(
                service.RevisionContainsProjectFileAsync(
                    projectRevision,
                    Path.Combine(Root, "project.bep"),
                    CancellationToken.None).GetAwaiter().GetResult(),
                Is.True);
            Assert.That(
                service.RevisionContainsProjectFileAsync(
                    baseline,
                    Path.Combine(Root, "project.bep"),
                    CancellationToken.None).GetAwaiter().GetResult(),
                Is.False);
        });
    }

    [Test]
    public async Task GetCommitFilesAsync_reports_added_modified_deleted_and_renamed_paths()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "one\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "delete.belm"), "delete\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "old.scene"), "rename\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline");

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "two\n");
        File.Delete(Path.Combine(Root, "delete.belm"));
        File.Move(Path.Combine(Root, "old.scene"), Path.Combine(Root, "new.scene"));
        await File.WriteAllTextAsync(Path.Combine(Root, "added.belm"), "added\n");
        using var service = CreateService();
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

        IReadOnlyList<FileChange> files = await service.GetCommitFilesAsync(
            commit.Sha,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(files, Does.Contain(new FileChange("project.bep", FileChangeStatus.Modified)));
            Assert.That(files, Does.Contain(new FileChange("delete.belm", FileChangeStatus.Deleted)));
            Assert.That(files, Does.Contain(new FileChange("added.belm", FileChangeStatus.Added)));
            Assert.That(
                files,
                Does.Contain(new FileChange("new.scene", FileChangeStatus.Renamed, "old.scene")));
        });
    }

    [Test]
    public async Task Commit_views_show_merge_changes_against_the_first_parent()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("checkout", "-b", "feature");
        await CommitFileAsync("feature.scene", "feature\n", "feature");
        await RunGitAsync("checkout", "main");
        await CommitFileAsync("main.belm", "main\n", "main");
        await RunGitAsync("merge", "--no-ff", "feature", "-m", "merge feature");
        string merge = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        IReadOnlyList<FileChange> files = await service.GetCommitFilesAsync(
            merge,
            CancellationToken.None);
        string diff = await service.GetDiffAsync(
            merge,
            "feature.scene",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                files,
                Does.Contain(new FileChange("feature.scene", FileChangeStatus.Added)));
            Assert.That(diff, Does.Contain("+feature"));
        });
    }

    [Test]
    public async Task Commit_read_apis_do_not_parse_caller_sha_as_an_option()
    {
        await CommitFileAsync("project.bep", "value\n", "baseline");
        using var service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await service.GetCommitFilesAsync(
                    "--format=%H",
                    CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await service.GetDiffAsync(
                    "--format=%H",
                    path: null,
                    CancellationToken.None));
        });
    }

    [Test]
    public async Task Commit_read_apis_accept_safe_hexadecimal_abbreviations()
    {
        await CommitFileAsync("project.bep", "value\n", "baseline");
        using var service = CreateService();
        CommitInfo commit = (await service.GetHistoryAsync(
            0,
            1,
            CancellationToken.None)).Single();

        IReadOnlyList<FileChange> files = await service.GetCommitFilesAsync(
            commit.ShortSha,
            CancellationToken.None);
        string diff = await service.GetDiffAsync(
            commit.ShortSha,
            path: null,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(files, Does.Contain(new FileChange("project.bep", FileChangeStatus.Added)));
            Assert.That(diff, Does.Contain("+value"));
        });
    }

    [TestCase("abc")]
    [TestCase("abcdz")]
    [TestCase("--abcd")]
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    public void Commit_read_apis_reject_unsafe_or_out_of_range_object_names(string sha)
    {
        using var service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await service.GetCommitFilesAsync(sha, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await service.GetDiffAsync(sha, null, CancellationToken.None));
        });
    }

    [Test]
    public async Task GetDiffAsync_returns_unified_text_for_one_file()
    {
        await CommitFileAsync("project.bep", "old value\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "new value\n");
        using var service = CreateService();
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

        string diff = await service.GetDiffAsync(
            commit.Sha,
            "project.bep",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.Contain("--- a/project.bep"));
            Assert.That(diff, Does.Contain("+++ b/project.bep"));
            Assert.That(diff, Does.Contain("-old value"));
            Assert.That(diff, Does.Contain("+new value"));
            Assert.That(diff, Does.Not.Contain(GitCliVersionControlService.DiffTruncationMarker));
        });
    }

    [Test]
    public async Task GetDiffAsync_disables_configured_color_output()
    {
        await RunGitAsync("config", "color.ui", "always");
        await CommitFileAsync("project.bep", "old value\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "new value\n");
        using var service = CreateService();
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

        string diff = await service.GetDiffAsync(
            commit.Sha,
            "project.bep",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.Contain("--- a/project.bep"));
            Assert.That(diff, Does.Contain("+new value"));
            Assert.That(diff, Does.Not.Contain("\u001b["));
        });
    }

    [Test]
    public async Task CommitAllAsync_rejects_required_content_beneath_a_symbolic_link_directory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This regression requires Unix symbolic-link semantics.");
        }

        await CommitFileAsync("project.bep", "{}\n", "initial");
        string externalRoot = CreateTemporaryDirectory();
        string linkedDirectory = Path.Combine(Root, "linked");
        CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, externalRoot);
        var project = new Project();
        project.Items.Add(new Scene(1920, 1080, "LinkedScene")
        {
            Uri = new Uri(Path.Combine(linkedDirectory, "linked.scene")),
        });
        string projectFile = Path.Combine(Root, "project.bep");
        CoreSerializer.StoreToUri(project, new Uri(projectFile));
        Assert.That(
            SerializedProjectGraph.GetRelativePaths(projectFile, Root),
            Does.Contain("linked/linked.scene"));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            projectFile: projectFile);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("symbolic-link directory 'linked'"));
    }

    [Test]
    public async Task CommitAllAsync_does_not_traverse_an_unreferenced_directory_symbolic_link()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This regression requires Unix symbolic-link semantics.");
        }

        await CommitFileAsync("project.bep", "{}\n", "initial");
        string externalRoot = CreateTemporaryDirectory();
        // The nested repository catches the old pre-enqueue probe; the scene catches a
        // partial fix that moves that probe but still recursively walks the link target.
        Directory.CreateDirectory(Path.Combine(externalRoot, ".git"));
        await File.WriteAllTextAsync(
            Path.Combine(externalRoot, "outside.scene"),
            "external content\n");
        for (int index = 0; index < 256; index++)
        {
            Directory.CreateDirectory(Path.Combine(externalRoot, $"branch-{index:D3}"));
        }

        string linkedDirectory = Path.Combine(Root, "unreferenced");
        CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, externalRoot);
        await RunGitAsync("add", "--", "unreferenced");
        await RunGitAsync("commit", "-m", "track unreferenced link");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
    }

    [Test]
    public async Task CommitAllAsync_rejects_a_required_media_file_symbolic_link()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This regression requires Unix symbolic-link semantics.");
        }

        await CommitFileAsync("project.bep", "{}\n", "initial");
        string externalRoot = CreateTemporaryDirectory();
        string externalMedia = Path.Combine(externalRoot, "external.mp4");
        await File.WriteAllTextAsync(externalMedia, "external media\n");
        string mediaDirectory = Path.Combine(Root, "resources");
        Directory.CreateDirectory(mediaDirectory);
        string linkedMedia = Path.Combine(mediaDirectory, "linked.mp4");
        CreateFileSymbolicLinkOrIgnore(linkedMedia, externalMedia);
        using var service = CreateService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(
            exception!.Message,
            Does.Contain("file symbolic link 'resources/linked.mp4'"));
    }

    [Test]
    public async Task GetDiffAsync_treats_pathspec_magic_like_top_as_a_literal_file_name()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows file names cannot contain a colon.");
        }

        await CommitFileAsync(":(top)", "old literal\n", "literal baseline");
        await CommitFileAsync("other.bep", "old other\n", "other baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ":(top)"), "new literal\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "other.bep"), "new other\n");
        using var service = CreateService();
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "literal path update",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

        string diff = await service.GetDiffAsync(
            commit.Sha,
            ":(top)",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.Contain("+new literal"));
            Assert.That(diff, Does.Not.Contain("+new other"));
        });
    }

    [Test]
    public async Task GetDiffAsync_caps_output_at_one_megabyte_and_appends_marker()
    {
        await CommitFileAsync("large.belm", "old\n", "baseline");
        string largeContents = string.Concat(
            Enumerable.Repeat("a changed line that remains text\n", 40000));
        await File.WriteAllTextAsync(Path.Combine(Root, "large.belm"), largeContents);
        using var service = CreateService();
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

        string diff = await service.GetDiffAsync(
            commit.Sha,
            "large.belm",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.EndWith(GitCliVersionControlService.DiffTruncationMarker));
            Assert.That(
                System.Text.Encoding.UTF8.GetByteCount(
                    diff[..^GitCliVersionControlService.DiffTruncationMarker.Length]),
                Is.LessThanOrEqualTo(GitCliVersionControlService.MaxDiffBytes));
        });
    }

    [Test]
    public async Task CommitProjectTreeAsync_matches_target_and_preserves_ignored_files()
    {
        byte[] targetProject = [0x7b, 0x0a, 0x7d, 0x0a];
        byte[] targetElement = [0x31, 0x32, 0x33, 0x0a];
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            "**/.beutl/\n*.tmp\n");
        await File.WriteAllBytesAsync(Path.Combine(Root, "project.bep"), targetProject);
        Directory.CreateDirectory(Path.Combine(Root, "elements"));
        await File.WriteAllBytesAsync(Path.Combine(Root, "elements", "base.belm"), targetElement);
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "target");
        string targetSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "later\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "elements", "base.belm"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "elements", "later.belm"), "later\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "later");
        string laterSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string stateDirectory = Path.Combine(Root, ".beutl");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(stateDirectory, "view.json"), "keep\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "atomic.tmp"), "keep\n");

        using var service = CreateService();
        CheckedOutBranchTip laterTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        CommitResult targetRestore = await service.CommitProjectTreeAsync(
            laterTip,
            targetSha,
            "beutl: restore target",
            SnapshotKind.Restore,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(Path.Combine(Root, "project.bep")), Is.EqualTo(targetProject));
            Assert.That(
                File.ReadAllBytes(Path.Combine(Root, "elements", "base.belm")),
                Is.EqualTo(targetElement));
            Assert.That(File.Exists(Path.Combine(Root, "elements", "later.belm")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(stateDirectory, "view.json")), Is.EqualTo("keep\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "atomic.tmp")), Is.EqualTo("keep\n"));
        });

        var targetRestoreCommit = (CommitRevision.Known)
            ((CommitResult.Committed)targetRestore).Revision;
        await service.CommitProjectTreeAsync(
            new CheckedOutBranchTip(laterTip.RefName, targetRestoreCommit.Sha),
            laterSha,
            "beutl: restore later",
            SnapshotKind.Restore,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("later\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "elements", "base.belm")),
                Is.EqualTo("changed\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "elements", "later.belm")),
                Is.EqualTo("later\n"));
            Assert.That(File.ReadAllText(Path.Combine(stateDirectory, "view.json")), Is.EqualTo("keep\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "atomic.tmp")), Is.EqualTo("keep\n"));
        });
    }

    [Test]
    public async Task CreateBranchAsync_switches_to_a_new_branch_at_the_selected_commit()
    {
        await CommitFileAsync("project.bep", "target\n", "target");
        string targetSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "current\n", "current");
        string mainSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        await service.CreateBranchAsync(
            "restored-state",
            targetSha,
            CancellationToken.None);

        GitCommandResult branch = await RunGitAsync("branch", "--show-current");
        GitCommandResult newBranchSha = await RunGitAsync("rev-parse", "HEAD");
        GitCommandResult originalBranchSha = await RunGitAsync("rev-parse", "main");
        Assert.Multiple(() =>
        {
            Assert.That(branch.Stdout.Trim(), Is.EqualTo("restored-state"));
            Assert.That(newBranchSha.Stdout.Trim(), Is.EqualTo(targetSha));
            Assert.That(originalBranchSha.Stdout.Trim(), Is.EqualTo(mainSha));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("target\n"));
        });

        await service.SwitchBranchAsync("main", CancellationToken.None);
        GitCommandResult switchedBack = await RunGitAsync("branch", "--show-current");

        Assert.Multiple(() =>
        {
            Assert.That(switchedBack.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("current\n"));
            Assert.That(
                runner.Invocations
                    .Where(static invocation => GetGitSubcommand(invocation.Arguments) == "switch")
                    .Select(static invocation => invocation.Options.ExecutionKind),
                Is.EqualTo(
                [
                    GitCommandExecutionKind.LocalWithLfs,
                    GitCommandExecutionKind.LocalWithLfs,
                ]));
        });
    }

    [Test]
    public async Task CanCreateBranchAsync_accepts_a_valid_unused_local_name()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        using var service = CreateService();

        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                "new-feature",
                CancellationToken.None),
            CancellationToken.None);

        Assert.That(canCreate, Is.True);
    }

    [Test]
    public async Task CanCreateBranchAsync_rejects_an_invalid_name_without_mutation()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        string originalSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                "invalid..branch",
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        GitCommandResult currentSha = await RunGitAsync("rev-parse", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(currentSha.Stdout.Trim(), Is.EqualTo(originalSha));
        });
    }

    [Test]
    public async Task CanCreateBranchAsync_rejects_an_existing_local_name()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", "existing");
        using var service = CreateService();

        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                "existing",
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [TestCase("parent", "parent/child")]
    [TestCase("parent/child", "parent")]
    public async Task CanCreateBranchAsync_rejects_local_ref_namespace_collisions(
        string existingName,
        string candidateName)
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", existingName);
        using var service = CreateService();

        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                candidateName,
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [Test]
    public async Task CanCreateBranchAsync_detects_an_existing_branch_when_a_tag_has_the_same_name()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", "ambiguous");
        await RunGitAsync("tag", "ambiguous");
        using var service = CreateService();

        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                "ambiguous",
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [TestCase("CaseAlias", "casealias")]
    [TestCase("CaseParent", "caseparent/child")]
    [TestCase("CaseParent/child", "caseparent")]
    public async Task CanCreateBranchAsync_rejects_case_aliases_when_loose_refs_share_the_same_storage_path(
        string existingName,
        string candidateName)
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", existingName);
        string aliasedExistingPath = Path.Combine(
            Root,
            ".git",
            "refs",
            "heads",
            existingName.ToLowerInvariant().Replace('/', Path.DirectorySeparatorChar));
        if (!Path.Exists(aliasedExistingPath))
        {
            Assert.Ignore("The repository ref storage is case-sensitive for this branch name.");
        }

        using var service = CreateService();
        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                candidateName,
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [TestCase("PackedAlias", "packedalias")]
    [TestCase("PackedParent", "packedparent/child")]
    [TestCase("PackedParent/child", "packedparent")]
    public async Task CanCreateBranchAsync_rejects_case_aliases_against_packed_refs_on_case_insensitive_storage(
        string existingName,
        string candidateName)
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", existingName);
        await RunGitAsync("pack-refs", "--all", "--prune");
        string refsDirectory = Path.Combine(Root, ".git", "refs");
        if (!Directory.Exists(Path.Combine(Root, ".GIT")))
        {
            Assert.Ignore("The repository ref storage is case-sensitive.");
        }

        string looseExistingPath = Path.Combine(
            refsDirectory,
            "heads",
            existingName.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(looseExistingPath), Is.False);

        using var service = CreateService();
        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                candidateName,
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [Test]
    public async Task CanCreateBranchAsync_rejects_previous_branch_shorthand_normalization()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", "alternate");
        await RunGitAsync("switch", "alternate");
        await RunGitAsync("switch", "main");
        using var service = CreateService();

        bool canCreate = await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            transaction => transaction.CanCreateBranchAsync(
                "@{-1}",
                CancellationToken.None),
            CancellationToken.None);

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(canCreate, Is.False);
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [Test]
    public async Task SwitchBranchAsync_rejects_Git_previous_branch_shorthand_even_when_the_ref_exists()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("branch", "alternate");
        await RunGitAsync("switch", "alternate");
        await RunGitAsync("switch", "main");
        await RunGitAsync("update-ref", "refs/heads/-", "HEAD");
        using var service = CreateService();

        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(
            CancellationToken.None);

        Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SwitchBranchAsync("-", CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.SwitchBranchAsync("-", CancellationToken.None);
                    return true;
                },
                CancellationToken.None));

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(branches.Select(branch => branch.Name), Does.Contain("-"));
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateBranch_paths_reject_option_like_revision_before_mutation(
        bool useExclusiveTransaction)
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        string originalSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            if (useExclusiveTransaction)
            {
                await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                    async transaction =>
                    {
                        await transaction.CreateBranchAsync(
                            "injected-option",
                            "--discard-changes",
                            CancellationToken.None);
                        return true;
                    },
                    CancellationToken.None);
            }
            else
            {
                await service.CreateBranchAsync(
                    "injected-option",
                    "--discard-changes",
                    CancellationToken.None);
            }
        });

        GitCommandResult currentBranch = await RunGitAsync("branch", "--show-current");
        GitCommandResult injectedBranch = await RunGitAsync("branch", "--list", "injected-option");
        GitCommandResult currentSha = await RunGitAsync("rev-parse", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(currentBranch.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(injectedBranch.Stdout, Is.Empty);
            Assert.That(currentSha.Stdout.Trim(), Is.EqualTo(originalSha));
        });
    }

    [Test]
    public async Task Branches_can_be_listed_created_and_switched_after_diverging()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        string baseSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        await service.CreateBranchAsync("alternate", baseSha, CancellationToken.None);
        IReadOnlyList<BranchInfo> afterCreate = await service.GetBranchesAsync(
            CancellationToken.None);
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        string alternateSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        await service.SwitchBranchAsync("main", CancellationToken.None);
        await CommitFileAsync("project.bep", "main\n", "main");
        string mainSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        IReadOnlyList<BranchInfo> onMain = await service.GetBranchesAsync(
            CancellationToken.None);

        await service.SwitchBranchAsync("alternate", CancellationToken.None);
        string mergeBase = (await RunGitAsync(
            "merge-base",
            "main",
            "alternate")).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(afterCreate.Single(branch => branch.Name == "alternate").IsCurrent, Is.True);
            Assert.That(mergeBase, Is.EqualTo(baseSha));
            Assert.That(mainSha, Is.Not.EqualTo(alternateSha));
            Assert.That(onMain.Single(branch => branch.Name == "main").IsCurrent, Is.True);
            Assert.That(onMain.Single(branch => branch.Name == "alternate").IsCurrent, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("alternate\n"));
        });
    }

    [Test]
    public void Branch_parser_reads_current_and_upstream_fields()
    {
        IReadOnlyList<BranchInfo> branches = GitCliVersionControlService.ParseBranches(
            "alternate\0 \0\0\nmain\0*\0origin/main\n");

        Assert.That(
            branches,
            Is.EqualTo(new[]
            {
                new BranchInfo("alternate", false, null),
                new BranchInfo("main", true, "origin/main"),
            }));
    }

    [Test]
    public async Task Worktree_mutations_are_rejected_until_the_project_is_closed()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            isWorktreeMutationAllowed: static () => false);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.CommitProjectTreeAsync(
                    new CheckedOutBranchTip("refs/heads/main", head),
                    head,
                    "blocked restore",
                    SnapshotKind.Restore,
                    CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.CreateBranchAsync(
                    "blocked-branch",
                    head,
                    CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.SwitchBranchAsync(
                    "main",
                    CancellationToken.None));
        });

        GitCommandResult branch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(branch.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("current\n"));
        });
    }

    [Test]
    public void Porcelain_v2_parser_reads_branch_counts_renames_copies_and_conflicts()
    {
        string output = string.Join('\0',
        [
            "# branch.oid abcdef",
            "# branch.head feature/test",
            "# branch.ab +2 -3",
            "1 .M N... 100644 100644 100644 aaaaaaa bbbbbbb project file.bep",
            "2 R. N... 100644 100644 100644 aaaaaaa bbbbbbb R100 renamed.scene",
            "old.scene",
            "2 C. N... 100644 100644 100644 aaaaaaa bbbbbbb C100 copied.scene",
            "source.scene",
            "u UU N... 100644 100644 100644 100644 aaaaaaa bbbbbbb ccccccc conflict.belm",
            "? added.belm",
            "",
        ]);

        WorkspaceStatus status = GitCliVersionControlService.ParseStatus(output);

        Assert.Multiple(() =>
        {
            Assert.That(status.Branch, Is.EqualTo("feature/test"));
            Assert.That(status.Ahead, Is.EqualTo(2));
            Assert.That(status.Behind, Is.EqualTo(3));
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(status.Changes, Has.Count.EqualTo(5));
            Assert.That(status.Changes[0],
                Is.EqualTo(new FileChange("project file.bep", FileChangeStatus.Modified)));
            Assert.That(status.Changes[1],
                Is.EqualTo(new FileChange("renamed.scene", FileChangeStatus.Renamed, "old.scene")));
            Assert.That(status.Changes[2],
                Is.EqualTo(new FileChange("copied.scene", FileChangeStatus.Added)));
            Assert.That(status.Changes[4],
                Is.EqualTo(new FileChange("added.belm", FileChangeStatus.Added)));
        });
    }

    [Test]
    public void Name_status_parser_reports_a_copy_as_an_addition()
    {
        string output = string.Join('\0', ["C100", "source.scene", "copied.scene", ""]);

        IReadOnlyList<FileChange> files = GitCliVersionControlService.ParseCommitFiles(output);

        Assert.That(
            files,
            Is.EqualTo(new[] { new FileChange("copied.scene", FileChangeStatus.Added) }));
    }

    [Test]
    public async Task GetStatusAsync_reports_real_untracked_file()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}");
        using var service = CreateService();

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.Branch, Is.EqualTo("main"));
            Assert.That(status.IsClean, Is.False);
            Assert.That(status.HasConflicts, Is.False);
            Assert.That(status.Changes,
                Does.Contain(new FileChange("project.bep", FileChangeStatus.Added)));
        });
    }

    [Test]
    public async Task GetStatusAsync_reports_files_inside_untracked_directories()
    {
        string mediaPath = Path.Combine(Root, "resources", "nested", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllTextAsync(mediaPath, "media");
        using var service = CreateService();

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                status.Changes,
                Does.Contain(new FileChange(
                    "resources/nested/large.mp4",
                    FileChangeStatus.Added)));
            Assert.That(
                status.Changes.Select(change => change.Path),
                Does.Not.Contain("resources/"));
        });
    }

    [Test]
    public async Task GetStatusAsync_detects_real_unmerged_paths()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main\n", "main");

        Assert.ThrowsAsync<GitOperationException>(
            async () => await RunGitAsync("merge", "alternate"));
        using var service = CreateService();

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(status.Changes,
                Does.Contain(new FileChange("project.bep", FileChangeStatus.Modified)));
        });
    }

    [Test]
    public async Task CommitAllAsync_reports_conflict_before_deserializing_malformed_project_graph()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main\n", "main");
        Assert.ThrowsAsync<GitOperationException>(async () => await RunGitAsync("merge", "alternate"));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(), Repository, static () => true,
            projectFile: Path.Combine(Root, "project.bep"));
        Assert.ThrowsAsync<VersionControlConflictedException>(async () =>
            await service.CommitAllAsync("blocked", SnapshotKind.Manual, CancellationToken.None));
    }

    [Test]
    public async Task CommitAllAsync_conflict_started_during_graph_deserialization_wins_over_parse_failure()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main\n", "main");

        var runner = new MergeAfterFirstStatusRunner(
            CreateRunner(),
            async () =>
            {
                try
                {
                    await RunGitAsync("merge", "--no-commit", "--no-ff", "alternate");
                }
                catch (GitOperationException)
                {
                }
            });
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            projectFile: Path.Combine(Root, "project.bep"));

        VersionControlConflictedException exception =
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitAllAsync(
                    "blocked snapshot",
                    SnapshotKind.Manual,
                    CancellationToken.None))!;

        Assert.Multiple(() =>
        {
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(exception.Guidance, Is.EqualTo(Strings.VersionControl_ConflictGuidance));
        });
    }

    [Test]
    public async Task Conflicted_repository_keeps_reads_available_and_blocks_every_mutation()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main\n", "main");
        Assert.ThrowsAsync<GitOperationException>(
            async () => await RunGitAsync("merge", "alternate"));
        string ignorePath = Path.Combine(Root, ".gitignore");
        await File.WriteAllTextAsync(ignorePath, "conflicted custom ignore\n");
        using var service = CreateService();

        GitAvailability availability = await service.GetAvailabilityAsync(CancellationToken.None);
        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            Root,
            CancellationToken.None);
        GitCommandResult topLevel = await RunGitAsync("rev-parse", "--show-toplevel");
        string expectedRepoRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(topLevel.Stdout.Trim()));
        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        IReadOnlyList<CommitInfo> history = await service.GetHistoryAsync(
            0,
            10,
            CancellationToken.None);
        IReadOnlyList<FileChange> files = await service.GetCommitFilesAsync(
            history[0].Sha,
            CancellationToken.None);
        string diff = await service.GetDiffAsync(
            history[0].Sha,
            path: null,
            CancellationToken.None);
        GitIdentity? identity = await service.GetIdentityAsync(CancellationToken.None);
        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(
            CancellationToken.None);
        IReadOnlyList<RemoteInfo> remotes = await service.GetRemotesAsync(
            CancellationToken.None);
        CheckedOutBranchTip expectedTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        VersionControlConflictedException[] exceptions =
        [
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitAllAsync(
                    "blocked",
                    SnapshotKind.Manual,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitProjectTreeAsync(
                    expectedTip,
                    history[0].Sha,
                    "blocked restore",
                    SnapshotKind.Restore,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CreateBranchAsync(
                    "blocked-branch",
                    history[0].Sha,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.SwitchBranchAsync(
                    "alternate",
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.InitializeAsync(
                    new InitOptions(Repository, UseLfsWhenAvailable: false),
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.EnsureRepositoryHygieneAsync(
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.SetLocalIdentityAsync(
                    new GitIdentity("Blocked", "blocked@example.invalid"),
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.SetRemoteAsync(
                    "https://example.invalid/repository.git",
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.PushAsync(
                    progress: null,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.PullFastForwardAsync(
                    expectedTip,
                    checkpoint: null,
                    Path.Combine(Root, "project.bep"),
                    CancellationToken.None))!,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(availability.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.RepoRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(discovered.ProjectRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(discovered.Pathspec, Is.EqualTo("."));
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(history, Is.Not.Empty);
            Assert.That(files, Is.Not.Null);
            Assert.That(diff, Is.Not.Null);
            Assert.That(branches, Is.Not.Empty);
            Assert.That(remotes, Is.Empty);
            Assert.That(
                identity,
                Is.EqualTo(new GitIdentity(
                    "Beutl Test",
                    "beutl-test@example.invalid")));
            Assert.That(
                exceptions.Select(exception => exception.Guidance),
                Is.All.EqualTo(Strings.VersionControl_ConflictGuidance));
        });

        GitCommandResult unmerged = await RunGitAsync("ls-files", "-u");
        Assert.Multiple(() =>
        {
            Assert.That(unmerged.Stdout, Does.Contain("project.bep"));
            Assert.That(
                File.ReadAllText(ignorePath),
                Is.EqualTo("conflicted custom ignore\n"));
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
        });
    }

    [Test]
    public async Task Conflict_free_external_merge_blocks_snapshot_and_project_tree_commit()
    {
        await CommitFileAsync("project.bep", "base project\n", "base project");
        await CommitFileAsync("other.belm", "base other\n", "base other");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("other.belm", "alternate other\n", "alternate update");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main project\n", "main update");
        await RunGitAsync("merge", "--no-commit", "--no-ff", "alternate");
        using var service = CreateService();
        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        CheckedOutBranchTip expectedTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        string indexBefore = (await RunGitAsync("write-tree")).Stdout.Trim();
        string headBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        VersionControlConflictedException snapshotException =
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitAllAsync(
                    "blocked snapshot",
                    SnapshotKind.Save,
                    CancellationToken.None))!;
        VersionControlConflictedException treeCommitException =
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitProjectTreeAsync(
                    expectedTip,
                    expectedTip.Commit,
                    "blocked project tree commit",
                    SnapshotKind.Restore,
                    CancellationToken.None))!;

        string mergeHeadPath = (await RunGitAsync("rev-parse", "--git-path", "MERGE_HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        string headAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(status.HasConflicts, Is.False);
            Assert.That(snapshotException.Guidance, Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(treeCommitException.Guidance, Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(File.Exists(Path.GetFullPath(Path.Combine(Root, mergeHeadPath))), Is.True);
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(headAfter, Is.EqualTo(headBefore));
        });
    }

    [Test]
    public async Task Pull_rejects_a_clean_external_operation_before_close_and_transition()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        CheckedOutBranchTip expectedTip;
        bool closeGateChecked = false;
        using (var readService = CreateService())
        {
            expectedTip = await readService.GetCheckedOutBranchTipAsync(CancellationToken.None);
        }

        string mergeHeadRecord = (await RunGitAsync("rev-parse", "--git-path", "MERGE_HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        string mergeHeadPath = Path.GetFullPath(
            Path.IsPathFullyQualified(mergeHeadRecord)
                ? mergeHeadRecord
                : Path.Combine(Root, mergeHeadRecord));
        await File.WriteAllTextAsync(mergeHeadPath, $"{expectedTip.Commit}\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            () =>
            {
                closeGateChecked = true;
                return false;
            });

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        VersionControlConflictedException preflightException =
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.PreflightPullAsync(
                    expectedTip,
                    CancellationToken.None))!;
        VersionControlConflictedException transitionException =
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.PullFastForwardAsync(
                    expectedTip,
                    checkpoint: null,
                    Path.Combine(Root, "project.bep"),
                    CancellationToken.None))!;

        Assert.Multiple(() =>
        {
            Assert.That(status.IsClean, Is.True);
            Assert.That(
                preflightException.Guidance,
                Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(
                transitionException.Guidance,
                Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(closeGateChecked, Is.False);
            Assert.That(File.Exists(mergeHeadPath), Is.True);
        });
    }

    [Test]
    public async Task Tree_transition_rechecks_external_operations_inside_the_HEAD_lease()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        await RunGitAsync("switch", "-c", "incoming");
        await CommitFileAsync("project.bep", "incoming\n", "incoming");
        string sourceCommit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("switch", "main");
        string mergeHeadRecord = (await RunGitAsync("rev-parse", "--git-path", "MERGE_HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        string mergeHeadPath = Path.GetFullPath(
            Path.IsPathFullyQualified(mergeHeadRecord)
                ? mergeHeadRecord
                : Path.Combine(Root, mergeHeadRecord));
        var runner = new AfterTransitionWorktreeAddRunner(
            CreateRunner(),
            () => File.WriteAllText(mergeHeadPath, $"{sourceCommit}\n"));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        CheckedOutBranchTip expectedTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitProjectTreeAsync(
                expectedTip,
                sourceCommit,
                "blocked transition",
                SnapshotKind.Restore,
                CancellationToken.None))!;

        string actualHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerException, Is.TypeOf<VersionControlConflictedException>());
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(actualHead, Is.EqualTo(expectedTip.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("current\n"));
            Assert.That(File.Exists(mergeHeadPath), Is.True);
        });
    }

    [Test]
    public async Task Tree_transition_does_not_rollback_over_an_external_operation_started_after_checkout()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        ProjectCheckpoint checkpoint;
        CheckedOutBranchTip baseTip;
        using (var checkpointService = CreateService())
        {
            baseTip = await checkpointService.GetCheckedOutBranchTipAsync(
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(Root, "project.bep"),
                "checkpointed\n");
            checkpoint = await checkpointService.CreateProjectCheckpointAsync(
                "beutl: external operation rollback checkpoint",
                CancellationToken.None);
        }

        await RunGitAsync(
            "restore",
            "--source=HEAD",
            "--worktree",
            "--",
            "project.bep");
        string mergeHeadRecord = (await RunGitAsync("rev-parse", "--git-path", "MERGE_HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        string mergeHeadPath = Path.GetFullPath(
            Path.IsPathFullyQualified(mergeHeadRecord)
                ? mergeHeadRecord
                : Path.Combine(Root, mergeHeadRecord));
        var runner = new AfterTransitionCheckoutRunner(
            CreateRunner(),
            () => File.WriteAllText(mergeHeadPath, $"{checkpoint.Commit}\n"));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.RestoreProjectCheckpointAsync(
                checkpoint,
                CancellationToken.None))!;

        string actualHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string actualIndexTree = (await RunGitAsync("write-tree")).Stdout.Trim();
        string checkpointTree = (await RunGitAsync("rev-parse", $"{checkpoint.Commit}^{{tree}}"))
            .Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerException, Is.TypeOf<VersionControlConflictedException>());
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(actualHead, Is.EqualTo(baseTip.Commit));
            Assert.That(actualIndexTree, Is.EqualTo(checkpointTree));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("checkpointed\n"));
            Assert.That(File.Exists(mergeHeadPath), Is.True);
        });
    }

    [Test]
    public async Task Project_checkpoint_excludes_modified_tracked_local_state()
    {
        string profilePath = Path.Combine(Root, ".beutl", "output-profile.json");
        string temporaryPath = Path.Combine(Root, "render.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "baseline project\n");
        await File.WriteAllTextAsync(profilePath, "baseline profile\n");
        await File.WriteAllTextAsync(temporaryPath, "baseline temporary\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpoint project\n");
        await File.WriteAllTextAsync(profilePath, "local profile\n");
        await File.WriteAllTextAsync(temporaryPath, "local temporary\n");
        using var service = CreateService();

        ProjectCheckpoint checkpoint = await service.CreateProjectCheckpointAsync(
            "beutl: filtered checkpoint",
            CancellationToken.None);
        string checkpointProject = (await RunGitAsync(
            "show",
            $"{checkpoint.Commit}:project.bep")).Stdout;
        string checkpointProfile = (await RunGitAsync(
            "show",
            $"{checkpoint.Commit}:.beutl/output-profile.json")).Stdout;
        string checkpointTemporary = (await RunGitAsync(
            "show",
            $"{checkpoint.Commit}:render.tmp")).Stdout;

        Assert.Multiple(() =>
        {
            Assert.That(checkpointProject, Is.EqualTo("checkpoint project\n"));
            Assert.That(checkpointProfile, Is.EqualTo("baseline profile\n"));
            Assert.That(checkpointTemporary, Is.EqualTo("baseline temporary\n"));
            Assert.That(File.ReadAllText(profilePath), Is.EqualTo("local profile\n"));
            Assert.That(File.ReadAllText(temporaryPath), Is.EqualTo("local temporary\n"));
        });
    }

    [Test]
    public async Task Snapshot_and_checkpoint_stage_with_the_lfs_aware_execution_kind()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "checkpoint\n");
        var runner = new RecordingArgumentsRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        await service.CreateProjectCheckpointAsync(
            "beutl: lfs-aware checkpoint",
            CancellationToken.None);
        await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        // Located anywhere in the argument list: the snapshot path prefixes its staging command
        // with -c advice.addIgnoredFile=false, so "add" is not always the first argument.
        static bool StagesEverything(IReadOnlyList<string> arguments)
        {
            for (int i = 0; i + 1 < arguments.Count; i++)
            {
                if (arguments[i] == "add" && arguments[i + 1] == "-A")
                {
                    return true;
                }
            }

            return false;
        }

        GitCommandExecutionKind[] executionKinds = runner.Invocations
            .Where(invocation => StagesEverything(invocation.Arguments))
            .Select(static invocation => invocation.Options.ExecutionKind)
            .ToArray();
        Assert.That(
            executionKinds,
            Is.EqualTo(new[]
            {
                GitCommandExecutionKind.LocalWithLfs,
                GitCommandExecutionKind.LocalWithLfs,
            }));
    }

    [Test]
    public async Task Branch_tip_rollback_reports_unsafe_when_an_external_operation_starts_after_checkout()
    {
        await CommitFileAsync("project.bep", "base\n", "initial");
        CheckedOutBranchTip targetTip;
        using (var readService = CreateService())
        {
            targetTip = await readService.GetCheckedOutBranchTipAsync(CancellationToken.None);
        }

        await CommitFileAsync("project.bep", "current\n", "current");
        CheckedOutBranchTip expectedTip;
        using (var readService = CreateService())
        {
            expectedTip = await readService.GetCheckedOutBranchTipAsync(CancellationToken.None);
        }

        string mergeHeadRecord = (await RunGitAsync("rev-parse", "--git-path", "MERGE_HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        string mergeHeadPath = Path.GetFullPath(
            Path.IsPathFullyQualified(mergeHeadRecord)
                ? mergeHeadRecord
                : Path.Combine(Root, mergeHeadRecord));
        var runner = new AfterTransitionCheckoutRunner(
            CreateRunner(),
            () => File.WriteAllText(mergeHeadPath, $"{targetTip.Commit}\n"));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        BranchTipRollbackResult result = await service.TryRollbackBranchTipAsync(
            expectedTip,
            targetTip,
            CancellationToken.None);

        string actualHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<BranchTipRollbackResult.UnsafeRepositoryState>());
            Assert.That(runner.InterceptionCount, Is.EqualTo(1));
            Assert.That(actualHead, Is.EqualTo(expectedTip.Commit));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("base\n"));
            Assert.That(File.Exists(mergeHeadPath), Is.True);
        });
    }

    [Test]
    public async Task CommitAllAsync_rechecks_an_external_merge_after_the_policy_notice()
    {
        await CommitFileAsync("project.bep", "base project\n", "base project");
        await CommitFileAsync("other.belm", "base other\n", "base other");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("other.belm", "alternate other\n", "alternate update");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main project\n", "main update");
        string mediaPath = Path.Combine(Root, "resources", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllTextAsync(mediaPath, "media\n");
        string? mergeIndex = null;
        int notices = 0;
        var config = new VersionControlConfig { LargeMediaWarningThresholdMb = 0 };
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(config: config),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            policyNoticeSink: async (_, _) =>
            {
                Interlocked.Increment(ref notices);
                await RunGitAsync("merge", "--no-commit", "--no-ff", "alternate");
                mergeIndex = (await RunGitAsync("write-tree")).Stdout.Trim();
            });
        string headBefore = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        VersionControlConflictedException exception =
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitAllAsync(
                    "blocked snapshot",
                    SnapshotKind.Save,
                    CancellationToken.None))!;

        string headAfter = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string indexAfter = (await RunGitAsync("write-tree")).Stdout.Trim();
        string mergeHeadPath = (await RunGitAsync("rev-parse", "--git-path", "MERGE_HEAD"))
            .Stdout.TrimEnd('\r', '\n');
        Assert.Multiple(() =>
        {
            Assert.That(notices, Is.EqualTo(1));
            Assert.That(exception.Guidance, Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(indexAfter, Is.EqualTo(mergeIndex));
            Assert.That(headAfter, Is.EqualTo(headBefore));
            Assert.That(File.Exists(Path.GetFullPath(Path.Combine(Root, mergeHeadPath))), Is.True);
        });
    }

    [Test]
    public async Task Concurrent_status_calls_are_serialized()
    {
        var runner = new ConcurrencyTrackingRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> first = service.GetStatusAsync(CancellationToken.None);
        Task<WorkspaceStatus> second = service.GetStatusAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.That(runner.MaxConcurrency, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_allows_an_in_flight_operation_to_release_the_gate()
    {
        var runner = new BlockingStatusRunner();
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> operation = service.GetStatusAsync(CancellationToken.None);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        runner.Complete();

        Assert.DoesNotThrowAsync(async () => await operation);
        Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await service.GetStatusAsync(CancellationToken.None));
    }

    [Test]
    public async Task Retirement_waits_for_started_operation_and_rejects_queued_and_new_calls()
    {
        var runner = new BlockingStatusRunner();
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> started = service.GetStatusAsync(CancellationToken.None);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
        Task<WorkspaceStatus> queued = service.GetStatusAsync(CancellationToken.None);
        Task retirement = ((IProjectVersionControlBackend)service).RetireAsync(
            finalSnapshot: null);

        Assert.Multiple(() =>
        {
            Assert.That(started.IsCompleted, Is.False);
            Assert.That(retirement.IsCompleted, Is.False);
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await service.GetStatusAsync(CancellationToken.None));
        });

        runner.Complete();
        Assert.DoesNotThrowAsync(async () => await started);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await queued);
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Retirement_creates_final_snapshot_after_started_raw_operation()
    {
        var runner = new BlockingFirstStatusRunner(CreateRunner());
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> operation = service.GetStatusAsync(CancellationToken.None);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "final state\n");
        Task retirement = ((IProjectVersionControlBackend)service).RetireAsync(
            new ProjectVersionControlFinalSnapshot(
                "beutl: snapshot on close",
                SnapshotKind.Close));

        runner.Complete();
        await operation;
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        GitCommandResult log = await RunGitAsync(
            "log",
            "-1",
            "--pretty=%s%n%(trailers:key=Beutl-Snapshot,valueonly)");

        Assert.Multiple(() =>
        {
            Assert.That(log.Stdout, Does.Contain("beutl: snapshot on close"));
            Assert.That(log.Stdout, Does.Contain("close"));
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await service.GetStatusAsync(CancellationToken.None));
        });
    }

    [Test]
    public async Task Retirement_requested_during_initialization_commits_the_final_close_state()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "initial state\n");
        var repository = new RepositoryInfo(projectRoot, projectRoot);
        GitCliRunner commandRunner = CreateRunner();
        var runner = new BlockingInitializationRunner(commandRunner);
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        try
        {
            Task initialization = service.InitializeAsync(
                new InitOptions(
                    repository,
                    UseLfsWhenAvailable: false)
                {
                    Identity = new GitIdentity(
                        "Initialization Test",
                        "initialization@example.invalid"),
                },
                CancellationToken.None);
            await runner.RepositoryInitialized.WaitAsync(TimeSpan.FromSeconds(5));

            Task retirement = ((IProjectVersionControlBackend)service).RetireAsync(
                new ProjectVersionControlFinalSnapshot(
                    "beutl: snapshot on close",
                    SnapshotKind.Close));
            Assert.Multiple(() =>
            {
                Assert.That(service.Repository, Is.EqualTo(repository));
                Assert.That(retirement.IsCompleted, Is.False);
            });

            runner.ContinueAfterRepositoryInitialization();
            await runner.InitialCommitCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "project.bep"),
                "final close state\n");
            runner.ContinueAfterInitialCommit();
            await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            await retirement.WaitAsync(TimeSpan.FromSeconds(5));

            GitCommandResult log = await commandRunner.RunAsync(
                repository,
                ["log", "-2", "--pretty=%s%n%(trailers:key=Beutl-Snapshot,valueonly)"],
                GitCommandOptions.Local,
                CancellationToken.None);
            GitCommandResult contents = await commandRunner.RunAsync(
                repository,
                ["show", "HEAD:project.bep"],
                GitCommandOptions.Local,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(log.Stdout, Does.Contain("beutl: snapshot on close"));
                Assert.That(log.Stdout, Does.Contain("beutl: initialize version control"));
                Assert.That(log.Stdout, Does.Contain("close"));
                Assert.That(log.Stdout, Does.Contain("init"));
                Assert.That(contents.Stdout, Is.EqualTo("final close state\n"));
            });
        }
        finally
        {
            runner.ContinueAfterRepositoryInitialization();
            runner.ContinueAfterInitialCommit();
            service.Dispose();
        }
    }

    [Test]
    public void Dispose_is_safe_when_called_concurrently()
    {
        GitCliVersionControlService service = CreateService();

        Assert.DoesNotThrow(() => Parallel.For(0, 64, _ => service.Dispose()));
        Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await service.GetStatusAsync(CancellationToken.None));
    }

    [Test]
    public async Task Git_runtime_is_reused_until_the_executable_config_changes()
    {
        string firstPath = Path.GetFullPath(Path.Combine(Root, "git-one"));
        string secondPath = Path.GetFullPath(Path.Combine(Root, "git-two"));
        var config = new VersionControlConfig { GitExecutablePath = firstPath };
        var probe = new RuntimeProbe();
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
        int runnerCreations = 0;
        using var service = new GitCliVersionControlService(
            locator,
            Repository,
            watcher: null,
            _ =>
            {
                runnerCreations++;
                return new StaticStatusRunner();
            });

        await service.GetStatusAsync(CancellationToken.None);
        await service.GetStatusAsync(CancellationToken.None);
        config.GitExecutablePath = secondPath;
        await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runnerCreations, Is.EqualTo(2));
            Assert.That(probe.VersionProbeCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Watcher_refresh_raises_StatusChanged_on_background_thread()
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Repository, timeProvider, startWatching: false);
        using var service = CreateService(watcher);
        int callerThread = Environment.CurrentManagedThreadId;
        var completion = new TaskCompletionSource<(WorkspaceStatus Status, int ThreadId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, status) =>
            completion.TrySetResult((status, Environment.CurrentManagedThreadId));
        await File.WriteAllTextAsync(Path.Combine(Root, "changed.bep"), "{}");

        watcher.NotifyPathChanged(Path.Combine(Root, "changed.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        (WorkspaceStatus status, int eventThread) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(eventThread, Is.Not.EqualTo(callerThread));
            Assert.That(status.Changes,
                Does.Contain(new FileChange("changed.bep", FileChangeStatus.Added)));
        });
    }

    [Test]
    public async Task StatusChanged_notifications_are_published_in_capture_order()
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Repository, timeProvider, startWatching: false);
        Action? drain = null;
        int scheduleCount = 0;
        var drainScheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner(),
            statusNotificationScheduler: action =>
            {
                scheduleCount++;
                drain = action;
                drainScheduled.TrySetResult();
            });
        var statuses = new List<WorkspaceStatus>();
        service.StatusChanged += (_, status) => statuses.Add(status);
        await File.WriteAllTextAsync(Path.Combine(Root, "changed.bep"), "{}\n");

        watcher.NotifyPathChanged(Path.Combine(Root, "changed.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        await drainScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CommitResult result = await service.CommitAllAsync(
            "beutl: ordered status notifications",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.That(statuses, Is.Empty);
        drain!();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(scheduleCount, Is.EqualTo(1));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(
                statuses[0].Changes,
                Does.Contain(new FileChange("changed.bep", FileChangeStatus.Added)));
            Assert.That(statuses[1].IsClean, Is.True);
        });
    }

    [Test]
    public async Task Watcher_snapshot_is_enqueued_before_the_operation_gate_is_released()
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Repository, timeProvider, startWatching: false);
        var schedulerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScheduler = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action? drain = null;
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner(),
            statusNotificationScheduler: action =>
            {
                drain = action;
                schedulerEntered.TrySetResult();
                releaseScheduler.Task.GetAwaiter().GetResult();
            });
        await File.WriteAllTextAsync(Path.Combine(Root, "changed.bep"), "{}\n");

        watcher.NotifyPathChanged(Path.Combine(Root, "changed.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        await schedulerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<CommitResult> commit = service.CommitAllAsync(
            "beutl: preserve watcher snapshot order",
            SnapshotKind.Save,
            CancellationToken.None);
        await Task.Delay(100);

        Assert.That(commit.IsCompleted, Is.False);
        releaseScheduler.TrySetResult();
        Assert.That(
            await commit.WaitAsync(TimeSpan.FromSeconds(5)),
            Is.TypeOf<CommitResult.Committed>());
        Action drainNotifications = drain
                                    ?? throw new InvalidOperationException(
                                        "The status drain was not scheduled.");
        drainNotifications();
    }

    [Test]
    public async Task Watcher_refresh_logs_unexpected_failures()
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Repository, timeProvider, startWatching: false);
        var logger = new RecordingLogger();
        var expected = new IOException("status failed");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => new ThrowingStatusRunner(expected),
            logger);

        watcher.NotifyPathChanged(Path.Combine(Root, "changed.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(expected));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to refresh version-control status after a repository change."));
        });
    }

    [Test]
    public async Task Durable_mutations_succeed_when_status_refresh_fails()
    {
        string remoteRoot = CreateTemporaryDirectory();
        var remoteRepository = new RepositoryInfo(remoteRoot, remoteRoot);
        await CreateRunner().RunAsync(
            remoteRepository,
            ["init", "--bare", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var runner = new FailingPostMutationStatusRunner(CreateRunner());
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);

        CommitResult commit = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        string committedSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await service.CreateBranchAsync("alternate", committedSha, CancellationToken.None);
        await service.SwitchBranchAsync("main", CancellationToken.None);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        RemoteOpResult push = await service.PushAsync(progress: null, CancellationToken.None);

        string localHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHead = (await CreateRunner().RunAsync(
            remoteRepository,
            ["rev-parse", "refs/heads/main"],
            GitCommandOptions.Local,
            CancellationToken.None)).Stdout.Trim();
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(commit, Is.TypeOf<CommitResult.Committed>());
            Assert.That(push, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(remoteHead, Is.EqualTo(localHead));
            Assert.That(runner.StatusFailureCount, Is.EqualTo(5));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.StatusFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to publish version-control status after a durable Git operation."));
        });
    }

    [Test]
    public async Task Pull_treats_a_local_ahead_branch_as_success_without_a_transition()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string remoteRoot = CreateTemporaryDirectory();
        var remoteRepository = new RepositoryInfo(remoteRoot, remoteRoot);
        await CreateRunner().RunAsync(
            remoteRepository,
            ["init", "--bare", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        using var service = CreateService();
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        Assert.That(
            await service.PushAsync(progress: null, CancellationToken.None),
            Is.TypeOf<RemoteOpResult.Success>());
        await CommitFileAsync("local.belm", "local only\n", "local update");
        CheckedOutBranchTip expectedTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        PullPreflightResult preflight = await service.PreflightPullAsync(
            expectedTip,
            CancellationToken.None);
        FastForwardPullResult pull = await service.PullFastForwardAsync(
            expectedTip,
            checkpoint: null,
            Path.Combine(Root, "project.bep"),
            CancellationToken.None);
        CheckedOutBranchTip actualTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(preflight.RequiresTransition, Is.False);
            Assert.That(pull.Result, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(pull.TransitionState, Is.EqualTo(PullTransitionState.Unchanged));
            Assert.That(pull.Tip, Is.EqualTo(expectedTip));
            Assert.That(actualTip, Is.EqualTo(expectedTip));
        });
    }

    [Test]
    public async Task CommitAllAsync_returns_the_created_commit_without_a_post_publish_HEAD_lookup()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var runner = new FailingPostCommitRevisionRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        GitCommandResult commitCount = await RunGitAsync("rev-list", "--count", "HEAD");
        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                ((CommitResult.Committed)result).Revision,
                Is.EqualTo(new CommitRevision.Known(head)));
            Assert.That(commitCount.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(runner.RevisionFailureCount, Is.Zero);
        });
    }

    [Test]
    public async Task InitializeAsync_succeeds_when_status_refresh_fails_after_initial_commit()
    {
        string projectRoot = CreateTemporaryDirectory();
        var projectRepository = new RepositoryInfo(projectRoot, projectRoot);
        GitCliRunner setupRunner = CreateRunner();
        await setupRunner.RunAsync(
            projectRepository,
            ["init", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await setupRunner.RunAsync(
            projectRepository,
            ["config", "user.name", "Beutl Test"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await setupRunner.RunAsync(
            projectRepository,
            ["config", "user.email", "beutl-test@example.invalid"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "initial\n");
        var runner = new FailingPostMutationStatusRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(projectRepository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult commitCount = await setupRunner.RunAsync(
            projectRepository,
            ["rev-list", "--count", "HEAD"],
            GitCommandOptions.Local,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Not.Null);
            Assert.That(
                RepositoryPathComparer.AreEquivalent(
                    service.Repository!.ProjectRoot,
                    projectRepository.ProjectRoot),
                Is.True);
            Assert.That(service.Repository.Pathspec, Is.EqualTo(projectRepository.Pathspec));
            Assert.That(commitCount.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(runner.StatusFailureCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StatusChanged_subscriber_failure_is_isolated_and_logged()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            logger);
        var expected = new InvalidOperationException("subscriber failed");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, _) => throw expected;
        service.StatusChanged += (_, _) => notified.TrySetResult();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(expected));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to notify a version-control status subscriber."));
        });
    }

    [Test]
    public async Task SetRemoteAsync_succeeds_when_post_mutation_policy_check_fails()
    {
        const string remoteUrl = "https://example.invalid/repository.git";
        var runner = new FailingPostRemoteAuxiliaryRunner(CreateRunner());
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);

        await service.SetRemoteAsync(remoteUrl, CancellationToken.None);

        string configuredRemote = (await RunGitAsync("remote", "get-url", "origin")).Stdout.Trim();
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(configuredRemote, Is.EqualTo(remoteUrl));
            // Only that the probe actually failed: setting a remote refreshes the LFS quota notice
            // and the status independently, and how many of those read the remote is not this
            // test's subject.
            Assert.That(runner.AuxiliaryFailureCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.AuxiliaryFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to publish the Git LFS quota notice after configuring the remote."));
        });
    }

    [Test]
    public async Task SetRemoteAsync_preserves_fetch_and_push_urls_when_staged_update_fails()
    {
        const string oldFetchUrl = "https://example.invalid/old-fetch.git";
        const string oldPushUrl = "https://example.invalid/old-push.git";
        const string newUrl = "https://example.invalid/new.git";
        await RunGitAsync("remote", "add", "origin", oldFetchUrl);
        await RunGitAsync("config", "--local", "--replace-all", "remote.origin.pushurl", oldPushUrl);
        var runner = new FailingStagedRemoteConfigRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<IOException>(async () =>
            await service.SetRemoteAsync(newUrl, CancellationToken.None));

        string fetchUrl = (await RunGitAsync("remote", "get-url", "origin")).Stdout.Trim();
        string pushUrl = (await RunGitAsync("remote", "get-url", "--push", "origin")).Stdout.Trim();
        Assert.Multiple(() =>
        {
            Assert.That(fetchUrl, Is.EqualTo(oldFetchUrl));
            Assert.That(pushUrl, Is.EqualTo(oldPushUrl));
            Assert.That(runner.StagedFailureCount, Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(Root, ".git", "config.lock")), Is.False);
        });
    }

    [TestCase("https://user:secret@example.invalid/repository.git")]
    [TestCase("http://user:secret@example.invalid/repository.git")]
    [TestCase("https://user@example.invalid/repository.git")]
    [TestCase("http://user@example.invalid/repository.git")]
    [TestCase("ftp://user:secret@example.invalid/repository.git")]
    [TestCase("ftp://user@example.invalid/repository.git")]
    [TestCase("ssh://git:secret@example.invalid/repository.git")]
    public async Task SetRemoteAsync_rejects_disallowed_remote_userinfo(string remoteUrl)
    {
        using var service = CreateService();

        ArgumentException? exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SetRemoteAsync(remoteUrl, CancellationToken.None));
        IReadOnlyList<RemoteInfo> remotes = await service.GetRemotesAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("credential helper"));
            Assert.That(exception.Message, Does.Not.Contain("secret"));
            Assert.That(remotes, Is.Empty);
        });
    }

    [TestCase("https://example.invalid/repository.git?access_token=secret")]
    [TestCase("https://example.invalid/repository.git#access_token=secret")]
    [TestCase("ssh://git@example.invalid/repository.git?access_token=secret")]
    public async Task SetRemoteAsync_rejects_remote_query_or_fragment(string remoteUrl)
    {
        using var service = CreateService();

        ArgumentException? exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SetRemoteAsync(remoteUrl, CancellationToken.None));
        IReadOnlyList<RemoteInfo> remotes = await service.GetRemotesAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("credential helper"));
            Assert.That(exception.Message, Does.Not.Contain("secret"));
            Assert.That(remotes, Is.Empty);
        });
    }

    [Test]
    public async Task SetRemoteAsync_allows_ssh_usernames()
    {
        const string remoteUrl = "ssh://git@example.invalid/repository.git";
        using var service = CreateService();

        await service.SetRemoteAsync(remoteUrl, CancellationToken.None);

        Assert.That(
            await service.GetRemotesAsync(CancellationToken.None),
            Is.EqualTo(new[] { new RemoteInfo("origin", remoteUrl) }));
    }

    [Test]
    public async Task RecoverableLockAvailable_subscriber_failure_is_isolated_and_logged()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new RemoteLockFailureRunner(CreateRunner(), expectedLock);
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);
        var expected = new InvalidOperationException("subscriber failed");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, _) => throw expected;
        service.RecoverableLockAvailable += (_, _) => notified.TrySetResult();

        RemoteOpResult result = await service.PushAsync(
            progress: null,
            CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(service.RecoverableLock, Is.EqualTo(expectedLock));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(expected));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to notify a recoverable repository-lock subscriber."));
        });
    }

    [Test]
    public async Task Superseded_equal_recoverable_lock_notification_is_not_published()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        string lockPath = Path.Combine(Root, ".git", "index.lock");
        DateTimeOffset timestamp =
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1);
        var firstOffer = new RepositoryLockInfo(lockPath, timestamp);
        var replacementOffer = new RepositoryLockInfo(lockPath, timestamp);
        var runner = new RemoteLockFailureRunner(CreateRunner(), firstOffer);
        var scheduled = new Queue<Action>();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            lockNotificationScheduler: scheduled.Enqueue);
        var notified = new List<RepositoryLockInfo>();
        service.RecoverableLockAvailable += (_, lockInfo) => notified.Add(lockInfo);

        await service.PushAsync(progress: null, CancellationToken.None);
        runner.LockInfo = replacementOffer;
        await service.PushAsync(progress: null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(scheduled, Has.Count.EqualTo(2));
            Assert.That(service.RecoverableLock, Is.SameAs(replacementOffer));
        });

        scheduled.Dequeue()();
        Assert.That(notified, Is.Empty);

        scheduled.Dequeue()();
        Assert.That(notified, Is.EqualTo(new[] { replacementOffer }));
    }

    [Test]
    public async Task Worktree_transitions_clear_the_repository_lfs_path_filters()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        string baseSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "later\n", "later");
        var recording = new ArgumentRecordingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => recording);

        CheckedOutBranchTip tip = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
        await service.CommitProjectTreeAsync(
            tip,
            baseSha,
            "beutl: restore base",
            SnapshotKind.Restore,
            CancellationToken.None);
        await service.CreateBranchAsync("alternate", baseSha, CancellationToken.None);
        await service.SwitchBranchAsync("alternate", CancellationToken.None);

        string[][] worktreeCommands = recording.Commands
            .Where(static arguments =>
                arguments.Contains("checkout") || arguments.Contains("switch"))
            .ToArray();

        Assert.That(worktreeCommands, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (string[] arguments in worktreeCommands)
            {
                // git-lfs copies a pointer excluded by these filters through unchanged, so a
                // transition that inherited them would leave pointer text where the media belongs.
                Assert.That(arguments, Does.Contain("lfs.fetchinclude="), string.Join(' ', arguments));
                Assert.That(arguments, Does.Contain("lfs.fetchexclude="), string.Join(' ', arguments));
                // Overwriting ignored files is Git's default; in an enclosing repository that would
                // silently destroy files the project never tracked.
                Assert.That(
                    arguments,
                    Does.Contain("--no-overwrite-ignore"),
                    string.Join(' ', arguments));
            }
        });
    }

    [Test]
    public async Task Branch_switches_refuse_to_overwrite_an_ignored_file()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        string baseSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("tracked-later.txt", "tracked\n", "add a file the other branch lacks");
        using var service = CreateService();
        await service.CreateBranchAsync("alternate", baseSha, CancellationToken.None);
        await service.SwitchBranchAsync("main", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "collides.txt\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore the collision path");
        await RunGitAsync("switch", "alternate");
        await File.WriteAllTextAsync(Path.Combine(Root, "collides.txt"), "tracked on main\n");
        await RunGitAsync("add", "--", "collides.txt");
        await RunGitAsync("commit", "-m", "track the collision path on the other branch");
        await RunGitAsync("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(Root, "collides.txt"), "local ignored content\n");

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.SwitchBranchAsync("alternate", CancellationToken.None));

        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(Root, "collides.txt")),
            Is.EqualTo("local ignored content\n"));
    }

    [Test]
    public async Task Lfs_prefetch_without_an_installed_filter_rejects_a_target_branch_pointer()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        await RunGitAsync("add", ".gitattributes");
        await RunGitAsync("commit", "-m", "lfs attributes");
        await RunGitAsync("switch", "-c", "target-lfs");
        Directory.CreateDirectory(Path.Combine(Root, "media"));
        byte[] media = Encoding.UTF8.GetBytes("target media\n");
        await File.WriteAllTextAsync(
            Path.Combine(Root, "media", "clip.mp4"),
            CreateNoncanonicalLfsPointer(media).PadRight(2048));
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "target lfs pointer");
        await RunGitAsync("switch", "main");
        await RunGitAsync("config", "diff.lfs.binary", "true");
        var recording = new ArgumentRecordingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false),
            Repository,
            watcher: null,
            _ => recording);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchBranchLfsObjectsAsync(
                        "target-lfs",
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Git LFS is not installed"));
            Assert.That((RunGitAsync("branch", "--show-current").GetAwaiter().GetResult())
                .Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(recording.Commands.Any(static command => command.Contains("lfs")),
                Is.False);
        });
    }

    [Test]
    public async Task Lfs_prefetch_without_an_installed_filter_honors_the_project_scope()
    {
        const string ProjectPathspec = "projects/edit";
        string projectRoot = Path.Combine(
            Root,
            ProjectPathspec.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        byte[] media = Encoding.UTF8.GetBytes("scoped media\n");
        await File.WriteAllTextAsync(
            Path.Combine(Root, "outside.mp4"),
            CreateLfsPointer(media));
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "outside lfs pointer");
        string outsideOnly = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false),
            new RepositoryInfo(Root, projectRoot),
            watcher: null,
            _ => CreateRunner());

        Assert.DoesNotThrowAsync(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        outsideOnly,
                        LfsPrefetchScope.ProjectPathspec,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        outsideOnly,
                        LfsPrefetchScope.RepositoryWide,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));

        Directory.CreateDirectory(Path.Combine(projectRoot, "media"));
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "media", "clip.mp4"),
            CreateLfsPointer(media));
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "project lfs pointer");
        string projectPointer = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        projectPointer,
                        LfsPrefetchScope.ProjectPathspec,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));
    }

    [Test]
    public async Task Lfs_prefetch_without_an_installed_filter_ignores_a_malformed_extension()
    {
        byte[] contents = Encoding.UTF8.GetBytes("not pointer data\n");
        string malformed = $"version https://git-lfs.github.com/spec/v1\n"
                           + $"oid sha256:{ComputeLfsOid(contents)}\n"
                           + "ext-note documentation\n"
                           + $"size {contents.Length}\n";
        await CommitFileAsync(
            "notes.txt",
            malformed.PadRight(2048),
            "pointer documentation");
        string commit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: false),
            Repository,
            watcher: null,
            _ => CreateRunner());

        Assert.DoesNotThrowAsync(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        commit,
                        LfsPrefetchScope.RepositoryWide,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));
    }

    [Test]
    public async Task Repository_wide_lfs_prefetch_clears_the_repository_lfs_path_filters()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        Directory.CreateDirectory(Path.Combine(Root, "media"));
        await File.WriteAllTextAsync(Path.Combine(Root, "media", "clip.mp4"), "media\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "media");
        string commit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        // A local path remote keeps the prefetch off the network; whether git-lfs is installed only
        // decides if the command fails, and the prefetch swallows that by design.
        await RunGitAsync("remote", "add", "origin", CreateTemporaryDirectory());
        var recording = new ArgumentRecordingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => recording);

        await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchBranchLfsObjectsAsync("main", CancellationToken.None);
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.RepositoryWide,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None);

        string[][] prefetches = recording.Commands
            .Where(static arguments =>
                arguments.Contains("lfs") && arguments.Contains("fetch"))
            .ToArray();
        Assert.That(prefetches, Has.Length.EqualTo(2), "The prefetch did not reach git-lfs.");
        Assert.Multiple(() =>
        {
            foreach (string[] prefetch in prefetches)
            {
                Assert.That(prefetch, Does.Contain("lfs.fetchinclude="));
                Assert.That(prefetch, Does.Contain("lfs.fetchexclude="));
                Assert.That(prefetch, Does.Contain("lfs.fetchrecentalways=false"));
                Assert.That(
                    prefetch.Any(static argument => argument.StartsWith("--include=", StringComparison.Ordinal)),
                    Is.False,
                    string.Join(' ', prefetch));
            }
        });
    }

    [Test]
    public async Task Project_scoped_lfs_prefetch_includes_only_the_repository_pathspec()
    {
        const string ProjectPathspec = "projects/edit";
        string projectRoot = Path.Combine(Root, "projects", "edit");
        Directory.CreateDirectory(Path.Combine(projectRoot, "media"));
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "media", "clip.mp4"), "media\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "nested media");
        string commit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("remote", "add", "origin", CreateTemporaryDirectory());
        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(CreateRunner(), LfsObjectListJson()));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            new RepositoryInfo(Root, projectRoot),
            watcher: null,
            _ => recording);

        await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.ProjectPathspec,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None);

        string[][] scopedCommands = recording.Commands
            .Where(static arguments =>
                arguments.Contains("lfs")
                && (arguments.Contains("fetch") || arguments.Contains("ls-files")))
            .ToArray();
        Assert.That(
            scopedCommands,
            Has.Length.EqualTo(2),
            "The fetch and its cache fallback must use the same project scope.");
        Assert.Multiple(() =>
        {
            foreach (string[] command in scopedCommands)
            {
                Assert.That(command, Does.Contain("lfs.fetchinclude="));
                Assert.That(command, Does.Contain("lfs.fetchexclude="));
                if (command.Contains("fetch"))
                {
                    Assert.That(command, Does.Contain("lfs.fetchrecentalways=false"));
                }
                Assert.That(command, Does.Contain($"--include={ProjectPathspec}/**"));
                Assert.That(command, Does.Contain("--exclude="));
            }
        });
    }

    [TestCase("projects/a,b")]
    [TestCase("nested/project[1]")]
    [TestCase("projects/with space")]
    [TestCase("projects/日本語")]
    public async Task Project_scoped_lfs_prefetch_uses_the_exact_subtree_for_nonliteral_filters(
        string projectPathspec)
    {
        string projectRoot = Path.Combine(
            Root,
            projectPathspec.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(projectRoot, "media"));
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "media", "clip.mp4"), "media\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "nested media in a literal path");
        string commit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string projectTree = (await RunGitAsync("rev-parse", $"{commit}:{projectPathspec}"))
            .Stdout.Trim();
        await RunGitAsync("remote", "add", "origin", CreateTemporaryDirectory());
        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(CreateRunner(), LfsObjectListJson()));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            new RepositoryInfo(Root, projectRoot),
            watcher: null,
            _ => recording);

        await ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.ProjectPathspec,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None);

        string[] treeLookup = recording.Commands.Single(static arguments =>
            arguments.Contains("ls-tree") && arguments.Contains("--format=%(objectname)"));
        string[][] scopedCommands = recording.Commands
            .Where(static arguments =>
                arguments.Contains("lfs")
                && (arguments.Contains("fetch") || arguments.Contains("ls-files")))
            .ToArray();
        Assert.That(scopedCommands, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(treeLookup, Does.Contain(projectPathspec));
            foreach (string[] command in scopedCommands)
            {
                Assert.That(command, Does.Contain(projectTree));
                Assert.That(command, Does.Not.Contain(commit));
                if (command.Contains("fetch"))
                {
                    Assert.That(command, Does.Contain("lfs.fetchrecentalways=false"));
                }
                Assert.That(
                    command.Any(static argument =>
                        argument.StartsWith("--include=", StringComparison.Ordinal)),
                    Is.False,
                    string.Join(' ', command));
            }
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Failed_lfs_prefetch_only_continues_when_the_target_objects_are_cached(
        bool cacheTheObject)
    {
        byte[] cachedContents = Encoding.UTF8.GetBytes("cached\n");
        string oid = ComputeLfsOid(cachedContents);
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        Directory.CreateDirectory(Path.Combine(Root, "media"));
        await File.WriteAllTextAsync(Path.Combine(Root, "media", "clip.mp4"), "media\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "media");
        await RunGitAsync("remote", "add", "origin", CreateTemporaryDirectory());
        if (cacheTheObject)
        {
            await WriteCachedLfsObjectAsync(
                Path.Combine(
                Root,
                ".git",
                "lfs",
                    "objects"),
                oid,
                cachedContents);
        }

        var runner = new UnreachableLfsEndpointRunner(
            CreateRunner(),
            LfsObjectListJson((oid, "media/clip.mp4")));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => runner);

        Task prefetch = ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchBranchLfsObjectsAsync("main", CancellationToken.None);
                return true;
            },
            CancellationToken.None);

        if (cacheTheObject)
        {
            // Everything the target needs is already local, so an unreachable endpoint must not
            // block the transition.
            Assert.DoesNotThrowAsync(() => prefetch);
        }
        else
        {
            // The checkout would otherwise download it after the project has closed, on a path that
            // cannot be cancelled.
            Assert.ThrowsAsync<GitOperationException>(() => prefetch);
        }
    }

    [Test]
    public async Task Failed_lfs_prefetch_rejects_a_corrupt_cached_object()
    {
        string commit = await CommitLfsPrefetchFixtureAsync(addRemote: true);
        byte[] expectedContents = Encoding.UTF8.GetBytes("expected object\n");
        string oid = ComputeLfsOid(expectedContents);
        await WriteCachedLfsObjectAsync(
            Path.Combine(Root, ".git", "lfs", "objects"),
            oid,
            Encoding.UTF8.GetBytes("corrupt object!\n"));
        var runner = new UnreachableLfsEndpointRunner(
            CreateRunner(),
            LfsObjectListJson((oid, "media/clip.mp4")));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        commit,
                        LfsPrefetchScope.RepositoryWide,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Lfs_prefetch_without_a_remote_requires_valid_cached_objects(
        bool cacheTheObject)
    {
        const string ProjectPathspec = "projects/edit";
        string projectRoot = Path.Combine(Root, "projects", "edit");
        string commit = await CommitLfsPrefetchFixtureAsync(
            addRemote: false,
            projectPathspec: ProjectPathspec);
        byte[] cachedContents = Encoding.UTF8.GetBytes("offline cached object\n");
        string oid = ComputeLfsOid(cachedContents);
        if (cacheTheObject)
        {
            await WriteCachedLfsObjectAsync(
                Path.Combine(Root, ".git", "lfs", "objects"),
                oid,
                cachedContents);
        }

        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(
                CreateRunner(),
                LfsObjectListJson((oid, $"{ProjectPathspec}/media/clip.mp4"))));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            new RepositoryInfo(Root, projectRoot),
            watcher: null,
            _ => recording);

        Task prefetch = ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.ProjectPathspec,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None);

        if (cacheTheObject)
        {
            Assert.DoesNotThrowAsync(() => prefetch);
        }
        else
        {
            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => prefetch);
            Assert.That(exception!.Message, Does.Contain("no remote is configured"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                recording.Commands.Any(static arguments => arguments.Contains("fetch")),
                Is.False);
            Assert.That(
                recording.Commands.Any(static arguments => arguments.Contains("ls-files")),
                Is.True);
            Assert.That(
                recording.Commands.Single(static arguments => arguments.Contains("ls-files")),
                Does.Contain($"--include={ProjectPathspec}/**"));
        });
    }

    [Test]
    public async Task Lfs_prefetch_inspects_a_target_that_introduces_the_first_lfs_paths()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "target-lfs");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        Directory.CreateDirectory(Path.Combine(Root, "media"));
        await File.WriteAllTextAsync(Path.Combine(Root, "media", "clip.mp4"), "target media\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "introduce lfs media");
        string target = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await RunGitAsync("switch", "main");
        await RunGitAsync("remote", "add", "origin", CreateTemporaryDirectory());

        byte[] missingContents = Encoding.UTF8.GetBytes("missing target object\n");
        string oid = ComputeLfsOid(missingContents);
        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(
                CreateRunner(),
                LfsObjectListJson((oid, "media/clip.mp4"))));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => recording);

        Assert.ThrowsAsync<GitOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        target,
                        LfsPrefetchScope.ProjectPathspec,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));

        string[] fetch = recording.Commands.Single(static arguments =>
            arguments.Contains("lfs") && arguments.Contains("fetch"));
        Assert.That(fetch, Does.Contain(target));
    }

    [TestCase("")]
    [TestCase("{}")]
    [TestCase("{\"files\":{}}")]
    [TestCase("{\"files\":[{}]}")]
    [TestCase("{\"files\":[{\"oid\":\"abcd\"}]}")]
    [TestCase("{\"files\":[{\"oid\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}]}")]
    public async Task Failed_lfs_prefetch_rejects_noncanonical_object_listings(string listing)
    {
        string commit = await CommitLfsPrefetchFixtureAsync(addRemote: true);
        var runner = new UnreachableLfsEndpointRunner(CreateRunner(), listing);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<GitOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        commit,
                        LfsPrefetchScope.RepositoryWide,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));
    }

    [Test]
    public async Task Failed_lfs_prefetch_rejects_a_truncated_bounded_object_listing()
    {
        string commit = await CommitLfsPrefetchFixtureAsync(addRemote: true);
        byte[] cachedContents = Encoding.UTF8.GetBytes("bounded cached object\n");
        string oid = ComputeLfsOid(cachedContents);
        await WriteCachedLfsObjectAsync(
            Path.Combine(Root, ".git", "lfs", "objects"),
            oid,
            cachedContents);
        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(
                CreateRunner(),
                LfsObjectListJson((oid, "media/clip.mp4")),
                lsFilesOutputTruncated: true));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => recording);

        Assert.ThrowsAsync<GitOperationException>(() =>
            ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
                async transaction =>
                {
                    await transaction.PrefetchCommitLfsObjectsAsync(
                        commit,
                        LfsPrefetchScope.RepositoryWide,
                        CancellationToken.None);
                    return true;
                },
                CancellationToken.None));

        int fetchIndex = recording.Commands.FindIndex(static arguments =>
            arguments.Contains("lfs") && arguments.Contains("fetch"));
        int listIndex = recording.Commands.FindIndex(static arguments =>
            arguments.Contains("lfs") && arguments.Contains("ls-files"));
        Assert.Multiple(() =>
        {
            Assert.That(fetchIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(listIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(recording.Options[fetchIndex].MaxStdoutBytes, Is.GreaterThan(0));
            Assert.That(recording.Options[listIndex].MaxStdoutBytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Failed_lfs_prefetch_uses_shared_cache_from_a_linked_worktree()
    {
        byte[] cachedContents = Encoding.UTF8.GetBytes("shared cached object\n");
        string oid = ComputeLfsOid(cachedContents);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitattributes"), "*.mp4 filter=lfs\n");
        await RunGitAsync("add", ".gitattributes");
        await RunGitAsync("commit", "-m", "attributes");
        string linkedRoot = CreateTemporaryDirectory();
        Directory.Delete(linkedRoot);
        await RunGitAsync("worktree", "add", "--detach", linkedRoot, "HEAD");
        string commonDir = (await RunGitAsync("rev-parse", "--git-common-dir")).Stdout.Trim();
        string commonRoot = Path.GetFullPath(Path.Combine(Root, commonDir));
        await WriteCachedLfsObjectAsync(
            Path.Combine(commonRoot, "lfs", "objects"),
            oid,
            cachedContents);
        await RunGitAsync("-C", linkedRoot, "remote", "add", "origin", CreateTemporaryDirectory());
        var runner = new UnreachableLfsEndpointRunner(
            CreateRunner(),
            LfsObjectListJson((oid, "clip.mp4")));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            new RepositoryInfo(linkedRoot, linkedRoot),
            watcher: null,
            _ => runner);

        Assert.DoesNotThrowAsync(() => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchBranchLfsObjectsAsync("HEAD", CancellationToken.None);
                return true;
            },
            CancellationToken.None));
    }

    [Test]
    public async Task Failed_lfs_prefetch_accepts_a_cached_object_with_a_newline_in_its_filename()
    {
        string commit = await CommitLfsPrefetchFixtureAsync(addRemote: true);
        byte[] cachedContents = Encoding.UTF8.GetBytes("cached newline object\n");
        string oid = ComputeLfsOid(cachedContents);
        await WriteCachedLfsObjectAsync(
            Path.Combine(Root, ".git", "lfs", "objects"),
            oid,
            cachedContents);
        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(
                CreateRunner(),
                LfsObjectListJson((oid, "media/line\nbreak.mp4"))));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => recording);

        Assert.DoesNotThrowAsync(() => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.RepositoryWide,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None));

        Assert.That(
            recording.Commands.Single(static arguments => arguments.Contains("ls-files")),
            Does.Contain("--json"));
    }

    [Test]
    public async Task Lfs_prefetch_reads_a_real_json_listing_for_a_newline_filename()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows file names cannot contain line-feed characters.");
        }

        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        string mediaDirectory = Path.Combine(Root, "media");
        Directory.CreateDirectory(mediaDirectory);
        string mediaPath = Path.Combine(mediaDirectory, "line\nbreak.mp4");
        await File.WriteAllTextAsync(mediaPath, "real cached newline object\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "add newline lfs path");
        string commit = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => CreateRunner());

        Assert.DoesNotThrowAsync(() => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.RepositoryWide,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None));
    }

    [Test]
    public async Task Lfs_prefetch_falls_back_for_a_pre_json_client_with_a_newline_filename()
    {
        string commit = await CommitLfsPrefetchFixtureAsync(addRemote: true);
        byte[] cachedContents = Encoding.UTF8.GetBytes("legacy cached newline object\n");
        string oid = ComputeLfsOid(cachedContents);
        await WriteCachedLfsObjectAsync(
            Path.Combine(Root, ".git", "lfs", "objects"),
            oid,
            cachedContents);
        var recording = new ArgumentRecordingRunner(
            new UnreachableLfsEndpointRunner(
                CreateRunner(),
                $"{oid} * media/line\nbreak.mp4\n",
                jsonUnsupported: true));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            Repository,
            watcher: null,
            _ => recording);

        Assert.DoesNotThrowAsync(() => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchCommitLfsObjectsAsync(
                    commit,
                    LfsPrefetchScope.RepositoryWide,
                    CancellationToken.None);
                return true;
            },
            CancellationToken.None));

        string[][] listings = recording.Commands
            .Where(static arguments => arguments.Contains("ls-files"))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Length.EqualTo(2));
            Assert.That(listings[0], Does.Contain("--json"));
            Assert.That(listings[1], Does.Not.Contain("--json"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Failed_lfs_prefetch_uses_configured_cache_from_a_linked_worktree(
        bool absoluteStorage)
    {
        byte[] cachedContents = Encoding.UTF8.GetBytes("configured cached object\n");
        string oid = ComputeLfsOid(cachedContents);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitattributes"), "*.mp4 filter=lfs\n");
        await RunGitAsync("add", ".gitattributes");
        await RunGitAsync("commit", "-m", "attributes");
        string linkedRoot = CreateTemporaryDirectory();
        Directory.Delete(linkedRoot);
        await RunGitAsync("worktree", "add", "--detach", linkedRoot, "HEAD");

        string configuredStorage = absoluteStorage
            ? Path.Combine(CreateTemporaryDirectory(), "lfs-cache")
            : Path.Combine("custom-lfs", "nested");
        string commonRoot = Path.GetFullPath(
            Path.Combine(Root, (await RunGitAsync("rev-parse", "--git-common-dir")).Stdout.Trim()));
        string storageRoot = absoluteStorage
            ? configuredStorage
            : Path.Combine(commonRoot, configuredStorage);
        await WriteCachedLfsObjectAsync(
            Path.Combine(storageRoot, "objects"),
            oid,
            cachedContents);
        await RunGitAsync("-C", linkedRoot, "config", "lfs.storage", configuredStorage);
        await RunGitAsync("-C", linkedRoot, "remote", "add", "origin", CreateTemporaryDirectory());

        var runner = new UnreachableLfsEndpointRunner(
            CreateRunner(),
            LfsObjectListJson((oid, "clip.mp4")));
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            new RepositoryInfo(linkedRoot, linkedRoot),
            watcher: null,
            _ => runner);

        Assert.DoesNotThrowAsync(() => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await transaction.PrefetchBranchLfsObjectsAsync("HEAD", CancellationToken.None);
                return true;
            },
            CancellationToken.None));
    }

    private async Task<string> CommitLfsPrefetchFixtureAsync(
        bool addRemote,
        string projectPathspec = ".")
    {
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        string projectRoot = projectPathspec == "."
            ? Root
            : Path.Combine(Root, projectPathspec.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(projectRoot, "media"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "media", "clip.mp4"), "media\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "media");
        if (addRemote)
        {
            await RunGitAsync("remote", "add", "origin", CreateTemporaryDirectory());
        }

        return (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
    }

    private static string ComputeLfsOid(byte[] contents)
    {
        return Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
    }

    private static string CreateLfsPointer(byte[] contents)
    {
        return $"version https://git-lfs.github.com/spec/v1\n"
               + $"oid sha256:{ComputeLfsOid(contents)}\n"
               + $"size {contents.Length}\n";
    }

    private static string CreateNoncanonicalLfsPointer(byte[] contents)
    {
        string oid = ComputeLfsOid(contents);
        return $"  ext-1-before sha256:{oid}\n\n"
               + "version http://git-media.io/v/2\n\n"
               + $"oid sha256:{oid}\n"
               + $"ext-0-test sha256:{oid}\n\n"
               + $"size +{contents.Length}  \n\n";
    }

    private static string LfsObjectListJson(params (string Oid, string Name)[] files)
    {
        return JsonSerializer.Serialize(new
        {
            files = files.Select(static file => new
            {
                oid = file.Oid,
                name = file.Name,
            }),
        });
    }

    private static async Task WriteCachedLfsObjectAsync(
        string objectRoot,
        string oid,
        byte[] contents)
    {
        string directory = Path.Combine(objectRoot, oid[..2], oid[2..4]);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, oid), contents);
    }

    private GitCliVersionControlService CreateService(RepositoryWatcher? watcher = null)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner());
    }

    private async Task WriteHookAsync(string name, string body)
    {
        string hookRecord = (await RunGitAsync(
                "rev-parse",
                "--git-path",
                $"hooks/{name}"))
            .Stdout.TrimEnd('\r', '\n');
        string hookPath = Path.GetFullPath(
            Path.IsPathFullyQualified(hookRecord)
                ? hookRecord
                : Path.Combine(Root, hookRecord));
        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        await File.WriteAllTextAsync(
            hookPath,
            "#!/bin/sh\n" + body,
            new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }
    }

    private string[] FindOwnedCommitMessageFiles()
    {
        string gitDirectory = Path.Combine(Root, ".git");
        return Directory.Exists(gitDirectory)
            ? Directory.GetFiles(
                gitDirectory,
                "beutl-commit-message-*.tmp",
                SearchOption.AllDirectories)
            : [];
    }

    private async Task<IReadOnlyList<string>> GetLocalConfigValuesAsync(string key)
    {
        try
        {
            GitCommandResult result = await RunGitAsync(
                "config",
                "--local",
                "--null",
                "--get-all",
                key);
            Assert.That(result.Stdout, Does.EndWith("\0"));
            return result.Stdout[..^1].Split('\0');
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return [];
        }
    }

    private static void CreateFileSymbolicLinkOrIgnore(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    private static void CreateDirectorySymbolicLinkOrIgnore(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    // Reading status resolves the branch's upstream and then verifies the origin ref before
    // counting. Reporting no upstream and a missing ref is what a repository without one looks
    // like, and it stops the walk before rev-list, which these stub runners would also have to
    // answer.
    private static GitCommandResult StubStatusResult(IReadOnlyList<string> arguments)
    {
        return arguments.FirstOrDefault() is "rev-parse" or "show-ref"
            ? throw new GitOperationException(1, string.Empty)
            : new GitCommandResult(0, "# branch.head main\0", string.Empty);
    }

    // Worktree-mutating commands carry `-c` overrides (LFS path filters, hooks) before the
    // subcommand, so tests match past that prefix instead of pinning its exact shape.
    private static bool IsTransitionCheckout(IReadOnlyList<string> arguments)
    {
        int index = SkipConfigOverrides(arguments);
        return index + 2 < arguments.Count
               && arguments[index] == "checkout"
               && arguments[index + 1] == "--detach"
               && arguments[index + 2] == "--no-overwrite-ignore";
    }

    private static string? GetGitSubcommand(IReadOnlyList<string> arguments)
    {
        int index = SkipConfigOverrides(arguments);
        return index < arguments.Count ? arguments[index] : null;
    }

    private static int SkipConfigOverrides(IReadOnlyList<string> arguments)
    {
        int index = 0;
        while (index + 1 < arguments.Count && arguments[index] == "-c")
        {
            index += 2;
        }

        return index;
    }

    private sealed class UnreachableLfsEndpointRunner(
        IGitCliRunner inner,
        string lsFilesOutput,
        bool lsFilesOutputTruncated = false,
        bool jsonUnsupported = false) : IGitCliRunner
    {
        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            string? subcommand = GetGitSubcommand(arguments);
            if (subcommand == "lfs" && arguments.Contains("fetch"))
            {
                throw new GitOperationException(2, "Fatal error: Server error: endpoint unreachable");
            }

            if (subcommand == "lfs" && arguments.Contains("ls-files"))
            {
                if (jsonUnsupported && arguments.Contains("--json"))
                {
                    throw new GitOperationException(2, "Error: unknown flag: --json");
                }

                return Task.FromResult(new GitCommandResult(
                    0,
                    lsFilesOutput,
                    "",
                    StdoutTruncated: lsFilesOutputTruncated));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class ArgumentRecordingRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public List<string[]> Commands { get; } = [];

        public List<GitCommandOptions> Options { get; } = [];

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add([.. arguments]);
            Options.Add(options);
            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class ConcurrencyTrackingRunner : IGitCliRunner
    {
        private int _concurrency;

        public bool HasActiveProcess => _concurrency > 0;

        public int MaxConcurrency { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            int concurrency = Interlocked.Increment(ref _concurrency);
            MaxConcurrency = Math.Max(MaxConcurrency, concurrency);
            try
            {
                await Task.Delay(100, cancellationToken);
                return StubStatusResult(arguments);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class BlockingStatusRunner : IGitCliRunner
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasActiveProcess => !_completion.Task.IsCompleted;

        public Task Started => _started.Task;

        public void Complete()
        {
            _completion.TrySetResult(true);
        }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            _started.TrySetResult(true);
            await _completion.Task.WaitAsync(cancellationToken);
            return StubStatusResult(arguments);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class BlockingInitializationRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private readonly TaskCompletionSource _initialCommitCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _repositoryInitialized = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseInitialCommit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRepositoryInitialization = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task InitialCommitCompleted => _initialCommitCompleted.Task;

        public Task RepositoryInitialized => _repositoryInitialized.Task;

        public void ContinueAfterInitialCommit()
        {
            _releaseInitialCommit.TrySetResult();
        }

        public void ContinueAfterRepositoryInitialization()
        {
            _releaseRepositoryInitialization.TrySetResult();
        }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (arguments.SequenceEqual(["symbolic-ref", "HEAD", "refs/heads/main"]))
            {
                _repositoryInitialized.TrySetResult();
                await _releaseRepositoryInitialization.Task.WaitAsync(cancellationToken);
            }
            else if (IsSnapshotRefUpdate(
                         arguments,
                         "beutl: initialize version control"))
            {
                _initialCommitCompleted.TrySetResult();
                await _releaseInitialCommit.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class BlockingFirstStatusRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task Started => _started.Task;

        public void Complete()
        {
            _completion.TrySetResult();
        }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Count > 0
                && arguments[0] == "status"
                && Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                _started.TrySetResult();
                await _completion.Task.WaitAsync(cancellationToken);
            }

            return await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class ThrowingStatusRunner(Exception exception) : IGitCliRunner
    {
        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            return Task.FromException<GitCommandResult>(exception);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class FailingPostMutationStatusRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _failNextStatus;
        private int _statusFailureCount;

        public IOException StatusFailure { get; } = new("post-mutation status failed");

        public int StatusFailureCount => Volatile.Read(ref _statusFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "status"
                && Interlocked.Exchange(ref _failNextStatus, 0) != 0)
            {
                Interlocked.Increment(ref _statusFailureCount);
                throw StatusFailure;
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (IsDurableMutation(arguments))
            {
                Volatile.Write(ref _failNextStatus, 1);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);

        private static bool IsDurableMutation(IReadOnlyList<string> arguments)
        {
            string? command = GetGitSubcommand(arguments);
            return IsCommitCommand(arguments)
                   || command is "push" or "switch"
                   || (command == "remote"
                       && arguments.Count > 1
                       && arguments[1] is "add" or "set-url");
        }
    }

    private sealed class FailingPostCommitRevisionRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _commitCompleted;
        private int _revisionFailureCount;

        public IOException RevisionFailure { get; } = new("post-commit revision lookup failed");

        public int RevisionFailureCount => Volatile.Read(ref _revisionFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (Volatile.Read(ref _commitCompleted) != 0
                && arguments.SequenceEqual(["rev-parse", "HEAD"]))
            {
                Interlocked.Increment(ref _revisionFailureCount);
                Volatile.Write(ref _commitCompleted, 0);
                throw RevisionFailure;
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (IsCommitCommand(arguments))
            {
                Volatile.Write(ref _commitCompleted, 1);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class FailingPostRemoteAuxiliaryRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _remoteMutated;
        private int _auxiliaryFailureCount;

        public IOException AuxiliaryFailure { get; } = new("post-remote auxiliary check failed");

        public int AuxiliaryFailureCount => Volatile.Read(ref _auxiliaryFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (Volatile.Read(ref _remoteMutated) != 0
                && arguments.SequenceEqual(["remote", "get-url", "origin"]))
            {
                Interlocked.Increment(ref _auxiliaryFailureCount);
                throw AuxiliaryFailure;
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if ((arguments.Count > 1
                 && arguments[0] == "remote"
                 && arguments[1] is "add" or "set-url")
                || (arguments.Count > 5
                    && arguments[0] == "config"
                    && arguments[1] == "--file"
                    && arguments[4] == "remote.origin.pushurl"))
            {
                Volatile.Write(ref _remoteMutated, 1);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class FailingStagedRemoteConfigRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _stagedFailureCount;

        public int StagedFailureCount => Volatile.Read(ref _stagedFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Count > 5
                && arguments[0] == "config"
                && arguments[1] == "--file"
                && arguments[4] == "remote.origin.pushurl")
            {
                Interlocked.Increment(ref _stagedFailureCount);
                throw new IOException("staged push URL update failed");
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class StaticStatusRunner : IGitCliRunner
    {
        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            return Task.FromResult(StubStatusResult(arguments));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class RemoteLockFailureRunner(
        IGitCliRunner inner,
        RepositoryLockInfo lockInfo) : IGitCliRunner
    {
        public RepositoryLockInfo LockInfo { get; set; } = lockInfo;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public int PushCalls { get; private set; }

        public int LockProbeCalls { get; private set; }

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "push")
            {
                PushCalls++;
                return Task.FromException<GitCommandResult>(new GitOperationException(
                    128,
                    $"fatal: Unable to create '{LockInfo.LockPath}': File exists.\n"));
            }

            return inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
        {
            LockProbeCalls++;
            return LockInfo;
        }

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo candidate)
            => false;
    }

    private sealed class FailingInitialCommitRunner(
        IGitCliRunner inner,
        RepositoryLockInfo? recoverableLock = null,
        CancellationTokenSource? cancellation = null,
        Action<RepositoryInfo>? beforeFailure = null) : IGitCliRunner
    {
        private int _initialCommitAttempts;

        public int InitialCommitAttempts => Volatile.Read(ref _initialCommitAttempts);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (IsSnapshotRefUpdate(arguments, "beutl: initialize version control"))
            {
                Interlocked.Increment(ref _initialCommitAttempts);
                beforeFailure?.Invoke(repository);
                if (cancellation is not null)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<GitCommandResult>(cancellation.Token);
                }

                return Task.FromException<GitCommandResult>(new GitOperationException(
                    1,
                    recoverableLock is null
                        ? "initial commit failed"
                        : $"fatal: Unable to create '{recoverableLock.LockPath}': index.lock exists."));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => recoverableLock ?? inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class LostInitialCommitResultRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _initialCommitAttempts;

        public IGitCliRunner Inner => inner;

        public int InitialCommitAttempts => Volatile.Read(ref _initialCommitAttempts);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (IsSnapshotRefUpdate(arguments, "beutl: initialize version control"))
            {
                Interlocked.Increment(ref _initialCommitAttempts);
                throw new TimeoutException("simulated lost initial commit result");
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class FailingSnapshotCommitRunner(
        IGitCliRunner inner,
        CancellationTokenSource? cancellation = null,
        Action<RepositoryInfo>? beforeFailure = null) : IGitCliRunner
    {
        private int _commitAttempts;

        public int CommitAttempts => Volatile.Read(ref _commitAttempts);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (IsSnapshotRefUpdate(arguments, "beutl: save snapshot"))
            {
                Interlocked.Increment(ref _commitAttempts);
                beforeFailure?.Invoke(repository);
                if (cancellation is not null)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<GitCommandResult>(cancellation.Token);
                }

                return Task.FromException<GitCommandResult>(new GitOperationException(
                    1,
                    "simulated snapshot commit failure"));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class TempIndexAddFailureRunner(
        IGitCliRunner inner,
        CancellationTokenSource? cancellation = null) : IGitCliRunner
    {
        private int _addAttempts;

        public int AddAttempts => Volatile.Read(ref _addAttempts);

        public string? TemporaryIndexPath { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (options.EnvironmentOverrides?.TryGetValue("GIT_INDEX_FILE", out string? observedIndexPath) == true
                && observedIndexPath!.Contains(".beutl-index-", StringComparison.Ordinal))
            {
                TemporaryIndexPath = observedIndexPath;
            }

            if (arguments.Contains("reset")
                && options.EnvironmentOverrides?.TryGetValue("GIT_INDEX_FILE", out string? indexPath) == true
                && indexPath!.Contains(".beutl-index-", StringComparison.Ordinal)
                && Interlocked.Increment(ref _addAttempts) == 1)
            {
                if (cancellation is not null)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<GitCommandResult>(cancellation.Token);
                }

                return Task.FromException<GitCommandResult>(new GitOperationException(
                    1,
                    "simulated temporary index reconciliation failure"));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class ExternalStageDuringTempIndexRunner(
        IGitCliRunner inner,
        RepositoryInfo liveRepository,
        string externalRelativePath) : IGitCliRunner
    {
        private int _interceptionPending = 1;

        public int InterceptionCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Contains("reset")
                && options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? temporaryIndexPath) == true
                && temporaryIndexPath!.Contains(".beutl-index-", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _interceptionPending, 0) == 1)
            {
                await inner.RunAsync(
                        liveRepository,
                        ["add", "--", externalRelativePath],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                InterceptionCount++;
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class LostSnapshotWorktreeAddResultRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _addAttempts;

        public int AddAttempts => Volatile.Read(ref _addAttempts);

        public string? TemporaryWorktreePath { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Count >= 5
                && arguments[0] == "worktree"
                && arguments[1] == "add"
                && Interlocked.Increment(ref _addAttempts) == 1)
            {
                TemporaryWorktreePath = arguments[4];
                await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress);
                throw new IOException("simulated lost worktree-add response");
            }

            return await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class LostSnapshotCommitResultRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _commitAttempts;

        public int CommitAttempts => Volatile.Read(ref _commitAttempts);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (IsSnapshotRefUpdate(arguments, "beutl: save snapshot"))
            {
                Interlocked.Increment(ref _commitAttempts);
                throw new TimeoutException("simulated lost snapshot commit result");
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class SwitchBranchBeforeSnapshotPublicationRunner(
        IGitCliRunner inner,
        RepositoryInfo liveRepository,
        string branch) : IGitCliRunner
    {
        private int _switchPending = 1;

        public int SwitchCount { get; private set; }

        public int BlockedSwitchCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (IsCommitCommand(arguments)
                && Interlocked.Exchange(ref _switchPending, 0) == 1)
            {
                try
                {
                    await inner.RunAsync(
                            liveRepository,
                            ["symbolic-ref", "HEAD", $"refs/heads/{branch}"],
                            GitCommandOptions.Local,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    SwitchCount++;
                }
                catch (GitOperationException ex) when (ex.IsRepositoryLockFailure)
                {
                    BlockedSwitchCount++;
                }
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class SwitchBranchBeforeSnapshotTreeRunner(
        IGitCliRunner inner,
        RepositoryInfo liveRepository,
        string branch) : IGitCliRunner
    {
        private int _switchPending = 1;

        public int SwitchCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (arguments.SequenceEqual(["rev-parse", "--git-path", "HEAD"])
                && Interlocked.Exchange(ref _switchPending, 0) == 1)
            {
                await inner.RunAsync(
                        liveRepository,
                        ["symbolic-ref", "HEAD", $"refs/heads/{branch}"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                SwitchCount++;
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class MoveCapturedRefBeforeSnapshotPublicationRunner(
        IGitCliRunner inner,
        string externalTip) : IGitCliRunner
    {
        private int _movePending = 1;

        public int MoveCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "update-ref"
                && arguments.Contains("beutl: save snapshot")
                && Interlocked.Exchange(ref _movePending, 0) == 1)
            {
                string branchRef = arguments[^3];
                string expectedOld = arguments[^1];
                await inner.RunAsync(
                        repository,
                        ["update-ref", branchRef, externalTip, expectedOld],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                MoveCount++;
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class MutateWorktreeBeforeIndexReconciliationRunner(
        IGitCliRunner inner,
        string projectFile,
        string contents) : IGitCliRunner
    {
        private int _mutationPending = 1;

        public int MutationCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (arguments.FirstOrDefault() == "write-tree"
                && options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? temporaryIndexPath) == true
                && temporaryIndexPath!.StartsWith(
                    Path.Combine(Path.GetTempPath(), "beutl-git-index-"),
                    StringComparison.Ordinal)
                && Interlocked.Exchange(ref _mutationPending, 0) == 1)
            {
                await File.WriteAllTextAsync(projectFile, contents, CancellationToken.None);
                MutationCount++;
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class StageProjectAfterSnapshotTreeRunner(
        IGitCliRunner inner,
        RepositoryInfo liveRepository,
        string projectFile,
        string contents) : IGitCliRunner
    {
        private int _stagePending = 1;

        public int StageCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (arguments.FirstOrDefault() == "write-tree"
                && options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? temporaryIndexPath) == true
                && temporaryIndexPath!.StartsWith(
                    Path.Combine(Path.GetTempPath(), "beutl-git-index-"),
                    StringComparison.Ordinal)
                && Interlocked.Exchange(ref _stagePending, 0) == 1)
            {
                await File.WriteAllTextAsync(projectFile, contents, CancellationToken.None);
                await inner.RunAsync(
                        liveRepository,
                        ["add", "--", "project.bep"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                StageCount++;
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class CreateNestedRepositoryDuringSnapshotRunner(
        IGitCliRunner inner,
        string projectRoot) : IGitCliRunner
    {
        private int _injectionPending = 1;

        public int InjectionCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Contains("add")
                && options.EnvironmentOverrides?.TryGetValue(
                    "GIT_INDEX_FILE",
                    out string? temporaryIndexPath) == true
                && temporaryIndexPath!.StartsWith(
                    Path.Combine(Path.GetTempPath(), "beutl-git-index-"),
                    StringComparison.Ordinal)
                && Interlocked.Exchange(ref _injectionPending, 0) == 1)
            {
                string nestedRoot = Path.Combine(projectRoot, "embedded");
                Directory.CreateDirectory(nestedRoot);
                await File.WriteAllTextAsync(
                    Path.Combine(nestedRoot, "notes.txt"),
                    "optional nested content\n",
                    CancellationToken.None);
                var nestedRepository = new RepositoryInfo(nestedRoot, nestedRoot);
                await inner.RunAsync(
                        nestedRepository,
                        ["init", "-b", "main"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await inner.RunAsync(
                        nestedRepository,
                        ["add", "--", "notes.txt"],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await inner.RunAsync(
                        nestedRepository,
                        [
                            "-c",
                            "user.name=External Test",
                            "-c",
                            "user.email=external@example.invalid",
                            "commit",
                            "-m",
                            "nested repository",
                        ],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                InjectionCount++;
            }

            return await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private static bool IsCommitCommand(IReadOnlyList<string> arguments)
    {
        return arguments.FirstOrDefault() == "commit"
               || arguments.FirstOrDefault() == "commit-tree"
               || arguments.Count >= 3
               && arguments[0] == "-c"
               && arguments[2] == "commit";
    }

    private static bool IsSnapshotRefUpdate(
        IReadOnlyList<string> arguments,
        string reflogMessage)
    {
        return arguments.FirstOrDefault() == "update-ref"
               && arguments.Contains("-m")
               && arguments.Contains(reflogMessage);
    }

    private sealed class FailingIdentityEmailWriteRunner(
        IGitCliRunner inner,
        Exception failure,
        Action? beforeFailure = null) : IGitCliRunner
    {
        private int _failurePending = 1;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Count >= 4
                && arguments[0] == "config"
                && arguments.Contains("--file")
                && arguments[^2] == "user.email"
                && Interlocked.Exchange(ref _failurePending, 0) == 1)
            {
                beforeFailure?.Invoke();
                return Task.FromException<GitCommandResult>(failure);
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class AfterTransitionWorktreeAddRunner(
        IGitCliRunner inner,
        Action afterWorktreeAdd) : IGitCliRunner
    {
        private int _interceptionPending = 1;

        public int InterceptionCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (arguments is ["worktree", "add", ..]
                && Interlocked.Exchange(ref _interceptionPending, 0) == 1)
            {
                InterceptionCount++;
                afterWorktreeAdd();
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class AfterTransitionCheckoutRunner(
        IGitCliRunner inner,
        Action afterCheckout) : IGitCliRunner
    {
        private int _interceptionPending = 1;

        public int InterceptionCount { get; private set; }

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (IsTransitionCheckout(arguments)
                && Interlocked.Exchange(ref _interceptionPending, 0) == 1)
            {
                InterceptionCount++;
                afterCheckout();
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class RecordingLfsRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _lfsInstallCalls;

        public int LfsInstallCalls => Volatile.Read(ref _lfsInstallCalls);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.SequenceEqual(["lfs", "install", "--local"]))
            {
                Interlocked.Increment(ref _lfsInstallCalls);
                return Task.FromResult(new GitCommandResult(0, "", ""));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class RecordingArgumentsRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public List<(IReadOnlyList<string> Arguments, GitCommandOptions Options)> Invocations { get; } = [];

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            string[] capturedArguments = arguments.ToArray();
            Commands.Add(capturedArguments);
            Invocations.Add((capturedArguments, options));
            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class CustomHookRejectingLfsInstallRunner(
        IGitCliRunner inner,
        string hookPath,
        string expectedHookContents) : IGitCliRunner
    {
        private int _lfsInstallCalls;

        public int LfsInstallCalls => Volatile.Read(ref _lfsInstallCalls);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.SequenceEqual(["lfs", "install", "--local"]))
            {
                Interlocked.Increment(ref _lfsInstallCalls);
                string observedHook = File.Exists(hookPath)
                    ? File.ReadAllText(hookPath)
                    : "<missing>";
                if (!string.Equals(observedHook, expectedHookContents, StringComparison.Ordinal))
                {
                    return Task.FromException<GitCommandResult>(
                        new InvalidOperationException(
                            "The custom pre-push hook changed before Git LFS installation."));
                }

                return Task.FromException<GitCommandResult>(
                    new GitOperationException(
                        2,
                        $"Hook already exists: pre-push\n\n{observedHook}"));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class RecordingInitializationRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public List<string> Commands { get; } = [];

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add(string.Join(' ', arguments));
            if (arguments.SequenceEqual(["lfs", "install", "--local"]))
            {
                return Task.FromResult(new GitCommandResult(0, "", ""));
            }

            return inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class MismatchedDiscoveryRunner(
        string discoveredRoot,
        string discoveredPrefix = "") : IGitCliRunner
    {
        public List<string> Commands { get; } = [];

        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add(string.Join(' ', arguments));
            if (arguments.SequenceEqual(["rev-parse", "--show-toplevel"]))
            {
                return Task.FromResult(new GitCommandResult(0, $"{discoveredRoot}\n", ""));
            }

            if (arguments.SequenceEqual(["rev-parse", "--show-prefix"]))
            {
                return Task.FromResult(new GitCommandResult(0, $"{discoveredPrefix}\n", ""));
            }

            return Task.FromResult(new GitCommandResult(0, "", ""));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class MergeAfterFirstStatusRunner(
        IGitCliRunner inner,
        Func<Task> merge) : IGitCliRunner
    {
        private int _interceptionCount;

        public int InterceptionCount => Volatile.Read(ref _interceptionCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken,
                    stderrProgress)
                .ConfigureAwait(false);
            if (GetGitSubcommand(arguments) == "status"
                && Interlocked.CompareExchange(ref _interceptionCount, 1, 0) == 0)
            {
                await merge().ConfigureAwait(false);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    public sealed class SnapshotTestProjectItem : ProjectItem;

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class RecordingLogger : ILogger
    {
        private readonly TaskCompletionSource<LogEntry> _entry =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LogEntry> Entry => _entry.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entry.TrySetResult(
                new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed class RuntimeProbe : IGitInstallationProbe
    {
        public int VersionProbeCount { get; private set; }

        public Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            if (arguments.SequenceEqual(["--version"]))
            {
                VersionProbeCount++;
                return Task.FromResult(new GitProbeResult(0, "git version 2.50.0", ""));
            }

            return Task.FromResult(new GitProbeResult(1, "", ""));
        }

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
    }
}
