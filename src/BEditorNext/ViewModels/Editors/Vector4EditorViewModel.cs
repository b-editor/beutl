using System.Numerics;

using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class Vector4EditorViewModel : BaseEditorViewModel<Vector4>
{
    public Vector4EditorViewModel(Setter<Vector4> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Vector4> Value { get; }

    public Vector4 Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Vector4 Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector4(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
