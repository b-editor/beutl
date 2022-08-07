using BeUtl.Framework;
using BeUtl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class AlignmentYEditorViewModel : BaseEditorViewModel<AlignmentY>
{
    public AlignmentYEditorViewModel(IAbstractProperty<AlignmentY> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsTop = property.GetObservable()
            .Select(x => x is AlignmentY.Top)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsCenter = property.GetObservable()
            .Select(x => x is AlignmentY.Center)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsBottom = property.GetObservable()
            .Select(x => x is AlignmentY.Bottom)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AlignmentY> Value { get; }

    public ReadOnlyReactivePropertySlim<bool> IsTop { get; }

    public ReadOnlyReactivePropertySlim<bool> IsCenter { get; }

    public ReadOnlyReactivePropertySlim<bool> IsBottom { get; }
}
