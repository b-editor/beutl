using System.ComponentModel;
using System.Reactive;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

internal sealed class DuckingSourceLayersEditorViewModel : PropertyEditorViewModel
{
    private readonly SerialDisposable _sceneSubscriptions = new();
    private readonly IListProperty<Reference<TimelineLayer>> _sources;
    private Scene? _scene;
    private Element? _element;
    private ITimelineOptionsProvider? _options;

    public DuckingSourceLayersEditorViewModel(IPropertyAdapter property, IListProperty<Reference<TimelineLayer>> sources)
        : base(property)
    {
        _sources = sources;
        Disposables.Add(_sceneSubscriptions);
        Disposables.Add(Layers);
        HasLayers = Layers.Select(layers => layers.Count > 0).ToReadOnlyReactivePropertySlim().AddTo(Disposables);
        Disposables.Add(_sources.CollectionChangedAsObservable().Subscribe(_ => RefreshLayers()));
    }

    public ReactivePropertySlim<IReadOnlyList<LayerChoice>> Layers { get; } = new([]);

    public ReadOnlyReactivePropertySlim<bool> HasLayers { get; }

    public override void Reset()
    {
        if (!CanEditProperty.Value || _sources.Count == 0) return;
        _sources.Clear();
        Commit();
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is not IServiceProvider || IsDisposed) return;

        _element = (_sources.GetOwnerObject() as IHierarchical)?.FindHierarchicalParent<Element>() ?? this.GetService<Element>();
        _scene = _element?.FindHierarchicalParent<Scene>() ?? this.GetService<Scene>();
        _options = this.GetService<ITimelineOptionsProvider>();
        _sceneSubscriptions.Disposable = null;
        if (_scene is not { } scene)
        {
            RefreshLayers();
            return;
        }

        var layerChanges = ObserveItems(scene.Layers, layer => Observable.Merge(
            Changed(layer.Name), Changed(layer.ZIndex)));
        var rowChanges = _options?.Options.Select(options => options.MaxLayerCount).DistinctUntilChanged().Select(_ => Unit.Default)
            ?? ObserveItems(scene.Children, element => Changed(element.ZIndex));
        var ownLayerChanges = _element is { } owner ? Changed(owner.ZIndex) : Observable.Empty<Unit>();
        _sceneSubscriptions.Disposable = Observable.Merge(layerChanges, rowChanges, ownLayerChanges)
            .Subscribe(_ => RefreshLayers());
    }

    private static IObservable<Unit> Changed<T>(IProperty<T> property)
        => property.GetObservable().Skip(1).Select(_ => Unit.Default);

    private static IObservable<Unit> ObserveItems<T>(IListProperty<T> items, Func<T, IObservable<Unit>> changes)
        => items.CollectionChangedAsObservable()
            .Select(_ => Unit.Default).StartWith(Unit.Default)
            .Select(_ => Observable.Merge(items.Select(changes)).StartWith(Unit.Default))
            .Switch();

    private void RefreshLayers()
    {
        if (_scene is not { } scene)
        {
            Layers.Value = [];
            return;
        }

        var models = new Dictionary<int, TimelineLayer>();
        foreach (TimelineLayer layer in scene.Layers)
            models.TryAdd(layer.ZIndex.Value, layer);

        // Rows in the timeline can exist before they have a model. Give them an identity on selection.
        IEnumerable<int> available = _options is { } provider
            ? Enumerable.Range(0, provider.Options.Value.MaxLayerCount)
            : scene.Children.Select(element => element.ZIndex.Value);
        int[] rows = [.. available.Union(models.Keys)
            .Where(row => row != _element?.ZIndex.Value).Order()];
        var selected = _sources.Select(source => source.Id).ToHashSet();
        if (!Layers.Value.Select(choice => choice.Row).SequenceEqual(rows))
        {
            var choices = rows.Select(row => new LayerChoice(this, row)).ToArray();
            UpdateChoices(choices);
            Layers.Value = choices;
        }
        else
        {
            UpdateChoices(Layers.Value);
        }

        void UpdateChoices(IReadOnlyList<LayerChoice> choices)
        {
            foreach (LayerChoice choice in choices)
            {
                TimelineLayer? layer = models.GetValueOrDefault(choice.Row);
                choice.Update(layer?.Name.Value, layer is not null && selected.Contains(layer.Id.Value));
            }
        }
    }

    private void SetSelected(int row, bool selected)
    {
        if (_scene is not { } scene || !CanEditProperty.Value || row == _element?.ZIndex.Value) return;

        TimelineLayer? layer = scene.Layers.FirstOrDefault(model => model.ZIndex.Value == row);
        if (selected)
        {
            if (layer is null)
            {
                layer = new TimelineLayer { ZIndex = { Value = row } };
                scene.Layers.Add(layer);
            }
            if (_sources.Any(source => source.Id == layer.Id.Value)) return;
            _sources.Add(new Reference<TimelineLayer>(layer));
        }
        else
        {
            if (layer is null) return;
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                if (_sources[i].Id == layer.Id.Value)
                    _sources.RemoveAt(i);
            }
        }

        Commit();
    }

    internal sealed class LayerChoice(DuckingSourceLayersEditorViewModel editor, int row) : INotifyPropertyChanged
    {
        private bool _selected;
        private string? _name;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Row { get; } = row;

        public string Name => _name ?? RowLabel;

        public string RowLabel { get; } = string.Format(AudioStrings.DuckingEffect_LayerNumber, row);

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected != value) editor.SetSelected(Row, value);
            }
        }

        internal void Update(string? name, bool selected)
        {
            if (string.IsNullOrWhiteSpace(name)) name = null;
            if (_name != name)
            {
                _name = name;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
            if (_selected != selected)
            {
                _selected = selected;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
