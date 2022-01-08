using System.Numerics;

using BEditorNext.Animation;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class Vector2AnimationEditorViewModel : AnimationEditorViewModel<Vector2>
{
    public Vector2AnimationEditorViewModel(Animation<Vector2> animation, BaseEditorViewModel<Vector2> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Vector2 Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector2(float.MaxValue, float.MaxValue));

    public Vector2 Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector2(float.MinValue, float.MinValue));
}
