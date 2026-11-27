using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentBackgroundWorkBar : UserControl
{
    public AgentBackgroundWorkBar() => InitializeComponent();

    private Flyout? WorkFlyout => WorkCard is { } card ? FlyoutBase.GetAttachedFlyout(card) as Flyout : null;

    private void OnOpenWork(object? sender, RoutedEventArgs e)
    {
        if (WorkFlyout is not { } flyout || TopLevel.GetTopLevel(this) is not { } topLevel
            || WorkCard.TranslatePoint(default, topLevel) is not { } anchor)
            return;

        WorkFlyoutBody.Width = Math.Max(0, WorkCard.Bounds.Width - 2);
        WorkFlyoutBody.MaxHeight = Math.Clamp(anchor.Y - 30, 80, 350);
        flyout.Popup.ShouldUseOverlayLayer = true;
        flyout.ShowAt(WorkCard);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible)
            WorkFlyout?.Hide();
    }
}
