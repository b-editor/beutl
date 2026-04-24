using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.EqualizerProperties.ViewModels;

public sealed class EqualizerPropertiesViewModel : IPropertyEditorContext, IServiceProvider
{
    private readonly IReadOnlyList<IPropertyAdapter> _props;
    private IServiceProvider? _parentServices;
    private bool _editorsCreated;
    private IPropertyAdapter? _bandCountAdapter;
    private IDisposable? _clockSubscription;
    private Element? _element;

    public EqualizerPropertiesViewModel(IReadOnlyList<IPropertyAdapter> props)
    {
        _props = props;

        SelectedBand = SelectedBandIndex
            .Select(i => i >= 0 && i < Bands.Count ? Bands[i] : null)
            .ToReadOnlyReactivePropertySlim();

        Bands.CollectionChanged += OnBandsCollectionChanged;
    }

    public PropertyEditorExtension Extension => EqualizerPropertiesExtension.Instance;

    public ObservableCollection<EqualizerBandItemViewModel> Bands { get; } = [];

    public ReactivePropertySlim<int> SelectedBandIndex { get; } = new(-1);

    public ReadOnlyReactivePropertySlim<EqualizerBandItemViewModel?> SelectedBand { get; }

    public ReactivePropertySlim<IPropertyEditorContext?> BandCountEditor { get; } = new();

    public ReactivePropertySlim<TimeSpan> CurrentTime { get; } = new();

    public ReactivePropertySlim<int> SampleRate { get; } = new(44100);

    public EqualizerEffect? TryGetEqualizerEffect()
    {
        if (_props.Count == 0) return null;
        return _props[0].GetEngineProperty()?.GetOwnerObject() as EqualizerEffect;
    }

    public object? GetService(Type serviceType)
    {
        return _parentServices?.GetService(serviceType);
    }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is IServiceProvider serviceProvider)
        {
            _parentServices = serviceProvider;
            _element = serviceProvider.GetService<Element>();
            CreateEditors();
            AcceptChildren();
            AttachClock();
            SampleRate.Value = _element?.FindHierarchicalParent<Project>()?.GetSampleRate() ?? 44100;
        }
    }

    private void AttachClock()
    {
        _clockSubscription?.Dispose();
        if (_parentServices?.GetService<IEditorClock>() is { } clock)
        {
            _clockSubscription = clock.CurrentTime.Subscribe(t => CurrentTime.Value = t);
        }
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        Bands.CollectionChanged -= OnBandsCollectionChanged;
        DetachEqualizerBands();

        foreach (var band in Bands)
        {
            band.Dispose();
        }
        Bands.Clear();

        BandCountEditor.Value?.Dispose();
        BandCountEditor.Value = null;

        _clockSubscription?.Dispose();
        _clockSubscription = null;
        CurrentTime.Dispose();
        SampleRate.Dispose();

        SelectedBand.Dispose();
        SelectedBandIndex.Dispose();
    }

    private void CreateEditors()
    {
        if (_editorsCreated) return;
        _editorsCreated = true;

        var factory = _parentServices?.GetService<IPropertyEditorFactory>();
        if (factory == null) return;

        var equalizer = TryGetEqualizerEffect();
        if (equalizer == null) return;

        _bandCountAdapter = _props.FirstOrDefault(p => p.GetEngineProperty() == equalizer.BandCountOption);
        if (_bandCountAdapter != null)
        {
            BandCountEditor.Value = factory.CreateEditor(_bandCountAdapter);
        }

        AttachEqualizerBands(equalizer);
        RebuildBands(equalizer, factory);
    }

    private void AttachEqualizerBands(EqualizerEffect equalizer)
    {
        if (equalizer.Bands is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += OnEqualizerBandsChanged;
        }
    }

    private void DetachEqualizerBands()
    {
        var equalizer = TryGetEqualizerEffect();
        if (equalizer?.Bands is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged -= OnEqualizerBandsChanged;
        }
    }

    private void OnEqualizerBandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var equalizer = TryGetEqualizerEffect();
        var factory = _parentServices?.GetService<IPropertyEditorFactory>();
        if (equalizer == null || factory == null) return;
        RebuildBands(equalizer, factory);
    }

    private void RebuildBands(EqualizerEffect equalizer, IPropertyEditorFactory factory)
    {
        foreach (var band in Bands)
        {
            band.Dispose();
        }
        Bands.Clear();

        for (int i = 0; i < equalizer.Bands.Count; i++)
        {
            Bands.Add(new EqualizerBandItemViewModel(equalizer.Bands[i], i, factory, _element, _parentServices));
        }

        // Re-evaluate SelectedBand so it points to a freshly created ViewModel
        // (keeping the same index would leave SelectedBand bound to a disposed item).
        int selected = SelectedBandIndex.Value;
        SelectedBandIndex.Value = -1;
        if (selected >= 0 && selected < Bands.Count)
        {
            SelectedBandIndex.Value = selected;
        }
    }

    private void AcceptChildren()
    {
        var visitor = new Visitor(this);
        BandCountEditor.Value?.Accept(visitor);

        foreach (var band in Bands)
        {
            band.FrequencyEditor?.Accept(visitor);
            band.GainEditor?.Accept(visitor);
            band.QEditor?.Accept(visitor);
            band.FilterTypeEditor?.Accept(visitor);
        }
    }

    private void OnBandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var visitor = new Visitor(this);
        if (e.NewItems != null)
        {
            foreach (EqualizerBandItemViewModel item in e.NewItems)
            {
                item.FrequencyEditor?.Accept(visitor);
                item.GainEditor?.Accept(visitor);
                item.QEditor?.Accept(visitor);
                item.FilterTypeEditor?.Accept(visitor);
            }
        }
    }

    private sealed record Visitor(EqualizerPropertiesViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType) => Obj._parentServices?.GetService(serviceType);
        public void Visit(IPropertyEditorContext context) { }
    }
}
