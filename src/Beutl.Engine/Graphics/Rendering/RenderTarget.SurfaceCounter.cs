using Beutl.Threading;

namespace Beutl.Graphics.Rendering;

public partial class RenderTarget
{
    private sealed class SKSurfaceCounter<T>(T value, bool deferRelease = false, long approximateBytes = 0)
        where T : class, IDisposable
    {
        private readonly Dispatcher? _dispatcher = Dispatcher.Current;
        private volatile int _refs = 1;

        public T? Value { get; private set; } = value;

        public int RefCount => _refs;

        public void AddRef()
        {
            int old = _refs;
            while (true)
            {
                ObjectDisposedException.ThrowIf(old == 0, this);
                int current = Interlocked.CompareExchange(ref _refs, old + 1, old);
                if (current == old)
                {
                    break;
                }

                old = current;
            }
        }

        public void Release()
        {
            int old = _refs;
            while (true)
            {
                ObjectDisposedException.ThrowIf(old <= 0, this);
                int current = Interlocked.CompareExchange(ref _refs, old - 1, old);

                if (current == old)
                {
                    if (old == 1)
                    {
                        var value = Value;
                        Value = null;
                        if (value != null)
                        {
                            // Finished, not Started: between the two the owner thread is still
                            // running an operation, so disposing here could free a surface in use.
                            // Past Finished, dispatching would queue onto a loop that no longer
                            // drains and the native resource would outlive the process instead.
                            if (_dispatcher is { HasShutdownFinished: false } dispatcher
                                && !dispatcher.CheckAccess())
                            {
                                dispatcher.Dispatch(() => ReleaseValue(value));
                            }
                            else
                            {
                                ReleaseValue(value);
                            }
                        }
                    }

                    break;
                }

                old = current;
            }
        }

        private void ReleaseValue(T value)
        {
            if (deferRelease && GpuResourceReclaimQueue.TryDefer(value, approximateBytes))
            {
                return;
            }

            value.Dispose();
        }
    }
}
