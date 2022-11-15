using Beutl.Framework;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class TimeSpanEditorViewModel : BaseEditorViewModel<TimeSpan>
{
    public TimeSpanEditorViewModel(IAbstractProperty<TimeSpan> property)
        : base(property)
    {
        IObservable<TimeSpan> observable = property.GetObservable();
        Text = observable
            .Select(x => x.ToString() ?? "")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;

        Value = observable
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<string> Text { get; }

    public ReadOnlyReactivePropertySlim<TimeSpan> Value { get; }
}
