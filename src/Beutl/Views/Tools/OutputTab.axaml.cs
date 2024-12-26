using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Beutl.Pages;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using AddOutputProfileDialog = Beutl.Views.Dialogs.AddOutputProfileDialog;

namespace Beutl.Views.Tools;

public partial class OutputTab : UserControl
{
    private readonly IDataTemplate _sharedDataTemplate = new _DataTemplate();

    public OutputTab()
    {
        InitializeComponent();
        contentControl.ContentTemplate = _sharedDataTemplate;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        var ext = OutputService.GetExtensions(viewModel.EditViewModel.Scene.FileName);
        if (ext.Length == 1)
        {
            viewModel.AddItem(ext[0]);
        }
        else
        {
            var dialogViewModel = new AddOutputProfileViewModel(viewModel);
            var dialog = new AddOutputProfileDialog { DataContext = dialogViewModel };

            await dialog.ShowAsync();
        }

        viewModel.Save();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputTabViewModel viewModel) return;

        viewModel.RemoveSelected();
        viewModel.Save();
    }

    private sealed class _DataTemplate : IDataTemplate
    {
        private readonly Dictionary<OutputExtension, Control> _contextToViewType = [];

        public Control? Build(object? param)
        {
            if (param is OutputProfileItem item)
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
            return data is OutputProfileItem;
        }
    }
}
