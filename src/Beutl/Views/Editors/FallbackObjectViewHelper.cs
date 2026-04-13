using Avalonia;
using Avalonia.Controls;
using Beutl.Editor.Components.Views;

namespace Beutl.Views.Editors;

public static class FallbackObjectViewHelper
{
    public static IDisposable Attach(Control host, Action<FallbackObjectView> addView)
    {
        return host.GetObservable(StyledElement.DataContextProperty)
            .Select(x => x as IFallbackObjectViewModel)
            .Select(x => x?.IsFallback.Select(_ => x) ?? Observable.ReturnThenNever<IFallbackObjectViewModel?>(null))
            .Switch()
            .Where(v => v?.IsFallback.Value == true)
            .Take(1)
            .Subscribe(_ => addView(new FallbackObjectView()));
    }
}
