using System.Collections.Specialized;
using Beutl.ProjectSystem;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    // The elements that can take part in a boundary transition, rebuilt on the first read after an edit.
    // Every element refreshes its transition parts after each edit; the rest of them skip the
    // whole-scene neighbour searches.
    private HashSet<Element>? _transitionParticipants;

    // Subscribed before any element view model, whose refreshes run later on the UI dispatcher.
    private void InitializeTransitionParticipants()
    {
        Observable.FromEventPattern(h => Scene.Edited += h, h => Scene.Edited -= h)
            .Subscribe(_ => _transitionParticipants = null)
            .AddTo(_disposables);
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Scene.Children.CollectionChanged += h,
                h => Scene.Children.CollectionChanged -= h)
            .Subscribe(_ => _transitionParticipants = null)
            .AddTo(_disposables);
    }

    internal bool MayTakePartInTransition(Element element)
    {
        return (_transitionParticipants ??= ElementTransitions.GetParticipants(Scene)).Contains(element);
    }
}
