using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed partial class ProxyStore
{
    // Each flush replaces the index with this instance's snapshot while holding _lock.
    private void FlushCore()
    {
        Directory.CreateDirectory(StoreRootPath);
        var index = new ProxyStoreIndex { Entries = [.. _entries.Values] };
        string json = JsonSerializer.Serialize(index, s_jsonOptions);
        string tmp = Path.Combine(StoreRootPath, $"{Path.GetFileName(_indexPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, _indexPath, overwrite: true);
            _touchDirty = false;
            _persistenceDegraded = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_persistenceDegraded)
            {
                s_logger.LogWarning(
                    ex,
                    "Proxy index persistence for '{IndexPath}' failed; skipping durable persistence and serving in-memory state until a later flush succeeds.",
                    _indexPath);
            }
            _persistenceDegraded = true;
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private async Task FlushTouchesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            lock (_lock)
            {
                if (!_touchDirty)
                    return;

                FlushCore();
            }

            ReclaimRetiredProxyFiles();
            Interlocked.Exchange(ref _touchFlushFaulted, 0);
        }
        catch (Exception ex)
        {
            // FlushCore degrades I/O and permission faults internally, so anything landing here is
            // unexpected (bug-class). Log once per failure streak — the finally block reschedules
            // this loop every second and a per-attempt error would flood the log.
            if (Interlocked.Exchange(ref _touchFlushFaulted, 1) == 0)
            {
                s_logger.LogError(
                    ex,
                    "Deferred proxy touch flush failed unexpectedly; LastUsedUtc updates stay pending and will retry every second.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _touchFlushScheduled, 0);
            lock (_lock)
            {
                if (_touchDirty)
                    ScheduleTouchFlush();
            }
        }
    }

    private void ScheduleTouchFlush()
    {
        if (Interlocked.Exchange(ref _touchFlushScheduled, 1) == 0)
            _ = FlushTouchesAsync();
    }

}
