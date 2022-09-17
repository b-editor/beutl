using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Api.Objects;

using BeUtl.Controls;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;

public sealed partial class AddResourceDialog : ContentDialog, IStyleable
{
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;
    private IDisposable? _disposable3;
    private IDisposable? _disposable4;

    public AddResourceDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    protected override async void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is AddResourceDialogViewModel viewModel)
        {
            args.Cancel = true;
            IsEnabled = false;
            PackageResource? result = await viewModel.AddAsync();
            if (result != null)
            {
                Hide(ContentDialogResult.Primary);
            }
            else
            {
                IsEnabled = true;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _disposable1 = OptionsItem1.GetObservable(OptionsDisplayItem.IsExpandedProperty)
            .Subscribe(_ => UpdateExpanded(OptionsItem1));
        _disposable2 = OptionsItem2.GetObservable(OptionsDisplayItem.IsExpandedProperty)
            .Subscribe(_ => UpdateExpanded(OptionsItem2));
        _disposable3 = OptionsItem3.GetObservable(OptionsDisplayItem.IsExpandedProperty)
            .Subscribe(_ => UpdateExpanded(OptionsItem3));
        _disposable4 = OptionsItem4.GetObservable(OptionsDisplayItem.IsExpandedProperty)
            .Subscribe(_ => UpdateExpanded(OptionsItem4));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _disposable1?.Dispose();
        _disposable2?.Dispose();
        _disposable3?.Dispose();
        _disposable4?.Dispose();

        _disposable1
            = _disposable2
            = _disposable3
            = _disposable4
            = null;
    }

    private void UpdateExpanded(OptionsDisplayItem obj)
    {
        if (!obj.IsExpanded)
        {
            return;
        }

        foreach (OptionsDisplayItem item in new OptionsDisplayItem[] { OptionsItem1, OptionsItem2, OptionsItem3, OptionsItem4 })
        {
            if (obj != item)
            {
                item.IsExpanded = false;
            }
        }

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(100);
            if (obj.Content is TextBox textBox)
            {
                FocusManager.Instance?.Focus(null);
                textBox.Focus();
            }
        });
    }
}
