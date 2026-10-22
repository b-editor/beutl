using Beutl.Editor.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// Edits a TimeSpan value, such as the time port of VideoSourceNode and SceneNode, with MediaTimeEditor.
// Remove it when #2885 step 4 moves those ports to MediaTime.
internal sealed class TimeSpanValueEditorViewModel(IPropertyAdapter<TimeSpan> property)
    : PropertyEditorViewModel<TimeSpan, MediaTime>(property)
{
    protected override MediaTime ToEdit(TimeSpan value) => MediaTime.FromTimeSpan(value);

    protected override TimeSpan FromEdit(MediaTime edit) => edit.ToTimeSpan();
}
