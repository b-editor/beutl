using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.TimelineTab.Generative;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.TimelineTab.Views;

/// <summary>
/// The timeline's AI task bar, in a panel that can be dragged by its title bar and stays open
/// until closed, as the color picker does: a file dialog or a click on the timeline must not
/// throw away a prompt being written.
/// </summary>
internal sealed class TimelineAiPopupFlyout(TimelineAiPopupViewModel viewModel) : FAPickerFlyoutBase
{
    /// <summary>The panel, once it has been shown.</summary>
    internal DraggablePickerFlyoutPresenter? Presenter { get; private set; }

    protected override Control CreatePresenter()
    {
        var presenter = Presenter = new DraggablePickerFlyoutPresenter
        {
            Padding = new Thickness(12, 40, 12, 12),
            ShowHideButtons = false,
            Content = new TimelineAiPopupView { DataContext = viewModel },
        };
        presenter.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Panel>("DragArea") is { } header)
            {
                header.Children.Add(new TextBlock
                {
                    Text = viewModel.Title,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(12, 0, 40, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                });
            }

            if (e.NameScope.Find<Button>("CloseButton") is { } close)
                close.IsVisible = true;
        };
        // The bar is laid out for its width; a scroll bar would only cover part of it.
        ScrollViewer.SetHorizontalScrollBarVisibility(presenter, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(presenter, ScrollBarVisibility.Disabled);
        presenter.CloseClicked += (_, _) => Hide();
        presenter.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Hide();
        };
        return presenter;
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        Popup.IsLightDismissEnabled = false;
    }

    // The panel has no accept button; Generate in the panel is its confirmation.
    protected override void OnConfirmed() => Hide();

    protected override bool ShouldShowConfirmationButtons() => false;
}
