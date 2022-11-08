using Avalonia.Styling;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class SceneSettings : ContentDialog, IStyleable
{
    public SceneSettings()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);
}
