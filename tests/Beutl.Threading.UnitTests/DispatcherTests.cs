using NUnit.Framework;

namespace Beutl.Threading.UnitTests;

public class DispatcherTests
{
    [Test]
    public void Invoke()
    {
        int id = Environment.CurrentManagedThreadId;
        var dispatcher = Dispatcher.Spawn();

        int dispatcherId = dispatcher.Invoke(() => Environment.CurrentManagedThreadId);
        Assert.That(id, Is.Not.EqualTo(dispatcherId));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeVoid_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.Catch<OperationCanceledException>(
            () => dispatcher.Invoke(() => { }, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void Invoke_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.Catch<OperationCanceledException>(
            () => dispatcher.Invoke(() => 100, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeSyncVoid_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(() => { }, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeAsyncVoid_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(async () => await Task.Delay(100), ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeSync_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(() => 100, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeAsync_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(async () => await Task.FromResult(100), ct: new(true)));

        dispatcher.Shutdown();
    }
}
