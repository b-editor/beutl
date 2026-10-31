using Reactive.Bindings;

namespace Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

// Binds an editor to a setting in both directions.
internal static class SettingBinding
{
    // Only an edit writes the setting, so opening a view leaves one that does not read, such as a bad color, alone.
    // A value the property's validator coerces, such as an opacity above 1, comes back to the editor coerced.
    public static ReactiveProperty<TValue> Bind<TSetting, TValue>(
        IProperty<TSetting> property, Func<TSetting, TValue> read, Func<TValue, TSetting> write, CompositeDisposable disposables)
    {
        ReactiveProperty<TValue> reactive = property.GetObservable()
            .Select(read)
            .ToReactiveProperty()
            .DisposeWith(disposables);
        reactive.Skip(1).Subscribe(value => property.Value = write(value)).DisposeWith(disposables);
        return reactive;
    }
}
