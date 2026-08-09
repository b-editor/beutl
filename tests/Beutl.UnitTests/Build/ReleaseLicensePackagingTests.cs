namespace Beutl.UnitTests.Build;

[TestFixture]
public class ReleaseLicensePackagingTests
{
    private static readonly IReadOnlyDictionary<string, string> s_expectedFiles =
        new Dictionary<string, string>
        {
            ["LICENSE"] = "MIT license",
            ["LICENSE.GPL"] = "GPL license",
            ["THIRD_PARTY_NOTICES.md"] = "Third-party notices",
            [Path.Combine("src", "Beutl.FFmpegWorker", "LICENSE")] = "Worker notice",
        };

    [Test]
    public void CopyTo_CopiesAndOverwritesEveryDistributionLegalFile()
    {
        string repositoryRoot = Path.Combine(Path.GetTempPath(), "beutl-license-source-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(Path.GetTempPath(), "beutl-license-output-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach ((string relativePath, string content) in s_expectedFiles)
            {
                string sourcePath = Path.Combine(repositoryRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                File.WriteAllText(sourcePath, content);
            }

            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "LICENSE"), "stale");

            ReleaseLegalFiles.CopyTo(repositoryRoot, destination);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(destination, "LICENSE")), Is.EqualTo("MIT license"));
                Assert.That(File.ReadAllText(Path.Combine(destination, "LICENSE.GPL")), Is.EqualTo("GPL license"));
                Assert.That(
                    File.ReadAllText(Path.Combine(destination, "THIRD_PARTY_NOTICES.md")),
                    Is.EqualTo("Third-party notices"));
                Assert.That(
                    File.ReadAllText(Path.Combine(destination, "LICENSE.FFmpegWorker")),
                    Is.EqualTo("Worker notice"));
                Assert.That(
                    Directory.GetFiles(destination).Select(Path.GetFileName),
                    Is.EquivalentTo(
                    [
                        "LICENSE",
                        "LICENSE.GPL",
                        "LICENSE.FFmpegWorker",
                        "THIRD_PARTY_NOTICES.md",
                    ]));
            });
        }
        finally
        {
            if (Directory.Exists(repositoryRoot)) Directory.Delete(repositoryRoot, recursive: true);
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Test]
    public void CopyTo_CopiesRepositoryLicensesAndSourceNotice()
    {
        string destination = Path.Combine(Path.GetTempPath(), "beutl-license-output-" + Guid.NewGuid().ToString("N"));
        try
        {
            ReleaseLegalFiles.CopyTo(FindRepositoryRoot(), destination);
            string thirdPartyNotices = File.ReadAllText(Path.Combine(destination, "THIRD_PARTY_NOTICES.md"));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(destination, "LICENSE")), Does.StartWith("MIT License"));
                Assert.That(
                    File.ReadAllText(Path.Combine(destination, "LICENSE.GPL")),
                    Does.Contain("GNU GENERAL PUBLIC LICENSE"));
                Assert.That(
                    File.ReadAllText(Path.Combine(destination, "LICENSE.FFmpegWorker")),
                    Does.Contain("Native FFmpeg libraries are not included in Beutl"));
                Assert.That(
                    thirdPartyNotices,
                    Does.Contain("FFmpeg4Sharp (Beutl.FFmpegWorker only)"));
                Assert.That(
                    thirdPartyNotices,
                    Does.Contain("FFmpeg.AutoGen/tree/444925cd53d3611fd4c8c295873fb631be56ab21"));
                Assert.That(thirdPartyNotices, Does.Contain("Copyright (c) 2025 Ruslan Balanukhin (Rationale One)"));
            });
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Test]
    public void ReleaseDefinitions_InstallAllLegalFilesInPlatformLocations()
    {
        string repositoryRoot = FindRepositoryRoot();
        string build = File.ReadAllText(Path.Combine(repositoryRoot, "nukebuild", "Build.cs"));
        string installer = File.ReadAllText(Path.Combine(repositoryRoot, "nukebuild", "beutl-setup.iss"));
        string flatpak = File.ReadAllText(
            Path.Combine(repositoryRoot, "packages", "flatpak", "net.beditor.Beutl.yml"));
        string flatpakMetainfo = File.ReadAllText(
            Path.Combine(repositoryRoot, "packages", "flatpak", "net.beditor.Beutl.metainfo.xml"));
        string copyrightPath = Path.Combine(
            repositoryRoot, "packages", "ubuntu22.04_amd64", "usr", "share", "doc", "beutl", "copyright");
        string copyright = File.ReadAllText(copyrightPath).ReplaceLineEndings("\n");
        string thirdPartyNotices = File.ReadAllText(Path.Combine(repositoryRoot, "THIRD_PARTY_NOTICES.md"));

        Assert.Multiple(() =>
        {
            Assert.That(build, Does.Contain("CopyDistributionLicenses(mainOutput);"));
            Assert.That(
                build,
                Does.Contain("CopyDistributionLicenses(output / \"Beutl.app\" / \"Contents\" / \"Resources\");"));
            Assert.That(
                installer,
                Does.Contain("Source: \"{#MySource}\\*\"; DestDir: \"{app}\"; Flags: ignoreversion recursesubdirs"));
            Assert.That(flatpak, Does.Contain("license-files:"));

            foreach (string outputName in new[]
                     {
                         "LICENSE",
                         "LICENSE.GPL",
                         "LICENSE.FFmpegWorker",
                         "THIRD_PARTY_NOTICES.md",
                     })
            {
                Assert.That(flatpak, Does.Contain($"- beutl-bin/{outputName}"));
            }

            Assert.That(
                flatpakMetainfo,
                Does.Contain("<project_license>MIT AND GPL-3.0-or-later</project_license>"));

            Assert.That(File.Exists(copyrightPath), Is.True);
            Assert.That(
                File.Exists(Path.Combine(repositoryRoot, "packages", "ubuntu22.04_amd64", "DEBIAN", "copyright")),
                Is.False);
            Assert.That(copyright, Does.Contain("Source: https://github.com/b-editor/beutl"));
            Assert.That(copyright, Does.Contain("License: Expat"));
            Assert.That(copyright, Does.Contain("Permission is hereby granted"));
            Assert.That(copyright, Does.Contain("Files: src/Beutl.FFmpegWorker/*"));
            Assert.That(copyright, Does.Contain("License: GPL-3+"));
            Assert.That(copyright, Does.Contain("/usr/share/common-licenses/GPL-3"));
            Assert.That(copyright, Does.Contain("/usr/lib/beutl/THIRD_PARTY_NOTICES.md"));
            Assert.That(copyright, Does.Contain(EncodeAsDebianFormattedText(thirdPartyNotices)));
            Assert.That(copyright, Does.Contain("FFmpeg4Sharp (Beutl.FFmpegWorker only)"));
            Assert.That(
                copyright.IndexOf("Files: *", StringComparison.Ordinal),
                Is.LessThan(copyright.IndexOf("Files: src/Beutl.FFmpegWorker/*", StringComparison.Ordinal)));
        });
    }

    private static string EncodeAsDebianFormattedText(string value)
    {
        string[] lines = value.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        return string.Join('\n', lines.Select(line => line.Length == 0 ? " ." : $" {line}"));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Beutl repository root.");
    }
}
