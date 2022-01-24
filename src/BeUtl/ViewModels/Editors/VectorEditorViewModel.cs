using BeUtl.Graphics;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class VectorEditorViewModel : BaseEditorViewModel<Vector>
{
    public VectorEditorViewModel(PropertyInstance<Vector> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Vector> Value { get; }

    public Vector Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector(float.MaxValue, float.MaxValue));

    public Vector Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector(float.MinValue, float.MinValue));
}
