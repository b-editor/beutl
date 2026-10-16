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

internal class OutputPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<ReactiveCollection<PinnableLibraryItem>?> ProfileItemsProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, ReactiveCollection<PinnableLibraryItem>?>(nameof(ProfileItems));

    public static readonly StyledProperty<ReactiveCollection<PinnableLibraryItem>?> PresetItemsProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, ReactiveCollection<PinnableLibraryItem>?>(nameof(PresetItems));

    public static readonly StyledProperty<PinnableLibraryItem?> SelectedProfileProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, PinnableLibraryItem?>(nameof(SelectedProfile));

    public static readonly StyledProperty<PinnableLibraryItem?> SelectedPresetProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, PinnableLibraryItem?>(nameof(SelectedPreset));

    public static readonly StyledProperty<bool> ShowPresetsProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, bool>(nameof(ShowPresets));

    public static readonly StyledProperty<bool> ShowSearchBoxProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, bool>(nameof(ShowSearchBox));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, string?>(nameof(SearchText));

    private const string SearchBoxPseudoClass = ":search-box";
    private const string ShowPresetsPseudoClass = ":show-presets";

    private readonly CompositeDisposable _keyboardDisposables = [];
    private TextBox? _searchTextBox;
    private ListBox? _profileListBox;
    private ListBox? _presetListBox;
    private ToggleButton? _profilesTabButton;
    private ToggleButton? _presetsTabButton;

    public OutputPickerFlyoutPresenter()
    {
        // The pin and more buttons belong to the item template, so the presenter handles their clicks.
        AddHandler(Button.ClickEvent, OnItemButtonClick, handledEventsToo: true);
    }

    public event Action<PinnableLibraryItem>? Pinned;

    public event Action<PinnableLibraryItem>? Unpinned;

    public event Action<PinnableLibraryItem, Control>? MoreMenuRequested;

    public ReactiveCollection<PinnableLibraryItem>? ProfileItems
    {
        get => GetValue(ProfileItemsProperty);
        set => SetValue(ProfileItemsProperty, value);
    }

    public ReactiveCollection<PinnableLibraryItem>? PresetItems
    {
        get => GetValue(PresetItemsProperty);
        set => SetValue(PresetItemsProperty, value);
    }

    public PinnableLibraryItem? SelectedProfile
    {
        get => GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    public PinnableLibraryItem? SelectedPreset
    {
        get => GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    public bool ShowPresets
    {
        get => GetValue(ShowPresetsProperty);
        set => SetValue(ShowPresetsProperty, value);
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _keyboardDisposables.Clear();
        base.OnApplyTemplate(e);

        _searchTextBox = e.NameScope.Find<TextBox>("SearchTextBox");
        _profileListBox = e.NameScope.Find<ListBox>("PART_ProfileListBox");
        _presetListBox = e.NameScope.Find<ListBox>("PART_PresetListBox");
        _profilesTabButton = e.NameScope.Find<ToggleButton>("ProfilesTabButton");
        _presetsTabButton = e.NameScope.Find<ToggleButton>("PresetsTabButton");

        if (_profileListBox != null) _profileListBox.Focusable = true;
        if (_presetListBox != null) _presetListBox.Focusable = true;

        _profilesTabButton?.AddDisposableHandler(Button.ClickEvent, OnProfilesTabClick)
            .DisposeWith(_keyboardDisposables);
        _presetsTabButton?.AddDisposableHandler(Button.ClickEvent, OnPresetsTabClick)
            .DisposeWith(_keyboardDisposables);

        this.AddDisposableHandler(KeyDownEvent, OnPresenterKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);

        _searchTextBox?.AddDisposableHandler(KeyDownEvent, OnSearchBoxKeyDown)
            .DisposeWith(_keyboardDisposables);

        _profileListBox?.AddDisposableHandler(KeyDownEvent, OnListBoxKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);

        _presetListBox?.AddDisposableHandler(KeyDownEvent, OnListBoxKeyDown, RoutingStrategies.Tunnel)
            .DisposeWith(_keyboardDisposables);
    }

    private void OnPresenterKeyDown(object? sender, KeyEventArgs e)
    {
        if (PickerListNavigation.IsSearchShortcut(e))
        {
            ShowSearchBox = true;
            e.Handled = true;
            _searchTextBox?.Focus();
            return;
        }

        if (PickerListNavigation.IsTabSwitch(e))
        {
            ShowPresets = !ShowPresets;
            e.Handled = true;
            FocusListBox();
        }
    }

    private void OnListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        PickerListNavigation.MoveSelection(listBox, e);

        FocusListBox();
    }

    private void OnProfilesTabClick(object? sender, RoutedEventArgs e)
    {
        ShowPresets = false;
    }

    private void OnPresetsTabClick(object? sender, RoutedEventArgs e)
    {
        ShowPresets = true;
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
                ShowSearchBox = false;
                FocusListBox();
                e.Handled = true;
                break;
        }
    }

    public override void FocusInitialElement()
    {
        Dispatcher.UIThread.Post(FocusListBox, DispatcherPriority.Input);
    }

    private void FocusListBox()
    {
        PickerListNavigation.FocusList(GetCurrentListBox());
    }

    private ListBox? GetCurrentListBox() => ShowPresets ? _presetListBox : _profileListBox;

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
        else if (change.Property == ShowPresetsProperty)
        {
            PseudoClasses.Set(ShowPresetsPseudoClass, ShowPresets);
        }
    }

    private void OnItemButtonClick(object? sender, RoutedEventArgs e)
    {
        switch (e.Source)
        {
            case ToggleButton { Name: "PinButton", DataContext: PinnableLibraryItem item } pinButton:
                UpdatePinState(item, pinButton.IsChecked == true);
                break;
            case Button { Name: "MoreItemButton", DataContext: PinnableLibraryItem item } moreButton:
                MoreMenuRequested?.Invoke(item, moreButton);
                break;
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
