using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Controls;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

public partial class AiSubtitleView : UserControl
{
    private IDisposable? _planReturnRefresh;
    private AiSubtitleDialogViewModel? _templatePreviewOwner;

    public AiSubtitleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _planReturnRefresh?.Dispose();
        _planReturnRefresh = DataContext is AiSubtitleDialogViewModel viewModel
            ? AiPlanReturnRefresh.Attach(
                this,
                viewModel.AiPlanCoordinator,
                () =>
                {
                    viewModel.RefreshAvailability();
                    viewModel.RefreshModels();
                })
            : null;
        UpdateTemplatePreviewOwner();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateTemplatePreviewOwner();
        (DataContext as AiSubtitleDialogViewModel)?.RefreshAvailability();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _templatePreviewOwner?.DetachTemplatePreview();
        _templatePreviewOwner = null;
        base.OnUnloaded(e);
    }

    private void UpdateTemplatePreviewOwner()
    {
        AiSubtitleDialogViewModel? next = IsLoaded
            ? DataContext as AiSubtitleDialogViewModel
            : null;
        if (ReferenceEquals(_templatePreviewOwner, next))
            return;

        _templatePreviewOwner?.DetachTemplatePreview();
        _templatePreviewOwner = next;
        _templatePreviewOwner?.AttachTemplatePreview();
    }

    private void OnConfirmHistoryOverwriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiSubtitleDialogViewModel viewModel)
        {
            viewModel.ConfirmPendingHistoryResult();
        }
    }

    private void OnCancelHistoryOverwriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiSubtitleDialogViewModel viewModel)
        {
            viewModel.DiscardPendingHistoryResult();
        }
    }
}

internal sealed class CaptionTemplatePreviewBitmapView : BitmapView
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new CaptionTemplatePreviewAutomationPeer(this);

    private sealed class CaptionTemplatePreviewAutomationPeer(
        CaptionTemplatePreviewBitmapView owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore()
            => AutomationControlType.Image;
    }
}
