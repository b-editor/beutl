using Avalonia.Controls;
using Avalonia.Interactivity;

using static Beutl.Pages.SettingsPages.PropertiesEditor;

namespace Beutl.Pages.SettingsPages;

public partial class PropertyEditorGroup : UserControl
{
    public PropertyEditorGroup()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
    }
}
