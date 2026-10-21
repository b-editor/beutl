using System.ComponentModel;
using System.Reactive.Linq;
using Beutl.Animation;

namespace Beutl.Editor.Components.Helpers;

// KeyFrameAnimation<T> registers UseGlobalClock once per value type, so it is observed through PropertyChanged.
internal static class KeyFrameAnimationObservables
{
    public static IObservable<bool> ObserveUseGlobalClock(this IKeyFrameAnimation animation)
        => Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => animation.PropertyChanged += handler,
                handler => animation.PropertyChanged -= handler)
            .Where(e => e.EventArgs.PropertyName == nameof(IKeyFrameAnimation.UseGlobalClock))
            .Select(_ => animation.UseGlobalClock)
            .StartWith(animation.UseGlobalClock);
}
