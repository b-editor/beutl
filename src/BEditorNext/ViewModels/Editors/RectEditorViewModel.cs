using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class RectEditorViewModel : BaseEditorViewModel<Rect>
{
    public RectEditorViewModel(Setter<Rect> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Rect> Value { get; }

    public Rect Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Rect(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Rect Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Rect(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
