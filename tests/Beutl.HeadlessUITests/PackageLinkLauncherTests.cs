using System.Collections.Concurrent;
using System.IO.Pipes;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageLinkLauncherTests
{
    private static string UniqueName() => "btl-launch-" + Guid.NewGuid().ToString("N")[..20];

    [Test]
    public async Task FailedForwardingDoesNotAuthorizeASecondApplication()
    {
        string name = UniqueName();
        using var owner = new Mutex(false, name, out bool createdNew);
        Assert.That(createdNew, Is.True);
        var result = await PackageLinkLauncher.PrepareAsync(
            ["beutl://install?package=Sample"], name, TimeSpan.FromMilliseconds(100));
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(PackageLinkLaunchAction.Failed));
            Assert.That(result.Broker, Is.Null);
            Assert.That(result.Arguments, Is.Empty);
        });
    }

    [Test]
    public async Task ConcurrentActivationsAllForwardToTheOwner()
    {
        string name = UniqueName();
        using var owner = PackageLinkBroker.TryCreate(name);
        Assert.That(owner, Is.Not.Null);
        var received = new ConcurrentQueue<string>();
        owner!.SetHandler(received.Enqueue);
        string[] uris = Enumerable.Range(0, 8).Select(index => $"beutl://install?package=Sample{index}").ToArray();
        var results = await Task.WhenAll(uris.Select(uri => PackageLinkLauncher.PrepareAsync([uri], name)));
        Assert.Multiple(() =>
        {
            Assert.That(results.All(result => result.Action == PackageLinkLaunchAction.Forwarded), Is.True);
            Assert.That(results.All(result => result.Broker == null), Is.True);
            Assert.That(received, Is.EquivalentTo(uris));
        });
    }

    [Test]
    public async Task BusyPipeIsRetriedWithoutStartingAnotherApplication()
    {
        string name = UniqueName();
        using var owner = PackageLinkBroker.TryCreate(name);
        Assert.That(owner, Is.Not.Null);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner!.SetHandler(uri => received.TrySetResult(uri));
        using var stalledClient = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await stalledClient.ConnectAsync(connectTimeout.Token);
        await stalledClient.WriteAsync(new byte[] { 1 }, connectTimeout.Token);

        const string uri = "beutl://install?package=Sample";
        var result = await PackageLinkLauncher.PrepareAsync([uri], name);
        Assert.That(result.Action, Is.EqualTo(PackageLinkLaunchAction.Forwarded));
        Assert.That(result.Broker, Is.Null);
        Assert.That(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(uri));
    }

    [Test]
    public async Task ForwardsMultipleUrisFromOneActivation()
    {
        string name = UniqueName();
        using var owner = PackageLinkBroker.TryCreate(name);
        var received = new ConcurrentQueue<string>();
        owner!.SetHandler(received.Enqueue);
        string[] uris = ["beutl://install?package=First", "beutl://install?package=Second"];
        var result = await PackageLinkLauncher.PrepareAsync(uris, name);
        Assert.That(result.Action, Is.EqualTo(PackageLinkLaunchAction.Forwarded));
        Assert.That(received, Is.EqualTo(uris));
    }

    [Test]
    public async Task ReleasedOwnershipAllowsANewApplicationToHandleTheLink()
    {
        string name = UniqueName();
        PackageLinkBroker.TryCreate(name)!.Dispose();
        string[] uris = ["beutl://install?package=Sample"];
        var result = await PackageLinkLauncher.PrepareAsync(uris, name);
        using var broker = result.Broker;
        Assert.That(result.Action, Is.EqualTo(PackageLinkLaunchAction.StartApplication));
        Assert.That(broker, Is.Not.Null);
        Assert.That(result.Arguments, Is.EqualTo(uris));
    }
}
