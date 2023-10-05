using Reactive.Bindings;

namespace Beutl.ViewModels;

public interface IUnknownObjectViewModel
{
    IReadOnlyReactiveProperty<bool> IsDummy { get; }

    IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    IObservable<string?> GetJsonString();

    void SetJsonString(string? json);
}
