using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Language;
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
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

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
                File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.tmp\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("*.bep text eol=lf\n"));
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
        var runner = new FailingInitialCommitRunner(CreateRunner(), expectedLock);
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

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.InitializeAsync(options, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Not.Null);
            Assert.That(service.RecoverableLock, Is.EqualTo(expectedLock));
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
            Assert.That(File.ReadAllText(ignorePath), Is.EqualTo(originalIgnoreContents));
            Assert.That(File.GetAttributes(ignorePath), Is.EqualTo(originalIgnoreAttributes));
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
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
            Assert.That(File.Exists(Path.Combine(Root, ".gitattributes")), Is.False);
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
        var runner = new RecordingInitializationRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: true),
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
        var runner = new RecordingInitializationRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: true),
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
            Assert.That(attributes, Does.Contain("*.bep text eol=lf"));
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
                Is.EqualTo(new[] { "**/.beutl/", "*.tmp" }));
            Assert.That(
                File.ReadAllLines(Path.Combine(Root, ".gitattributes"))
                    .Count(static line => line == "*.bep text eol=lf"),
                Is.EqualTo(1));
        });
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
            Assert.That(lines, Does.Contain("*.bep text eol=lf"));
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
            Assert.That(contents, Does.Contain("*.bep text eol=lf"));
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
            Assert.That(contents, Does.Contain("*.bep text eol=lf\n"));
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
            beforeHygieneFileCommit: async (path, cancellationToken) =>
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
            Assert.That(contents, Does.Contain("*.bep text eol=lf\n"));
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
    public async Task InitializeAsync_sets_main_with_git_2_23_compatible_commands()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new RecordingInitializationRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: false),
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
            await service.RemoveRecoverableLockAsync(CancellationToken.None),
            Is.True);
        Assert.That(File.Exists(lockPath), Is.False);
        Assert.That(
            await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None),
            Is.TypeOf<CommitResult.Committed>());
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
        Assert.That(
            await service.RemoveRecoverableLockAsync(CancellationToken.None),
            Is.True);
        Assert.That(File.Exists(lockPath), Is.False);
    }

    [Test]
    public async Task Mapped_remote_failure_still_exposes_recoverable_lock()
    {
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new RemoteLockFailureRunner(expectedLock);
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
        RepositoryLockInfo actual = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(actual, Is.EqualTo(expectedLock));
            Assert.That(service.RecoverableLock, Is.EqualTo(expectedLock));
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
            .Where(static command => command.FirstOrDefault() is "log" or "show")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(parsedCommands, Has.Count.EqualTo(3));
            Assert.That(parsedCommands, Has.All.Contains("--no-show-signature"));
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
        await File.WriteAllTextAsync(Path.Combine(externalRoot, "linked.scene"), "{}\n");
        string linkedDirectory = Path.Combine(Root, "linked");
        CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, externalRoot);
        using var service = CreateService();

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("symbolic-link directory 'linked'"));
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
                    .Where(static invocation => invocation.Arguments.FirstOrDefault() == "switch")
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
    public void Porcelain_v2_parser_reads_branch_counts_renames_and_conflicts()
    {
        string output = string.Join('\0',
        [
            "# branch.oid abcdef",
            "# branch.head feature/test",
            "# branch.ab +2 -3",
            "1 .M N... 100644 100644 100644 aaaaaaa bbbbbbb project file.bep",
            "2 R. N... 100644 100644 100644 aaaaaaa bbbbbbb R100 renamed.scene",
            "old.scene",
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
            Assert.That(status.Changes, Has.Count.EqualTo(4));
            Assert.That(status.Changes[0],
                Is.EqualTo(new FileChange("project file.bep", FileChangeStatus.Modified)));
            Assert.That(status.Changes[1],
                Is.EqualTo(new FileChange("renamed.scene", FileChangeStatus.Renamed, "old.scene")));
            Assert.That(status.Changes[3],
                Is.EqualTo(new FileChange("added.belm", FileChangeStatus.Added)));
        });
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

        GitCommandExecutionKind[] executionKinds = runner.Invocations
            .Where(static invocation =>
                invocation.Arguments.Count > 1
                && invocation.Arguments[0] == "add"
                && invocation.Arguments[1] == "-A")
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
    public async Task CommitAllAsync_reports_committed_when_post_commit_revision_lookup_fails()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var runner = new FailingPostCommitRevisionRunner(CreateRunner());
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        GitCommandResult commitCount = await RunGitAsync("rev-list", "--count", "HEAD");
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                ((CommitResult.Committed)result).Revision,
                Is.TypeOf<CommitRevision.Unavailable>());
            Assert.That(commitCount.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(runner.RevisionFailureCount, Is.EqualTo(1));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.RevisionFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to resolve the revision created by a successful Git commit."));
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
            Assert.That(runner.AuxiliaryFailureCount, Is.EqualTo(1));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.AuxiliaryFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to publish the Git LFS quota notice after configuring the remote."));
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
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new RemoteLockFailureRunner(expectedLock);
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

    private GitCliVersionControlService CreateService(RepositoryWatcher? watcher = null)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner());
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
                return new GitCommandResult(0, "# branch.head main\0", "");
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
            return new GitCommandResult(0, "# branch.head main\0", "");
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
            else if (IsCommitCommand(arguments)
                     && arguments.Contains("Beutl-Snapshot: init"))
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
            string? command = arguments.FirstOrDefault();
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
            if (arguments.Count > 1
                && arguments[0] == "remote"
                && arguments[1] is "add" or "set-url")
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
            return Task.FromResult(new GitCommandResult(0, "# branch.head main\0", ""));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class RemoteLockFailureRunner(RepositoryLockInfo lockInfo) : IGitCliRunner
    {
        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            return arguments.FirstOrDefault() == "push"
                ? Task.FromException<GitCommandResult>(new GitOperationException(
                    128,
                    $"fatal: Unable to create '{lockInfo.LockPath}': File exists.\n"))
                : Task.FromResult(new GitCommandResult(0, "# branch.head main\0", ""));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => lockInfo;

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
            if (IsCommitCommand(arguments)
                && arguments.Contains("Beutl-Snapshot: init"))
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
            if (IsCommitCommand(arguments)
                && arguments.Contains("Beutl-Snapshot: init"))
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
        CancellationTokenSource? cancellation = null) : IGitCliRunner
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
            if (IsCommitCommand(arguments)
                && arguments.Contains("Beutl-Snapshot: save"))
            {
                Interlocked.Increment(ref _commitAttempts);
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
            if (IsCommitCommand(arguments)
                && arguments.Contains("Beutl-Snapshot: save"))
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

    private static bool IsCommitCommand(IReadOnlyList<string> arguments)
    {
        return arguments.FirstOrDefault() == "commit"
               || arguments.Count >= 3
               && arguments[0] == "-c"
               && arguments[2] == "commit";
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
            if (arguments is
                [
                    "-c",
                    "core.hooksPath=/dev/null",
                    "checkout",
                    "--detach",
                    "--no-overwrite-ignore",
                    _,
                ]
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

    private sealed class RecordingInitializationRunner : IGitCliRunner
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
            if (arguments.FirstOrDefault() == "rev-parse"
                && arguments.Contains("--git-path"))
            {
                string[] paths = arguments
                    .Select((argument, index) => (argument, index))
                    .Where(static item => item.argument == "--git-path")
                    .Select(item => Path.Combine(
                        repository.RepoRoot,
                        ".git",
                        arguments[item.index + 1]))
                    .ToArray();
                return Task.FromResult(new GitCommandResult(
                    0,
                    string.Join('\n', paths) + '\n',
                    ""));
            }

            if (arguments.FirstOrDefault() == "rev-parse")
            {
                throw new GitOperationException(
                    128,
                    "fatal: not a git repository (or any of the parent directories): .git\n");
            }

            if (arguments.SequenceEqual(["config", "--get", "user.name"]))
            {
                return Task.FromResult(new GitCommandResult(0, "Beutl Test\n", ""));
            }

            if (arguments.SequenceEqual(["config", "--get", "user.email"]))
            {
                return Task.FromResult(new GitCommandResult(0, "beutl-test@example.invalid\n", ""));
            }

            if (arguments.SequenceEqual(["symbolic-ref", "--quiet", "HEAD"]))
            {
                return Task.FromResult(new GitCommandResult(0, "refs/heads/main\n", ""));
            }

            if (arguments.FirstOrDefault() == "status")
            {
                return Task.FromResult(new GitCommandResult(
                    0,
                    "# branch.head main\0? .gitignore\0? .gitattributes\0",
                    ""));
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
