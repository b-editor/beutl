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
        AvailableExtensions = OutputService.GetExtensions(outputTabViewModel.EditViewModel.Scene.FileName);

        CanAdd = SelectedExtension.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<bool> CanAdd { get; }

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
