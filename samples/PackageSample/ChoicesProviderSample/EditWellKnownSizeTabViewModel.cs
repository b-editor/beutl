using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace PackageSample;

public sealed class EditWellKnownSizeTabViewModel(ToolTabExtension extension) : IToolContext
{
    private int _disposed;

    public ToolTabExtension Extension { get; } = extension;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Edit Well known size");

    public AddWellKnownSizeScreenViewModel AddScreen { get; } = new AddWellKnownSizeScreenViewModel();

    public RemoveWellKnownSizeScreenViewModel RemoveScreen { get; } = new RemoveWellKnownSizeScreenViewModel();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        AddScreen.Dispose();
        RemoveScreen.Dispose();
        IsSelected.Dispose();
        (Header as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }
}
