using System.Numerics;

using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class Vector2EditorViewModel : BaseEditorViewModel<Vector2>
{
    public Vector2EditorViewModel(Setter<Vector2> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Vector2> Value { get; }

    public Vector2 Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector2(float.MaxValue, float.MaxValue));

    public Vector2 Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector2(float.MinValue, float.MinValue));
}
