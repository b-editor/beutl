using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class UpdateDialog : FAContentDialog
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    // The handoff can still fail, and the dialog is where the user reads why, so it stays open.
    // A successful handoff shuts Beutl down.
    protected override void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (args.Cancel || DataContext is not UpdateDialogViewModel vm) return;

        args.Cancel = true;
        _ = vm.HandlePrimaryButtonClick();
    }

    // Return to the editor, scheduling the ready update on quit where the installer supports it.
    protected override void OnSecondaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnSecondaryButtonClick(args);
        if (!args.Cancel && DataContext is UpdateDialogViewModel vm)
            AppUpdateController.ScheduleInstallOnQuit(vm, Beutl.Controls.ControlNotifications.GetOrDefault(this));
    }

    // Every other way out of the dialog, including Escape, ends the update.
    protected override void OnClosing(FAContentDialogClosingEventArgs args)
    {
        base.OnClosing(args);
        if (!args.Cancel && args.Result != FAContentDialogResult.Secondary
            && DataContext is UpdateDialogViewModel vm)
        {
            vm.Cancel();
        }
    }
}
