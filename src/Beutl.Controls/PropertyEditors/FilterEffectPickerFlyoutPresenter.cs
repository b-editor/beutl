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
using Beutl.Reactive;
using Beutl.Services;
using FluentAvalonia.UI.Media;
using Reactive.Bindings;

namespace Beutl.Controls.PropertyEditors;

public class FilterEffectPickerFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<LibraryItem?> SelectedItemProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, LibraryItem?>(nameof(SelectedItem));

    public static readonly StyledProperty<ReactiveCollection<LibraryItem>?> ItemsProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, ReactiveCollection<LibraryItem>?>(nameof(Items));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, bool>(nameof(IsBusy));

    public static readonly StyledProperty<bool> ShowAllProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, bool>(nameof(ShowAll));

    public static readonly StyledProperty<bool> ShowSearchBoxProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, bool>(nameof(ShowSearchBox));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<FilterEffectPickerFlyoutPresenter, string?>(nameof(SearchText));

    private readonly CompositeDisposable _disposables = [];
    private const string SearchBoxPseudoClass = ":search-box";
    private const string IsBusyPseudoClass = ":busy";

    public LibraryItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ReactiveCollection<LibraryItem>? Items
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
        _disposables.Clear();
        base.OnApplyTemplate(e);
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
}
