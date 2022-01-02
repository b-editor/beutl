using BEditorNext.Animation;
using BEditorNext.Graphics;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class VectorAnimationEditorViewModel : AnimationEditorViewModel<Vector>
{
    public VectorAnimationEditorViewModel(Animation<Vector> animation, BaseEditorViewModel<Vector> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Vector Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Vector(float.MaxValue, float.MaxValue));

    public Vector Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Vector(float.MinValue, float.MinValue));
}
