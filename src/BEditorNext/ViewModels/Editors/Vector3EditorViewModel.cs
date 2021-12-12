using System.Numerics;

using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class Vector3EditorViewModel : BaseEditorViewModel<Vector3>
{
    public Vector3EditorViewModel(Setter<Vector3> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Vector3> Value { get; }

    public Vector3 Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector3(float.MaxValue, float.MaxValue, float.MaxValue));

    public Vector3 Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector3(float.MinValue, float.MinValue, float.MinValue));
}
