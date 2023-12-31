using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Pages;

public partial class OutputPage : UserControl
{
    private static readonly IDataTemplate s_sharedDataTemplate = new _DataTemplate();

    public OutputPage()
    {
        InitializeComponent();
        contentControl.ContentTemplate = s_sharedDataTemplate;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is OutputPageViewModel viewModel)
        {
            viewModel.Restore();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is OutputPageViewModel viewModel)
        {
            viewModel.Save();
        }
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputPageViewModel viewModel)
        {
            var dialogViewModel = new AddOutputQueueViewModel();
            var dialog = new AddOutputQueueDialog
            {
                DataContext = dialogViewModel
            };

            await dialog.ShowAsync();
            dialogViewModel.Dispose();

            viewModel.Save();
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputPageViewModel viewModel)
        {
            viewModel.RemoveSelected();
            viewModel.Save();
        }
    }

    private sealed class _DataTemplate : IDataTemplate
    {
        private readonly Dictionary<OutputExtension, Control> _contextToViewType = [];

        public Control? Build(object? param)
        {
            if (param is OutputQueueItem item)
            {
                if (_contextToViewType.TryGetValue(item.Context.Extension, out Control? control))
                {
                    control.DataContext = item.Context;
                    return control;
                }
                else if (item.Context.Extension.TryCreateControl(item.Context.TargetFile, out control))
                {
                    _contextToViewType[item.Context.Extension] = control;
                    control.DataContext = item.Context;
                    return control;
                }
            }

            return null;
        }

        public bool Match(object? data)
        {
            return data is OutputQueueItem;
        }
    }
}
