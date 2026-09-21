using System.Diagnostics;
using System.Text.Json.Nodes;
using Beutl.Api.Services;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageInstallerCrashRecoveryTests
{
    private const string PackageName = "Beutl.Package.CrashRecovery";
    private const string WorkerHomeVariable = "BEUTL_TEST_DATA_INSTALL_HOME";
    private const string WorkerActionVariable = "BEUTL_TEST_DATA_INSTALL_ACTION";
    private const string WorkerStepVariable = "BEUTL_TEST_DATA_INSTALL_STEP";

    [Test]
    public async Task RecoveryWaitsForAnotherProcessHoldingThePublicationLock()
    {
        string home = CreateHome();
        using var held = new FileStream(Path.Combine(home, ".data-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Task worker = RunWorker(home, "recover");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string waiting = Path.Combine(home, "waiting-for-lock.txt");
            while (!File.Exists(waiting) && !worker.IsCompleted) await Task.Delay(25, timeout.Token);
            if (worker.IsCompleted) await worker;
            Assert.That(File.Exists(waiting), Is.True, "The child must reach lock contention before the parent releases the lock.");
        }
        finally
        {
            held.Dispose();
            try { await worker; }
            finally { Directory.Delete(home, true); }
        }
    }

    [TestCase("prepared", false)]
    [TestCase("backup-materials", false)]
    [TestCase("publish-materials", false)]
    [TestCase("backup-templates", false)]
    [TestCase("publish-templates", false)]
    [TestCase("published", true)]
    [TestCase("registered", true)]
    [TestCase("committed", true)]
    public async Task ProcessTermination_RecoversBothPayloadsAndRegistration(string step, bool committed)
    {
        string home = CreateHome();
        try
        {
            await RunWorker(home, "update", step);
            await RunWorker(home, "recover");
            AssertPayloads(home, committed ? "new" : "old");
            AssertRegistration(home, committed ? "2.0.0" : "1.0.0");
            Assert.That(Directory.GetDirectories(home, ".data-install-*"), Is.Empty);
            await RunWorker(home, "recover");
            AssertPayloads(home, committed ? "new" : "old");
        }
        finally { Directory.Delete(home, true); }
    }

    [TestCase("unpublish-templates")]
    [TestCase("restore-templates")]
    [TestCase("unpublish-materials")]
    [TestCase("restore-materials")]
    [TestCase("recovered")]
    public async Task ProcessTerminationDuringRecovery_CanResume(string step)
    {
        string home = CreateHome();
        try
        {
            await RunWorker(home, "update", "publish-templates");
            await RunWorker(home, "recover", step);
            await RunWorker(home, "recover");
            AssertPayloads(home, "old");
            AssertRegistration(home, "1.0.0");
            Assert.That(Directory.GetDirectories(home, ".data-install-*"), Is.Empty);
        }
        finally { Directory.Delete(home, true); }
    }

    [TestCase("publish-materials")]
    [TestCase("publish-templates")]
    public async Task InterruptedFirstInstall_RemovesOnlyItsOwnPayloads(string step)
    {
        string home = CreateHome();
        try
        {
            await RunWorker(home, "first-install", step);
            string unowned = Path.Combine(home, "materials", "UserContent");
            Directory.CreateDirectory(unowned);
            File.WriteAllText(Path.Combine(unowned, "keep.txt"), "user");
            await RunWorker(home, "recover");
            Assert.That(Directory.Exists(Path.Combine(home, "materials", PackageName)), Is.False);
            Assert.That(Directory.Exists(Path.Combine(home, "templates", PackageName)), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(unowned, "keep.txt")), Is.EqualTo("user"));
            Assert.That(Directory.GetDirectories(home, ".data-install-*"), Is.Empty);
        }
        finally { Directory.Delete(home, true); }
    }

    [TestCase("backup-materials", false)]
    [TestCase("published", true)]
    public async Task InterruptedTagRemoval_RecoversReplacementsAndRemovalsTogether(string step, bool committed)
    {
        string home = CreateHome();
        try
        {
            await RunWorker(home, "remove-materials", step);
            await RunWorker(home, "recover");
            Assert.That(Directory.Exists(Path.Combine(home, "materials", PackageName)), Is.EqualTo(!committed));
            Assert.That(File.ReadAllText(Path.Combine(home, "templates", PackageName, "item.txt")), Is.EqualTo(committed ? "new" : "old"));
            AssertRegistration(home, committed ? "2.0.0" : "1.0.0");
        }
        finally { Directory.Delete(home, true); }
    }

    [TestCase("legacy")]
    [TestCase("invalid-json")]
    [TestCase("wrong-destination")]
    [TestCase("unowned-destination")]
    public async Task AmbiguousRecovery_PreservesBackupsAndUnownedData(string scenario)
    {
        string home = CreateHome();
        try
        {
            await RunWorker(home, "update", "publish-materials");
            string staging = Directory.GetDirectories(home, ".data-install-*").Single();
            string journal = Path.Combine(staging, "install.json");
            if (scenario == "legacy") File.Delete(journal);
            else if (scenario == "invalid-json") File.WriteAllText(journal, "{");
            else if (scenario == "wrong-destination")
            {
                JsonNode data = JsonNode.Parse(File.ReadAllText(journal))!;
                data["Payloads"]![0]!["Destination"] = Path.Combine(home, "user");
                File.WriteAllText(journal, data.ToJsonString());
            }
            else
            {
                string destination = Path.Combine(home, "materials", PackageName);
                Directory.Delete(destination, true);
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "item.txt"), "user");
            }
            await RunWorker(home, "recover");
            Assert.That(File.ReadAllText(Path.Combine(staging, "backup-materials", "item.txt")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(home, "materials", PackageName, "item.txt")),
                Is.EqualTo(scenario == "unowned-destination" ? "user" : "new"));
            Assert.That(File.ReadAllText(Path.Combine(home, "templates", PackageName, "item.txt")), Is.EqualTo("old"));
        }
        finally { Directory.Delete(home, true); }
    }

    // The test assembly's executable entry point runs only this method, without starting
    // VSTest or discovering the whole suite. Kill still terminates a real installer process:
    // no exception handler, Dispose, or finally block can roll it back.
    internal static void RunCrashWorker()
    {
        string? home = Environment.GetEnvironmentVariable(WorkerHomeVariable);
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home) || !Directory.Exists(home))
            throw new InvalidOperationException("The package worker requires an existing absolute test home.");

        string? action = Environment.GetEnvironmentVariable(WorkerActionVariable);
        if (action is not ("recover" or "update" or "first-install" or "remove-materials"))
            throw new InvalidOperationException("The package worker requires a supported action.");
        string? stop = Environment.GetEnvironmentVariable(WorkerStepVariable);
        if (action != "recover" && string.IsNullOrEmpty(stop))
            throw new InvalidOperationException("A publishing worker requires a termination checkpoint.");

        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, home);
        if (Helper.AppRoot != home)
            throw new InvalidOperationException("The package worker did not initialize in its isolated test home.");
        void AfterStep(string step)
        {
            if (step == "waiting-for-lock") File.WriteAllText(Path.Combine(home!, "waiting-for-lock.txt"), "waiting");
            if (step != stop) return;
            File.WriteAllText(Path.Combine(home!, "stopped-at.txt"), step);
            Process.GetCurrentProcess().Kill();
            Thread.Sleep(Timeout.Infinite);
        }

        if (action == "recover")
        {
            PackageInstaller.RecoverDataPackageInstalls(afterStep: AfterStep);
            return;
        }

        using var client = new HttpClient();
        var repository = new InstalledPackageRepository();
        var installer = new PackageInstaller(client, repository, null!);
        if (action != "first-install")
        {
            LocalPackage old = CreatePackage(home!, "1.0.0", "old");
            installer.InstallDataPackage(old);
            repository.UpgradePackages(new PackageIdentity(PackageName, NuGetVersion.Parse("1.0.0")));
        }
        LocalPackage current = CreatePackage(home!, "2.0.0", "new");
        if (action == "remove-materials") current.Tags = [PackageKinds.TemplateTag];
        installer.AfterDataInstallStep = AfterStep;
        using var deployment = installer.PrepareDataPackage(current, CancellationToken.None);
        deployment.Commit(() => repository.UpgradePackages(new PackageIdentity(PackageName, NuGetVersion.Parse("2.0.0"))));
        throw new InvalidOperationException("The worker did not reach the requested termination point.");
    }

    private static LocalPackage CreatePackage(string home, string version, string content)
    {
        string installed = Path.Combine(home, "packages", PackageName + "." + version);
        foreach (string kind in new[] { "materials", "templates" })
        {
            string directory = Path.Combine(installed, kind);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "item.txt"), content);
        }
        return new LocalPackage
        {
            Name = PackageName,
            Version = version,
            InstalledPath = installed,
            Tags = [PackageKinds.MaterialTag, PackageKinds.TemplateTag]
        };
    }

    private static string CreateHome()
    {
        // macOS's per-user temp path plus the child runtime's socket name can exceed
        // sockaddr_un.sun_path. Keep the child TMPDIR short on Unix.
        string root = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        string home = Path.Combine(root, "bdc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        return home;
    }

    private static void AssertPayloads(string home, string expected)
    {
        foreach (string kind in new[] { "materials", "templates" })
            Assert.That(File.ReadAllText(Path.Combine(home, kind, PackageName, "item.txt")), Is.EqualTo(expected), kind);
    }

    private static void AssertRegistration(string home, string expected)
    {
        JsonArray registrations = JsonNode.Parse(File.ReadAllText(Path.Combine(home, "installedPackages.json")))!.AsArray();
        Assert.That(registrations.Single()!["Version"]!.GetValue<string>(), Is.EqualTo(expected));
    }

    private static async Task RunWorker(string home, string action, string? step = null)
    {
        string checkpoint = Path.Combine(home, "stopped-at.txt");
        File.Delete(checkpoint);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(PackageInstallerCrashRecoveryTests).Assembly.Location);
        start.ArgumentList.Add(TestWorkerProgram.PackageInstallWorkerArgument);
        start.Environment[BeutlEnvironment.HomeVariable] = home;
        start.Environment[WorkerHomeVariable] = home;
        start.Environment[WorkerActionVariable] = action;
        // Put runtime temporary files under the directory this parent owns, including
        // anything a terminated worker cannot clean up itself.
        start.Environment["TMPDIR"] = home;
        start.Environment["TMP"] = home;
        start.Environment["TEMP"] = home;
        if (step is null) start.Environment.Remove(WorkerStepVariable);
        else start.Environment[WorkerStepVariable] = step;
        using Process worker = Process.Start(start)!;
        Task<string> stdout = worker.StandardOutput.ReadToEndAsync();
        Task<string> stderr = worker.StandardError.ReadToEndAsync();
        bool timedOut = false;
        try { await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch (TimeoutException)
        {
            timedOut = true;
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
        }
        string output = await stdout + await stderr;
        Assert.That(timedOut, Is.False, "The worker timed out. " + output);
        if (step is null) Assert.That(worker.ExitCode, Is.Zero, output);
        else
        {
            Assert.That(worker.ExitCode, Is.Not.Zero, "Worker should have been killed. " + output);
            Assert.That(File.Exists(checkpoint), Is.True, output);
            Assert.That(File.ReadAllText(checkpoint), Is.EqualTo(step), output);
        }
    }
}
