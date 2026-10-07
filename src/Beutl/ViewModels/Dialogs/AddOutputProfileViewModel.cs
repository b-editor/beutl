using Beutl.Services;
using Beutl.ViewModels.Tools;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class AddOutputProfileViewModel
{
    private readonly OutputTabViewModel _outputTabViewModel;

    public AddOutputProfileViewModel(OutputTabViewModel outputTabViewModel)
    {
        _outputTabViewModel = outputTabViewModel;
        AvailableExtensions = outputTabViewModel.GetExtensions(outputTabViewModel.EditViewModel.Scene.GetType());
    }

    public ReactiveProperty<OutputExtension?> SelectedExtension { get; } = new();

    public OutputExtension[] AvailableExtensions { get; }

    public void Add()
    {
        if (SelectedExtension.Value != null)
        {
            _outputTabViewModel.AddItem(SelectedExtension.Value);
        }
    }
}
