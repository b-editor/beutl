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
    private const double MaxPaletteWidth = 640;

    private readonly ILogger<CommandPaletteView> _logger = Log.CreateLogger<CommandPaletteView>();
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _visualTreeDisposables = [];
    private WeakReference<IInputElement>? _previouslyFocused;

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

            Dispatcher.UIThread.Post(() =>
            {
                // 閉じた時点で既にパレット外の要素へフォーカスが移っている場合は奪い返さない
                // (典型例: コマンドが同期的にダイアログを開いてフォーカスを取った後)。
                // それ以外の経路 (Esc / Backdrop / フォーカスを動かさない単純コマンド) は
                // 必ず restore しないと focused element が null のまま残り、ContextCommandManager
                // が登録した KeyDown ハンドラに Ctrl+Shift+P が届かなくなって再表示できなくなる。
                IInputElement? current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (current is Visual currentVisual
                    && currentVisual.FindAncestorOfType<CommandPaletteView>(includeSelf: true) is null)
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _visualTreeDisposables.Clear();

        // narrow window で PaletteContainer 自体が右マージンを侵食して clipping するのを防ぐため、
        // TopLevel サイズに応じて Width を動的に縮める (常に MaxPaletteWidth、ではなく上限)。
        // MaxWidth + HorizontalAlignment="Center" にしてしまうとリストの中身に応じて幅が変動
        // (スクロールバー表示時等) するので、明示的な Width 設定で中身依存を切る方針。
        // Width が明示されていれば中身に依らないため、XAML 側の HorizontalAlignment="Center" は維持して問題ない。
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            // GetObservable は購読時に現在値を即座に発火するため、初期 Width 設定もこの購読に任せる。
            topLevel.GetObservable(BoundsProperty)
                .Subscribe(bounds => UpdatePaletteWidth(bounds.Width))
                .AddTo(_visualTreeDisposables);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Dispose ではなく Clear を呼ぶことで、Avalonia により Detach→再 Attach された際の
        // OnDataContextChanged 内 _disposables.Clear() が破棄済みインスタンスに対して呼ばれる事態を避ける。
        _visualTreeDisposables.Clear();
        _disposables.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdatePaletteWidth(double availableWidth)
    {
        Thickness margin = PaletteContainer.Margin;
        double usable = availableWidth - margin.Left - margin.Right;
        PaletteContainer.Width = Math.Min(MaxPaletteWidth, Math.Max(0, usable));
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
