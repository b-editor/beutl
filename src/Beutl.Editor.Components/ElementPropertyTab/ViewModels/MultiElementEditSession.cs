using System.Collections;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

// The selected elements while the element property tab edits them together. The tab shows a copy of one of them in the
// view it has for one element, of which it shows only what each of the others holds too (MultiElementCorrespondence),
// and every edit of the copy happens to each of them (MultiElementReplicator, MultiElementMirror). Nothing else edits
// the copy, so only the tab's edits reach the others; an edit made elsewhere, or an undo, shows the elements again in a
// new copy when it changed what the copy holds or what the elements have in common.
public sealed class MultiElementEditSession : IDisposable
{
    // How often the editors compare the elements' values again while the playhead moves.
    private static readonly TimeSpan s_playheadSampling = TimeSpan.FromMilliseconds(100);
    private readonly IServiceProvider _services;
    private readonly Scene? _scene;
    private readonly ReactivePropertySlim<Element> _copy;
    private readonly ReactivePropertySlim<string?> _lockedNote = new();
    private readonly ReactivePropertySlim<string?> _nothingInCommonNote = new();
    private readonly Subject<Unit> _changed = new();
    private readonly Subject<Unit> _valuesChanged = new();
    private readonly CompositeDisposable _disposables = [];
    private CopyState _current;

