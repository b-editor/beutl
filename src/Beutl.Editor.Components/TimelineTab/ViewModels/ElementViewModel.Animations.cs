using System.Collections.Specialized;
using Avalonia.Threading;
using Beutl.Animation;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

// What the clip shows of its objects' animations, and the inline layers its "Animation" menu opens. The marks are read
// from this element alone, after an edit of it, and only while its clip is on screen.
public sealed partial class ElementViewModel
{
    // Adding or removing a key raises no Edited, so the keys of each animation the marks were read from are watched.
    private readonly Dictionary<IKeyFrameAnimation, NotifyCollectionChangedEventHandler> _watchedAnimations =
        new(ReferenceEqualityComparer.Instance);
    private bool _animationMarksStale = true;
    private bool _animationMarksQueued;

    public ReactivePropertySlim<IReadOnlyList<MediaTime>> KeyFrameMarkTimes { get; } = new([]);

    // How many times the marks were read, for tests that check that an edit reads only its own element's.
    internal int AnimationMarksReadCount { get; private set; }

    private void InitializeAnimationMarks()
    {
        EventHandler edited = (_, _) => InvalidateAnimationMarks();
        Model.Edited += edited;
        Disposable.Create(() =>
        {
            Model.Edited -= edited;
            UnwatchAnimations();
        }).AddTo(_disposables);
    }

    // A clip off screen only notes that its marks are out of date, and reads them when it is shown again.
    private void OnAnimationMarksViewAttached(bool attached)
    {
        if (attached && _animationMarksStale && !_isDisposed)
            ReadAnimationMarks();
    }

    private void InvalidateAnimationMarks()
    {
        _animationMarksStale = true;
        if (_isDisposed || !_timelineViewAttached || _animationMarksQueued) return;

        // One read for all the edits of a gesture or a transaction.
        _animationMarksQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _animationMarksQueued = false;
            if (_animationMarksStale && _timelineViewAttached && !_isDisposed)
                ReadAnimationMarks();
        });
    }

    private void ReadAnimationMarks()
    {
        _animationMarksStale = false;
        AnimationMarksReadCount++;
        var found = new HashSet<IKeyFrameAnimation>(ReferenceEqualityComparer.Instance);
        IReadOnlyList<MediaTime> times = ElementAnimations.ReadMarkTimes(Model, animation => found.Add(animation));

        foreach (IKeyFrameAnimation animation in _watchedAnimations.Keys.Where(item => !found.Contains(item)).ToArray())
        {
            animation.KeyFrames.CollectionChanged -= _watchedAnimations[animation];
            _watchedAnimations.Remove(animation);
        }

        foreach (IKeyFrameAnimation animation in found)
        {
            if (_watchedAnimations.ContainsKey(animation)) continue;

            NotifyCollectionChangedEventHandler handler = (_, _) => InvalidateAnimationMarks();
            animation.KeyFrames.CollectionChanged += handler;
            _watchedAnimations.Add(animation, handler);
        }

        if (!times.SequenceEqual(KeyFrameMarkTimes.Value))
            KeyFrameMarkTimes.Value = times;
    }

    private void UnwatchAnimations()
    {
        foreach ((IKeyFrameAnimation animation, NotifyCollectionChangedEventHandler handler) in _watchedAnimations)
        {
            animation.KeyFrames.CollectionChanged -= handler;
        }

        _watchedAnimations.Clear();
    }

    // The rows of the clip's "Animation" menu, read when the menu opens.
    internal List<AnimationMenuItem> GetAnimationMenu() => ElementAnimations.ReadMenu(Model);

    internal bool IsInlineShown(IAnimatablePropertyAdapter property) => Timeline.FindInline(Model, property) != null;

    // Opens the property's inline layer, or closes it when it is open.
    internal void ToggleInline(IAnimatablePropertyAdapter property)
    {
        if (!IsEditable.Value) return;

        if (Timeline.FindInline(Model, property) is { } shown)
            Timeline.DetachInline(shown);
        else
            Timeline.AttachInline(property, Model);
    }

    internal void ShowAllInlines(IEnumerable<IAnimatablePropertyAdapter> properties)
    {
        if (!IsEditable.Value) return;

        foreach (IAnimatablePropertyAdapter property in properties)
        {
            if (Timeline.FindInline(Model, property) is null)
                Timeline.AttachInline(property, Model);
        }
    }
}
