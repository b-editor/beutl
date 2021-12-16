using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt64EditorViewModel : BaseNumberEditorViewModel<ulong>
{
    public UInt64EditorViewModel(Setter<ulong> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ulong> Value { get; }

    public override ulong Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, ulong.MaxValue);

    public override ulong Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, ulong.MinValue);

    public override INumberEditorService<ulong> EditorService { get; } = NumberEditorService.Instance.Get<ulong>();
}
