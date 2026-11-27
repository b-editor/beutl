using Avalonia.Controls;
using Avalonia.Layout;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Sits over the header text during comparison selection, leaving the disclosure, drag and edit buttons usable.
internal sealed class FilterEffectComparisonHeader : Button
{
    public FilterEffectComparisonHeader()
    {
        Padding = default;
        MinWidth = 0;
        MinHeight = 0;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Cursor = Cursors.Hand;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        (DataContext as FilterEffectEditorViewModel)?.ChooseComparisonEffect();
    }
}
