using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Primitives;

namespace Beutl.Views.Editors;

public static class ExpandTransitionHelper
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ListItemDuration = TimeSpan.FromMilliseconds(167);

    public static IDisposable Attach(ToggleButton toggle, Visual content, TimeSpan? duration = null)
    {
        var transition = new CrossFade(duration ?? DefaultDuration);
        CancellationTokenSource? cts = null;

        return toggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                cts?.Cancel();
                cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;

                if (v == true)
                {
                    await transition.Start(null, content, token);
                }
                else
                {
                    await transition.Start(content, null, token);
                }
            });
    }
}
