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
    private readonly Task<IDisposable> _releaser;

    public MyAsyncLock()
    {
        _releaser = Task.FromResult<IDisposable>(new Releaser(this));
    }

    public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        Task wait = _semaphore.WaitAsync(cancellationToken);
        if (wait.IsCompleted)
        {
            return _releaser;
        }
        else
        {
            return wait.ContinueWith(
                continuationFunction: (_, state) => (IDisposable)state!,
                state: _releaser.Result,
                cancellationToken: cancellationToken,
                continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
                scheduler: TaskScheduler.Default);
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly MyAsyncLock _mutex;

        public Releaser(MyAsyncLock toRelease)
        {
            _mutex = toRelease;
        }

        public void Dispose()
        {
            _mutex._semaphore.Release();
        }
    }
}
#endif
