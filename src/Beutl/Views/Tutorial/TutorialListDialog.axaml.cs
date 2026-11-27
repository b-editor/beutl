using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Services.Tutorials;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Tutorial;

internal partial class TutorialListDialog : FAContentDialog
{
    private readonly ITutorialService _service;

    public TutorialListDialog(ITutorialService service)
    {
        _service = service;
        InitializeComponent();
        LoadTutorials();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void LoadTutorials()
    {
        TutorialList.ItemsSource = _service.GetAvailableTutorials();
    }

    private async void OnTutorialItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tutorialId })
        {
            Hide();
            await _service.StartTutorial(tutorialId, autoFulfillPrerequisites: true);
        }
    }
}
