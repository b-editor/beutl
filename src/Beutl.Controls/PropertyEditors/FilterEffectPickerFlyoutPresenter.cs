#nullable enable

using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Beutl.Reactive;
using Beutl.Services;
using FluentAvalonia.UI.Media;
using Reactive.Bindings;

namespace Beutl.Controls.PropertyEditors;

public record PinnableLibraryItem(LibraryItem Item, bool IsPinned);

public class FilterEffectPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<PinnableLibraryItem?> SelectedItemProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, PinnableLibraryItem?>(nameof(SelectedItem));

    public static readonly StyledProperty<ReactiveCollection<PinnableLibraryItem>?> ItemsProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, ReactiveCollection<PinnableLibraryItem>?>(nameof(Items));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, bool>(nameof(IsBusy));

    public static readonly StyledProperty<bool> ShowAllProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, bool>(nameof(ShowAll));

    public static readonly StyledProperty<bool> ShowSearchBoxProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, bool>(nameof(ShowSearchBox));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, string?>(nameof(SearchText));

    private readonly CompositeDisposable _disposables = [];
    private ListBox? _listBox;
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_listBox != null)
        {
            _listBox.ContainerPrepared -= ListBoxOnContainerPrepared;
            _listBox.ContainerClearing -= ListBoxOnContainerClearing;
        }

        _disposables.Clear();
        base.OnApplyTemplate(e);
        _listBox = e.NameScope.Get<ListBox>("PART_ListBox");
        _listBox.ContainerPrepared += ListBoxOnContainerPrepared;
        _listBox.ContainerClearing += ListBoxOnContainerClearing;
    }

    private void ListBoxOnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
    }

    private void ListBoxOnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem listBoxItem)
        {
            var btn = listBoxItem.FindControl<ToggleButton>("ToggleButton");
        }
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
