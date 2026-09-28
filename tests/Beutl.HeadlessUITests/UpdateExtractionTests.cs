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
                AddApplicationPayload(zip);
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
                AddApplicationPayload(zip, prefix: "Beutl.app/Contents/MacOS/");
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

    [Test]
    [TestCase("win", false)]
    [TestCase("win", true)]
    [TestCase("linux", false)]
    [TestCase("linux", true)]
    [TestCase("osx", false)]
    [TestCase("osx", true)]
    public async Task A_complete_zip_payload_is_accepted(string os, bool standalone)
    {
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string archive = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                AddApplicationPayload(zip, os, standalone);
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync("zip", archive, destination, os: os, standalone: standalone), Is.True);
            Assert.That(File.Exists(archive), Is.False);
            Assert.That(File.Exists(Path.Combine(destination, os == "win" ? "Beutl.exe" : "Beutl")), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    [TestCase("zip", "readme", false)]
    [TestCase("zip", "Beutl", false)]
    [TestCase("zip", "Beutl.dll", false)]
    [TestCase("zip", "Beutl.deps.json", false)]
    [TestCase("zip", "Beutl.runtimeconfig.json", false)]
    [TestCase("zip", "Beutl", true)]
    [TestCase("zip", "Beutl.dll", true)]
    [TestCase("zip", "System.Private.CoreLib.dll", false)]
    [TestCase("zip", "libcoreclr.dylib", false)]
    [TestCase("zip", "libhostfxr.dylib", false)]
    [TestCase("zip", "libhostpolicy.dylib", false)]
    [TestCase("app", "Beutl.dll", false)]
    public async Task An_incomplete_payload_is_rejected_without_touching_the_installed_app(string type, string missing, bool empty)
    {
        if (type == "app" && !OperatingSystem.IsMacOS()) Assert.Ignore("Uses the macOS ditto extractor.");
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string installed = Directory.CreateDirectory(Path.Combine(root, "installed")).FullName;
            File.WriteAllText(Path.Combine(installed, "Beutl"), "working application");
            string archive = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                if (missing == "readme")
                {
                    using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
                    writer.Write("This is not an application.");
                }
                else
                {
                    string prefix = type == "app" ? "Beutl.app/Contents/MacOS/" : "";
                    if (type == "app")
                    {
                        using var writer = new StreamWriter(zip.CreateEntry("Beutl.app/Contents/Info.plist").Open());
                        writer.Write("<plist><dict/></plist>");
                    }
                    AddApplicationPayload(zip, prefix: prefix, omitted: missing);
                    if (empty) zip.CreateEntry(prefix + missing);
                }
            }
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync(type, archive, destination), Is.False);
            Assert.That(File.Exists(archive), Is.True);
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(installed, "Beutl")), Is.EqualTo("working application"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task A_zip_cannot_write_into_a_sibling_of_the_extraction_directory()
    {
        string root = Directory.CreateTempSubdirectory("beutl-update-").FullName;
        try
        {
            string sibling = Directory.CreateDirectory(Path.Combine(root, "extracted-other")).FullName;
            string existing = Path.Combine(sibling, "keep.txt");
            File.WriteAllText(existing, "existing data");
            string archive = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                AddApplicationPayload(zip);
                using var writer = new StreamWriter(zip.CreateEntry("../extracted-other/keep.txt").Open());
                writer.Write("overwrite");
            }
            string destination = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;

            Assert.That(await ExtractAsync("zip", archive, destination), Is.False);
            Assert.That(File.ReadAllText(existing), Is.EqualTo("existing data"));
            Assert.That(Directory.Exists(destination), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AddApplicationPayload(
        ZipArchive zip, string os = "osx", bool standalone = true, string prefix = "", string? omitted = null)
    {
        string executable = os == "win" ? "Beutl.exe" : "Beutl";
        List<string> files = [executable, "Beutl.dll", "Beutl.deps.json", "Beutl.runtimeconfig.json"];
        if (standalone)
        {
            files.Add("System.Private.CoreLib.dll");
            foreach (string library in new[] { "coreclr", "hostfxr", "hostpolicy" })
                files.Add(os == "win" ? library + ".dll" : "lib" + library + (os == "linux" ? ".so" : ".dylib"));
        }
        foreach (string file in files.Where(file => file != omitted))
        {
            var entry = zip.CreateEntry(prefix + file);
            if (file == executable) entry.ExternalAttributes = Convert.ToInt32("100755", 8) << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write(file == executable ? "#!/bin/sh\nexit 0\n" : "required application file");
        }
    }

    private static Task<bool> ExtractAsync(
        string type, string archive, string destination, bool cancel = false, string os = "osx", bool standalone = true)
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
            OS = os,
            Arch = "arm64",
            Version = "test",
            Standalone = standalone ? "true" : "false",
            Type = type,
        };
        if (cancel) viewModel.Cancel();
        return (Task<bool>)typeof(UpdateDialogViewModel)
            .GetMethod("ExtractIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [metadata, archive, destination])!;
    }
}
