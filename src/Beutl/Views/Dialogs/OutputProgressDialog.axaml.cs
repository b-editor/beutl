using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class OutputProgressDialog : FAContentDialog
{
    private readonly IOutputExecutionController? _execution;
    private IDisposable? _runningSubscription;

    public OutputProgressDialog()
    {
        InitializeComponent();
        CloseButtonText = Strings.Close;
    }

    public OutputProgressDialog(IOutputExecutionController execution)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        InitializeComponent();
        _runningSubscription = execution.IsRunning.Subscribe(
            isRunning => CloseButtonText = isRunning ? Strings.Cancel : Strings.Close);
        Closed += (_, _) =>
        {
            _runningSubscription?.Dispose();
            _runningSubscription = null;
        };
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void OnCloseButtonClick(FAContentDialog sender, FAContentDialogButtonClickEventArgs args)
    {
        if (_execution?.IsRunning.Value == true)
        {
            _execution.Cancel();
        }
    }
}
