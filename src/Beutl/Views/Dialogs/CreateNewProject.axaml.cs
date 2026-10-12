using Avalonia.Interactivity;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class CreateNewProject : FAContentDialog
{
    private IDisposable? _sBtnBinding;

    public CreateNewProject()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    // 戻る
    protected override void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (carousel.SelectedIndex == 1)
        {
            args.Cancel = true;
            IsPrimaryButtonEnabled = false;
            _sBtnBinding?.Dispose();
            SecondaryButtonText = Strings.Next;
            IsSecondaryButtonEnabled = true;
            carousel.Previous();
        }
    }

    // '次へ' or '新規作成'
    protected override void OnSecondaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnSecondaryButtonClick(args);
        if (DataContext is not CreateNewProjectViewModel vm) return;

        if (carousel.SelectedIndex == 1)
        {
            vm.Create.Execute();
        }
        else
        {
            args.Cancel = true;

            IsPrimaryButtonEnabled = true;
            _sBtnBinding = this.Bind(IsSecondaryButtonEnabledProperty, vm.CanCreate);
            SecondaryButtonText = Strings.CreateNew;
            carousel.Next();
        }
    }

    private async void PickLocation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateNewProjectViewModel vm
            && await LocationPicker.PickFolderAsync(this) is { } localPath)
        {
            vm.Location.Value = localPath;
        }
    }
}
