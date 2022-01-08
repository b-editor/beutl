using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class SizeEditorViewModel : BaseEditorViewModel<Size>
{
    public SizeEditorViewModel(Setter<Size> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Size> Value { get; }

    public Size Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Size(float.MaxValue, float.MaxValue));

    public Size Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Size(float.MinValue, float.MinValue));
}
