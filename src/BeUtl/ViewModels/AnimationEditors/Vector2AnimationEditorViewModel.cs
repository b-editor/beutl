using System.Numerics;

using BeUtl.Animation;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class Vector2AnimationEditorViewModel : AnimationEditorViewModel<Vector2>
{
    public Vector2AnimationEditorViewModel(Animation<Vector2> animation, EditorViewModelDescription description)
        : base(animation, description)
    {
    }

    public Vector2 Maximum => Setter.GetMaximumOrDefault(new Vector2(float.MaxValue, float.MaxValue));

    public Vector2 Minimum => Setter.GetMinimumOrDefault(new Vector2(float.MinValue, float.MinValue));
}
