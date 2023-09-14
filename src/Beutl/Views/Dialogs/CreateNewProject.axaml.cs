using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class CreateNewProject : ContentDialog
{
    private IDisposable? _sBtnBinding;

    public CreateNewProject()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    // 戻る
    protected override void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (carousel.SelectedIndex == 1)
        {
            args.Cancel = true;
            // '戻る'を無効化
            IsPrimaryButtonEnabled = false;
            // IsSecondaryButtonEnabledのバインド解除
            _sBtnBinding?.Dispose();
            // '新規作成'を'次へ'に変更
            SecondaryButtonText = Strings.Next;
            // '次へ'を有効化
            IsSecondaryButtonEnabled = true;
            carousel.Previous();
        }
    }

    // '次へ' or '新規作成'
    protected override void OnSecondaryButtonClick(ContentDialogButtonClickEventArgs args)
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

            // '戻る'を表示
            IsPrimaryButtonEnabled = true;
            // IsSecondaryButtonEnabledとCanCreateをバインド
            _sBtnBinding = this.Bind(IsSecondaryButtonEnabledProperty, vm.CanCreate);
            // '次へ'を'新規作成に変更'
            SecondaryButtonText = Strings.CreateNew;
            carousel.Next();
        }
    }

    // 場所を選択
    private async void PickLocation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateNewProjectViewModel vm && VisualRoot is Window parent)
        {
            var options = new FolderPickerOpenOptions();
            IReadOnlyList<IStorageFolder> result = await parent.StorageProvider.OpenFolderPickerAsync(options);

            if (result.Count > 0
                && result[0].TryGetLocalPath() is string localPath)
            {
                vm.Location.Value = localPath;
            }
        }
    }
}
