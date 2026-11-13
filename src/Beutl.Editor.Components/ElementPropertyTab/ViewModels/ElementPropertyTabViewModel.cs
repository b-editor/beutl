using System.Collections.Specialized;
using System.Reactive;
using System.Text.Json.Nodes;

using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

public sealed class ElementPropertyTabViewModel : IPinnableToolContext
{
    private const string PinnedElementJsonKey = "pinnedElementId";
    // A range selection changes the selection with each pointer move; the view of several elements waits for it to settle.
    private static readonly TimeSpan s_multipleSelectionDelay = TimeSpan.FromMilliseconds(100);
    private readonly CompositeDisposable _disposables = [];
    private readonly ToolTabPin _pin;
    private readonly IEditorSelection? _selection;
    private IEditorContext _editorContext;
    private IDisposable? _disposable1;
    private Element? _oldElement;
    private Element? _oldDisplayedElement;
    // The element whose view state the shown items keep: the selected one for the copy of several elements.
    private Element? _stateOwner;
    private bool _isDisposed;

    public ElementPropertyTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        // A pin keeps one element, so there is nothing to pin while the tab shows several.
        _pin = new ToolTabPin(Element.CombineLatest(MultipleElements, (element, multiple) => element != null && multiple == null));

        DisplayedElement = MultipleElements
            .Select(multiple => multiple?.Copy ?? (IObservable<Element?>)Element)
            .Switch()
            .DistinctUntilChanged()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanEdit = DisplayedElement.Select(element => element is null
                ? Observable.Return(false)
                : ElementEditability.Observe(element, editorContext.GetService<Scene>()))
            .Switch()
            .ToReadOnlyReactivePropertySlim();

        Header = _pin.IsPinned
            .CombineLatest(Element, (pinned, element) => pinned ? element : null)
            .Select(ToolTabHeaderHelper.ObserveElementLabel)
            .Switch()
            .Select(label => ToolTabHeaderHelper.Compose(Strings.ElementProperty, label))
            .ToReadOnlyReactivePropertySlim(Strings.ElementProperty)
            .DisposeWith(_disposables)!;

        Element.Subscribe(OnElementChanged).DisposeWith(_disposables);
        DisplayedElement.Subscribe(OnDisplayedElementChanged).DisposeWith(_disposables);

        _selection = editorContext.GetService<IEditorSelection>();
        if (_selection != null)
        {
            IObservable<Unit> selectionChanges = _selection.SelectedElements.CollectionChangedAsObservable()
                .Select(_ => Unit.Default)
                .Merge(_selection.SelectedObject.Skip(1).Select(_ => Unit.Default))
                .Merge(_pin.IsPinned.Skip(1).Select(_ => Unit.Default));
            // Leaving the view of several elements does not wait, so a click on one element shows it at once.
            selectionChanges
                .Where(_ => MultipleElements.Value != null && !ShowsMultipleElements)
                .Subscribe(_ => UpdateMultipleElements())
                .DisposeWith(_disposables);
            selectionChanges
                .Throttle(s_multipleSelectionDelay)
                .ObserveOnUIDispatcher()
                .Subscribe(_ => UpdateMultipleElements())
                .DisposeWith(_disposables);
            UpdateMultipleElements();
        }

        // An edit, an undo or a lock can change the objects of the elements, or what of them can be written.
        editorContext.GetService<HistoryManager>()?.StateChanged
            .ObserveOnUIDispatcher()
            .Subscribe(_ => MultipleElements.Value?.Refresh())
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Finds the tab to show <paramref name="element"/> in: the one already showing it, else an unpinned one.
    /// </summary>
    public static ElementPropertyTabViewModel? FindReusable(IEditorContext editorContext, Element element)
    {
        return ToolTabReuse.Find<ElementPropertyTabViewModel>(
            editorContext,
            t => t.Element.Value == element,
            t => t.Element.Value is null,
            retargetAnyOpen: true);
    }

    private void OnElementChanged(Element? element)
    {
        if (_oldElement != null)
        {
            _oldElement.DetachedFromHierarchy -= OnElementDetached;
        }
        _oldElement = element;

        if (element != null)
        {
            element.DetachedFromHierarchy += OnElementDetached;
        }
    }

