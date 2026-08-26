using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

internal sealed class WindowShutdownCoordinator
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly ILogger s_logger = Log.CreateLogger<WindowShutdownCoordinator>();
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task> _shutdownAsync;
    private readonly Action _close;
    private readonly TimeSpan _timeout;
    private Task? _shutdownTask;
    private int _canClose;

    public WindowShutdownCoordinator(
        Func<CancellationToken, Task> shutdownAsync,
        Action close,
        TimeSpan? timeout = null)
    {
        _shutdownAsync = shutdownAsync ?? throw new ArgumentNullException(nameof(shutdownAsync));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _timeout = timeout ?? DefaultTimeout;

        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public bool CanClose => Volatile.Read(ref _canClose) != 0;

    internal Task? LateCompletionObservation { get; private set; }

    public Task BeginShutdownAsync()
    {
        lock (_gate)
        {
            return _shutdownTask ??= RunAsync();
        }
    }

    private async Task RunAsync()
    {
        // Let the cancelled Closing event unwind before Close re-enters it.
        await Task.Yield();

        using var timeout = new CancellationTokenSource(_timeout);
        Task cleanupTask = Task.CompletedTask;
        bool deadlineExceeded = false;
        bool abandoned = false;
        try
        {
            cleanupTask = _shutdownAsync(timeout.Token);
            await cleanupTask.WaitAsync(timeout.Token);
        }
        catch (ProjectCloseAbortedException ex)
        {
            // The project kept itself open to protect unsaved edits, so closing the window would
            // discard them anyway. Stay open and let the user save and try again.
            abandoned = true;
            s_logger.LogWarning(
                ex,
                "Application shutdown was abandoned; the window stays open so the project can be saved.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            deadlineExceeded = true;
            s_logger.LogWarning(
                "Application shutdown exceeded the {Timeout} deadline; closing before cleanup finished.",
                _timeout);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Application shutdown cleanup failed; closing the window.");
        }
        finally
        {
            if (deadlineExceeded)
            {
                LateCompletionObservation = ObserveLateCompletionAsync(cleanupTask);
            }

            if (abandoned)
            {
                // Dropped so a later close attempt runs the shutdown again instead of joining this
                // abandoned one.
                lock (_gate)
                {
                    _shutdownTask = null;
                }
            }
            else
            {
                Volatile.Write(ref _canClose, 1);
                try
                {
                    _close();
                }
                catch (Exception ex)
                {
                    s_logger.LogError(ex, "Failed to close the window after shutdown cleanup.");
                }
            }
        }
    }

    private static async Task ObserveLateCompletionAsync(Task cleanupTask)
    {
        try
        {
            await cleanupTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Application shutdown cleanup failed after the deadline expired.");
        }
    }
}
