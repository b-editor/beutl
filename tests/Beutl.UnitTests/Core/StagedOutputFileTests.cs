using Beutl.IO;

namespace Beutl.UnitTests.Core;

public class StagedOutputFileTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Linked_outputs_keep_the_link_and_publish_to_its_target(bool cancel, bool missingTarget)
    {
        string root = Directory.CreateTempSubdirectory("beutl-linked-output-").FullName;
        try
        {
            string target = Path.Combine(root, "actual-output");
            string link = Path.Combine(root, "selected.mp4");
            if (!missingTarget) File.WriteAllText(target, "original");
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
                                       || ex is IOException && OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
            {
                Assert.Ignore("Symlink creation is not available.");
            }
            using (var output = new StagedOutputFile(link))
            {
                Assert.That(Path.GetFileName(output.TemporaryPath), Is.EqualTo("selected.mp4"));
                File.WriteAllText(output.TemporaryPath, "complete");
                if (cancel) Assert.Throws<OperationCanceledException>(() => output.Commit(new CancellationToken(true)));
                else output.Commit(CancellationToken.None);
            }
            Assert.That(new FileInfo(link).LinkTarget, Is.Not.Null);
            Assert.That(File.Exists(target), Is.EqualTo(!cancel || !missingTarget));
            if (File.Exists(target)) Assert.That(File.ReadAllText(target), Is.EqualTo(cancel ? "original" : "complete"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Several_files_reach_the_destination_folder_and_leave_the_others_alone()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "frame_0.png"), "old");
            File.WriteAllText(Path.Combine(root, "frame_9.png"), "unrelated");
            using (var output = new StagedOutputFile(Path.Combine(root, "frame.png")))
            {
                string[] files = [StagedFile(output, "frame_0.png", "new 0"), StagedFile(output, "frame_1.png", "new 1")];

                IReadOnlyList<string> published = output.Commit(files, CancellationToken.None);

                Assert.That(published, Is.EqualTo(new[] { Path.Combine(root, "frame_0.png"), Path.Combine(root, "frame_1.png") }));
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(root, "frame_0.png")), Is.EqualTo("new 0"));
                Assert.That(File.ReadAllText(Path.Combine(root, "frame_1.png")), Is.EqualTo("new 1"));
                Assert.That(File.ReadAllText(Path.Combine(root, "frame_9.png")), Is.EqualTo("unrelated"));
                Assert.That(File.Exists(Path.Combine(root, "frame.png")), Is.False);
                Assert.That(Directory.GetDirectories(root), Is.Empty);
            });
        }
        finally { Directory.Delete(root, true); }
    }

    // The encoder chose the name, not the user, so nothing should reach through a link that happens to have it.
    [TestCase(true)]
    [TestCase(false)]
    public void A_link_with_the_name_of_an_extra_file_is_replaced_and_not_followed(bool fail)
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            string target = Path.Combine(root, "elsewhere.txt");
            string link = Path.Combine(root, "frame_0.png");
            File.WriteAllText(target, "untouched");
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
                                       || ex is IOException && OperatingSystem.IsWindows())
            {
                Assert.Ignore("Symlink creation is not available.");
            }

            // A folder in the way of the second file fails the commit after the link has been replaced.
            if (fail) Directory.CreateDirectory(Path.Combine(root, "frame_1.png"));
            using (var output = new StagedOutputFile(Path.Combine(root, "frame.png")))
            {
                string[] files = [StagedFile(output, "frame_0.png", "new 0"), StagedFile(output, "frame_1.png", "new 1")];

                if (fail) Assert.Catch(() => output.Commit(files, CancellationToken.None));
                else output.Commit(files, CancellationToken.None);
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(target), Is.EqualTo("untouched"));
                Assert.That(new FileInfo(link).LinkTarget, fail ? Is.EqualTo(target) : Is.Null);
                Assert.That(File.ReadAllText(link), Is.EqualTo(fail ? "untouched" : "new 0"));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    // The second file cannot take the place of a folder, so the first one is put back.
    [Test]
    public void A_failure_part_way_puts_back_what_was_already_replaced()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "frame_0.png"), "old");
            Directory.CreateDirectory(Path.Combine(root, "frame_1.png"));
            using (var output = new StagedOutputFile(Path.Combine(root, "frame.png")))
            {
                string[] files =
                [
                    StagedFile(output, "frame_0.png", "new 0"),
                    StagedFile(output, "frame_1.png", "new 1"),
                    StagedFile(output, "frame_2.png", "new 2")
                ];

                Assert.Catch(() => output.Commit(files, CancellationToken.None));
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(root, "frame_0.png")), Is.EqualTo("old"));
                Assert.That(File.Exists(Path.Combine(root, "frame_2.png")), Is.False);
                Assert.That(Directory.GetDirectories(root).Select(Path.GetFileName), Is.EqualTo(new[] { "frame_1.png" }));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    // Another program holding a published file open is what stops the file underneath it from coming back.
    [Test]
    public void A_previous_file_that_cannot_be_put_back_survives_in_a_folder_the_failure_names()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "frame_0.png"), "old");
            Directory.CreateDirectory(Path.Combine(root, "frame_1.png"));
            IOException failure;
            using (var output = new StagedOutputFile(Path.Combine(root, "frame.png"))
                   {
                       RestoreFile = (_, _) => throw new IOException("in use")
                   })
            {
                string[] files = [StagedFile(output, "frame_0.png", "new 0"), StagedFile(output, "frame_1.png", "new 1")];

                failure = Assert.Throws<IOException>(() => output.Commit(files, CancellationToken.None))!;
            }

            string kept = Directory.GetDirectories(root).Single(path => path.EndsWith("-previous", StringComparison.Ordinal));
            Assert.Multiple(() =>
            {
                Assert.That(failure.Message, Does.Contain(kept));
                Assert.That(File.ReadAllText(Path.Combine(kept, "frame_0.png")), Is.EqualTo("old"));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Two_files_that_would_reach_the_same_path_are_refused()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            string target = Path.Combine(root, "frame_0.png");
            string link = Path.Combine(root, "selected.png");
            File.WriteAllText(target, "old");
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
                                       || ex is IOException && OperatingSystem.IsWindows())
            {
                Assert.Ignore("Symlink creation is not available.");
            }

            using (var output = new StagedOutputFile(link))
            {
                File.WriteAllText(output.TemporaryPath, "chosen");
                string[] files = [output.TemporaryPath, StagedFile(output, "frame_0.png", "frame")];

                Assert.Throws<InvalidOperationException>(() => output.Commit(files, CancellationToken.None));
            }

            Assert.That(File.ReadAllText(target), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void A_file_that_was_not_written_stops_the_others_from_moving()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "frame_0.png"), "old");
            using (var output = new StagedOutputFile(Path.Combine(root, "frame.png")))
            {
                string written = StagedFile(output, "frame_0.png", "new 0");
                string missing = Path.Combine(Path.GetDirectoryName(written)!, "frame_1.png");

                Assert.Throws<FileNotFoundException>(() => output.Commit([written, missing], CancellationToken.None));
            }

            Assert.That(File.ReadAllText(Path.Combine(root, "frame_0.png")), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase("outside.png")]
    [TestCase("nested/inside.png")]
    public void A_file_outside_the_staging_folder_is_refused(string relativePath)
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "frame_0.png"), "old");
            using var output = new StagedOutputFile(Path.Combine(root, "frame.png"));
            string staged = StagedFile(output, "frame_0.png", "new 0");
            string outside = relativePath.StartsWith("nested", StringComparison.Ordinal)
                ? Path.Combine(Path.GetDirectoryName(staged)!, relativePath)
                : Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
            File.WriteAllText(outside, "elsewhere");

            Assert.Throws<InvalidOperationException>(() => output.Commit([staged, outside], CancellationToken.None));
            Assert.That(File.ReadAllText(Path.Combine(root, "frame_0.png")), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Cancellation_before_the_commit_of_several_files_moves_nothing()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "frame_0.png"), "old");
            using (var output = new StagedOutputFile(Path.Combine(root, "frame.png")))
            {
                string[] files = [StagedFile(output, "frame_0.png", "new 0"), StagedFile(output, "frame_1.png", "new 1")];

                Assert.Throws<OperationCanceledException>(() => output.Commit(files, new CancellationToken(canceled: true)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(root, "frame_0.png")), Is.EqualTo("old"));
                Assert.That(File.Exists(Path.Combine(root, "frame_1.png")), Is.False);
                Assert.That(Directory.GetDirectories(root), Is.Empty);
            });
        }
        finally { Directory.Delete(root, true); }
    }

    // What a single-file encoder gets by default stays the plain rename, including through a link.
    [Test]
    public void The_output_file_alone_publishes_to_the_destination()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "out.mp4");
        try
        {
            File.WriteAllText(destination, "old");
            using (var output = new StagedOutputFile(destination))
            {
                File.WriteAllText(output.TemporaryPath, "complete");

                Assert.That(output.Commit([output.TemporaryPath], CancellationToken.None), Is.EqualTo(new[] { destination }));
            }

            Assert.That(File.ReadAllText(destination), Is.EqualTo("complete"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string StagedFile(StagedOutputFile output, string name, string contents)
    {
        string path = Path.Combine(Path.GetDirectoryName(output.TemporaryPath)!, name);
        File.WriteAllText(path, contents);
        return path;
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void Only_a_successful_commit_replaces_the_existing_output(bool cancel)
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "existing output.mp4");
        try
        {
            File.WriteAllText(destination, "old complete contents");
            using (var output = new StagedOutputFile(destination))
            {
                Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(output.TemporaryPath)), Is.EqualTo(root));
                Assert.That(Path.GetFileName(output.TemporaryPath), Is.EqualTo(Path.GetFileName(destination)));
                File.WriteAllText(output.TemporaryPath, "new");
                Assert.That(File.ReadAllText(destination), Is.EqualTo("old complete contents"));
                if (cancel)
                    Assert.Throws<OperationCanceledException>(() => output.Commit(new CancellationToken(true)));
                else
                    output.Commit(CancellationToken.None);
            }

            Assert.That(File.ReadAllText(destination), Is.EqualTo(cancel ? "old complete contents" : "new"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void A_missing_output_cannot_replace_an_existing_file()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "existing.mp4");
        try
        {
            File.WriteAllText(destination, "old");
            using (var output = new StagedOutputFile(destination))
                Assert.Throws<FileNotFoundException>(() => output.Commit(CancellationToken.None));

            Assert.That(File.ReadAllText(destination), Is.EqualTo("old"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
    [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite)]
    public void Commit_preserves_the_current_destination_permissions(UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required.");
            return;
        }

        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "private.mp4");
        try
        {
            File.WriteAllText(destination, "old");
            using (var output = new StagedOutputFile(destination))
            {
                File.WriteAllText(output.TemporaryPath, "complete");
                File.SetUnixFileMode(output.TemporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                // Permissions may change while an encoder is still running.
                File.SetUnixFileMode(destination, mode);

                output.Commit(CancellationToken.None);
            }

            Assert.That(File.GetUnixFileMode(destination), Is.EqualTo(mode));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("complete"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void A_new_output_keeps_the_encoder_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required.");
            return;
        }

        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "new.mp4");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        try
        {
            using (var output = new StagedOutputFile(destination))
            {
                File.WriteAllText(output.TemporaryPath, "complete");
                File.SetUnixFileMode(output.TemporaryPath, mode);
                output.Commit(CancellationToken.None);
            }

            Assert.That(File.GetUnixFileMode(destination), Is.EqualTo(mode));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("complete"));
        }
        finally { Directory.Delete(root, true); }
    }
}
