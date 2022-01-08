using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class FloatEditorViewModel : BaseNumberEditorViewModel<float>
{
    public FloatEditorViewModel(Setter<float> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> Value { get; }

    public override float Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, float.MaxValue);

    public override float Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, float.MinValue);

    public override INumberEditorService<float> EditorService { get; } = NumberEditorService.Instance.Get<float>();
}
