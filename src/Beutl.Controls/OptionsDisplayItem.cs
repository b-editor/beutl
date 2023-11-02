using System.Windows.Input;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

// https://github.com/amwx/FluentAvalonia/blob/master/FluentAvaloniaSamples/Controls/OptionsDisplayItem.cs
public class OptionsDisplayItem : TemplatedControl
{
    public static readonly StyledProperty<object> HeaderProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, object>(nameof(Header));

    public static readonly StyledProperty<object> DescriptionProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, object>(nameof(Description));

    public static readonly StyledProperty<FAIconElement> IconProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, FAIconElement>(nameof(Icon));

    public static readonly StyledProperty<bool> NavigatesProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, bool>(nameof(Navigates));

    public static readonly StyledProperty<Control> ActionButtonProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, Control>(nameof(ActionButton));

    public static readonly StyledProperty<bool> ExpandsProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, bool>(nameof(Expands));

    public static readonly StyledProperty<bool> ClickableProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, bool>(nameof(Clickable), true);

    public static readonly StyledProperty<object> ContentProperty =
        ContentControl.ContentProperty.AddOwner<OptionsDisplayItem>();

    public static readonly StyledProperty<bool> IsExpandedProperty =
        Expander.IsExpandedProperty.AddOwner<OptionsDisplayItem>();

    public static readonly StyledProperty<ICommand> NavigationCommandProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, ICommand>(nameof(NavigationCommand));

    public static readonly StyledProperty<object> NavigationCommandParameterProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, object>(nameof(NavigationCommandParameter));

    public static readonly StyledProperty<IPageTransition> ContentTransitionProperty =
        AvaloniaProperty.Register<OptionsDisplayItem, IPageTransition>(nameof(ContentTransition));

    public OptionsDisplayItem()
    {
        PseudoClasses.Set(":clickable", Clickable);
    }

    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public FAIconElement Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool Navigates
    {
        get => GetValue(NavigatesProperty);
        set => SetValue(NavigatesProperty, value);
    }

    public bool Clickable
    {
        get => GetValue(ClickableProperty);
        set => SetValue(ClickableProperty, value);
    }

    public Control ActionButton
    {
        get => GetValue(ActionButtonProperty);
        set => SetValue(ActionButtonProperty, value);
    }

    public bool Expands
    {
        get => GetValue(ExpandsProperty);
        set => SetValue(ExpandsProperty, value);
    }

    public object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public ICommand NavigationCommand
    {
        get => GetValue(NavigationCommandProperty);
        set => SetValue(NavigationCommandProperty, value);
    }

    public object NavigationCommandParameter
    {
        get => GetValue(NavigationCommandParameterProperty);
        set => SetValue(NavigationCommandParameterProperty, value);
    }

    public IPageTransition ContentTransition
    {
        get => GetValue(ContentTransitionProperty);
        set => SetValue(ContentTransitionProperty, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> NavigationRequestedEvent =
        RoutedEvent.Register<OptionsDisplayItem, RoutedEventArgs>(nameof(NavigationRequested), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> NavigationRequested
    {
        add => AddHandler(NavigationRequestedEvent, value);
        remove => RemoveHandler(NavigationRequestedEvent, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change is AvaloniaPropertyChangedEventArgs<bool> boolChanges)
        {
            if (change.Property == NavigatesProperty)
            {
                if (Expands)
                    throw new InvalidOperationException("Control cannot both Navigate and Expand");

                PseudoClasses.Set(":navigates", boolChanges.NewValue.GetValueOrDefault<bool>());
            }
            else if (change.Property == ExpandsProperty)
            {
                if (Navigates)
                    throw new InvalidOperationException("Control cannot both Navigate and Expand");

                PseudoClasses.Set(":expands", boolChanges.NewValue.GetValueOrDefault<bool>());
            }
            else if (change.Property == IsExpandedProperty)
            {
                PseudoClasses.Set(":expanded", boolChanges.NewValue.GetValueOrDefault<bool>());

                OnIsExpandedChanged(change);
            }
            else if (change.Property == ClickableProperty)
            {
                PseudoClasses.Set(":clickable", boolChanges.NewValue.GetValueOrDefault<bool>());
            }
        }
        else if (change.Property == IconProperty)
        {
            PseudoClasses.Set(":icon", change.NewValue != null);
        }
        else if (change.Property == ContentProperty)
        {
            if (change.OldValue is ILogical oldChild)
            {
                LogicalChildren.Remove(oldChild);
            }

            if (change.NewValue is ILogical newChild)
            {
                LogicalChildren.Add(newChild);
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _layoutRoot = e.NameScope.Find<Border>("LayoutRoot");
        _layoutRoot.PointerPressed += OnLayoutRootPointerPressed;
        _layoutRoot.PointerReleased += OnLayoutRootPointerReleased;
        _layoutRoot.PointerCaptureLost += OnLayoutRootPointerCaptureLost;
    }

    protected virtual async void OnIsExpandedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (Content != null && ContentTransition != null && Content is Visual visualContent)
        {
            _lastTransitionCts?.Cancel();
            _lastTransitionCts = new CancellationTokenSource();

            if (IsExpanded)
            {
                await ContentTransition.Start(null, visualContent, false, _lastTransitionCts.Token);
            }
            else
            {
                await ContentTransition.Start(visualContent, null, false, _lastTransitionCts.Token);
            }
        }
    }

    private void OnLayoutRootPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (Clickable
            && e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _isPressed = true;
            PseudoClasses.Set(":pressed", true);
        }
    }

    private void OnLayoutRootPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        PointerPoint pt = e.GetCurrentPoint(this);
        if (_isPressed && pt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _isPressed = false;

            PseudoClasses.Set(":pressed", false);

            if (Expands)
                IsExpanded = !IsExpanded;

            if (Navigates)
            {
                RaiseEvent(new RoutedEventArgs(NavigationRequestedEvent, this));
                if (NavigationCommand?.CanExecute(NavigationCommandParameter) == true)
                    NavigationCommand.Execute(NavigationCommandParameter);
            }
        }
    }

    private void OnLayoutRootPointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
    {
        _isPressed = false;
        PseudoClasses.Set(":pressed", false);
    }

    private bool _isPressed;
    private Border _layoutRoot;
    private CancellationTokenSource _lastTransitionCts;
}
