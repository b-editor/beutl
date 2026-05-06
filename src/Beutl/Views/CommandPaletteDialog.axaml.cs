using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views;

public sealed partial class CommandPaletteDialog : ContentDialog
{
    public CommandPaletteDialog()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public CommandPaletteEntry? PendingExecution { get; private set; }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                vm.MoveSelection(5);
                e.Handled = true;
                break;
            case Key.PageUp:
                vm.MoveSelection(-5);
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected(vm);
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void ExecuteSelected(CommandPaletteViewModel vm)
    {
        // 選択中エントリを保留して dialog を閉じる。
        // 実行は呼び出し側 (await ShowAsync 後) で行う。
        // こうすることで vm の Dispose タイミングと衝突しない。
        PendingExecution = vm.GetSelected();
        Hide();
    }
}
