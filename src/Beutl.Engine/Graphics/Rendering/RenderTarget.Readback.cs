using Beutl.Media;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public partial class RenderTarget
{
    // A pending SnapshotAsync read, completed by the finished callback the GPU context runs when it is polled.
    internal sealed class SurfaceReadback(Bitmap destination)
    {
        private readonly TaskCompletionSource<Bitmap> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Bitmap> Completion => _completion.Task;

        public SKImageInfo Info => destination.SKBitmap.Info;

        // Whichever of Complete and Fail claims the read first alone touches the bitmap. A dispatcher can raise
        // ShutdownFinished on its own thread while the thread that subscribed fails the read after finding the shutdown
        // already over, so the claim has to be atomic rather than a check of the task.
        private int _settled;

        private bool IsSettled => Volatile.Read(ref _settled) != 0;

        // Skia calls this once, on the thread that polled the context, and the result is valid only during the call.
        public void Complete(SKImageReadPixelsResult? result)
        {
            if (!TrySettle())
                return;

            if (result is null)
            {
                FailSettled(new InvalidOperationException(
                    "Failed to read the render target surface into the destination bitmap."));
                return;
            }

            try
            {
                result.CopyPlaneTo(0, destination.GetPixelSpan());
            }
            catch (Exception ex)
            {
                FailSettled(ex);
                return;
            }

            _completion.TrySetResult(destination);
        }

        public void Fail(Exception exception)
        {
            if (TrySettle())
                FailSettled(exception);
        }

        private bool TrySettle() => Interlocked.Exchange(ref _settled, 1) == 0;

        private void FailSettled(Exception exception)
        {
            // Disposed before the task faults, so nothing that resumes on the failure can find the bitmap alive.
            destination.Dispose();
            _completion.TrySetException(exception);
        }

        // Polls checkAsyncWorkCompletion on the dispatcher, at medium priority, until the read completes. A dispatcher
        // stops running queued work once it shuts down, so the shutdown fails the read instead of stranding it. Like
        // DispatcherCleanup, that waits for ShutdownFinished: the failure then runs on the dispatcher's thread after its
        // loop has exited, never alongside a poll that is completing the read.
        public void PollUntilComplete(Dispatcher dispatcher, Action checkAsyncWorkCompletion, Func<bool> isContextAbandoned)
        {
            EventHandler onShutdownFinished = (_, _) => Fail(new OperationCanceledException(
                "The dispatcher that owns the render target shut down before the read completed."));
            dispatcher.ShutdownFinished += onShutdownFinished;
            _ = _completion.Task.ContinueWith(
                _ => dispatcher.ShutdownFinished -= onShutdownFinished,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            SchedulePoll();

            // A shutdown that finished before the subscription raised its event with nothing to fail.
            if (dispatcher.HasShutdownFinished)
                onShutdownFinished(dispatcher, EventArgs.Empty);

            void SchedulePoll()
            {
                // Scheduled afresh for every poll, at the priority Dispatch and Invoke default to. The dispatcher always
                // takes the highest priority it holds, so a low-priority poll would never run while medium work stayed
                // queued, and a loop that awaited a delay would resume through its synchronization context at high
                // priority, ahead of that work.
                dispatcher.Schedule(TimeSpan.FromMilliseconds(1), Poll, DispatchPriority.Medium);
            }

            void Poll()
            {
                if (IsSettled)
                    return;

                try
                {
                    if (isContextAbandoned())
                    {
                        Fail(new ObjectDisposedException(nameof(GRContext)));
                        return;
                    }

                    checkAsyncWorkCompletion();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                    return;
                }

                if (!IsSettled)
                    SchedulePoll();
            }
        }
    }
}
