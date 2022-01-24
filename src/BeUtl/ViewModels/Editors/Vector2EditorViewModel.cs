using System.Numerics;

using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class Vector2EditorViewModel : BaseEditorViewModel<Vector2>
{
    public Vector2EditorViewModel(PropertyInstance<Vector2> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Vector2> Value { get; }

    public Vector2 Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector2(float.MaxValue, float.MaxValue));

    public Vector2 Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector2(float.MinValue, float.MinValue));
}
