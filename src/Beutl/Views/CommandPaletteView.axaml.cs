using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Logging;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class CommandPaletteView : UserControl
{
    private const int PageStep = 8;

    private readonly ILogger<CommandPaletteView> _logger = Log.CreateLogger<CommandPaletteView>();
    private readonly CompositeDisposable _disposables = [];
    private WeakReference<IInputElement>? _previouslyFocused;
    private bool _closedByCommandExecution;

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
            // Skip(1) で初期値の false を読み飛ばし、購読セット時にフォーカス操作を行わないようにする。
            viewModel.IsOpen
                .Skip(1)
                .Subscribe(HandleIsOpenChanged)
                .AddTo(_disposables);
        }
    }

    private void HandleIsOpenChanged(bool open)
    {
        if (open)
        {
            // パレットを閉じた時に呼び出し元へフォーカスを戻せるよう、開く直前のフォーカス位置を控えておく。
            IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            _previouslyFocused = focused is not null ? new WeakReference<IInputElement>(focused) : null;

            Dispatcher.UIThread.Post(() =>
            {
                QueryTextBox.Focus();
                QueryTextBox.SelectAll();
            }, DispatcherPriority.Background);
        }
        else
        {
            // パレットが閉じた直後に IsVisible=false へ遷移するため、TextBox がフォーカスを保持したまま
            // 不可視化されると Avalonia がフォーカスをクリアし、以後 MainView の KeyDown ハンドラに
            // ショートカット (Ctrl+Shift+P) が到達しなくなって再表示できなくなる。
            // 開いたときに保存しておいた呼び出し元へフォーカスを戻す。失効していた場合は MainView へ。
            WeakReference<IInputElement>? captured = _previouslyFocused;
            _previouslyFocused = null;
            bool closedByCommand = _closedByCommandExecution;
            _closedByCommandExecution = false;

            Dispatcher.UIThread.Post(() =>
            {
                // 閉じた時点で既にパレット外の要素へフォーカスが移っている場合は、Esc/Backdrop/コマンド実行
                // いずれの経路であっても奪い返さない (典型例: コマンドが同期的にダイアログを開いて
                // フォーカスを取った後)。closedByCommand 依存ではない点に注意。
                IInputElement? current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (current is Visual currentVisual
                    && currentVisual.FindAncestorOfType<CommandPaletteView>(includeSelf: true) is null)
                {
                    return;
                }

                // コマンド実行経路で current が null の場合、async でダイアログを開くコマンドが
                // フォーカスを取りに行く前にこのコールバックが走ったレースの可能性が高いため、
                // 復帰をスキップしてダイアログ側のフォーカスに任せる。
                // Esc/Backdrop 経路ではこの分岐に入らないため、従来通り復帰して
                // フォーカスが null のままになるのを防ぎ、Ctrl+Shift+P の再到達性を確保する。
                if (closedByCommand && current is null)
                {
                    return;
                }

                if (captured is not null
                    && captured.TryGetTarget(out IInputElement? target)
                    && target is Visual { IsEffectivelyVisible: true } visual
                    && visual.GetVisualRoot() is not null
                    && target.Focus())
                {
                    return;
                }

                MainView? mainView = this.FindAncestorOfType<MainView>();
                if (mainView is null || !mainView.Focus())
                {
                    // ここまで来てフォーカス復帰に失敗した場合、focused element が null のまま残り
                    // MainView の KeyDown ハンドラに Ctrl+Shift+P が届かなくなって再表示できなくなる。
                    // このバグの再発時に検知できるよう警告として残す (通常は発火しない想定)。
                    _logger.LogWarning(
                        "Failed to restore focus after command palette closed (mainViewFound={Found}). Ctrl+Shift+P may stop responding.",
                        mainView is not null);
                }
            }, DispatcherPriority.Background);
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
        // IME 変換中のキー入力（候補ナビゲーション等）はパレット操作に奪わずに IME に委ねる。
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

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
        // IME 変換中の Enter/Escape は IME による確定・取消に使われるため横取りしない。
        if (e.Handled || e.Key == Key.ImeProcessed || DataContext is not CommandPaletteViewModel viewModel)
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
                ExecuteSelectedFromUserAction(viewModel);
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
            ExecuteSelectedFromUserAction(viewModel);
            e.Handled = true;
        }
    }

    private void ScrollSelectionIntoView(CommandPaletteViewModel viewModel)
    {
        if (viewModel.SelectedCommand.Value is { } selected)
            ResultsListBox.ScrollIntoView(selected);
    }

    // ExecuteSelected は item が { IsEnabled: true } のときだけ Close() を同期的に呼ぶ。
    // 通常経路では Close → IsOpen 変化 → HandleIsOpenChanged(false) が同期的に走り、その中で
    // フラグをローカルにスナップショットしてからクリアするので追加処理は不要。
    // しかし item が null / IsEnabled=false で ExecuteSelected が Close を呼ばない場合は
    // HandleIsOpenChanged が走らずフラグが立ったまま残り、次回の Esc/Backdrop による Close 時に
    // 誤って「コマンド経由のクローズ」と判定されてしまう。finally で確実にリークを防ぐ。
    private void ExecuteSelectedFromUserAction(CommandPaletteViewModel viewModel)
    {
        _closedByCommandExecution = true;
        try
        {
            viewModel.ExecuteSelected();
        }
        finally
        {
            _closedByCommandExecution = false;
        }
    }
}
