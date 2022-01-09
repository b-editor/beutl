using System.Numerics;

using BEditorNext.Animation;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class Vector4AnimationEditorViewModel : AnimationEditorViewModel<Vector4>
{
    public Vector4AnimationEditorViewModel(Animation<Vector4> animation, BaseEditorViewModel<Vector4> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Vector4 Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Vector4 Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector4(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
