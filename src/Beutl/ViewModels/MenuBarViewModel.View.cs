using System.Diagnostics.CodeAnalysis;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(ResetDockLayout))]
    private void InitializeViewCommands(IObservable<bool> isSceneOpened)
    {
        ResetDockLayout = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnResetDockLayout);
    }

    // View
    //    Reset dock layout
    public ReactiveCommandSlim ResetDockLayout { get; private set; }

    private static void OnResetDockLayout()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
        {
            viewModel.DockHost.ResetLayout();
        }
    }
}
