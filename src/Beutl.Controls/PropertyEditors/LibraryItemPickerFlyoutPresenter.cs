using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Reactive;
using Reactive.Bindings;

namespace Beutl.Controls.PropertyEditors;

public class PinnableLibraryItem(string displayName, bool isPinned, object userData, string? description = null) : IEquatable<PinnableLibraryItem?>
{
    public string DisplayName { get; } = displayName;

    public bool IsPinned { get; } = isPinned;

    public object UserData { get; } = userData;

    public string? Description { get; } = description;

    // The moving picture a tile of the picker shows, or null for an item without one.
    public IAnimatedThumbnailSource? Thumbnail { get; init; }

    public bool Equals(PinnableLibraryItem? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return DisplayName == other.DisplayName && IsPinned == other.IsPinned && Equals(UserData, other.UserData);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is PinnableLibraryItem other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsPinned, UserData);
    }
}

public class LibraryItemPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<PinnableLibraryItem?> SelectedItemProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, PinnableLibraryItem?>(nameof(SelectedItem));

    public static readonly StyledProperty<ReactiveCollection<PinnableLibraryItem>?> ItemsProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, ReactiveCollection<PinnableLibraryItem>?>(
            nameof(Items));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(IsBusy));

    public static readonly StyledProperty<bool> ShowAllProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowAll));

    public static readonly StyledProperty<bool> ShowSearchBoxProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowSearchBox));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, string?>(nameof(SearchText));

    public static readonly StyledProperty<bool> ShowReferencesTabProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowReferencesTab));

    public static readonly StyledProperty<bool> ShowReferencesProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowReferences));

    public static readonly StyledProperty<ReactiveCollection<PinnableLibraryItem>?> ReferenceItemsProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, ReactiveCollection<PinnableLibraryItem>?>(nameof(ReferenceItems));

    public static readonly StyledProperty<string?> NoResultsTextProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, string?>(nameof(NoResultsText));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<bool> CanShowTilesProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(CanShowTiles));

    public static readonly StyledProperty<bool> ShowTilesProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowTiles));

    // How many tiles a row of the tile view holds, which the arrow keys move by.
    internal const int TileColumns = 2;

    private const string SearchBoxPseudoClass = ":search-box";
    private const string IsBusyPseudoClass = ":busy";
    private const string ShowReferencesPseudoClass = ":show-references";
    private const string ShowReferencesTabPseudoClass = ":show-references-tab";
    private const string NoResultsPseudoClass = ":no-results";
    private const string CanShowTilesPseudoClass = ":can-show-tiles";
    private const string TilesPseudoClass = ":tiles";

    private readonly CompositeDisposable _keyboardDisposables = [];
    private TextBox? _searchTextBox;
    private ListBox? _listBox;
    private ListBox? _tileListBox;
    private ListBox? _referenceListBox;

    public LibraryItemPickerFlyoutPresenter()
    {
        // The pin buttons belong to the item template, so the presenter handles their clicks.
        AddHandler(Button.ClickEvent, OnItemButtonClick, handledEventsToo: true);
    }

    public event Action<PinnableLibraryItem>? Pinned;

    public event Action<PinnableLibraryItem>? Unpinned;

    public PinnableLibraryItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ReactiveCollection<PinnableLibraryItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public bool ShowAll
    {
        get => GetValue(ShowAllProperty);
        set => SetValue(ShowAllProperty, value);
    }

    public bool ShowSearchBox
    {
        get => GetValue(ShowSearchBoxProperty);
        set => SetValue(ShowSearchBoxProperty, value);
    }

    public string? SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool ShowReferencesTab
    {
        get => GetValue(ShowReferencesTabProperty);
        set => SetValue(ShowReferencesTabProperty, value);
    }

    public bool ShowReferences
    {
        get => GetValue(ShowReferencesProperty);
        set => SetValue(ShowReferencesProperty, value);
    }

    public ReactiveCollection<PinnableLibraryItem>? ReferenceItems
    {
        get => GetValue(ReferenceItemsProperty);
        set => SetValue(ReferenceItemsProperty, value);
    }

    // 検索に一致する項目がないとき、リストの代わりに表示する文言 (null なら何も表示しない)
    public string? NoResultsText
    {
        get => GetValue(NoResultsTextProperty);
        set => SetValue(NoResultsTextProperty, value);
    }

    // The template of the listed items. A theme sets it here: the list sets its own from the control template,
    // which a style on the list cannot override.
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    // Whether the items have moving thumbnails, so the picker offers to show them as tiles and opens big enough
    // for tiles in either view: a picker keeps the size it opened with.
    public bool CanShowTiles
    {
        get => GetValue(CanShowTilesProperty);
        set => SetValue(CanShowTilesProperty, value);
    }

    // Whether the items show as tiles with their thumbnails rather than as a list, when they have thumbnails.
    public bool ShowTiles
    {
        get => GetValue(ShowTilesProperty);
        set => SetValue(ShowTilesProperty, value);
    }

    private bool IsShowingTiles => CanShowTiles && ShowTiles && !ShowReferences;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _keyboardDisposables.Clear();
        base.OnApplyTemplate(e);

        _searchTextBox = e.NameScope.Find<TextBox>("SearchTextBox");
        _listBox = e.NameScope.Find<ListBox>("PART_ListBox");
        _tileListBox = e.NameScope.Find<ListBox>("PART_TileListBox");
        _referenceListBox = e.NameScope.Find<ListBox>("PART_ReferenceListBox");

        this.AddDisposableHandler(KeyDownEvent, OnPresenterKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);

        this.AddDisposableHandler(TextInputEvent, OnPresenterTextInput)
            .DisposeWith(_keyboardDisposables);

        _searchTextBox?.AddDisposableHandler(KeyDownEvent, OnSearchBoxKeyDown)
            .DisposeWith(_keyboardDisposables);

        foreach (ListBox? listBox in new[] { _listBox, _tileListBox, _referenceListBox })
        {
            if (listBox is null) continue;

            listBox.Focusable = true;
            listBox.AddDisposableHandler(KeyDownEvent, OnListBoxKeyDown, RoutingStrategies.Tunnel)
                .DisposeWith(_keyboardDisposables);
            listBox.GetObservable(ItemsControl.ItemCountProperty)
                .Subscribe(_ => UpdateNoResults())
                .DisposeWith(_keyboardDisposables);
        }
    }

    private void OnPresenterKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+F (Windows/Linux) または Cmd+F (macOS): 検索ボックスを表示してフォーカス
        if (PickerListNavigation.IsSearchShortcut(e))
        {
            ShowSearchBox = true;
            e.Handled = true;
            _searchTextBox?.Focus();
            return;
        }

        // Tab / Shift+Tab: 型タブと参照タブを切り替え
        if (ShowReferencesTab && PickerListNavigation.IsTabSwitch(e))
        {
            ShowReferences = !ShowReferences;
            e.Handled = true;
            FocusListBox();
        }
    }

    private void OnListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        MoveSelection(listBox, e);

        FocusListBox();
    }

    // The tiles move by tile and by row, the lists by item.
    private void MoveSelection(ListBox listBox, KeyEventArgs e)
    {
        if (ReferenceEquals(listBox, _tileListBox))
            PickerListNavigation.MoveSelectionInGrid(listBox, e, TileColumns);
        else
            PickerListNavigation.MoveSelection(listBox, e);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                // 検索ボックスにフォーカスを残したまま、結果の選択を移動する（タイルは行ごと）
                if (GetCurrentListBox() is { } listBox)
                {
                    MoveSelection(listBox, e);
                }

                e.Handled = true;
                break;
            case Key.Escape:
                // 検索ボックスを閉じて ListBox にフォーカスを戻す
                ShowSearchBox = false;
                FocusListBox();
                e.Handled = true;
                break;
        }
    }

    // リストにフォーカスがあるときに入力された文字は、検索ボックスに送って検索を始める
    // (空白は入力済みの語の区切りとしてだけ送り、それ以外はリスト側のキー操作として扱う)
    private void OnPresenterTextInput(object? sender, TextInputEventArgs e)
    {
        if (_searchTextBox is null || string.IsNullOrEmpty(e.Text)) return;
        if (string.IsNullOrWhiteSpace(e.Text) && string.IsNullOrEmpty(SearchText)) return;

        ShowSearchBox = true;
        string text = SearchText + e.Text;
        SearchText = text;
        _searchTextBox.Focus();
        _searchTextBox.CaretIndex = text.Length;
        e.Handled = true;
    }

    public override void FocusInitialElement()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ShowSearchBox && _searchTextBox is not null)
            {
                _searchTextBox.Focus();
            }
            else
            {
                FocusListBox();
            }
        }, DispatcherPriority.Input);
    }

    private void FocusListBox()
    {
        PickerListNavigation.FocusList(GetCurrentListBox());
    }

    private ListBox? GetCurrentListBox()
    {
        if (ShowReferences)
            return _referenceListBox;

        return IsShowingTiles ? _tileListBox : _listBox;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowSearchBoxProperty)
        {
            PseudoClasses.Set(SearchBoxPseudoClass, ShowSearchBox);
            if (ShowSearchBox)
            {
                _searchTextBox?.Focus();
            }
        }
        else if (change.Property == IsBusyProperty)
        {
            PseudoClasses.Set(IsBusyPseudoClass, IsBusy);
        }
        else if (change.Property == ShowReferencesProperty)
        {
            PseudoClasses.Set(ShowReferencesPseudoClass, ShowReferences);
            PseudoClasses.Set(TilesPseudoClass, IsShowingTiles);
            SelectedItem = null;
            UpdateNoResults();
        }
        else if (change.Property == CanShowTilesProperty || change.Property == ShowTilesProperty)
        {
            PseudoClasses.Set(CanShowTilesPseudoClass, CanShowTiles);
            bool listFocused = GetCurrentListBox()?.IsKeyboardFocusWithin == true
                               || _listBox?.IsKeyboardFocusWithin == true
                               || _tileListBox?.IsKeyboardFocusWithin == true;
            PseudoClasses.Set(TilesPseudoClass, IsShowingTiles);
            UpdateNoResults();
            // The other view shows the same selection; the keys move it there now.
            if (listFocused)
                FocusListBox();

            if (GetCurrentListBox() is { SelectedItem: { } selected } shown)
                shown.ScrollIntoView(selected);
        }
        else if (change.Property == ShowReferencesTabProperty)
        {
            PseudoClasses.Set(ShowReferencesTabPseudoClass, ShowReferencesTab);
        }
        else if (change.Property == SearchTextProperty || change.Property == NoResultsTextProperty)
        {
            UpdateNoResults();
        }
    }

    private void UpdateNoResults()
    {
        PseudoClasses.Set(NoResultsPseudoClass,
            !string.IsNullOrEmpty(NoResultsText)
            && !string.IsNullOrWhiteSpace(SearchText)
            && GetCurrentListBox() is { ItemCount: 0 });
    }

    private void OnItemButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is ToggleButton { Name: "ToggleButton", DataContext: PinnableLibraryItem item } pinButton)
        {
            UpdatePinState(item, pinButton.IsChecked == true);
        }
    }

    private void UpdatePinState(PinnableLibraryItem item, bool isPinned)
    {
        if (isPinned)
        {
            Pinned?.Invoke(item);
        }
        else
        {
            Unpinned?.Invoke(item);
        }
    }
}
