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

public sealed class PinnableOutputItem(
    string displayName,
    bool isPinned,
    object userData,
    string? description = null) : IEquatable<PinnableOutputItem?>
{
    public string DisplayName { get; } = displayName;

    public bool IsPinned { get; } = isPinned;

    public object UserData { get; } = userData;

    public string? Description { get; } = description;

    public bool Equals(PinnableOutputItem? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return DisplayName == other.DisplayName && IsPinned == other.IsPinned && Equals(UserData, other.UserData);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is PinnableOutputItem other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsPinned, UserData);
    }
}

public class OutputPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<ReactiveCollection<PinnableOutputItem>?> ProfileItemsProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, ReactiveCollection<PinnableOutputItem>?>(nameof(ProfileItems));

    public static readonly StyledProperty<ReactiveCollection<PinnableOutputItem>?> PresetItemsProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, ReactiveCollection<PinnableOutputItem>?>(nameof(PresetItems));

    public static readonly StyledProperty<PinnableOutputItem?> SelectedProfileProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, PinnableOutputItem?>(nameof(SelectedProfile));

    public static readonly StyledProperty<PinnableOutputItem?> SelectedPresetProperty =
        AvaloniaProperty.Register<OutputPickerFlyoutPresenter, PinnableOutputItem?>(nameof(SelectedPreset));

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

    public event Action<PinnableOutputItem>? Pinned;

    public event Action<PinnableOutputItem>? Unpinned;

    public event Action<PinnableOutputItem, Control>? MoreMenuRequested;

    public ReactiveCollection<PinnableOutputItem>? ProfileItems
    {
        get => GetValue(ProfileItemsProperty);
        set => SetValue(ProfileItemsProperty, value);
    }

    public ReactiveCollection<PinnableOutputItem>? PresetItems
    {
        get => GetValue(PresetItemsProperty);
        set => SetValue(PresetItemsProperty, value);
    }

    public PinnableOutputItem? SelectedProfile
    {
        get => GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    public PinnableOutputItem? SelectedPreset
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
        if (e is { Key: Key.F, KeyModifiers: KeyModifiers.Control or KeyModifiers.Meta })
        {
            ShowSearchBox = true;
            e.Handled = true;
            _searchTextBox?.Focus();
            return;
        }

        if (e is { Key: Key.Tab, KeyModifiers: KeyModifiers.None or KeyModifiers.Shift })
        {
            ShowPresets = !ShowPresets;
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

    internal void UpdatePinState(PinnableOutputItem item, bool isPinned)
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

    internal void RequestMoreMenu(PinnableOutputItem item, Control anchor)
    {
        MoreMenuRequested?.Invoke(item, anchor);
    }
}
