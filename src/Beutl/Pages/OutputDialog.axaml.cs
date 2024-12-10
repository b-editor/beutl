using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Windowing;

namespace Beutl.Pages;

public partial class OutputDialog : AppWindow
{
    private readonly IDataTemplate _sharedDataTemplate = new _DataTemplate();

    public OutputDialog()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows())
        {
            TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBar.Height = 40;
        }
        else if (OperatingSystem.IsMacOS())
        {
            Grid.Margin = new Thickness(8, 30, 0, 0);
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        }

        contentControl.ContentTemplate = _sharedDataTemplate;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is OutputDialogViewModel viewModel)
        {
            viewModel.Restore();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is OutputDialogViewModel viewModel)
        {
            viewModel.Save();
        }
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputDialogViewModel viewModel)
        {
            var dialogViewModel = new AddOutputQueueViewModel();
            var dialog = new AddOutputQueueDialog { DataContext = dialogViewModel };

            await dialog.ShowAsync();
            dialogViewModel.Dispose();

            viewModel.Save();
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputDialogViewModel viewModel)
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
