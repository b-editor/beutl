using System.Reactive;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

public sealed class TrackMatteSettingsViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Element _element;
    private readonly Scene _scene;
    private bool _updating;

    public TrackMatteSettingsViewModel(Element element, IServiceProvider services)
    {
        _element = element;
        _scene = services.GetRequiredService<Scene>();
        var attributes = services.GetRequiredService<IElementAttributeService>();
        Modes = [
            new(MatteMode.None, StatusStrings.None),
            new(MatteMode.Alpha, Strings.MatteAlpha),
            new(MatteMode.Luma, Strings.MatteLuma),
            new(MatteMode.InvertedAlpha, Strings.MatteInvertedAlpha),
            new(MatteMode.InvertedLuma, Strings.MatteInvertedLuma),
        ];

        SelectedSource.Skip(1).Subscribe(source =>
        {
            if (_updating || source is null) return;
            MatteMode mode = source.ZIndex == null ? MatteMode.None
                : element.MatteMode.Value == MatteMode.None ? MatteMode.Alpha : element.MatteMode.Value;
            attributes.SetTrackMatte(element, source.ZIndex, mode);
            Refresh();
        }).DisposeWith(_disposables);
        SelectedMode.Skip(1).Subscribe(mode =>
        {
            if (_updating || mode is null) return;
            attributes.SetTrackMatte(element, element.MatteSourceZIndex.Value, mode.Mode);
            Refresh();
        }).DisposeWith(_disposables);
        ShowInOutput.Skip(1).Subscribe(visible =>
        {
            if (_updating) return;
            attributes.SetVisibleInOutput(element, visible);
            Refresh();
        }).DisposeWith(_disposables);

        element.MatteSourceZIndex.GetObservable().Subscribe(_ => Refresh()).DisposeWith(_disposables);
        element.MatteMode.GetObservable().Subscribe(_ => Refresh()).DisposeWith(_disposables);
        element.IsVisibleInOutput.GetObservable().Subscribe(_ => Refresh()).DisposeWith(_disposables);
        Observable.FromEventPattern(h => _scene.Edited += h, h => _scene.Edited -= h)
            .Subscribe(_ => Refresh()).DisposeWith(_disposables);
        _scene.Children.CollectionChangedAsObservable().Subscribe(_ => Refresh()).DisposeWith(_disposables);
        _scene.Layers.CollectionChangedAsObservable().Select(_ => Unit.Default).StartWith(Unit.Default)
            .Select(_ => _scene.Layers.Select(layer => Observable.Merge(
                layer.Name.GetObservable().Select(_ => Unit.Default),
                layer.ZIndex.GetObservable().Select(_ => Unit.Default))).Merge().StartWith(Unit.Default))
            .Switch().Subscribe(_ => Refresh()).DisposeWith(_disposables);
    }

    public sealed record SourceChoice(int? ZIndex, string Label);
    public sealed record ModeChoice(MatteMode Mode, string Label);

    public CoreList<SourceChoice> Sources { get; } = [];
    public IReadOnlyList<ModeChoice> Modes { get; }
    public ReactivePropertySlim<SourceChoice?> SelectedSource { get; } = new();
    public ReactivePropertySlim<ModeChoice?> SelectedMode { get; } = new();
    public ReactivePropertySlim<bool> ShowInOutput { get; } = new(true);

    private void Refresh()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            IEnumerable<int> layers = _scene.Layers.Select(layer => layer.ZIndex.Value)
                .Concat(_scene.Children.Select(element => element.ZIndex.Value))
                .Where(z => z < _element.ZIndex.Value);
            // A saved reference stays selectable after a layer move, or while that layer is empty.
            if (_element.MatteSourceZIndex.Value is { } selected) layers = layers.Append(selected);
            SourceChoice[] sources = [new(null, StatusStrings.None), .. layers.Distinct().Order()
                .Select(z => new SourceChoice(z, FormatLayer(z)))];
            if (!Sources.SequenceEqual(sources)) Sources.Replace(sources);

            SelectedSource.Value = Sources.First(source => source.ZIndex == _element.MatteSourceZIndex.Value);
            SelectedMode.Value = Modes.FirstOrDefault(mode => mode.Mode == _element.MatteMode.Value);
            ShowInOutput.Value = _element.IsVisibleInOutput.Value;
        }
        finally
        {
            _updating = false;
        }
    }

    private string FormatLayer(int zIndex)
    {
        string? name = _scene.Layers.FirstOrDefault(layer => layer.ZIndex.Value == zIndex)?.Name.Value;
        return string.IsNullOrWhiteSpace(name) ? zIndex.ToString(CultureInfo.CurrentCulture) : $"{zIndex}: {name}";
    }

    public void Dispose()
    {
        _disposables.Dispose();
        SelectedSource.Dispose();
        SelectedMode.Dispose();
        ShowInOutput.Dispose();
    }
}
