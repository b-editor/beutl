using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentXEditorViewModel : ValueEditorViewModel<AlignmentX>
{
    public AlignmentXEditorViewModel(IAbstractProperty<AlignmentX> property)
        : base(property)
    {
        IsLeft = property.GetObservable()
            .Select(x => x is AlignmentX.Left)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsCenter = property.GetObservable()
            .Select(x => x is AlignmentX.Center)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsRight = property.GetObservable()
            .Select(x => x is AlignmentX.Right)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsLeft { get; }

    public ReadOnlyReactivePropertySlim<bool> IsCenter { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRight { get; }
}
