using System.Windows.Input;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

// https://github.com/amwx/FluentAvalonia/blob/master/FluentAvaloniaSamples/Controls/OptionsDisplayItem.cs
public partial class OptionsDisplayItem : TemplatedControl
{
    public OptionsDisplayItem()
    {
        UpdateClickability();
    }

    [GeneratedStyledProperty]
    public partial object? Header { get; set; }

    [GeneratedStyledProperty]
    public partial object? Description { get; set; }

    [GeneratedStyledProperty]
    public partial FAIconElement? Icon { get; set; }

    [GeneratedStyledProperty]
    public partial bool Navigates { get; set; }

    [GeneratedStyledProperty(DefaultValue = true)]
    public partial bool Clickable { get; set; }

    [GeneratedStyledProperty]
    public partial Control? ActionButton { get; set; }

    [GeneratedStyledProperty]
    public partial bool Expands { get; set; }

    [GeneratedStyledProperty(AddOwnerFrom = typeof(ContentControl))]
    public partial object? Content { get; set; }

    [GeneratedStyledProperty(AddOwnerFrom = typeof(Expander))]
    public partial bool IsExpanded { get; set; }

    [GeneratedStyledProperty]
    public partial ICommand? NavigationCommand { get; set; }

    [GeneratedStyledProperty]
    public partial object? NavigationCommandParameter { get; set; }

    [GeneratedStyledProperty]
    public partial IPageTransition? ContentTransition { get; set; }

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
            if (change.Property == OptionsGroupPanel.IsGroupedProperty)
            {
                PseudoClasses.Set(":grouped", boolChanges.NewValue.GetValueOrDefault<bool>());
            }
            else if (change.Property == NavigatesProperty)
            {
                if (Expands)
                    throw new InvalidOperationException("Control cannot both Navigate and Expand");

                PseudoClasses.Set(":navigates", boolChanges.NewValue.GetValueOrDefault<bool>());
                UpdateClickability();
            }
            else if (change.Property == ExpandsProperty)
            {
                if (Navigates)
                    throw new InvalidOperationException("Control cannot both Navigate and Expand");

                PseudoClasses.Set(":expands", boolChanges.NewValue.GetValueOrDefault<bool>());
                UpdateClickability();
            }
            else if (change.Property == IsExpandedProperty)
            {
                PseudoClasses.Set(":expanded", boolChanges.NewValue.GetValueOrDefault<bool>());

                OnIsExpandedChanged(change);
            }
            else if (change.Property == ClickableProperty)
            {
                UpdateClickability();
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
        if (_layoutRoot != null)
        {
            _layoutRoot.PointerPressed += OnLayoutRootPointerPressed;
            _layoutRoot.PointerReleased += OnLayoutRootPointerReleased;
            _layoutRoot.PointerCaptureLost += OnLayoutRootPointerCaptureLost;
        }
    }

    private async void OnIsExpandedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (ContentTransition != null && Content is Visual visualContent)
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

    private bool IsHeaderClickable => Clickable && (Expands || Navigates);

    private void UpdateClickability()
    {
        bool clickable = IsHeaderClickable;
        PseudoClasses.Set(":clickable", clickable);
        if (!clickable)
        {
            _isPressed = false;
            PseudoClasses.Set(":pressed", false);
        }
    }

    private void OnLayoutRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsHeaderClickable
            && e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _isPressed = true;
            PseudoClasses.Set(":pressed", true);
        }
    }

    private void OnLayoutRootPointerReleased(object? sender, PointerReleasedEventArgs e)
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

    private void OnLayoutRootPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPressed = false;
        PseudoClasses.Set(":pressed", false);
    }

    private bool _isPressed;
    private Border? _layoutRoot;
    private CancellationTokenSource? _lastTransitionCts;
}
