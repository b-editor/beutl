using Avalonia;
using Avalonia.Controls.Primitives;
using Beutl.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public static class ExpandTransitionHelper
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ListItemDuration = TimeSpan.FromMilliseconds(167);

    // Opening fades the content in as it moves down into place; closing fades it out quickly before it goes.
    public static IDisposable Attach(ToggleButton toggle, Visual content, TimeSpan? duration = null)
    {
        TimeSpan revealDuration = duration ?? DefaultDuration;

        // Content whose visibility is also bound can be shown by its data context after the first state was applied,
        // so collapsed content is hidden again once it is in a window.
        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (toggle.IsChecked != true && content.IsVisible)
            {
                UiMotion.Cancel(content);
                content.IsVisible = false;
                content.Opacity = 1;
            }
        }

        content.AttachedToVisualTree += OnAttached;

        IDisposable subscription = toggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v =>
            {
                if (v == true)
                {
                    content.IsVisible = true;
                    UiMotion.Reveal(content, revealDuration);
                }
                else if (content.IsVisible)
                {
                    double from = content.Opacity;
                    UiMotion.Run(content, UiMotion.Fast, progress => content.Opacity = from * (1 - progress), () =>
                    {
                        content.IsVisible = false;
                        content.Opacity = 1;
                    });
                }
            });

        return Disposable.Create(() =>
        {
            subscription.Dispose();
            content.AttachedToVisualTree -= OnAttached;
        });
    }
}
