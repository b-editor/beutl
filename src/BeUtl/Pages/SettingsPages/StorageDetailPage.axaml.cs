using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.SettingsPages;

public partial class StorageDetailPage : UserControl
{
    public StorageDetailPage()
    {
        InitializeComponent();
    }

    private async void UploadClick(object? sender, RoutedEventArgs e)
    {
        // Todo:
        if (DataContext is StorageDetailPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                Title = "ファイルをアップロード",
                Content = @"ファイルをアップロードします。
プロフィール画像や公開するパッケージに表示するロゴ画像、スクリーンショットで使うファイルの場合は'内部'を選択、
パッケージのリリースのアセットなど大容量のファイルは外部のファイルホスティングサービスでファイルを公開し、
ダウンロードURLを取得した後に'外部'を選択して続行してください。

重要事項:
  - 外部のファイルホスティングサービスを使う場合はプライバシーポリシーの記載が必要です。
  - この機能はアップロードされたファイルが公開されることを前提としています。",
                PrimaryButtonText = "外部",
                SecondaryButtonText = "内部",
                CloseButtonText = "キャンセル"
            };

            // Todo: 2022/09/25 12:45の続き
            await dialog.ShowAsync();
        }
    }

    private void NavigateStorageSettingsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(StorageSettingsPage), null, SharedNavigationTransitionInfo.Instance);
        }
    }
}
