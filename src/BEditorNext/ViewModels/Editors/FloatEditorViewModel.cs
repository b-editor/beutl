using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class FloatEditorViewModel : BaseEditorViewModel<float>
{
    public FloatEditorViewModel(Setter<float> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<float> Value { get; }

    public float Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, float.MaxValue);

    public float Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, float.MinValue);
}
