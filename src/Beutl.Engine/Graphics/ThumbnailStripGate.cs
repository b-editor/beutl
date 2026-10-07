namespace Beutl.Graphics;

internal sealed class ThumbnailStripGate(int capacity)
{
    private readonly SemaphoreSlim _semaphore = new(capacity, capacity);

    public int AvailableSlots => _semaphore.CurrentCount;

    // Returns false when cancelled while waiting; the caller then holds no slot and must not release one.
    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Release() => _semaphore.Release();
}
