using System.Collections.Specialized;
using System.Text.Json.Nodes;

using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
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
    private readonly CompositeDisposable _disposables = [];
    private readonly ToolTabPin _pin;
    private IEditorContext _editorContext;
    private IDisposable? _disposable1;
    private Element? _oldElement;

    public ElementPropertyTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _pin = new ToolTabPin(Element.Select(element => element != null));

        CanEdit = Element.Select(element => element is null
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
            SaveState(_oldElement);
        }
        _oldElement = element;

        _disposable1?.Dispose();
        _disposable1 = null;
        ClearItems();
        if (element != null)
        {
            element.DetachedFromHierarchy += OnElementDetached;
            Items.AddRange(CreateItems(element.Objects));
            _disposable1 = element.Objects.CollectionChangedAsObservable()
                .Subscribe(OnObjectsChanged);

            RestoreState(element);
        }
    }

    // A removed element leaves the tab empty, which also releases the pin.
    private void OnElementDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        Element.Value = null;
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
        foreach (EngineObjectPropertyViewModel item in Items.GetMarshal().Value.Slice(index, count))
        {
            item?.Dispose();
        }
        Items.RemoveRange(index, count);
    }

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ReactiveProperty<Element?> Element { get; } = new();

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
        foreach (EngineObjectPropertyViewModel? item in Items)
        {
            if (item is null || !IsWithin(target, item.Model))
                continue;

            item.IsExpanded.Value = true;
            RevealRequested.Execute((item, item.FindPropertyEditor(target, propertyName)));
            return true;
        }

        return false;
    }

    internal static bool IsWithin(CoreObject target, IHierarchical root)
    {
        for (IHierarchical? current = target as IHierarchical; current is not null; current = current.HierarchicalParent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (Element.Value != null)
        {
            SaveState(Element.Value);
            Element.Value = null;
        }
        _disposables.Dispose();
        _disposable1?.Dispose();

        CanEdit.Dispose();
        _pin.Dispose();
        RevealRequested.Dispose();
        Element.Dispose();
        _editorContext = null!;
    }

    private static string ViewStateDirectory(Element element)
    {
        string directory = Path.GetDirectoryName(element.Uri!.LocalPath)!;

        directory = Path.Combine(directory, EditorConstants.BeutlFolder, EditorConstants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
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
        foreach (EngineObjectPropertyViewModel? item in Items)
        {
            json.Add(item?.SaveState());
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
        foreach (EngineObjectPropertyViewModel? item in Items.GetMarshal().Value)
        {
            item?.Dispose();
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
        else if (Element.Value != null)
        {
            RestoreState(Element.Value);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (Element.Value != null)
        {
            SaveState(Element.Value);
        }

        _pin.WriteToJson(json);
        if (IsPinned.Value && Element.Value is { } element)
            json[PinnedElementJsonKey] = element.Id;
        else
            json.Remove(PinnedElementJsonKey);
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Element.Value;

        return _editorContext.GetService(serviceType);
    }
}
