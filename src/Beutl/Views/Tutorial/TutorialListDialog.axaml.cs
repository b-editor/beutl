using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Services.Tutorials;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Tutorial;

public partial class TutorialListDialog : FAContentDialog
{
    public TutorialListDialog()
    {
        InitializeComponent();
        LoadTutorials();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void LoadTutorials()
    {
        ITutorialService service = TutorialService.Current;
        if (service == null)
            return;

        TutorialList.ItemsSource = service.GetAvailableTutorials();
    }

    private async void OnTutorialItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tutorialId })
        {
            Hide();
            await TutorialService.Current.StartTutorial(tutorialId, autoFulfillPrerequisites: true);
        }
    }
}
