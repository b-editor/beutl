#nullable enable

using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    private const string SearchBoxPseudoClass = ":search-box";
    private const string IsBusyPseudoClass = ":busy";
    private const string ShowReferencesPseudoClass = ":show-references";
    private const string ShowReferencesTabPseudoClass = ":show-references-tab";

    private readonly CompositeDisposable _keyboardDisposables = [];
    private TextBox? _searchTextBox;
    private ListBox? _listBox;
    private ListBox? _referenceListBox;

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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _keyboardDisposables.Clear();
        base.OnApplyTemplate(e);

        _searchTextBox = e.NameScope.Find<TextBox>("SearchTextBox");
        _listBox = e.NameScope.Find<ListBox>("PART_ListBox");
        _referenceListBox = e.NameScope.Find<ListBox>("PART_ReferenceListBox");
        _listBox?.Focusable = true;
        _referenceListBox?.Focusable = true;

        this.AddDisposableHandler(KeyDownEvent, OnPresenterKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);

        _searchTextBox?.AddDisposableHandler(KeyDownEvent, OnSearchBoxKeyDown)
            .DisposeWith(_keyboardDisposables);

        _listBox?.AddDisposableHandler(KeyDownEvent, OnListBoxKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);

        _referenceListBox?.AddDisposableHandler(KeyDownEvent, OnListBoxKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);
    }

    private void OnPresenterKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+F (Windows/Linux) または Cmd+F (macOS): 検索ボックスを表示してフォーカス
        if (e is { Key: Key.F, KeyModifiers: KeyModifiers.Control or KeyModifiers.Meta })
        {
            ShowSearchBox = true;
            e.Handled = true;
            _searchTextBox?.Focus();
            return;
        }

        // Tab / Shift+Tab: 型タブと参照タブを切り替え
        if (ShowReferencesTab && e is { Key: Key.Tab, KeyModifiers: KeyModifiers.None or KeyModifiers.Shift })
        {
            ShowReferences = !ShowReferences;
            e.Handled = true;
            FocusListBox();
        }
    }

    private void OnListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        switch (e.Key)
        {
            case Key.Up:
                listBox.SelectedIndex = listBox.SelectedIndex <= 0
                    ? 0
                    : listBox.SelectedIndex - 1;
                ScrollSelectedIntoView(listBox);
                e.Handled = true;
                break;
            case Key.Down:
                listBox.SelectedIndex = listBox.SelectedIndex >= listBox.ItemCount - 1
                    ? listBox.ItemCount - 1
                    : listBox.SelectedIndex + 1;
                ScrollSelectedIntoView(listBox);
                e.Handled = true;
                break;
        }

        FocusListBox();
    }

    private static void ScrollSelectedIntoView(ListBox listBox)
    {
        if (listBox.SelectedItem is { } item)
        {
            listBox.ScrollIntoView(item);
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                FocusListBox();
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

    public void FocusInitialElement()
    {
        Dispatcher.UIThread.Post(FocusListBox, DispatcherPriority.Input);
    }

    private void FocusListBox()
    {
        var target = GetCurrentListBox();
        if (target is null) return;
        if (target.SelectedIndex < 0 && target.ItemCount > 0)
            target.SelectedIndex = 0;

        target.Focus();
    }

    private ListBox? GetCurrentListBox() => ShowReferences ? _referenceListBox : _listBox;

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
            SelectedItem = null;
        }
        else if (change.Property == ShowReferencesTabProperty)
        {
            PseudoClasses.Set(ShowReferencesTabPseudoClass, ShowReferencesTab);
        }
    }

    internal void UpdatePinState(PinnableLibraryItem item, bool isPinned)
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
