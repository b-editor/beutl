using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.ViewModels.Tools;
using Beutl.Views.Dialogs;
using Reactive.Bindings;

namespace Beutl.Views.Tools;

public partial class OutputView : UserControl
{
    private static readonly IReadOnlyReactiveProperty<bool> s_notRunning = new ReactivePropertySlim<bool>();
    private readonly IOutputExecutionController? _execution;

    public OutputView()
    {
        InitializeComponent();
    }

    public OutputView(IOutputExecutionController execution)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        InitializeComponent();
    }

    public IReadOnlyReactiveProperty<bool> IsRunning => _execution?.IsRunning ?? s_notRunning;

    private async void SelectDestinationFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel viewModel
            && TopLevel.GetTopLevel(this) is TopLevel topLevel)
        {
            var options = new FilePickerSaveOptions()
            {
                FileTypeChoices = viewModel.GetFilePickerFileTypes()
            };
            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(options);

            if (file != null && file.TryGetLocalPath() is string localPath)
            {
                viewModel.DestinationFile.Value = localPath;
                file.Dispose();
            }
        }
    }

    private async void StartOutputClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputViewModel viewModel) return;

        try
        {
            IOutputExecutionController outputExecution = _execution
                ?? throw new InvalidOperationException("The output execution controller was not provided.");
            if (!outputExecution.TryStart(out Task? execution))
            {
                return;
            }

            if (!outputExecution.IsRunning.Value)
            {
                await AwaitExecutionAsync(execution);
                return;
            }

            var dialog = new OutputProgressDialog(outputExecution) { DataContext = viewModel };
            _ = dialog.ShowAsync();
            await AwaitExecutionAsync(execution);
            if (viewModel.WasCancelled.Value || execution.IsCanceled)
            {
                dialog.Hide();
            }
        }
        catch (Exception ex)
        {
            if (!OutputViewModel.WasFailureReported(ex))
            {
                await ex.Handle();
            }
        }
    }

    private static async Task AwaitExecutionAsync(Task execution)
    {
        try
        {
            await execution;
        }
        catch (OperationCanceledException) when (execution.IsCanceled)
        {
        }
    }

    private void CancelOutputClick(object? sender, RoutedEventArgs e)
    {
        _execution?.Cancel();
    }
}
