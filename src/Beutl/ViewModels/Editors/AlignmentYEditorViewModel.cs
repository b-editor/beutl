using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentYEditorViewModel : ValueEditorViewModel<AlignmentY>
{
    public AlignmentYEditorViewModel(IAbstractProperty<AlignmentY> property)
        : base(property)
    {
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

    public ReadOnlyReactivePropertySlim<bool> IsTop { get; }

    public ReadOnlyReactivePropertySlim<bool> IsCenter { get; }

    public ReadOnlyReactivePropertySlim<bool> IsBottom { get; }
}
