using System.Numerics;

using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class Vector3EditorViewModel : BaseEditorViewModel<Vector3>
{
    public Vector3EditorViewModel(PropertyInstance<Vector3> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Vector3> Value { get; }

    public Vector3 Maximum => Setter.GetMaximumOrDefault(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue));

    public Vector3 Minimum => Setter.GetMinimumOrDefault(new Vector3(float.MinValue, float.MinValue, float.MinValue));
}
