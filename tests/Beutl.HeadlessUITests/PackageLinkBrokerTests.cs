using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageLinkBrokerTests
{
    [Test]
    public void FlatpakUsesThePerApplicationSharedRuntimeDirectory()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Flatpak uses Unix paths.");

        Assert.That(PackageLinkBroker.ResolvePipeName("install", "net.beditor.Beutl", "/run/user/1000"),
            Is.EqualTo("/run/user/1000/app/net.beditor.Beutl/install"));
        Assert.That(PackageLinkBroker.ResolvePipeName("install", null, "/run/user/1000"), Is.EqualTo("install"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("relative")]
    public void FlatpakDoesNotFallBackToSandboxLocalTemporaryFiles(string? runtimeDirectory)
    {
        Assert.Throws<IOException>(() => PackageLinkBroker.ResolvePipeName("install", "net.beditor.Beutl", runtimeDirectory));
    }

    [Test]
    public async Task SharedSocketAndLockPermitOneOwnerAndCanBeReopened()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Flatpak uses Unix-domain socket paths.");

        string directory = Path.Combine(Path.GetTempPath(), "btl-" + Guid.NewGuid().ToString("N")[..8]);
        string name = Path.Combine(directory, "install");
        try
        {
            using (var broker = PackageLinkBroker.TryCreate(name))
            {
                Assert.That(broker, Is.Not.Null);
                using var duplicate = PackageLinkBroker.TryCreate(name);
                Assert.That(duplicate, Is.Null);
                var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                broker!.SetHandler(uri => delivered.TrySetResult(uri));
                const string uri = "beutl://install?package=Sample";
                Assert.That(await PackageLinkBroker.TryForwardAsync(uri, name), Is.True);
                Assert.That(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(uri));
            }

            using var reopened = PackageLinkBroker.TryCreate(name);
            Assert.That(reopened, Is.Not.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task HoldsAStartupLinkUntilTheApplicationRegistersItsHandler()
    {
        string name = "btl-test-" + Guid.NewGuid().ToString("N")[..24];
        using var broker = PackageLinkBroker.TryCreate(name);
        Assert.That(broker, Is.Not.Null);
        using var duplicate = PackageLinkBroker.TryCreate(name);
        Assert.That(duplicate, Is.Null);

        const string first = "beutl://install?package=Sample&version=1.0.0";
        Assert.That(await PackageLinkBroker.TryForwardAsync(first, name), Is.True);
        var received = new List<string>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker!.SetHandler(uri =>
        {
            received.Add(uri);
            if (received.Count == 2)
                delivered.TrySetResult();
        });
        Assert.That(received, Is.EqualTo(new[] { first }));

        const string second = "beutl://install?package=Other&version=2.0.0";
        Assert.That(await PackageLinkBroker.TryForwardAsync(second, name), Is.True);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(received, Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public async Task IgnoresInvalidLinksAndStillAcceptsTheNextValidLink()
    {
        string name = "btl-test-" + Guid.NewGuid().ToString("N")[..24];
        using var broker = PackageLinkBroker.TryCreate(name);
        Assert.That(broker, Is.Not.Null);
        Assert.That(await PackageLinkBroker.TryForwardAsync("beutl://uninstall?package=Sample", name), Is.False);
        var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        broker!.SetHandler(uri => delivered.TrySetResult(uri));
        const string valid = "beutl://install?package=Sample";
        Assert.That(await PackageLinkBroker.TryForwardAsync(valid, name), Is.True);
        Assert.That(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(valid));
    }
}
