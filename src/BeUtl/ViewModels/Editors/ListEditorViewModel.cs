using System.Collections;
using System.ComponentModel;

using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class ListEditorViewModel : BaseEditorViewModel
{
    public ListEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        List = property.GetObservable()
            .Select(x => x as IList)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ObserveCount = List.SelectMany(x =>
            {
                if (x is INotifyPropertyChanged n)
                {
                    return n.PropertyChangedAsObservable()
                        .Where(x => x.PropertyName == "Count")
                        .Select(_ => x.Count)
                        .Publish(x.Count)
                        .RefCount();
                }
                else
                {
                    return Observable.Return(x?.Count ?? 0);
                }
            })
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        CountString = ObserveCount
            .Select(x => string.Format(Message.CountItems, x))
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<IList?> List { get; }

    public ReactiveProperty<int> ObserveCount { get; }

    public ReadOnlyReactivePropertySlim<string> CountString { get; }
}
