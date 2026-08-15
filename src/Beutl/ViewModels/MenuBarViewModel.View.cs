using System.Diagnostics.CodeAnalysis;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(ResetDockLayout), nameof(ApplyDockLayout), nameof(OpenDockLayoutTab))]
    private void InitializeViewCommands(IObservable<bool> isSceneOpened)
    {
        ResetDockLayout = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnResetDockLayout);

        ApplyDockLayout = new ReactiveCommandSlim<DockLayoutPresetItem>(isSceneOpened)
            .WithSubscribe(OnApplyDockLayout);

        // Saving, renaming and deleting live in the dock layout tool tab.
        OpenDockLayoutTab = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnOpenDockLayoutTab);
    }

    // View
    //    Reset dock layout
    //    Apply a saved dock layout
    //    Open the dock layout tool tab
    public ReactiveCommandSlim ResetDockLayout { get; private set; }

    public ReactiveCommandSlim<DockLayoutPresetItem> ApplyDockLayout { get; private set; }

    public ReactiveCommandSlim OpenDockLayoutTab { get; private set; }

    public ICoreList<DockLayoutPresetItem> DockLayoutPresets => DockLayoutPresetService.Instance.Items;

    private void OnResetDockLayout()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
        {
            viewModel.DockHost.ResetLayout();
        }
    }

    private void OnOpenDockLayoutTab()
    {
        if (!TryGetSelectedEditViewModel(out EditViewModel? viewModel)) return;

        if (viewModel.DockHost.FindToolContext(typeof(DockLayoutTabExtension)) is { } existing)
        {
            existing.IsSelected.Value = true;
            return;
        }

        if (DockLayoutTabExtension.Instance.TryCreateContext(viewModel, out IToolContext? context)
            && !viewModel.OpenToolTab(context))
        {
            context.Dispose();
        }
    }

    private void OnApplyDockLayout(DockLayoutPresetItem? preset)
    {
        if (preset is null) return;
        if (!TryGetSelectedEditViewModel(out EditViewModel? viewModel)) return;

        if (viewModel.DockHost.ApplyLayout(preset.Layout))
        {
            _logger.LogInformation("Applied dock layout preset '{Name}'.", preset.Name.Value);
        }
        else
        {
            _logger.LogWarning("Failed to apply dock layout preset '{Name}'.", preset.Name.Value);
            NotificationService.ShowError(Strings.DockLayout, MessageStrings.OperationFailed);
        }
    }
}