    // The items follow the element shown: the selected one, or the copy that stands for several.
    private void OnDisplayedElementChanged(Element? element)
    {
        if (_oldDisplayedElement != null && _stateOwner != null)
        {
            SaveState(_stateOwner);
        }
        _oldDisplayedElement = element;
        _stateOwner = MultipleElements.Value is { } multiple && ReferenceEquals(element, multiple.Copy.Value)
            ? multiple.Shown
            : element;

        _disposable1?.Dispose();
        _disposable1 = null;
        ClearItems();
        if (element != null)
        {
            Items.AddRange(CreateItems(element.Objects));
            _disposable1 = element.Objects.CollectionChangedAsObservable()
                .Subscribe(OnObjectsChanged);

            if (_stateOwner != null)
                RestoreState(_stateOwner);
        }
    }

    // A removed element leaves the tab empty, which also releases the pin.
    private void OnElementDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        Element.Value = null;
    }

    // Shows the selected elements together while there are several, or the one element again.
    internal void UpdateMultipleElements()
    {
        if (_isDisposed) return;

        MultiElementEditSession? current = MultipleElements.Value;
        if (GetMultipleSelection() is not { } elements)
        {
            MultipleElements.Value = null;
            current?.Dispose();
        }
        else if (current != null && current.Elements.SequenceEqual(elements))
        {
            current.Refresh();
        }
        else
        {
            MultipleElements.Value = new MultiElementEditSession(elements, _editorContext);
            current?.Dispose();
        }
    }

    private bool ShowsMultipleElements => _selection is { SelectedElements.Count: > 1 } && !IsPinned.Value;

    // The selected elements when there are several to show: first the one the selection last moved to, whose values the
    // view shows, then the others in timeline order.
    private Element[]? GetMultipleSelection()
    {
        if (!ShowsMultipleElements) return null;

        ICoreList<Element> selected = _selection!.SelectedElements;
        Element[] ordered = [.. selected.OrderBy(element => element.Start.Value).ThenBy(element => element.ZIndex.Value)];
        Element first = _selection.SelectedObject.Value is Element selectedObject && selected.Contains(selectedObject)
            ? selectedObject
            : ordered[0];
        return [first, .. ordered.Where(element => element != first)];
    }

    private void OnObjectsChanged(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Items.InsertRange(e.NewStartingIndex, CreateItems(e.NewItems!.Cast<EngineObject>()));
                break;

            case NotifyCollectionChangedAction.Move:
                int newIndex = e.NewStartingIndex;
                if (newIndex > e.OldStartingIndex)
                {
                    newIndex += e.OldItems!.Count;
                }

                Items.MoveRange(e.OldStartingIndex, e.OldItems!.Count, newIndex);
                break;

            case NotifyCollectionChangedAction.Replace:
                RemoveItems(e.OldStartingIndex, e.OldItems!.Count);
                newIndex = e.NewStartingIndex;
                if (newIndex > e.OldStartingIndex)
                {
                    newIndex -= e.OldItems!.Count;
                }

                Items.InsertRange(newIndex, CreateItems(e.NewItems!.Cast<EngineObject>()));
                break;

            case NotifyCollectionChangedAction.Remove:
                RemoveItems(e.OldStartingIndex, e.OldItems!.Count);
                break;

            case NotifyCollectionChangedAction.Reset:
                ClearItems();
                break;
        }
    }

    private IEnumerable<EngineObjectPropertyViewModel> CreateItems(IEnumerable<EngineObject> objects)
    {
        return objects.Select(x => new EngineObjectPropertyViewModel(x, this));
    }

    private void RemoveItems(int index, int count)
    {
        foreach (EngineObjectPropertyViewModel item in Items.AsSpan().Slice(index, count))
        {
            item.Dispose();
        }
        Items.RemoveRange(index, count);
    }

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ReactiveProperty<Element?> Element { get; } = new();

    // The selected elements while there are several and the tab is not pinned, which it edits together.
    public ReactivePropertySlim<MultiElementEditSession?> MultipleElements { get; } = new();

    // What the items show: Element, or the copy that stands for the selected elements.
    public ReadOnlyReactivePropertySlim<Element?> DisplayedElement { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    public CoreList<EngineObjectPropertyViewModel> Items { get; } = [];

    public ToolTabExtension Extension => ElementPropertyTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReactiveProperty<bool> IsPinned => _pin.IsPinned;

    public IReadOnlyReactiveProperty<bool> HasTarget => _pin.HasTarget;

    // Asks the view to scroll an object (and, when known, one of its properties) into view and
    // flash it.
    public ReactiveCommand<(EngineObjectPropertyViewModel Item, IPropertyEditorContext? Property)> RevealRequested { get; } = new();

    // Expands and reveals the editor showing `target`, an object inside the shown element.
    // Returns false when the target is the element itself or not shown here.
    public bool Reveal(CoreObject target, string? propertyName)
    {
        foreach (EngineObjectPropertyViewModel item in Items)
        {
            if (!IsWithin(target, item.Model))
                continue;

            item.IsExpanded.Value = true;
            RevealRequested.Execute((item, item.FindPropertyEditor(target, propertyName)));
            return true;
        }

        return false;
    }

    internal static bool IsWithin(CoreObject target, IHierarchical root)
    {
        return (target as IHierarchical)?.EnumerateAncestors<IHierarchical>().Any(h => ReferenceEquals(h, root)) == true;
    }

    public void Dispose()
    {
        if (_stateOwner != null)
        {
            SaveState(_stateOwner);
            _stateOwner = null;
        }
        Element.Value = null;
        _isDisposed = true;
        _disposables.Dispose();
        _disposable1?.Dispose();
        // While the tab shows several elements, Element going null leaves the copy displayed, so its items stay.
        ClearItems();
        MultipleElements.Value?.Dispose();
        MultipleElements.Value = null;

        CanEdit.Dispose();
        _pin.Dispose();
        MultipleElements.Dispose();
        RevealRequested.Dispose();
        Element.Dispose();
        _editorContext = null!;
    }

    private static string ViewStateDirectory(Element element)
    {
        return EditorConstants.GetViewStateDirectory(element.Uri!);
    }

    // Creates the view-state directory when it is missing.
    private static string ViewStateFile(Element element)
    {
        string viewStateDir = ViewStateDirectory(element);
        string name = Path.GetFileNameWithoutExtension(element.Uri!.LocalPath);
        return Path.Combine(viewStateDir, $"{name}.property.config");
    }

    private void SaveState(Element element)
    {
        string viewStateFile = ViewStateFile(element);
        var json = new JsonArray();
        foreach (EngineObjectPropertyViewModel item in Items)
        {
            json.Add(item.SaveState());
        }

        json.JsonSave(viewStateFile);
    }

    private void RestoreState(Element element)
    {
        string viewStateFile = ViewStateFile(element);

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is JsonArray array)
            {
                foreach ((JsonNode? item, EngineObjectPropertyViewModel? itemViewModel) in array.Zip(Items))
                {
                    if (item != null && itemViewModel != null)
                    {
                        itemViewModel.RestoreState(item);
                    }
                }
            }
        }
    }

    private void ClearItems()
    {
        foreach (EngineObjectPropertyViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }
        Items.Clear();
    }

    public void ReadFromJson(JsonObject json)
    {
        // A pinned tab comes back on its element, or unpinned when that element is gone.
        if (ToolTabPin.WasPinned(json)
            && json.TryGetPropertyValueAsJsonValue(PinnedElementJsonKey, out Guid id)
            && _editorContext.GetService<Scene>()?.FindById(id) is Element element)
        {
            Element.Value = element;
            _pin.IsPinned.Value = true;
        }
        else if (_stateOwner != null)
        {
            RestoreState(_stateOwner);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (_stateOwner != null)
        {
            SaveState(_stateOwner);
        }

        _pin.WriteToJson(json);
        if (IsPinned.Value && Element.Value is { } element)
            json[PinnedElementJsonKey] = element.Id.Value;
        else
            json.Remove(PinnedElementJsonKey);
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return DisplayedElement.Value;
        if (serviceType == typeof(IPropertyEditorMirror))
            return MultipleElements.Value?.Mirror;

        return _editorContext.GetService(serviceType);
    }
}
