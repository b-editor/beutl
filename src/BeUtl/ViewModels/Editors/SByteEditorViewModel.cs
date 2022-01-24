using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class SByteEditorViewModel : BaseNumberEditorViewModel<sbyte>
{
    public SByteEditorViewModel(PropertyInstance<sbyte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<sbyte> Value { get; }

    public override sbyte Maximum => Setter.GetMaximumOrDefault(sbyte.MaxValue);

    public override sbyte Minimum => Setter.GetMinimumOrDefault(sbyte.MinValue);

    public override INumberEditorService<sbyte> EditorService { get; } = NumberEditorService.Instance.Get<sbyte>();
}
