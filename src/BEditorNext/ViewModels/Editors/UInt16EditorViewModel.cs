using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt16EditorViewModel : BaseNumberEditorViewModel<ushort>
{
    public UInt16EditorViewModel(Setter<ushort> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ushort> Value { get; }

    public override ushort Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, ushort.MaxValue);

    public override ushort Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, ushort.MinValue);

    public override INumberEditorService<ushort> EditorService { get; } = NumberEditorService.Instance.Get<ushort>();
}
