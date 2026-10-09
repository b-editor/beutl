using Avalonia.Controls;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;

namespace Beutl.Views.Dock;

/// <summary>Opens an empty tab in its dock; the tab lists the tools to choose from.</summary>
public sealed class ToolTabAddButton : Button
{
    protected override void OnClick()
    {
        base.OnClick();

        if (DataContext is IToolDock target
            && target.Factory is BeutlDockFactory factory)
        {
            factory.OpenNewToolTab(target);
        }
    }
}
