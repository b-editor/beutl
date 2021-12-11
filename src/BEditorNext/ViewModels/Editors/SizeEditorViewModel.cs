
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class SizeEditorViewModel : BaseEditorViewModel<Size>
{
    public SizeEditorViewModel(Setter<Size> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<Size> Value { get; }

    public Size Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Size(float.MaxValue, float.MaxValue));

    public Size Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Size(float.MinValue, float.MinValue));
}