    internal MultiElementEditSession(IReadOnlyList<Element> elements, IServiceProvider services)
    {
        _services = services;
        _scene = services.GetService<Scene>();
        Elements = elements;
        Shown = ChooseShown();
        _current = CreateCopy(Shown);
        _copy = new ReactivePropertySlim<Element>(_current.Element);
        UpdateNotes();

        // The values the editors show move with the playhead, and so does whether the elements' values differ.
        services.GetService<IEditorClock>()?.CurrentTime
            .Skip(1)
            .Sample(s_playheadSampling)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => _valuesChanged.OnNext(Unit.Default))
            .DisposeWith(_disposables);
    }

    // The selected elements, the one shown first when it can be edited.
    public IReadOnlyList<Element> Elements { get; }

    // The element the copy was made from, whose values the tab shows.
    public Element Shown { get; private set; }

    // What the tab shows and edits in place of the elements.
    public IReadOnlyReactiveProperty<Element> Copy => _copy;

    // How many of the elements a lock keeps as they are, or null when none.
    public IReadOnlyReactiveProperty<string?> LockedNote => _lockedNote;

    // Says that the tab shows nothing, as the elements have no object in common, or null when they have one.
    public IReadOnlyReactiveProperty<string?> NothingInCommonNote => _nothingInCommonNote;

    internal IPropertyEditorMirror Mirror => _current.Mirror;

    // After an edit, an undo or a lock: copies the shown element again when it changed in another way than through the
    // copy, when another element is to be shown because a lock now keeps the shown one as it is, or when the elements
    // no longer have in common what the copy shows.
    public void Refresh()
    {
        Element shown = ChooseShown();
        _current.KeepPlaceOf(Shown);
        if (shown != Shown
            || !_current.Compared.SequenceEqual(ComparedElements(shown))
            || !Matches(shown, _current.Element)
            || !_current.ShowsWhatTheElementsShare())
        {
            Shown = shown;
            CopyState previous = _current;
            _current = CreateCopy(shown);
            _copy.Value = _current.Element;
            previous.Dispose();
        }
        else
        {
            // Unlocking the elements, or the layers they lie on, changes nothing the copy holds of them.
            _current.KeepLock(!Elements.Any(IsEditable));
            _current.Mirror.Forget();
            _changed.OnNext(Unit.Default);
        }

        UpdateNotes();
        _valuesChanged.OnNext(Unit.Default);
    }

    public void Dispose()
    {
        _current.Dispose();
        _disposables.Dispose();
        _changed.Dispose();
        _valuesChanged.Dispose();
        _lockedNote.Dispose();
        _nothingInCommonNote.Dispose();
        _copy.Dispose();
    }

    // The first of the elements an edit can change, so that what the tab shows is what an edit changes.
    private Element ChooseShown() => Elements.FirstOrDefault(IsEditable) ?? Elements[0];

    private bool IsEditable(Element element)
        => element.HierarchicalParent is Scene && ElementEditability.IsEditable(element, _scene);

    // The elements other than shown that the tab compares the copy with: those an edit can change, or all of them while
    // none can, as the copy then only shows what they have in common.
    private Element[] ComparedElements(Element shown)
    {
        Element[] others = [.. Elements.Where(element => element != shown)];
        Element[] editable = [.. others.Where(IsEditable)];
        return editable.Length > 0 || IsEditable(shown) ? editable : others;
    }

    private CopyState CreateCopy(Element shown)
    {
        var copy = (Element)CoreSerializer.DeserializeFromJsonObject(Serialize(shown), typeof(Element));
        // The copy can be edited while one of the elements can; when none can, the tab shows it read-only.
        copy.IsLocked.Value = !Elements.Any(IsEditable);
        Element[] compared = ComparedElements(shown);
        var correspondence = new MultiElementCorrespondence(copy);
        var root = new MultiEditRoot(
            copy, ((IHierarchical)shown).FindHierarchicalRoot(), correspondence, compared, _changed);
        var replicator = new MultiElementReplicator(
            correspondence, shown, [.. Elements.Where(element => element != shown)], _scene,
            _services.GetService<IFrameCacheInvalidator>());
        var mirror = new MultiElementMirror(replicator, correspondence, compared, _valuesChanged, _services);
        return new CopyState(copy, root, correspondence, replicator, mirror, compared, _changed);
    }

    private void UpdateNotes()
    {
        int locked = Elements.Count(element => !IsEditable(element));
        _lockedNote.Value = locked > 0 ? string.Format(MessageStrings.MultipleElements_LockedNote, locked) : null;
        _nothingInCommonNote.Value = _current.SharesAnObject() ? null : MessageStrings.MultipleElements_NothingInCommon;
    }

    // Whether the copy still holds what the element does. The copy is read from the element and edits reach the element
    // exactly, Ids included, so they compare equal until something else changes the element.
    private static bool Matches(Element element, Element copy)
        => JsonNode.DeepEquals(Comparable(Serialize(element)), Comparable(Serialize(copy)));

    private static JsonObject Serialize(Element element) => CoreSerializer.SerializeToJsonObject(element);

    // The copy's lock says whether the tab can edit it, it has no file of its own, and it follows the element's time;
    // where the element lies on the timeline is the timeline's to change.
    private static JsonObject Comparable(JsonObject json)
    {
        json.Remove(nameof(Element.IsLocked));
        json.Remove("Uri");
        json.Remove(nameof(Element.Start));
        json.Remove(nameof(Element.Length));
        json.Remove(nameof(Element.ZIndex));
        return json;
    }

    // A copy with what edits it and what it shares with the elements.
    private sealed class CopyState : IDisposable
    {
        private readonly MultiEditRoot _root;
        private readonly MultiElementCorrespondence _correspondence;
        private readonly MultiElementReplicator _replicator;
        private readonly Subject<Unit> _changed;
        private string _shared;
        // Set when an edit of the copy changed what the elements hold, which it did to each of them alike.
        private bool _changedThroughCopy;

        public CopyState(
            Element element, MultiEditRoot root, MultiElementCorrespondence correspondence, MultiElementReplicator replicator,
            MultiElementMirror mirror, IReadOnlyList<Element> compared, Subject<Unit> changed)
        {
            Element = element;
            _root = root;
            _correspondence = correspondence;
            _replicator = replicator;
            Mirror = mirror;
            Compared = compared;
            _changed = changed;
            _shared = DescribeShared();
            replicator.StructureChanged += OnStructureChanged;
        }

        public Element Element { get; }

        public MultiElementMirror Mirror { get; }

        public IReadOnlyList<Element> Compared { get; }

        // Whether the tab shows one of the copy's objects, which each of the elements has in its place.
        public bool SharesAnObject() => Element.Objects.Any(_root.Shows);

        // The copy keeps the time and the layer of the element it was made from, so that its editors find keyframes
        // where that element's editors do, and a lock of a layer keeps the copy as it keeps that element. The copy's own
        // changes of time and layer reach no element.
        public void KeepPlaceOf(Element shown)
        {
            if (Element.Start.Value != shown.Start.Value) Element.Start.Value = shown.Start.Value;
            if (Element.Length.Value != shown.Length.Value) Element.Length.Value = shown.Length.Value;
            if (Element.ZIndex.Value != shown.ZIndex.Value) Element.ZIndex.Value = shown.ZIndex.Value;
        }

        // The copy can be edited while one of the elements can.
        public void KeepLock(bool locked)
        {
            if (Element.IsLocked.Value != locked) Element.IsLocked.Value = locked;
        }

        // Whether the elements still have in common what the copy shows of them: an edit made elsewhere can add or
        // remove an object of one of them, while one through the copy does the same to each.
        public bool ShowsWhatTheElementsShare()
        {
            _correspondence.Invalidate();
            string shared = DescribeShared();
            if (_changedThroughCopy)
            {
                _changedThroughCopy = false;
                _shared = shared;
                return true;
            }

            return shared == _shared;
        }

        public void Dispose()
        {
            _replicator.StructureChanged -= OnStructureChanged;
            Mirror.Dispose();
            // Letting go of a copy detaches it, which closes the tool tabs its editors opened on it.
            _replicator.Dispose();
            _root.Dispose();
        }

        // The editors' changes of what the elements hold are the copy's too, so they share what they did. What they
        // share is read after the edit, as a property tells of a new value before the value joins its owner.
        private void OnStructureChanged()
        {
            Mirror.Forget();
            _changedThroughCopy = true;
            _changed.OnNext(Unit.Default);
        }

        // Which objects and properties of the copy the tab shows: a mark for each in the order of the copy.
        private string DescribeShared()
        {
            var text = new StringBuilder();
            Visit(Element);
            return text.ToString();

            void Visit(CoreObject obj)
            {
                foreach (IProperty property in MultiElementCorrespondence.DisplayedProperties(obj))
                {
                    text.Append(_root.Shows(obj, property) ? '1' : '0');
                    switch (property.Value)
                    {
                        case CoreObject child when IsHeldBy(child, obj):
                            VisitItem(child);
                            break;
                        case IList list:
                            foreach (object? item in list)
                            {
                                if (item is CoreObject listed and not IKeyFrame && IsHeldBy(listed, obj))
                                    VisitItem(listed);
                            }

                            break;
                    }
                }
            }

            void VisitItem(CoreObject obj)
            {
                bool shown = _root.Shows(obj);
                text.Append(shown ? '(' : '-');
                if (shown)
                {
                    Visit(obj);
                    text.Append(')');
                }
            }
        }

        private static bool IsHeldBy(CoreObject value, CoreObject owner)
            => ReferenceEquals((value as IHierarchical)?.HierarchicalParent, owner);
    }

    // The root of a copy, so that its objects count as attached, tool tabs opened on them see them leave, and the editors
    // of its objects show only what each of the elements it is compared with holds too.
    private sealed class MultiEditRoot : Hierarchical, IHierarchicalRoot, IPropertyEditorScope, IChildObjectSource, IDisposable
    {
        private readonly IHierarchical? _elementsRoot;
        private readonly MultiElementCorrespondence _correspondence;
        private readonly IReadOnlyList<Element> _compared;
        private Element? _copy;

        // elementsRoot is the root of the elements' own hierarchy, such as the application that holds their project.
        public MultiEditRoot(
            Element copy, IHierarchical? elementsRoot, MultiElementCorrespondence correspondence, IReadOnlyList<Element> compared,
            IObservable<Unit> changed)
        {
            _copy = copy;
            _elementsRoot = elementsRoot;
            _correspondence = correspondence;
            _compared = compared;
            Changed = changed;
            ((IModifiableHierarchical)this).AddChild(copy);
        }

        public event EventHandler<IHierarchical>? DescendantAttached;

        public event EventHandler<IHierarchical>? DescendantDetached;

        public IObservable<Unit> Changed { get; }

        public void OnDescendantAttached(IHierarchical descendant) => DescendantAttached?.Invoke(this, descendant);

        public void OnDescendantDetached(IHierarchical descendant) => DescendantDetached?.Invoke(this, descendant);

        // The keyframes of the elements can lie at other times, which an editor of keyframes shows of one element. The
        // rows of separated components edit the components of each element, so a property whose components the copy
        // separates is shown when each element separates them too.
        public bool Shows(CoreObject owner, IProperty property)
            => owner is IKeyFrame or IPropertyAnimation
               || _compared.All(element => _correspondence.Find(owner, element) is { } found
                                           && MultiElementCorrespondence.FindDisplayedProperty(found, property) is { } shared
                                           && (property.GetPropertyAnimation() is not IComponentAnimation
                                               || shared.GetPropertyAnimation() is IComponentAnimation));

        public bool Shows(CoreObject obj)
            => obj is IKeyFrame or IPropertyAnimation || _compared.All(element => _correspondence.Find(obj, element) != null);

        // A search by Id from an object of the copy goes on from here into the elements' own hierarchy, as one from the
        // elements does: an expression or a reference of the copy names objects of other elements and scenes there. The
        // copy comes first, as the element it stands for holds the same Ids, so that what it names of that element is its
        // own.
        IEnumerable<object?> IChildObjectSource.GetChildObjects()
        {
            foreach (IProperty property in Properties)
                yield return property;

            if (_copy is not null)
                yield return _copy;

            if (_elementsRoot is not null)
                yield return _elementsRoot;
        }

        public IReadOnlyList<Type> TypesOf(CoreObject obj)
            => [.. _compared.Select(element => _correspondence.Find(obj, element)?.GetType())
                .Prepend(obj.GetType())
                .OfType<Type>()
                .Distinct()];

        public void Dispose()
        {
            if (_copy is null) return;

            ((IModifiableHierarchical)this).RemoveChild(_copy);
            _copy = null;
        }
    }
}
