using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

internal static class SettingsPageBinding
{
    public static ReactiveProperty<T> Bind<T>(IProperty<T> setting, CompositeDisposable disposables)
        where T : struct
        => Bind(setting.GetObservable(), value => setting.Value = value, disposables);

    public static ReactiveProperty<T> Bind<T>(IObservable<T> source, Action<T> write, CompositeDisposable disposables)
        where T : struct
    {
        ReactiveProperty<T> property = source
            .ToReactiveProperty()
            .DisposeWith(disposables);
        property.Subscribe(write).DisposeWith(disposables);
        return property;
    }
}
