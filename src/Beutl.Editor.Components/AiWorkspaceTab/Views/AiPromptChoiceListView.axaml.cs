using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.AiWorkspaceTab.Views;

public partial class AiPromptChoiceListView : UserControl
{
    public AiPromptChoiceListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Which of the library's two lists this instance shows. The commands come
    /// from the shared view model either way, so only the items differ.
    /// </summary>
    [GeneratedStyledProperty]
    public partial IEnumerable? Choices { get; set; }
}
