using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

public class UpdateExtractionTests
{
    [Test]
    public async Task Zip_extraction_preserves_helper_executability()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix executable permissions.");
            return;
        }
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string archive = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Beutl.FFmpegWorker");
                entry.ExternalAttributes = Convert.ToInt32("100755", 8) << 16;
                using var writer = new StreamWriter(entry.Open());
                writer.Write("#!/bin/sh\nexit 0\n");
            }
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync("zip", archive, destination), Is.True);
            string helper = Path.Combine(destination, "Beutl.FFmpegWorker");
            Assert.That(File.GetUnixFileMode(helper).HasFlag(UnixFileMode.UserExecute), Is.True);
            using Process process = Process.Start(new ProcessStartInfo(helper) { UseShellExecute = false })!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            Assert.That(process.ExitCode, Is.Zero);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_failed_macOS_extraction_keeps_the_archive_and_removes_staging(bool validZipWithoutApp)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Uses the macOS ditto extractor.");
            return;
        }
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string archive = Path.Combine(root, "update.zip");
            if (validZipWithoutApp)
            {
                using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
                writer.Write("This is not an application bundle.");
            }
            else
            {
                File.WriteAllText(archive, "broken archive");
            }
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync("app", archive, destination), Is.False);
            Assert.That(File.Exists(archive), Is.True, "Keep the failed download available for diagnosis/retry.");
            Assert.That(Directory.Exists(destination), Is.False, "An incomplete app must not remain installable.");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task A_complete_macOS_bundle_is_extracted_successfully()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Uses the macOS ditto extractor.");
            return;
        }
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string archive = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("Beutl.app/Contents/Info.plist").Open()))
                    writer.Write("<plist><dict/></plist>");
                var executable = zip.CreateEntry("Beutl.app/Contents/MacOS/Beutl");
                executable.ExternalAttributes = Convert.ToInt32("100755", 8) << 16;
                using var executableWriter = new StreamWriter(executable.Open());
                executableWriter.Write("#!/bin/sh\nexit 0\n");
            }
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync("app", archive, destination), Is.True);
            Assert.That(File.Exists(archive), Is.False);
            Assert.That(File.GetUnixFileMode(Path.Combine(destination, "Beutl.app", "Contents", "MacOS", "Beutl"))
                .HasFlag(UnixFileMode.UserExecute), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    [TestCase("zip")]
    [TestCase("app")]
    public async Task Cancellation_does_not_leave_installable_staging(string type)
    {
        if (type == "app" && !OperatingSystem.IsMacOS()) Assert.Ignore("Uses the macOS ditto extractor.");
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string archive = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("partial.txt").Open()))
                writer.Write("partial update");
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync(type, archive, destination, cancel: true), Is.False);
            Assert.That(File.Exists(archive), Is.True);
            Assert.That(Directory.Exists(destination), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task<bool> ExtractAsync(string type, string archive, string destination, bool cancel = false)
    {
        var viewModel = new UpdateDialogViewModel(new AppUpdateResponse
        {
            LatestVersion = "test",
            Url = null,
            DownloadUrl = null,
            IsLatest = false,
            MustLatest = false,
        });
        var metadata = new AssetMetadataJson
        {
            Id = "test",
            OS = "osx",
            Arch = "arm64",
            Version = "test",
            Standalone = "true",
            Type = type,
        };
        if (cancel) viewModel.Cancel();
        return (Task<bool>)typeof(UpdateDialogViewModel)
            .GetMethod("ExtractIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [metadata, archive, destination])!;
    }
}
