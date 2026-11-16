using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

// The update endpoint steers where the download goes and which file is installed, so the
// downloader trusts only HTTPS and a server-supplied name that cannot leave its folder.
[TestFixture, NonParallelizable]
public sealed class UpdateDownloadTests
{
    private const string Url = "https://downloads.example.test/update.zip";
    private string _root = null!;
    private string? _previousHome;

    [SetUp]
    public void SetUp()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "beutl-update-" + Guid.NewGuid().ToString("N")));
        _previousHome = Environment.GetEnvironmentVariable(BeutlEnvironment.HomeVariable);
        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, _root);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, _previousHome);
        Directory.Delete(_root, true);
    }

    private static UpdateDialogViewModel ViewModel(string url, string? sha256 = null, HttpClient? client = null,
        string type = "installer") => new(new AppUpdateResponse
        {
            LatestVersion = "2.0.0",
            Url = null,
            DownloadUrl = url,
            Sha256 = sha256,
            IsLatest = false,
            MustLatest = false,
        }, false, httpClient: client, loadMetadata: () => Task.FromResult<AssetMetadataJson?>(new AssetMetadataJson
        {
            Id = "test",
            OS = type == "app" ? "osx" : "linux",
            Arch = "x64",
            Version = "2.0.0",
            Standalone = "false",
            Type = type,
        }), claimUpdateFolders: () => true);

    private static HttpResponseMessage Response(params byte[] content) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content),
    };

    [Test]
    public async Task HttpDownloadUrlIsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel("http://downloads.example.test/update.zip");
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(viewModel.ProgressText.Value, Is.EqualTo(MessageStrings.DownloadFailed));
        });
    }

    [Test]
    public async Task RedirectToHttpIsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://downloads.example.test/update.zip");
        response.RequestMessage = request;
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.That(result, Is.Null);
        Assert.That(File.Exists(path), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContentDispositionPathIsReducedToItsFileName(bool windowsSeparators)
    {
        string escaped = $"evil-{Guid.NewGuid():N}.exe";
        string fileName = windowsSeparators ? $@"..\..\{escaped}" : $"../../{escaped}";
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        string expected = Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", escaped);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(expected), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(File.Exists(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "..", escaped)), Is.False);
        });
    }

    [Test]
    public async Task ContentDispositionNameWithDirectoriesKeepsOnlyTheFileName()
    {
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "nested/dir/update.zip" };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "update.zip")));
    }

    [Test]
    public async Task DownloadCannotOverwriteTheUpdateScript()
    {
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "update.sh" };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "update.sh")));
        Assert.That(result, Is.Not.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.sh")));
    }

    [Test]
    public async Task ContentDispositionNameCannotCarryShellSyntax()
    {
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "$(touch pwned).deb" };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "__touch_pwned_.deb")));
    }

    [Test]
    public async Task UrlNameIsUsedWithoutItsQueryWhenTheServerGivesNoName()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel("https://downloads.example.test/releases/Beutl-2.0.zip?token=opaque");

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "Beutl-2.0.zip")));
        Assert.That(File.Exists(result), Is.True);
    }

    [Test]
    public async Task ContentThatDoesNotMatchTheAdvertisedSha256IsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url, new string('0', 64));
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(Directory.GetFiles(_root), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContentMatchingTheAdvertisedSha256IsAccepted(bool prefixed)
    {
        byte[] content = [1, 2, 3, 4];
        string hash = Convert.ToHexString(SHA256.HashData(content));
        using var response = Response(content);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url, prefixed ? "sha256:" + hash : hash);
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.That(result, Is.EqualTo(path));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(content));
    }

    [Test]
    public async Task MalformedSha256IsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url, "not-a-hash");
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.That(result, Is.Null);
        Assert.That(File.Exists(path), Is.False);
    }

    [TestCase("installer", "match", 1)]
    [TestCase("debian", "match", 1)]
    [TestCase("installer", "damaged", 2)]
    [TestCase("debian", "no hash", 2)]
    public async Task NextLaunchReusesOnlyTheVerifiedDownload(string type, string state, int requests)
    {
        byte[] bytes = [1, 2, 3, 4];
        string? hash = state == "no hash" ? null : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var handler = new AssetHandler("release-" + (type == "installer" ? "setup.exe" : "2.deb"), bytes);
        using var client = new HttpClient(handler);
        var first = ViewModel(Url, hash, client, type);
        first.Start();
        await first.UpdateTask!;
        Assert.That(first.Phase.Value, Is.EqualTo(AppUpdatePhase.Ready));
        first.Cancel();

        string path = Path.Combine(_root, "tmp", "download", handler.Name);
        if (state == "damaged") File.WriteAllBytes(path, [4, 3, 2, 1]);
        var next = ViewModel(Url, hash, client, type);
        try
        {
            next.Start();
            await next.UpdateTask!;
            Assert.That(next.Phase.Value, Is.EqualTo(AppUpdatePhase.Ready));
            Assert.That(handler.Requests, Is.EqualTo(requests));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }
        finally { next.Cancel(); }
    }

    [Test]
    public async Task OldVersionsArePrunedOnlyAfterAVerifiedReplacement()
    {
        using var cache = UpdateDownloadCache.Open(_root);
        string old = Path.Combine(_root, "release-1.zip");
        string partial = Path.Combine(_root, ".unfinished.part");
        File.WriteAllBytes(old, [1]);
        File.WriteAllBytes(partial, [2]);
        var vm = ViewModel(Url, Convert.ToHexString(SHA256.HashData(new byte[] { 2 })));
        using var bad = new HttpClient(new AssetHandler("release-2.zip", [3]));
        Assert.That(await vm.DownloadFile(httpClient: bad, cache: cache), Is.Null);
        Assert.That(File.Exists(old), Is.True);

        using var good = new HttpClient(new AssetHandler("release-2.zip", [2]));
        string? file = await vm.DownloadFile(httpClient: good, cache: cache);
        Assert.That(file, Is.EqualTo(Path.Combine(_root, "release-2.zip")));
        Assert.That(File.Exists(old), Is.False);
        Assert.That(File.Exists(partial), Is.True);
    }

    [Test]
    public async Task ConcurrentPreparationsKeepTheirInstallersUntilReleased()
    {
        var first = ViewModel(Url, Convert.ToHexString(SHA256.HashData(new byte[] { 1 })));
        var later = ViewModel(Url, Convert.ToHexString(SHA256.HashData(new byte[] { 2 })));
        using var original = new HttpClient(new AssetHandler("setup.exe", [1]));
        using var handler = new AssetHandler("setup.exe", [2]);
        using var client = new HttpClient(handler);
        using (var primary = UpdateDownloadCache.Open(_root))
        using (var secondary = UpdateDownloadCache.Open(_root))
        {
            string? ready = await first.DownloadFile(httpClient: original, cache: primary);
            string? same = await first.DownloadFile(httpClient: client, cache: secondary);
            Assert.That(handler.Requests, Is.Zero);
            Assert.That(File.ReadAllBytes(same!), Is.EqualTo(new byte[] { 1 }));

            string? replacement = await later.DownloadFile(httpClient: client, cache: secondary);
            Assert.That(File.ReadAllBytes(replacement!), Is.EqualTo(new byte[] { 2 }));
            Assert.That(File.ReadAllBytes(ready!), Is.EqualTo(new byte[] { 1 }));
        }

        using var next = UpdateDownloadCache.Open(_root);
        string? cached = await later.DownloadFile(httpClient: client, cache: next);
        Assert.That(handler.Requests, Is.EqualTo(1));
        Assert.That(Directory.GetFiles(_root, "*.exe", SearchOption.AllDirectories), Is.EquivalentTo(new[] { cached }));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task DebianInstallerKeepsThePackageUntilAptFinishes(bool manual, int installExitCode)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Uses the Debian installer's Bash command.");
        string cacheRoot = Path.Combine(_root, "user's downloads");
        using var cache = UpdateDownloadCache.Open(cacheRoot);
        string original = Path.Combine(cache.DirectoryPath, "release.deb");
        File.WriteAllText(original, "verified package");
        string bashEnv = Path.Combine(_root, "mock-apt.sh");
        File.WriteAllText(bashEnv, """
            sudo() {
              case "$1 $2" in
                "apt update") echo updating; read -r gate; return 0 ;;
                "apt install") echo "$3"; cat "$3" && return "$APT_EXIT" ;;
                *) return 99 ;;
              esac
            }
            """);

        ProcessStartInfo Shell(IReadOnlyList<string> args)
        {
            var info = new ProcessStartInfo(args[0]) { RedirectStandardInput = true, RedirectStandardOutput = true };
            foreach (string arg in args.Skip(1)) info.ArgumentList.Add(arg);
            info.Environment["BASH_ENV"] = bashEnv;
            info.Environment["APT_EXIT"] = installExitCode.ToString();
            return info;
        }

        Process? child = null;
        if (manual)
            child = Process.Start(Shell(["bash", "-c", DebianPackageInstaller.GetManualCommand(original)]));
        else
            DebianPackageInstaller.Start(original, Shell(DebianPackageInstaller.Command), info => child = Process.Start(info));
        using var apt = child!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            Assert.That(await apt.StandardOutput.ReadLineAsync(timeout.Token), Is.EqualTo("updating"));
            cache.Dispose();
            using var newer = UpdateDownloadCache.Open(cacheRoot);
            string replacement = Path.Combine(newer.DirectoryPath, "new.deb");
            File.WriteAllText(replacement, "new package");
            newer.RemoveOtherDownloads(replacement);
            Assert.That(File.Exists(original), Is.False);
        }
        finally
        {
            apt.StandardInput.Close();
            await apt.WaitForExitAsync(timeout.Token);
        }

        string? handoff = await apt.StandardOutput.ReadLineAsync(timeout.Token);
        Assert.That(await apt.StandardOutput.ReadToEndAsync(timeout.Token), Is.EqualTo("verified package"));
        Assert.That(apt.ExitCode, Is.EqualTo(installExitCode));
        Assert.That(Directory.Exists(Path.GetDirectoryName(handoff!)), Is.False);
    }

    [Test]
    public void FailedDebianTerminalLaunchRemovesOnlyItsHandoff()
    {
        string original = Path.Combine(_root, "release.deb");
        File.WriteAllText(original, "verified package");
        string? handoff = null;
        void FailToStart(ProcessStartInfo info)
        {
            handoff = info.ArgumentList[^1];
            Assert.That(File.ReadAllText(Path.Combine(handoff, "update.deb")), Is.EqualTo("verified package"));
            throw new IOException("Terminal failed to start.");
        }

        Assert.Throws<IOException>(() => DebianPackageInstaller.Start(original, new ProcessStartInfo(), FailToStart));
        Assert.That(handoff, Is.Not.Null);
        Assert.That(Directory.Exists(handoff), Is.False);
        Assert.That(File.Exists(original), Is.True);
    }

    [Test]
    public async Task CacheLookupAndPruningLeaveLinkedFilesAlone()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Creating file links requires privileges on Windows.");
        using var cache = UpdateDownloadCache.Open(Path.Combine(_root, "download"));
        byte[] bytes = [1, 2, 3, 4];
        string outside = Path.Combine(_root, "outside");
        File.WriteAllBytes(outside, bytes);
        string link = Path.Combine(cache.DirectoryPath, "old.zip");
        File.CreateSymbolicLink(link, outside);
        var vm = ViewModel(Url, Convert.ToHexString(SHA256.HashData(bytes)));
        using var handler = new AssetHandler("release.zip", bytes);
        using var client = new HttpClient(handler);

        Assert.That(await vm.DownloadFile(httpClient: client, cache: cache), Is.EqualTo(Path.Combine(cache.DirectoryPath, "release.zip")));
        Assert.That(handler.Requests, Is.EqualTo(1));
        Assert.That(File.Exists(link), Is.True);
        Assert.That(File.ReadAllBytes(outside), Is.EqualTo(bytes));
    }

    [Test]
    public async Task NextLaunchRestagesTheAppFromTheVerifiedArchive()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Ignore("Uses the desktop archive updater.");
        bool mac = OperatingSystem.IsMacOS();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (mac)
            {
                using var writer = new StreamWriter(zip.CreateEntry("Beutl.app/Contents/Info.plist").Open());
                writer.Write("<plist><dict/></plist>");
            }
            UpdateExtractionTests.AddApplicationPayload(zip, standalone: false, prefix: mac ? "Beutl.app/Contents/MacOS/" : "");
        }

        byte[] bytes = buffer.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var handler = new AssetHandler("release.zip", bytes);
        using var client = new HttpClient(handler);
        string staging = Path.Combine(_root, "tmp", "update");
        string app = mac ? Path.Combine(staging, "Beutl.app", "Contents", "MacOS")
            : UpdateDialogViewModel.GetZipStagingDirectory(AppContext.BaseDirectory, staging);
        string dll = Path.Combine(app, "Beutl.dll");
        string stale = Path.Combine(app, "stale.txt");

        async Task Prepare()
        {
            var vm = ViewModel(Url, hash, client, mac ? "app" : "zip");
            vm.Start();
            await vm.UpdateTask!;
            Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Ready), vm.ProgressText.Value);
        }

        await Prepare();
        File.Delete(dll);
        File.WriteAllText(stale, "old staging");
        await Prepare();

        Assert.That(handler.Requests, Is.EqualTo(1));
        Assert.That(File.Exists(dll), Is.True);
        Assert.That(File.Exists(stale), Is.False);
    }

    [TestCase("http://downloads.example.test/release", 4)]
    [TestCase(Url, 3)]
    public async Task ExistingBytesCannotBypassTheUrlOrSizeCheck(string url, long limit)
    {
        using var cache = UpdateDownloadCache.Open(_root);
        byte[] bytes = [1, 2, 3, 4];
        File.WriteAllBytes(Path.Combine(_root, "release.zip"), bytes);
        var vm = ViewModel(url, Convert.ToHexString(SHA256.HashData(bytes)));
        using var client = new HttpClient(new AssetHandler("release.zip", bytes));
        Assert.That(await vm.DownloadFile(httpClient: client, maximumBytes: limit, cache: cache), Is.Null);
    }

    private sealed class AssetHandler(string name, byte[] bytes) : HttpMessageHandler
    {
        public string Name => name;
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            var response = Response(bytes);
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = name };
            return Task.FromResult(response);
        }
    }

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(response);
    }
}
