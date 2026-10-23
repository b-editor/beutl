using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class CubeSourceEditorViewModel(IPropertyAdapter<CubeSource?> property)
    : LocalFileSourceEditorViewModel<CubeSource>(property)
{
    public void SetValueAndCommit(CubeSource? oldValue, CubeSource? newValue)
    {
        if (!IsElementEditable) return;
        if (!EqualityComparer<CubeSource?>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is { } kf)
            {
                kf.Value.Value = newValue;
            }
            else
            {
                PropertyAdapter.SetValue(newValue);
            }

            Commit();
        }
    }
}
