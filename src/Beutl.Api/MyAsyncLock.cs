// 以下のような再入場を調べるための、デバッグ用のコードです。

// using (await lock.LockAsync())
// {
//     await Second();
// }

// async Task Second()
// {
//     using (await lock.LockAsync())
//     {
//     }
// }

#if !DEBUG
global using MyAsyncLock = Nito.AsyncEx.AsyncLock;
#endif

namespace Beutl.Api;

#if DEBUG
public sealed class MyAsyncLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        // Awaiting the semaphore propagates cancellation and never returns a releaser for
        // an acquisition that did not happen; there is no continuation that could be
        // canceled after the semaphore was acquired.
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(this);
    }

    private sealed class Releaser(MyAsyncLock toRelease) : IDisposable
    {
        public void Dispose()
        {
            toRelease._semaphore.Release();
        }
    }
}
#endif
