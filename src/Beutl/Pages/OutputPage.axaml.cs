using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

using Beutl.Framework;
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

        this.GetObservable(IsVisibleProperty)
            .Where(b => b)
            .SkipUntil(this.GetObservable(DataContextProperty).Where(x => x is OutputPageViewModel))
            .Take(1)
            .Subscribe(_ =>
            {
                if (DataContext is OutputPageViewModel viewModel)
                {
                    viewModel.Restore();
                }
            });
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
        private readonly Dictionary<OutputExtension, IControl> _contextToViewType = new();

        public IControl? Build(object? param)
        {
            if (param is OutputQueueItem item)
            {
                if (_contextToViewType.TryGetValue(item.Context.Extension, out IControl? control))
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
