using System.Collections.Specialized;
using System.Reactive;
using Avalonia.Threading;
using Beutl.ProjectSystem;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    // The elements that can take part in a boundary transition, rebuilt on the first read after an edit.
    // Every element refreshes its transition parts after each edit; the rest of them skip the
    // whole-scene neighbour searches.
    private HashSet<Element>? _transitionParticipants;
    private bool _transitionUpdateQueued;

    // Subscribed before any element view model, whose refreshes run later on the UI dispatcher.
    private void InitializeTransitionParticipants()
    {
        Observable.FromEventPattern(h => Scene.Edited += h, h => Scene.Edited -= h)
            .Select(_ => Unit.Default)
            .Merge(Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => Scene.Children.CollectionChanged += h,
                    h => Scene.Children.CollectionChanged -= h)
                .Select(_ => Unit.Default))
            .Subscribe(_ =>
            {
                _transitionParticipants = null;
                if (_transitionUpdateQueued || _isDisposed) return;
                _transitionUpdateQueued = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _transitionUpdateQueued = false;
                    UpdateTransitionParts();
                });
            })
            .AddTo(_disposables);

        Scale.Subscribe(_ => UpdateTransitionParts()).AddTo(_disposables);
    }

    // One refresh per batch of edits, instead of a queued callback per clip per edit
    // (and quadratic event unsubscription on close).
    private void UpdateTransitionParts()
    {
        if (_isDisposed) return;
        foreach (ElementViewModel element in Elements)
            element.UpdateTransitionParts();
    }

    internal bool MayTakePartInTransition(Element element)
    {
        return (_transitionParticipants ??= ElementTransitions.GetParticipants(Scene)).Contains(element);
    }
}
