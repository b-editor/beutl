using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class IdentityOperationCancellationTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Dispose_DuringLinkedCancellation_DoesNotWaitForCallback(int cancellationOrigin)
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = operation.CancellationToken.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });

        Task cancellation = Task.CompletedTask;
        Task disposal = Task.CompletedTask;
        try
        {
            switch (cancellationOrigin)
            {
                case 0:
                    identity.Switch(static () => { });
                    break;
                case 1:
                    cancellation = lifetime.DisposeAsync().AsTask();
                    break;
                case 2:
                    cancellation = Task.Run(operation.Cancel);
                    break;
            }

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposal = Task.Run(operation.Dispose);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(operation.IsCurrent, Is.False);
            Assert.That(operation.TryPublish(static () => { }), Is.False);
        }
        finally
        {
            release.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            operation.Dispose();
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task Dispose_UnlinksCancellationWithoutCancellingTheOperation()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        CancellationToken token = operation.CancellationToken;
        using WaitHandle waitHandle = token.WaitHandle;
        int callbacks = 0;
        using CancellationTokenRegistration registration = token.Register(() => callbacks++);

        operation.Dispose();
        identity.Switch(static () => { });
        await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(callbacks, Is.Zero);
            Assert.That(waitHandle.SafeWaitHandle.IsClosed, Is.True);
        });
        Assert.DoesNotThrow(operation.Dispose);
    }

    [Test]
    public async Task CancellationCallback_CanDisposeOperationAndReleasesItsWaitHandle()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        CancellationToken token = operation.CancellationToken;
        using WaitHandle waitHandle = token.WaitHandle;
        using CancellationTokenRegistration registration = token.Register(operation.Dispose);

        await Task.Run(operation.Cancel).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(operation.IsCurrent, Is.False);
            Assert.That(waitHandle.SafeWaitHandle.IsClosed, Is.True);
        });
    }

    [Test]
    public async Task Dispose_AfterRetiredGenerationCancelled_ClosesGenerationWhenOperationExits()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        IdentityOperationLifetime.Generation generation = operation.Generation;
        using WaitHandle waitHandle = generation.Cancellation.Token.WaitHandle;

        try
        {
            identity.Switch(static () => { });
            await generation.CancellationTask!.WaitAsync(TimeSpan.FromSeconds(5));

            identity.Dispose();
            operation.Dispose();

            Assert.That(waitHandle.SafeWaitHandle.IsClosed, Is.True);
        }
        finally
        {
            operation.Dispose();
        }
    }
}
