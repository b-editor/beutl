using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

namespace Beutl.Editor.Components.CutDetectionTab.ViewModels;

public sealed class CutDetectionTabViewModel : IToolContext
{
    private readonly IEditorContext _editor;
    private readonly Scene _scene;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<Element?> _target = new();
    private bool _disposed;

    public CutDetectionTabViewModel(IEditorContext editor)
    {
        _editor = editor;
        _scene = editor.GetRequiredService<Scene>();
        TargetName = _target.Select(ToolTabHeaderHelper.ObserveElementLabel).Switch()
            .ToReadOnlyReactivePropertySlim(string.Empty).AddTo(_disposables)!;
        Header = TargetName.Select(name => ToolTabHeaderHelper.Compose(Strings.DetectCuts, name))
            .ToReadOnlyReactivePropertySlim(Strings.DetectCuts).AddTo(_disposables)!;
        HasAnalysis = Analysis.Select(model => model != null).ToReadOnlyReactivePropertySlim().AddTo(_disposables);
        _disposables.Add(_target);
        _disposables.Add(Analysis);
        _disposables.Add(IsApplying);
        _disposables.Add(IsSelected);
    }

    public ToolTabExtension Extension => CutDetectionTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header { get; }
    public IReadOnlyReactiveProperty<string> TargetName { get; }
    public ReadOnlyReactivePropertySlim<bool> HasAnalysis { get; }
    public ReactivePropertySlim<CutDetectionViewModel?> Analysis { get; } = new();
    public ReactivePropertySlim<bool> IsApplying { get; } = new();
    public Element? Target => _target.Value;
    public IReadOnlyReactiveProperty<MediaTime>? CurrentTime => _editor.GetService<IEditorClock>()?.CurrentTime;

    public static void Open(IEditorContext editor, Element element)
    {
        if (!CutDetectionViewModel.CanAnalyze(editor.GetRequiredService<Scene>(), element)) return;
        var tab = editor.FindToolTab<CutDetectionTabViewModel>() ?? new CutDetectionTabViewModel(editor);
        tab.Show(element);
        if (!editor.OpenToolTab(tab)) tab.Dispose();
    }

    public void Show(Element element)
    {
        if (_disposed) return;
        _target.Value = element;
        Refresh();
    }

    public void Refresh() => Refresh(startAnalysis: true);

    private void Refresh(bool startAnalysis)
    {
        if (_disposed) return;
        CutDetectionViewModel? previous = Analysis.Value;
        previous?.Dispose();
        Analysis.Value = null;
        if (Target is not { } element || !CutDetectionViewModel.CanAnalyze(_scene, element)) return;

        var history = _editor.GetRequiredService<HistoryManager>();
        if (startAnalysis) history.FlushPendingMutations();
        var model = new CutDetectionViewModel(_scene, element, history,
            _editor.GetRequiredService<GlobalConfiguration>().EditorConfig,
            _editor.GetService<IPreviewPlayer>(), ExtensionGuard.FindService<DecoderRegistry>(_editor));
        if (previous != null)
        {
            model.Threshold.Value = previous.Threshold.Value;
            model.MinimumMilliseconds.Value = previous.MinimumMilliseconds.Value;
            model.ModeIndex.Value = previous.ModeIndex.Value;
            model.IncludeLinked.Value = previous.IncludeLinked.Value;
        }

        Analysis.Value = model;
        if (startAnalysis) _ = model.AnalyzeAsync();
    }

    public async Task ApplyAsync()
    {
        if (_disposed || IsApplying.Value || Analysis.Value is not { } model) return;
        IsApplying.Value = true;
        try
        {
            await model.ApplyAsync();
        }
        finally
        {
            if (!_disposed) IsApplying.Value = false;
        }
    }

    public object? GetService(Type serviceType) => serviceType == typeof(Element) ? Target : _editor.GetService(serviceType);

    public void ReadFromJson(JsonObject json)
    {
        if (!json.TryGetPropertyValueAsJsonValue("elementId", out Guid id)
            || _scene.Children.FirstOrDefault(e => e.Id.Value == id) is not { } element) return;
        if (_disposed) return;
        _target.Value = element;
        Refresh(startAnalysis: false);
        if (Analysis.Value is not { } model) return;
        if (json.TryGetPropertyValueAsJsonValue("threshold", out decimal threshold)) model.Threshold.Value = threshold;
        if (json.TryGetPropertyValueAsJsonValue("minimum", out decimal minimum)) model.MinimumMilliseconds.Value = minimum;
        if (json.TryGetPropertyValueAsJsonValue("mode", out int mode)) model.ModeIndex.Value = Math.Clamp(mode, 0, 1);
        if (json.TryGetPropertyValueAsJsonValue("linked", out bool linked)) model.IncludeLinked.Value = linked;
    }

    public void WriteToJson(JsonObject json)
    {
        if (Target is not { } element || !_scene.Children.Contains(element) || Analysis.Value is not { } model) return;
        json["elementId"] = element.Id.Value;
        json["threshold"] = model.Threshold.Value;
        json["minimum"] = model.MinimumMilliseconds.Value;
        json["mode"] = model.ModeIndex.Value;
        json["linked"] = model.IncludeLinked.Value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Analysis.Value?.Dispose();
        _disposables.Dispose();
    }
}
