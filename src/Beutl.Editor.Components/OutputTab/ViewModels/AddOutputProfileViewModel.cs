using Reactive.Bindings;

namespace Beutl.Editor.Components.OutputTab.ViewModels;

public sealed class AddOutputProfileViewModel
{
    private readonly OutputTabViewModel _outputTabViewModel;

    public AddOutputProfileViewModel(OutputTabViewModel outputTabViewModel)
    {
        _outputTabViewModel = outputTabViewModel;
        AvailableExtensions = outputTabViewModel.GetExtensions(outputTabViewModel.Scene.GetType());
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
