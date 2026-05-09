using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.ViewModels;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class CommandPaletteView : UserControl
{
    private const int PageStep = 8;

    private readonly CompositeDisposable _disposables = [];

    public CommandPaletteView()
    {
        InitializeComponent();

        // ListBox にフォーカスがある状態でも Escape / Enter を捕捉できるように、
        // ルートで Bubble をフックする。矢印キーは ListBox の標準動作に任せる。
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Bubble, handledEventsToo: false);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();

        if (DataContext is CommandPaletteViewModel viewModel)
        {
            viewModel.IsOpen
                .Subscribe(open =>
                {
                    if (open)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            QueryTextBox.Focus();
                            QueryTextBox.SelectAll();
                        }, DispatcherPriority.Background);
                    }
                })
                .AddTo(_disposables);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Dispose ではなく Clear を呼ぶことで、Avalonia により Detach→再 Attach された際の
        // OnDataContextChanged 内 _disposables.Clear() が破棄済みインスタンスに対して呼ばれる事態を避ける。
        _disposables.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel viewModel)
        {
            viewModel.Close();
            e.Handled = true;
        }
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveSelection(1);
                ScrollSelectionIntoView(viewModel);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.MoveSelection(-1);
                ScrollSelectionIntoView(viewModel);
                e.Handled = true;
                break;
            case Key.PageDown:
                viewModel.MoveSelection(PageStep);
                ScrollSelectionIntoView(viewModel);
                e.Handled = true;
                break;
            case Key.PageUp:
                viewModel.MoveSelection(-PageStep);
                ScrollSelectionIntoView(viewModel);
                e.Handled = true;
                break;
            case Key.Home:
                viewModel.SelectFirst();
                ScrollSelectionIntoView(viewModel);
                e.Handled = true;
                break;
            case Key.End:
                viewModel.SelectLast();
                ScrollSelectionIntoView(viewModel);
                e.Handled = true;
                break;
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not CommandPaletteViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.Close();
                e.Handled = true;
                break;
            case Key.Enter:
                viewModel.ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel viewModel)
        {
            return;
        }

        // 別アイテム上でダブルクリックされた場合に SelectedItem の更新が
        // 反映される前に実行してしまわないよう、起点アイテムから VM を取り出す。
        if (e.Source is Visual source
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: CommandPaletteItemViewModel item })
        {
            viewModel.SelectedCommand.Value = item;
            viewModel.ExecuteSelected();
            e.Handled = true;
        }
    }

    private void ScrollSelectionIntoView(CommandPaletteViewModel viewModel)
    {
        if (viewModel.SelectedCommand.Value is { } selected)
            ResultsListBox.ScrollIntoView(selected);
    }
}
