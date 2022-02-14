using Avalonia.Styling;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Views.Dialogs;

public partial class SceneSettings : ContentDialog, IStyleable
{
    public SceneSettings()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);
}
