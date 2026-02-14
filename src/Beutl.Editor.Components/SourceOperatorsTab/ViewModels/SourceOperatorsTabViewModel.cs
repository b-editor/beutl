using System.Collections.Specialized;
using System.Text.Json.Nodes;

using Beutl.Editor.Services;
using Beutl.Operation;
using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.SourceOperatorsTab.ViewModels;

public sealed class SourceOperatorsTabViewModel : IToolContext
{
    private readonly IDisposable _disposable0;
    private IEditorContext _editorContext;
    private IDisposable? _disposable1;
    private Element? _oldElement;

    public SourceOperatorsTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        Element = editorContext.GetRequiredService<IEditorSelection>().SelectedObject
            .Select(x => x as Element)
            .ToReactiveProperty();

        _disposable0 = Element.Subscribe(element =>
        {
            if (_oldElement != null)
            {
                SaveState(_oldElement);
            }
            _oldElement = element;

            ClearItems();
            if (element != null)
            {
                _disposable1?.Dispose();

                Items.AddRange(element.Operation.Children.Select(x => new SourceOperatorViewModel(x, this)));
                _disposable1 = element.Operation.Children.CollectionChangedAsObservable()
                    .Subscribe(e =>
                    {
                        void RemoveItems(CoreList<SourceOperatorViewModel> items, int index, int count)
                        {
                            ISupportCloseAnimation? closeAnm = this.GetService<ISupportCloseAnimation>();
                            foreach (SourceOperatorViewModel item in items.GetMarshal().Value.Slice(index, count))
                            {
                                closeAnm?.Close(item.Model);
                                item?.Dispose();
                            }
                            items.RemoveRange(index, count);
                        }

                        switch (e.Action)
                        {
                            case NotifyCollectionChangedAction.Add:
                                Items.InsertRange(e.NewStartingIndex, e.NewItems!
                                    .Cast<SourceOperator>()
                                    .Select(x => new SourceOperatorViewModel(x, this)));
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
                                RemoveItems(Items, e.OldStartingIndex, e.OldItems!.Count);
                                newIndex = e.NewStartingIndex;
                                if (newIndex > e.OldStartingIndex)
                                {
                                    newIndex -= e.OldItems!.Count;
                                }

                                Items.InsertRange(newIndex, e.NewItems!
                                    .Cast<SourceOperator>()
                                    .Select(x => new SourceOperatorViewModel(x, this)));
                                break;

                            case NotifyCollectionChangedAction.Remove:
                                RemoveItems(Items, e.OldStartingIndex, e.OldItems!.Count);
                                break;

                            case NotifyCollectionChangedAction.Reset:
                                ClearItems(true);
                                break;
                        }
                    });

                RestoreState(element);
            }
        });
    }

    public string Header => Strings.SourceOperators;

    public Action<SourceOperator>? RequestScroll { get; set; }

    public ReactiveProperty<Element?> Element { get; }

    [Obsolete("Use Element property instead.")]
    public ReactiveProperty<Element?> Layer => Element;

    public CoreList<SourceOperatorViewModel> Items { get; } = [];

    public ToolTabExtension Extension => SourceOperatorsTabExtension.Instance;

    public IEditorContext ParentContext => _editorContext;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public void ScrollTo(SourceOperator obj)
    {
        RequestScroll?.Invoke(obj);
    }

    public void Dispose()
    {
        if (Element.Value != null)
        {
            SaveState(Element.Value);
            Element.Value = null;
        }
        _disposable0.Dispose();
        _disposable1?.Dispose();

        Element.Dispose();
        _editorContext = null!;
        RequestScroll = null;
    }

    private static string ViewStateDirectory(Element element)
    {
        string directory = Path.GetDirectoryName(element.Uri!.LocalPath)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState(Element element)
    {
        string viewStateDir = ViewStateDirectory(element);
        var json = new JsonArray();
        foreach (SourceOperatorViewModel? item in Items)
        {
            json.Add(item?.SaveState());
        }

        string name = Path.GetFileNameWithoutExtension(element.Uri!.LocalPath);
        json.JsonSave(Path.Combine(viewStateDir, $"{name}.operators.config"));
    }

    private void RestoreState(Element element)
    {
        string viewStateDir = ViewStateDirectory(element);
        string name = Path.GetFileNameWithoutExtension(element.Uri!.LocalPath);
        string viewStateFile = Path.Combine(viewStateDir, $"{name}.operators.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is JsonArray array)
            {
                foreach ((JsonNode? item, SourceOperatorViewModel? op) in array.Zip(Items))
                {
                    if (item != null && op != null)
                    {
                        op.RestoreState(item);
                    }
                }
            }
        }
    }

    private void ClearItems(bool closeAnm = false)
    {
        ISupportCloseAnimation? closeService = closeAnm ? this.GetService<ISupportCloseAnimation>() : null;
        foreach (SourceOperatorViewModel? item in Items.GetMarshal().Value)
        {
            closeService?.Close(item.Model);
            item?.Dispose();
        }
        Items.Clear();
    }

    public void ReadFromJson(JsonObject json)
    {
        if (Element.Value != null)
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
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Element.Value;

        return _editorContext.GetService(serviceType);
    }
}
