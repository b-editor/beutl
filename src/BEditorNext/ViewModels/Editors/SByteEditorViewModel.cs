using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class SByteEditorViewModel : BaseNumberEditorViewModel<sbyte>
{
    public SByteEditorViewModel(Setter<sbyte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<sbyte> Value { get; }

    public override sbyte Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, sbyte.MaxValue);

    public override sbyte Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, sbyte.MinValue);

    public override INumberEditorService<sbyte> EditorService { get; } = NumberEditorService.Instance.Get<sbyte>();
}
