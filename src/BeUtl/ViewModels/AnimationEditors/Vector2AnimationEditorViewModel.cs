using System.Numerics;

using BeUtl.Animation;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class Vector2AnimationEditorViewModel : AnimationEditorViewModel<Vector2>
{
    public Vector2AnimationEditorViewModel(AnimationSpan<Vector2> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public Vector2 Maximum => WrappedProperty.GetMaximumOrDefault(new Vector2(float.MaxValue, float.MaxValue));

    public Vector2 Minimum => WrappedProperty.GetMinimumOrDefault(new Vector2(float.MinValue, float.MinValue));
}
