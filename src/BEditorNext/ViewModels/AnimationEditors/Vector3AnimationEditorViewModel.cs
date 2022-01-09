using System.Numerics;

using BEditorNext.Animation;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class Vector3AnimationEditorViewModel : AnimationEditorViewModel<Vector3>
{
    public Vector3AnimationEditorViewModel(Animation<Vector3> animation, BaseEditorViewModel<Vector3> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Vector3 Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector3(float.MaxValue, float.MaxValue, float.MaxValue));

    public Vector3 Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector3(float.MinValue, float.MinValue, float.MinValue));
}
