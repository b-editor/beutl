using Beutl.Configuration;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class VersionControlPolicyTests : RealGitTestRepository
{
    [Test]
    public async Task InitializeAsync_awaits_large_media_notice_before_initial_add_and_commit()
    {
        var config = new VersionControlConfig
        {
            LargeMediaWarningThresholdMb = 1,
        };
        var notices = new List<VersionControlPolicyNotice>();
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, new byte[(1024 * 1024) + 1]);
        using var service = CreateService(
            config,
            lfsInstalled: false,
            async notice =>
            {
                GitCommandResult staged = await RunGitAsync(
                    "diff",
                    "--cached",
                    "--name-only");
                Assert.That(staged.Stdout, Is.Empty);
                notices.Add(notice);
            });

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult count = await RunGitAsync("rev-list", "--count", "HEAD");
        Assert.Multiple(() =>
        {
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(notices[0], Is.TypeOf<VersionControlPolicyNotice.LargeMediaWithoutLfs>());
            Assert.That(
                ((VersionControlPolicyNotice.LargeMediaWithoutLfs)notices[0]).Path,
                Is.EqualTo("resources/large.mp4"));
        });
    }

    [Test]
    public async Task Large_media_without_lfs_warns_once_and_does_not_block_commits()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        var config = new VersionControlConfig
        {
            LargeMediaWarningThresholdMb = 1,
        };
        var notices = new List<VersionControlPolicyNotice>();
        var recordingRunner = new RecordingRunner(Runner);
        using var service = CreateService(
            config,
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            runner: recordingRunner);
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, new byte[(1024 * 1024) + 1]);

        CommitResult first = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);
        await File.AppendAllTextAsync(mediaPath, "more");
        CommitResult second = await service.CommitAllAsync(
            "large media update",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<CommitResult.Committed>());
            Assert.That(second, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(notices[0], Is.TypeOf<VersionControlPolicyNotice.LargeMediaWithoutLfs>());
            var notice = (VersionControlPolicyNotice.LargeMediaWithoutLfs)notices[0];
            Assert.That(notice.Path, Is.EqualTo("resources/large.mp4"));
            Assert.That(notice.SizeBytes, Is.GreaterThan(1024 * 1024));
            Assert.That(
                recordingRunner.Commands,
                Has.None.Matches<RecordedCommand>(static command =>
                    command.Arguments.FirstOrDefault() == "check-attr"));
        });
    }

    [Test]
    public async Task Unrelated_lfs_rule_does_not_suppress_large_media_notice()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "assets/*.psd filter=lfs diff=lfs merge=lfs -text\n");
        await WriteLargeMediaAsync(Root, "resources/large.mp4");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: true,
            notice =>
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
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(
                notices.Single(),
                Is.EqualTo(new VersionControlPolicyNotice.LargeMediaWithoutLfs(
                    "resources/large.mp4",
                    (1024 * 1024) + 1)));
        });
    }

    [Test]
    public async Task Matching_lfs_rule_suppresses_large_media_notice()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        await WriteLargeMediaAsync(Root, "resources/large.mp4");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: true,
            notice =>
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
            Assert.That(notices, Is.Empty);
        });
    }

    [Test]
    public async Task Mixed_large_media_candidates_warn_for_first_uncovered_path()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        await WriteLargeMediaAsync(Root, "resources/01-covered.mp4");
        await WriteLargeMediaAsync(Root, "resources/02-uncovered.wav");
        var notices = new List<VersionControlPolicyNotice>();
        var recordingRunner = new RecordingRunner(Runner);
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            runner: recordingRunner);

        CommitResult result = await service.CommitAllAsync(
            "mixed media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(
                notices.Single(),
                Is.EqualTo(new VersionControlPolicyNotice.LargeMediaWithoutLfs(
                    "resources/02-uncovered.wav",
                    (1024 * 1024) + 1)));
            RecordedCommand[] attributeQueries = recordingRunner.Commands
                .Where(static command => command.Arguments.FirstOrDefault() == "check-attr")
                .ToArray();
            Assert.That(attributeQueries, Has.Length.EqualTo(1));
            Assert.That(
                attributeQueries.Single().Options.StandardInput,
                Is.EqualTo("resources/01-covered.mp4\0resources/02-uncovered.wav\0"));
        });
    }

    [Test]
    public async Task Effective_lfs_query_chunks_all_covered_paths_below_the_capture_limit()
    {
        string[] paths = Enumerable.Range(0, 20_000)
            .Select(static index => $"resources/{index:D5}.mp4")
            .ToArray();
        var runner = new LfsAttributeEchoRunner();

        HashSet<string> covered = await GitCliVersionControlService.GetEffectiveLfsPathsAsync(
            Repository,
            runner,
            paths,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(covered.SetEquals(paths), Is.True);
            Assert.That(runner.Commands, Has.Count.EqualTo(3));
            Assert.That(
                runner.Commands,
                Has.All.Matches<RecordedCommand>(command =>
                    command.Options.MaxStdoutBytes == 256 * 1024
                    && command.Options.StandardInput!
                        .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                        .Sum(static path => System.Text.Encoding.UTF8.GetByteCount(path) + 12)
                    <= command.Options.MaxStdoutBytes));
        });
    }

    [Test]
    public async Task Truncated_custom_filter_preserves_the_valid_covered_prefix()
    {
        string[] paths = Enumerable.Range(0, 100)
            .Select(static index =>
                $"resources/{index:D3}-{new string('a', 2_480)}.mp4")
            .ToArray();
        var runner = new TruncatingCustomAttributeRunner();

        HashSet<string> covered = await GitCliVersionControlService.GetEffectiveLfsPathsAsync(
            Repository,
            runner,
            paths,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runner.Commands, Has.Count.EqualTo(1));
            Assert.That(covered.SetEquals(paths.Take(99)), Is.True);
            Assert.That(covered.Contains(paths[^1]), Is.False);
        });
    }

    [Test]
    public async Task Nested_literal_path_uses_outer_repository_and_exact_null_terminated_input()
    {
        string projectDirectory = OperatingSystem.IsWindows()
            ? "nested project [literal]"
            : ":nested project\n[literal]";
        const string mediaRelativePath = "resources/movie [draft] #1.mp4";
        string nestedRoot = Path.Combine(Root, projectDirectory);
        await CommitFileAsync(
            $"{projectDirectory}/project.bep",
            "initial\n",
            "initial");
        await File.WriteAllTextAsync(
            Path.Combine(nestedRoot, ".gitattributes"),
            "resources/*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        await WriteLargeMediaAsync(nestedRoot, mediaRelativePath);
        var repository = new RepositoryInfo(Root, nestedRoot);
        var notices = new List<VersionControlPolicyNotice>();
        var recordingRunner = new RecordingRunner(Runner);
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            repository,
            recordingRunner);

        CommitResult result = await service.CommitAllAsync(
            "literal media",
            SnapshotKind.Manual,
            CancellationToken.None);

        RecordedCommand query = recordingRunner.Commands.Single(static command =>
            command.Arguments.FirstOrDefault() == "check-attr");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Is.Empty);
            Assert.That(query.Repository, Is.EqualTo(repository));
            Assert.That(
                query.Arguments,
                Is.EqualTo(new[] { "check-attr", "--stdin", "-z", "filter" }));
            Assert.That(
                query.Options.StandardInput,
                Is.EqualTo($"{projectDirectory}/{mediaRelativePath}\0"));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(query.Options.StandardInput, Does.StartWith(":").And.Contain("\n"));
            }

            Assert.That(query.Options.UseLiteralPathspecs, Is.True);
        });
    }

    [TestCase(CheckAttributeFault.Malformed)]
    [TestCase(CheckAttributeFault.Truncated)]
    [TestCase(CheckAttributeFault.StandardError)]
    [TestCase(CheckAttributeFault.Unset)]
    [TestCase(CheckAttributeFault.Unspecified)]
    [TestCase(CheckAttributeFault.PathMismatch)]
    [TestCase(CheckAttributeFault.CommandFailure)]
    [TestCase(CheckAttributeFault.Timeout)]
    [TestCase(CheckAttributeFault.IoFailure)]
    public async Task Non_exact_lfs_attribute_result_does_not_suppress_large_media_notice(
        CheckAttributeFault fault)
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/*.mp4 filter=lfs diff=lfs merge=lfs -text\n");
        await WriteLargeMediaAsync(Root, "resources/large.mp4");
        var notices = new List<VersionControlPolicyNotice>();
        var faultRunner = new CheckAttributeFaultRunner(Runner, fault);
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            runner: faultRunner);

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(
                notices.Single(),
                Is.EqualTo(new VersionControlPolicyNotice.LargeMediaWithoutLfs(
                    "resources/large.mp4",
                    (1024 * 1024) + 1)));
        });
    }

    [Test]
    public async Task Large_media_removed_during_attribute_query_does_not_block_commit()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "assets/*.psd filter=lfs diff=lfs merge=lfs -text\n");
        const string mediaRelativePath = "resources/large.mp4";
        await WriteLargeMediaAsync(Root, mediaRelativePath);
        string mediaPath = Path.Combine(Root, mediaRelativePath);
        var notices = new List<VersionControlPolicyNotice>();
        var deletingRunner = new DeleteDuringAttributeQueryRunner(Runner, mediaPath);
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            runner: deletingRunner);

        CommitResult result = await service.CommitAllAsync(
            "media removed",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(File.Exists(mediaPath), Is.False);
            Assert.That(notices, Is.Empty);
        });
    }

    [Test]
    public async Task First_remote_with_active_lfs_shows_one_quota_notice()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        var config = new VersionControlConfig { UseLfsWhenAvailable = true };
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            config,
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await service.SetRemoteAsync(
            Path.Combine(Root, "remote-one.git"),
            CancellationToken.None);
        await RunGitAsync("remote", "remove", "origin");
        await service.SetRemoteAsync(
            Path.Combine(Root, "remote-two.git"),
            CancellationToken.None);

        Assert.That(
            notices,
            Is.EqualTo(new[]
            {
                new VersionControlPolicyNotice.LfsRemoteQuota(),
            }));
    }

    [Test]
    public async Task Existing_remote_with_active_lfs_shows_the_quota_notice_when_reconfigured()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        await RunGitAsync("remote", "add", "origin", Path.Combine(Root, "old.git"));
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig { UseLfsWhenAvailable = true },
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await service.SetRemoteAsync(
            Path.Combine(Root, "replacement.git"),
            CancellationToken.None);

        Assert.That(
            notices,
            Is.EqualTo(new[] { new VersionControlPolicyNotice.LfsRemoteQuota() }));
    }

    [Test]
    public async Task Existing_remote_with_active_lfs_shows_the_quota_notice_during_initialization()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "resources/**/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        await RunGitAsync("remote", "add", "origin", Path.Combine(Root, "existing.git"));
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig { UseLfsWhenAvailable = false },
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await service.InitializeAsync(
            new InitOptions(Repository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        Assert.That(
            notices,
            Is.EqualTo(new[] { new VersionControlPolicyNotice.LfsRemoteQuota() }));
    }

    [Test]
    public async Task Warning_presentation_failure_never_blocks_the_commit()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        var config = new VersionControlConfig
        {
            LargeMediaWarningThresholdMb = 1,
        };
        using var service = CreateService(
            config,
            lfsInstalled: false,
            _ => throw new InvalidOperationException("Notification surface unavailable."));
        string mediaPath = Path.Combine(Root, "resources", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(mediaPath, new byte[(1024 * 1024) + 1]);

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<CommitResult.Committed>());
    }

    [Test]
    public async Task Missing_identity_notice_is_once_per_repository_and_commit_resumes_after_identity_is_set()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        foreach (SnapshotKind kind in new[]
                 {
                     SnapshotKind.Save,
                     SnapshotKind.Close,
                     SnapshotKind.Safety,
                     SnapshotKind.Restore,
                     SnapshotKind.Recovery,
                 })
        {
            Assert.That(
                await service.CommitAllAsync("automatic snapshot", kind, CancellationToken.None),
                Is.TypeOf<CommitResult.SkippedNoIdentity>());
        }

        await service.SetLocalIdentityAsync(
            new GitIdentity("Local User", "local@example.invalid"),
            CancellationToken.None);
        CommitResult committed = await service.CommitAllAsync(
            "automatic snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(notices[0], Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    [Test]
    public async Task Missing_identity_notice_is_repository_local()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");

        string secondRoot = CreateTemporaryDirectory();
        var secondRepository = new RepositoryInfo(secondRoot, secondRoot);
        GitCliRunner secondRunner = CreateRunner();
        await secondRunner.RunAsync(
            secondRepository,
            ["init", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.name", "Second User"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.email", "second@example.invalid"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "project.bep"), "initial\n");
        await secondRunner.RunAsync(
            secondRepository,
            ["add", "--", "project.bep"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["commit", "-m", "initial"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.name", ""],
            GitCommandOptions.Local,
            CancellationToken.None);
        await secondRunner.RunAsync(
            secondRepository,
            ["config", "--local", "user.email", ""],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "project.bep"), "changed\n");

        var notices = new List<VersionControlPolicyNotice>();
        Task PresentNotice(VersionControlPolicyNotice notice)
        {
            notices.Add(notice);
            return Task.CompletedTask;
        }

        using var first = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            PresentNotice);
        using var second = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            PresentNotice,
            secondRepository);

        await first.CommitAllAsync("automatic snapshot", SnapshotKind.Save, CancellationToken.None);
        await second.CommitAllAsync("automatic snapshot", SnapshotKind.Save, CancellationToken.None);

        Assert.That(
            notices.Select(static notice => notice.GetType()),
            Is.EqualTo(new[]
            {
                typeof(VersionControlPolicyNotice.MissingIdentity),
                typeof(VersionControlPolicyNotice.MissingIdentity),
            }));
    }

    [Test]
    public async Task Project_checkpoint_creation_reports_missing_identity_before_mutation()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await File.WriteAllTextAsync(Path.Combine(Root, "local.belm"), "local edit\n");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        Assert.ThrowsAsync<GitIdentityRequiredException>(
            async () => await service.CreateProjectCheckpointAsync(
                "safety checkpoint",
                CancellationToken.None));
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        GitCommandResult checkpoints = await RunGitAsync(
            "for-each-ref",
            "--format=%(refname)",
            "refs/beutl/checkpoints");

        Assert.Multiple(() =>
        {
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(checkpoints.Stdout, Is.Empty);
            Assert.That(notices.Single(), Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    [Test]
    public async Task Project_tree_skip_reports_missing_identity()
    {
        await CommitFileAsync("project.bep", "original\n", "original");
        string original = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "current\n", "current");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });
        CheckedOutBranchTip current = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");

        CommitResult result = await service.CommitProjectTreeAsync(
            current,
            original,
            "restore snapshot",
            SnapshotKind.Restore,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.SkippedNoIdentity>());
            Assert.That(notices.Single(), Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    private GitCliVersionControlService CreateService(
        VersionControlConfig config,
        bool lfsInstalled,
        Func<VersionControlPolicyNotice, Task> presentNotice,
        RepositoryInfo? repository = null,
        IGitCliRunner? runner = null)
    {
        if (runner is not null)
        {
            return new GitCliVersionControlService(
                CreateInstalledLocator(lfsInstalled, config),
                repository ?? Repository,
                watcher: null,
                _ => runner,
                policyNoticeSink: (notice, _) => presentNotice(notice));
        }

        return new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled, config),
            repository ?? Repository,
            static () => true,
            (notice, _) => presentNotice(notice));
    }

    private static VersionControlConfig CreateLargeMediaConfig()
        => new() { LargeMediaWarningThresholdMb = 1 };

    private static async Task WriteLargeMediaAsync(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[(1024 * 1024) + 1]);
    }

    private sealed record RecordedCommand(
        RepositoryInfo Repository,
        IReadOnlyList<string> Arguments,
        GitCommandOptions Options);

    private sealed class RecordingRunner(IGitCliRunner inner) : IGitCliRunner
    {
        public List<RecordedCommand> Commands { get; } = [];

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add(new RecordedCommand(repository, [.. arguments], options));
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

    private sealed class LfsAttributeEchoRunner : IGitCliRunner
    {
        public List<RecordedCommand> Commands { get; } = [];

        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(new RecordedCommand(repository, [.. arguments], options));
            string[] paths = options.StandardInput!
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
            string stdout = string.Concat(
                paths.Select(static path => $"{path}\0filter\0lfs\0"));
            return Task.FromResult(new GitCommandResult(0, stdout, string.Empty));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class TruncatingCustomAttributeRunner : IGitCliRunner
    {
        public List<RecordedCommand> Commands { get; } = [];

        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(new RecordedCommand(repository, [.. arguments], options));
            string[] paths = options.StandardInput!
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
            string stdout = string.Concat(paths.Select((path, index) =>
                $"{path}\0filter\0{(index == paths.Length - 1 ? new string('x', 20_000) : "lfs")}\0"));
            int captureLimit = options.MaxStdoutBytes!.Value;
            Assert.That(stdout.Length, Is.GreaterThan(captureLimit));
            return Task.FromResult(new GitCommandResult(
                0,
                stdout[..captureLimit],
                string.Empty,
                StdoutTruncated: true));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    public enum CheckAttributeFault
    {
        Malformed,
        Truncated,
        StandardError,
        Unset,
        Unspecified,
        PathMismatch,
        CommandFailure,
        Timeout,
        IoFailure,
    }

    private sealed class DeleteDuringAttributeQueryRunner(
        IGitCliRunner inner,
        string path) : IGitCliRunner
    {
        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "check-attr")
            {
                File.Delete(path);
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

    private sealed class CheckAttributeFaultRunner(
        IGitCliRunner inner,
        CheckAttributeFault fault) : IGitCliRunner
    {
        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "check-attr"
                && fault == CheckAttributeFault.CommandFailure)
            {
                throw new GitOperationException(128, "attribute query failed");
            }

            if (arguments.FirstOrDefault() == "check-attr"
                && fault == CheckAttributeFault.Timeout)
            {
                throw new TimeoutException("attribute query timed out");
            }

            if (arguments.FirstOrDefault() == "check-attr"
                && fault == CheckAttributeFault.IoFailure)
            {
                throw new IOException("attribute query failed during I/O");
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (arguments.FirstOrDefault() != "check-attr")
            {
                return result;
            }

            string path = options.StandardInput![..^1];
            return fault switch
            {
                CheckAttributeFault.Malformed => result with
                {
                    Stdout = $"{path}\0filter\0lfs",
                },
                CheckAttributeFault.Truncated => result with { StdoutTruncated = true },
                CheckAttributeFault.StandardError => result with { Stderr = "attribute warning\n" },
                CheckAttributeFault.Unset => result with
                {
                    Stdout = $"{path}\0filter\0unset\0",
                },
                CheckAttributeFault.Unspecified => result with
                {
                    Stdout = $"{path}\0filter\0unspecified\0",
                },
                CheckAttributeFault.PathMismatch => result with
                {
                    Stdout = $"other/{path}\0filter\0lfs\0",
                },
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            };
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }
}
