using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.SilenceSplitTab.ViewModels;

public sealed class SilenceSplitTabViewModel : IToolContext
{
    private readonly IEditorContext _editor;
    private readonly Scene _scene;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<Element?> _target = new();
    private bool _disposed;

    public SilenceSplitTabViewModel(IEditorContext editor)
    {
        _editor = editor;
        _scene = editor.GetRequiredService<Scene>();
        TargetName = _target.Select(ToolTabHeaderHelper.ObserveElementLabel).Switch()
            .ToReadOnlyReactivePropertySlim(string.Empty).AddTo(_disposables)!;
        Header = TargetName.Select(name => ToolTabHeaderHelper.Compose(Strings.AutoSplitBySilence, name))
            .ToReadOnlyReactivePropertySlim(Strings.AutoSplitBySilence).AddTo(_disposables)!;
        HasAnalysis = Analysis.Select(model => model != null).ToReadOnlyReactivePropertySlim().AddTo(_disposables);
        _disposables.Add(_target);
        _disposables.Add(Analysis);
        _disposables.Add(IsApplying);
        _disposables.Add(IsSelected);
    }

    public ToolTabExtension Extension => SilenceSplitTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header { get; }
    public IReadOnlyReactiveProperty<string> TargetName { get; }
    public ReadOnlyReactivePropertySlim<bool> HasAnalysis { get; }
    public ReactivePropertySlim<SilenceSplitViewModel?> Analysis { get; } = new();
    public ReactivePropertySlim<bool> IsApplying { get; } = new();
    public Element? Target => _target.Value;
    public IReadOnlyReactiveProperty<MediaTime>? CurrentTime => _editor.GetService<IEditorClock>()?.CurrentTime;

    public static void Open(IEditorContext editor, Element element)
    {
        if (!SilenceSplitViewModel.CanAnalyze(editor.GetRequiredService<Scene>(), element)) return;
        var tab = editor.FindToolTab<SilenceSplitTabViewModel>() ?? new SilenceSplitTabViewModel(editor);
        tab.Show(element);
        if (!editor.OpenToolTab(tab)) tab.Dispose();
    }

    public void Show(Element element)
    {
        if (_disposed) return;
        _target.Value = element;
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed) return;
        var previous = Analysis.Value;
        previous?.Dispose();
        Analysis.Value = null;
        if (Target is not { } element || !SilenceSplitViewModel.CanAnalyze(_scene, element)) return;

        var history = _editor.GetRequiredService<HistoryManager>();
        history.FlushPendingMutations();
        var model = new SilenceSplitViewModel(_scene, element, history, _editor.GetRequiredService<IPreviewPlayer>());
        if (previous != null)
        {
            model.ThresholdDb.Value = previous.ThresholdDb.Value;
            model.MinimumMilliseconds.Value = previous.MinimumMilliseconds.Value;
            model.PaddingMilliseconds.Value = previous.PaddingMilliseconds.Value;
            model.ModeIndex.Value = previous.ModeIndex.Value;
            model.IncludeLinked.Value = previous.IncludeLinked.Value;
        }
        Analysis.Value = model;
        _ = model.AnalyzeAsync();
    }

    public async Task ApplyAsync()
    {
        if (_disposed || IsApplying.Value || Analysis.Value is not { } model) return;
        IsApplying.Value = true;
        CancellationToken cancellationToken = model.LifetimeToken;
        string savingStatus = Strings.SilenceSplit_Saving;
        bool saving = false;
        int? savedPercent = null;
        Exception? saveError = null;
        var autoSave = _editor.GetService<AutoSaveService>();
        using var progress = autoSave?.SaveProgress.Subscribe(value =>
        {
            savedPercent = value;
            if (saving && IsCurrent()) ShowSaveProgress();
        });
        using var errors = autoSave?.SaveError.Subscribe(error => saveError = error);
        try
        {
            if (!await model.ApplyAsync() || !IsCurrent()) return;
            if (_editor.GetService<IProjectFileWriteAdmission>() is { } files)
            {
                saving = true;
                model.IsBusy.Value = true;
                ShowSaveProgress();
                // Let the history-triggered save enter the existing write queue before waiting
                // behind it. This observes the save; it does not write the document a second time.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                using (await files.BeginProjectFileWriteAsync(cancellationToken)) { }
                if (!IsCurrent()) return;
                model.Progress.Value = 100;
                model.Status.Value = saveError == null ? Strings.Completed : string.Format(Strings.SilenceSplit_Failed, saveError.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed && ReferenceEquals(Analysis.Value, model))
                model.Status.Value = string.Format(Strings.SilenceSplit_Failed, ex.Message);
        }
        finally
        {
            if (IsCurrent())
            {
                model.IsBusy.Value = false;
                model.IsProgressIndeterminate.Value = false;
            }
            if (!_disposed) IsApplying.Value = false;
        }

        bool IsCurrent() => !_disposed && ReferenceEquals(Analysis.Value, model);

        void ShowSaveProgress()
        {
            model.Progress.Value = savedPercent ?? 0;
            model.IsProgressIndeterminate.Value = !savedPercent.HasValue;
            model.Status.Value = savedPercent.HasValue ? $"{savingStatus} {savedPercent}%" : savingStatus;
        }
    }

    public object? GetService(Type serviceType) => serviceType == typeof(Element) ? Target : _editor.GetService(serviceType);

    public void ReadFromJson(JsonObject json)
    {
        if (!json.TryGetPropertyValueAsJsonValue("elementId", out Guid id)
            || _scene.Children.FirstOrDefault(e => e.Id.Value == id) is not { } element) return;
        Show(element);
        if (Analysis.Value is not { } model) return;
        if (json.TryGetPropertyValueAsJsonValue("threshold", out decimal threshold)) model.ThresholdDb.Value = threshold;
        if (json.TryGetPropertyValueAsJsonValue("minimum", out decimal minimum)) model.MinimumMilliseconds.Value = minimum;
        if (json.TryGetPropertyValueAsJsonValue("padding", out decimal padding)) model.PaddingMilliseconds.Value = padding;
        if (json.TryGetPropertyValueAsJsonValue("mode", out int mode) && Enum.IsDefined(typeof(SilenceSplitMode), mode)) model.ModeIndex.Value = mode;
        if (json.TryGetPropertyValueAsJsonValue("linked", out bool linked)) model.IncludeLinked.Value = linked;
    }

    public void WriteToJson(JsonObject json)
    {
        if (Target is not { } element || !_scene.Children.Contains(element) || Analysis.Value is not { } model) return;
        json["elementId"] = element.Id.Value;
        json["threshold"] = model.ThresholdDb.Value;
        json["minimum"] = model.MinimumMilliseconds.Value;
        json["padding"] = model.PaddingMilliseconds.Value;
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
