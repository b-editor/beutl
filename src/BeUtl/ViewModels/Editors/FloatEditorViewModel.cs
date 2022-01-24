using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class FloatEditorViewModel : BaseNumberEditorViewModel<float>
{
    public FloatEditorViewModel(PropertyInstance<float> setter)
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
