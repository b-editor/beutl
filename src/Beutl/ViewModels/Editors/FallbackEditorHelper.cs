using Beutl.Editor.Components.Helpers;
using Beutl.Serialization;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

internal static class FallbackEditorHelper
{
    // The fallback state of an editor whose value can be a placeholder for a type that failed to load.
    public static (
        IReadOnlyReactiveProperty<bool> IsFallback,
        IReadOnlyReactiveProperty<string> ActualTypeName,
        IReadOnlyReactiveProperty<string> FallbackMessage) ObserveFallbackInfo<T>(
        IObservable<T> value, CompositeDisposable disposables)
        where T : class?
    {
        IReadOnlyReactiveProperty<bool> isFallback = value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        IReadOnlyReactiveProperty<string> actualTypeName = value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(disposables);

        IReadOnlyReactiveProperty<string> fallbackMessage = value.Select(FallbackHelper.GetFallbackMessage)
            .ToReadOnlyReactivePropertySlim(MessageStrings.RestoreFailedTypeNotFound)
            .DisposeWith(disposables);

        return (isFallback, actualTypeName, fallbackMessage);
    }
}
