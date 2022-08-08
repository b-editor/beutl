using BeUtl.Framework;
using BeUtl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class AlignmentXEditorViewModel : BaseEditorViewModel<AlignmentX>
{
    public AlignmentXEditorViewModel(IAbstractProperty<AlignmentX> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

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

    public ReadOnlyReactivePropertySlim<AlignmentX> Value { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLeft { get; }

    public ReadOnlyReactivePropertySlim<bool> IsCenter { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRight { get; }
}
