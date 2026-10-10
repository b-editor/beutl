using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
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
    public async Task Ignored_project_files_are_reported_once_and_left_out_of_snapshots()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            "**/.beutl/\n*.[tT][mM][pP]\nfootage/\n*.wav\n");
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await WriteProjectFileAsync("footage/clip.mp4", "clip\n");
        await WriteProjectFileAsync("audio/voice.wav", "voice\n");
        await WriteProjectFileAsync(".beutl/view-state.json", "{}\n");
        await WriteProjectFileAsync("render-cache.tmp", "scratch\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await WriteProjectFileAsync("project.bep", "first\n");
        CommitResult first = await service.CommitAllAsync(
            "first",
            SnapshotKind.Save,
            CancellationToken.None);
        await WriteProjectFileAsync("project.bep", "second\n");
        CommitResult second = await service.CommitAllAsync(
            "second",
            SnapshotKind.Save,
            CancellationToken.None);

        GitCommandResult trackedFiles = await RunGitAsync("ls-tree", "-r", "--name-only", "HEAD");
        Assert.Multiple(() =>
        {
            // The user's own ignore rules decide, as with plain Git; the snapshot is not refused.
            Assert.That(first, Is.TypeOf<CommitResult.Committed>());
            Assert.That(second, Is.TypeOf<CommitResult.Committed>());
            Assert.That(trackedFiles.Stdout, Does.Not.Contain("footage/"));
            Assert.That(trackedFiles.Stdout, Does.Not.Contain("voice.wav"));
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(
                notices.OfType<VersionControlPolicyNotice.IgnoredProjectFiles>().Single().Paths,
                Is.EquivalentTo(new[] { "audio/voice.wav", "footage/" }));
        });
    }

    [Test]
    public async Task Ignored_project_files_notice_names_the_project_folder_an_enclosing_repository_ignores()
    {
        string projectRoot = Path.Combine(Root, "projects", "movie");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "projects/\n");
        await RunGitAsync("add", "--", ".gitignore");
        await RunGitAsync("commit", "-m", "ignore projects");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        var repository = new RepositoryInfo(Root, projectRoot);
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            repository: repository);

        // Nothing in an ignored folder ever shows as changed, so only the notice can tell.
        CommitResult result = await service.CommitAllAsync(
            "snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(
                notices.OfType<VersionControlPolicyNotice.IgnoredProjectFiles>().Single().Paths,
                Is.EqualTo(new[] { "projects/" }));
        });
    }

    [Test]
    public async Task Snapshot_refuses_a_project_file_that_the_ignore_rules_leave_out()
    {
        await CommitFileAsync("notes.txt", "baseline\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.bep\n");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile);

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "snapshot",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("project.bep"));
            Assert.That(
                RunGitAsync("rev-parse", "HEAD").Result.Stdout.Trim(),
                Is.EqualTo(baseTip));
        });
    }

    // check-ignore reads its input as pathspecs, so a folder name that starts with a colon would
    // otherwise be taken as pathspec magic and a different path checked.
    [Test]
    public async Task Snapshot_refuses_an_ignored_project_file_in_a_folder_whose_name_starts_with_a_colon()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows does not allow a colon in a folder name.");
        }

        await CommitFileAsync("notes.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, ":movie");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "/:movie/*.bep\n");
        string projectFile = Path.Combine(projectRoot, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile,
            repository: new RepositoryInfo(Root, projectRoot));

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "snapshot",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(":movie/project.bep"));
            Assert.That(
                RunGitAsync("rev-parse", "HEAD").Result.Stdout.Trim(),
                Is.EqualTo(baseTip));
        });
    }

    [Test]
    public async Task Snapshot_records_a_tracked_project_file_that_an_ignore_rule_matches()
    {
        string projectFile = Path.Combine(Root, "project.bep");
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.bep\n");
        await File.WriteAllTextAsync(projectFile, "changed\n");
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile);

        CommitResult result = await service.CommitAllAsync(
            "snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                RunGitAsync("show", "HEAD:project.bep").Result.Stdout,
                Is.EqualTo("changed\n"));
        });
    }

    [Test]
    public async Task Ignored_scratch_files_do_not_crowd_reportable_files_out_of_the_notice()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            "**/.beutl/\n*.[tT][mM][pP]\n*.wav\n");
        await CommitFileAsync("project.bep", "initial\n", "initial");
        // Sorted ahead of the media file, these alone would overflow the capture limit below.
        for (int i = 0; i < 64; i++)
        {
            await WriteProjectFileAsync($"render-{i:D3}.tmp", "scratch\n");
        }

        await WriteProjectFileAsync("voice.wav", "voice\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            runner: new IgnoredListingCapRunner(Runner, maxStdoutBytes: 256));

        await WriteProjectFileAsync("project.bep", "changed\n");
        CommitResult result = await service.CommitAllAsync(
            "snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        var notice = notices.OfType<VersionControlPolicyNotice.IgnoredProjectFiles>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notice.Paths, Is.EqualTo(new[] { "voice.wav" }));
            Assert.That(notice.Truncated, Is.False);
        });
    }

    [Test]
    public async Task Ignored_project_files_notice_says_when_the_listing_was_cut_off()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.wav\n");
        await CommitFileAsync("project.bep", "initial\n", "initial");
        string[] ignoredFiles = Enumerable.Range(0, 64)
            .Select(static i => $"voice-{i:D3}.wav")
            .ToArray();
        foreach (string ignoredFile in ignoredFiles)
        {
            await WriteProjectFileAsync(ignoredFile, "voice\n");
        }

        var notices = new List<VersionControlPolicyNotice>();
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(false, new VersionControlConfig()),
            Repository,
            watcher: null,
            _ => new IgnoredListingCapRunner(Runner, maxStdoutBytes: 100),
            logger: logger,
            policyNoticeSink: (notice, _) =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await WriteProjectFileAsync("project.bep", "changed\n");
        await service.CommitAllAsync("snapshot", SnapshotKind.Save, CancellationToken.None);

        var notice = notices.OfType<VersionControlPolicyNotice.IgnoredProjectFiles>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(notice.Truncated, Is.True);
            // 100 bytes hold seven whole 14-byte entries; the cut-off eighth is not reported.
            Assert.That(notice.Paths, Is.EqualTo(ignoredFiles.Take(7)));
            // The notice shows only a few paths, so the full list is logged with it.
            Assert.That(
                logger.Entries,
                Has.One.Matches<(LogLevel Level, string Message)>(entry =>
                    entry.Level == LogLevel.Information
                    && ignoredFiles.Take(7).All(entry.Message.Contains)));
        });
    }

    [Test]
    public async Task CommitAllAsync_refuses_an_untracked_project_file_that_an_ignore_rule_matches()
    {
        await CommitFileAsync(".gitignore", "*.bep\n", "ignore project files");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await WriteProjectFileAsync("notes.txt", "notes\n");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile);

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "snapshot",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'project.bep'"));
            Assert.That(exception.Message, Does.Contain("ignore rules"));
            Assert.That(
                RunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult().Stdout.Trim(),
                Is.EqualTo(baseTip));
        });
    }

    [Test]
    public async Task InitializeAsync_refuses_an_untracked_project_file_that_an_ignore_rule_matches()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.bep\n");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile);

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(Repository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        GitCommandResult commits = await RunGitAsync("rev-list", "--all", "--count");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'project.bep'"));
            Assert.That(commits.Stdout.Trim(), Is.EqualTo("0"));
        });
    }

    [Test]
    public async Task A_tracked_project_file_that_an_ignore_rule_matches_still_snapshots()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await CommitFileAsync(".gitignore", "*.bep\n", "ignore project files");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "changed\n");
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile);

        CommitResult result = await service.CommitAllAsync(
            "snapshot",
            SnapshotKind.Save,
            CancellationToken.None);

        GitCommandResult committed = await RunGitAsync("show", "HEAD:project.bep");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(committed.Stdout, Is.EqualTo("changed\n"));
        });
    }

    [Test]
    public async Task Large_media_acknowledgement_failure_does_not_block_commit()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await WriteLargeMediaAsync(Root, "resources/large.mp4");
        var notices = new List<VersionControlPolicyNotice>();
        var runner = new LargeMediaAcknowledgementFailingRunner(Runner);
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            runner: runner);

        CommitResult result = await service.CommitAllAsync(
            "large media",
            SnapshotKind.Manual,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(notices, Has.Count.EqualTo(1));
            Assert.That(runner.AcknowledgementWriteAttempts, Is.EqualTo(1));
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
    public async Task Inherited_lfs_attributes_show_the_quota_notice_for_a_nested_project()
    {
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "nested-project/resources/*.[mM][pP]4 filter=lfs diff=lfs merge=lfs -text\n");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, ".gitattributes"),
            "*.bep text eol=lf\n");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await RunGitAsync("add", "--", ".gitattributes", "nested-project");
        await RunGitAsync("commit", "-m", "nested project");
        var notices = new List<VersionControlPolicyNotice>();
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = CreateService(
            new VersionControlConfig { UseLfsWhenAvailable = false },
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            repository);

        await service.SetRemoteAsync(
            Path.Combine(Root, "nested-remote.git"),
            CancellationToken.None);

        Assert.That(
            notices,
            Is.EqualTo(new[] { new VersionControlPolicyNotice.LfsRemoteQuota() }));
    }

    [Test]
    public async Task Lfs_rule_scoped_to_tracked_media_outside_resources_shows_the_quota_notice()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await CommitFileAsync("media/clip.mp4", "clip\n", "media");
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitattributes"),
            "media/** filter=lfs diff=lfs merge=lfs -text\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            new VersionControlConfig { UseLfsWhenAvailable = false },
            lfsInstalled: true,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            });

        await service.SetRemoteAsync(
            Path.Combine(Root, "media-remote.git"),
            CancellationToken.None);

        Assert.That(
            notices,
            Is.EqualTo(new[] { new VersionControlPolicyNotice.LfsRemoteQuota() }));
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
    public async Task Large_media_added_while_the_identity_prompt_is_open_is_still_reported()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        var notices = new List<VersionControlPolicyNotice>();
        using var service = CreateService(
            CreateLargeMediaConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            requestIdentity: async () =>
            {
                await WriteLargeMediaAsync(Root, "resources/late.mp4");
                return new GitIdentity("Prompted User", "prompted@example.invalid");
            });

        await service.CommitAllAsync("beutl: snapshot on save", SnapshotKind.Save, CancellationToken.None);

        Assert.That(
            notices.OfType<VersionControlPolicyNotice.LargeMediaWithoutLfs>().Single().Path,
            Is.EqualTo("resources/late.mp4"));
    }

    [Test]
    public async Task Project_files_ignored_while_the_identity_prompt_is_open_are_still_reported()
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
            },
            requestIdentity: async () =>
            {
                await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.log\n");
                await File.WriteAllTextAsync(Path.Combine(Root, "render.log"), "late\n");
                return new GitIdentity("Prompted User", "prompted@example.invalid");
            });

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                notices.OfType<VersionControlPolicyNotice.IgnoredProjectFiles>().Single().Paths,
                Is.EqualTo(new[] { "render.log" }));
        });
    }

    [Test]
    public async Task Project_file_ignored_while_the_identity_prompt_is_open_is_refused()
    {
        await CommitFileAsync("notes.txt", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        string projectFile = Path.Combine(Root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        string baseTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            _ => Task.CompletedTask,
            projectFile: projectFile,
            requestIdentity: async () =>
            {
                await File.WriteAllTextAsync(Path.Combine(Root, ".gitignore"), "*.bep\n");
                return new GitIdentity("Prompted User", "prompted@example.invalid");
            });

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("project.bep"));
            Assert.That(RunGitAsync("rev-parse", "HEAD").Result.Stdout.Trim(), Is.EqualTo(baseTip));
        });
    }

    [Test]
    public async Task First_automatic_snapshot_without_identity_asks_once_and_records_the_snapshot()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        var notices = new List<VersionControlPolicyNotice>();
        int requests = 0;
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            requestIdentity: () =>
            {
                requests++;
                return Task.FromResult<GitIdentity?>(
                    new GitIdentity("Prompted User", "prompted@example.invalid"));
            });

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        GitCommandResult author = await RunGitAsync("log", "-1", "--format=%an <%ae>");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                author.Stdout.Trim(),
                Is.EqualTo("Prompted User <prompted@example.invalid>"));
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(notices, Is.Empty);
        });
    }

    [Test]
    public async Task Dismissed_identity_request_is_not_repeated_for_later_automatic_snapshots()
    {
        await CommitFileAsync("project.bep", "initial\n", "initial");
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "changed\n");
        var notices = new List<VersionControlPolicyNotice>();
        int requests = 0;
        using var service = CreateService(
            new VersionControlConfig(),
            lfsInstalled: false,
            notice =>
            {
                notices.Add(notice);
                return Task.CompletedTask;
            },
            requestIdentity: () =>
            {
                requests++;
                return Task.FromResult<GitIdentity?>(null);
            });

        foreach (SnapshotKind kind in new[] { SnapshotKind.Save, SnapshotKind.Safety })
        {
            Assert.That(
                await service.CommitAllAsync("automatic snapshot", kind, CancellationToken.None),
                Is.TypeOf<CommitResult.SkippedNoIdentity>());
        }

        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(1));
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
    public async Task Restore_reports_missing_identity_before_changing_files()
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
        await RunGitAsync("config", "--local", "user.name", "");
        await RunGitAsync("config", "--local", "user.email", "");

        CommitResult result = await service.ExecuteExclusiveAsync(
            transaction => transaction.RestoreProjectTreeAsync(
                original,
                "restore snapshot",
                SnapshotKind.Restore,
                CancellationToken.None),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.SkippedNoIdentity>());
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("current\n"));
            Assert.That(notices.Single(), Is.TypeOf<VersionControlPolicyNotice.MissingIdentity>());
        });
    }

    private GitCliVersionControlService CreateService(
        VersionControlConfig config,
        bool lfsInstalled,
        Func<VersionControlPolicyNotice, Task> presentNotice,
        RepositoryInfo? repository = null,
        IGitCliRunner? runner = null,
        string? projectFile = null,
        Func<Task<GitIdentity?>>? requestIdentity = null)
    {
        Func<CancellationToken, Task<GitIdentity?>>? identityRequest =
            requestIdentity is null ? null : _ => requestIdentity();
        if (runner is not null)
        {
            return new GitCliVersionControlService(
                CreateInstalledLocator(lfsInstalled, config),
                repository ?? Repository,
                watcher: null,
                _ => runner,
                policyNoticeSink: (notice, _) => presentNotice(notice),
                projectFile: projectFile,
                identityRequest: identityRequest);
        }

        return new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled, config),
            repository ?? Repository,
            static () => true,
            (notice, _) => presentNotice(notice),
            projectFile,
            identityRequest);
    }

    private async Task WriteProjectFileAsync(string relativePath, string contents)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
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

    // Lowers the capture limit of the ignored-files listing so a few entries overflow it.
    private sealed class IgnoredListingCapRunner(IGitCliRunner inner, int maxStdoutBytes) : IGitCliRunner
    {
        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "ls-files" && arguments.Contains("--ignored"))
            {
                options = options with { MaxStdoutBytes = maxStdoutBytes };
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

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class LargeMediaAcknowledgementFailingRunner(IGitCliRunner inner)
        : IGitCliRunner
    {
        public int AcknowledgementWriteAttempts { get; private set; }

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
                && arguments[1] == "--local"
                && arguments[2].StartsWith(
                    "beutl.largeMediaNoticeShown-",
                    StringComparison.Ordinal)
                && arguments[3] == "true")
            {
                AcknowledgementWriteAttempts++;
                return Task.FromException<GitCommandResult>(
                    new IOException("simulated acknowledgement write failure"));
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
