using System.Collections;

using BeUtl.Framework;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class ListEditorViewModel : BaseEditorViewModel
{
    public ListEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        List = property.GetObservable()
            .Select(x => x as IList)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<IList?> List { get; }
}
