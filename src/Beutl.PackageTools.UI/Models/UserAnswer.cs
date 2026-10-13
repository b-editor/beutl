using Reactive.Bindings;
using Reactive.Bindings.TinyLinq;

namespace Beutl.PackageTools.UI.Models;

// A yes or no answer that the user can give before or while a task waits for it.
public sealed class UserAnswer
{
    private TaskCompletionSource<bool>? _pending;

    public UserAnswer()
    {
        HasValue = Value.Select(v => v.HasValue)
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<bool?> Value { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasValue { get; }

    public void Answer(bool value)
    {
        Value.Value = value;
        _pending?.SetResult(value);
    }

    public Task<bool> WaitAsync(CancellationToken token)
    {
        Task<bool> task;
        if (!Value.Value.HasValue)
        {
            _pending = new TaskCompletionSource<bool>();
            task = _pending.Task;
        }
        else
        {
            task = Task.FromResult(Value.Value.Value);
        }

        return task.WaitAsync(token);
    }
}
