using System.Runtime.CompilerServices;
using Beutl.Extensibility;

namespace Beutl.Api.Services;

/// <summary>
/// Coordinates host-owned registration retirement with package removal.
/// </summary>
internal static class ExtensionRegistrationLifetimes
{
    private static readonly ConditionalWeakTable<Extension, Retirement> s_retirements = new();

    public static void Retire(Extension extension, Func<ValueTask> retireAsync)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(retireAsync);

        Retirement retirement = s_retirements.GetValue(
            extension,
            static _ => new Retirement());
        retirement.BeginTracking();

        ValueTask drain;
        try
        {
            drain = retireAsync();
        }
        catch (Exception ex)
        {
            drain = new ValueTask(Task.FromException(ex));
        }

        retirement.CompleteTracking(drain);
    }

    internal static ExtensionRemovalDrain SealRemoval(IReadOnlyList<Extension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var drains = new Task[extensions.Count];
        for (int index = 0; index < extensions.Count; index++)
        {
            Extension extension = extensions[index]
                ?? throw new ArgumentException("Extensions cannot contain null.", nameof(extensions));
            drains[index] = s_retirements.GetValue(
                extension,
                static _ => new Retirement()).Seal();
            s_retirements.Remove(extension);
        }

        return new ExtensionRemovalDrain(extensions, drains);
    }

    private sealed class Retirement
    {
        private readonly object _gate = new();
        private readonly List<Task> _drains = [];
        private Task? _drainTask;
        private bool _sealed;
        private int _pendingRetirements;

        public void BeginTracking()
        {
            lock (_gate)
            {
                if (_sealed)
                {
                    throw new InvalidOperationException(
                        "An extension registration retired after package removal was sealed.");
                }

                _pendingRetirements++;
            }
        }

        public void CompleteTracking(ValueTask drain)
        {
            Task task = drain.IsCompletedSuccessfully
                ? Task.CompletedTask
                : drain.AsTask();

            lock (_gate)
            {
                if (!task.IsCompletedSuccessfully)
                    _drains.Add(task);

                _pendingRetirements--;
                Monitor.PulseAll(_gate);
            }
        }

        public Task Seal()
        {
            lock (_gate)
            {
                if (_drainTask is not null)
                    return _drainTask;

                _sealed = true;
                while (_pendingRetirements != 0)
                    Monitor.Wait(_gate);

                _drainTask = _drains.Count == 0
                    ? Task.CompletedTask
                    : Task.WhenAll(_drains);
                _drains.Clear();
                return _drainTask;
            }
        }
    }
}

internal interface ILiveUnloadExtension
{
}

internal sealed class ExtensionRemovalDrain(
    IReadOnlyList<Extension> extensions,
    IReadOnlyList<Task> drains)
{
    public IReadOnlyList<Extension> Extensions { get; } = extensions;

    public async Task DrainAllAsync()
    {
        List<Exception>? failures = null;
        for (int index = 0; index < drains.Count; index++)
        {
            try
            {
                await drains[index].ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(new InvalidOperationException(
                    $"Extension '{Extensions[index].GetType().FullName}' failed to drain.",
                    ex));
            }
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }
}
