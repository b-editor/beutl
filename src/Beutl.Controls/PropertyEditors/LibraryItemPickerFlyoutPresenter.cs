#nullable enable

using Avalonia;
using Avalonia.Controls;
using Reactive.Bindings;

namespace Beutl.Controls.PropertyEditors;

public class PinnableLibraryItem(string displayName, bool isPinned, object userData) : IEquatable<PinnableLibraryItem?>
{
    public string DisplayName { get; } = displayName;

    public bool IsPinned { get; } = isPinned;

    public object UserData { get; } = userData;

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
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, ReactiveCollection<PinnableLibraryItem>?>(nameof(Items));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(IsBusy));

    public static readonly StyledProperty<bool> ShowAllProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowAll));

    public static readonly StyledProperty<bool> ShowSearchBoxProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, bool>(nameof(ShowSearchBox));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<LibraryItemPickerFlyoutPresenter, string?>(nameof(SearchText));

    private const string SearchBoxPseudoClass = ":search-box";
    private const string IsBusyPseudoClass = ":busy";

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowSearchBoxProperty)
        {
            PseudoClasses.Set(SearchBoxPseudoClass, ShowSearchBox);
        }
        else if (change.Property == IsBusyProperty)
        {
            PseudoClasses.Set(IsBusyPseudoClass, IsBusy);
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
