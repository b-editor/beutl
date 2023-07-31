using Avalonia.Styling;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class SceneSettings : ContentDialog
{
    public SceneSettings()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);
}
