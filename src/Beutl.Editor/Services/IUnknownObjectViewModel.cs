using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IUnknownObjectViewModel
{
    IReadOnlyReactiveProperty<bool> IsFallback { get; }

    IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    IObservable<string?> GetJsonString();

    void SetJsonString(string? json);
}
