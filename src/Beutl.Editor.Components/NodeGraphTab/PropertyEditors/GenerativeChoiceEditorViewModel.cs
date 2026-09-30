using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using Beutl.Logging;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.PropertyEditors;

/// <summary>
/// Picks a generative node's model, aspect ratio or background from the catalog, the way
/// the AI dialog's controls do: labelled models, and shapes narrowed to the chosen model.
/// </summary>
internal sealed class GenerativeChoiceEditorViewModel : IPropertyEditorContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<GenerativeChoiceEditorViewModel>();
    private readonly IPropertyAdapter _property;
    private readonly GenerativeChoice _choice;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex = new(-1);
    private IReadOnlyList<GenerativeModelInfo> _models = [];
    private object[] _values = [];
    private IReadOnlyList<EnumItem> _items = [];
    private WeakReference<EnumEditor>? _editorRef;
    private bool _loadStarted;
    private IGenerativeModelCatalog? _catalog;
    private bool _catalogLoaded;
    private bool _disposed;

    public GenerativeChoiceEditorViewModel(
        IPropertyAdapter property,
        GenerativeChoice choice,
        PropertyEditorExtension extension)
    {
        _property = property;
        _choice = choice;
        Extension = extension;
        _selectedIndex.DisposeWith(_disposables);
        _property.GetObservable()
            .Subscribe(_ => Dispatcher.UIThread.Post(Rebuild))
            .DisposeWith(_disposables);
        if (_choice.Kind != GenerativeChoiceKind.Model && _choice.Node.ModelProperty is { } model)
        {
            // Skip the current value: only a change of model can narrow the shapes.
            model.GetObservable()
                .Skip(1)
                .Subscribe(_ => Dispatcher.UIThread.Post(() => Rebuild(modelChanged: true)))
                .DisposeWith(_disposables);
        }

        Rebuild();
    }

    public PropertyEditorExtension Extension { get; }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (!_loadStarted
            && visitor is IServiceProvider services
            && services.GetService(typeof(IGenerativeModelCatalog)) is IGenerativeModelCatalog catalog)
        {
            _loadStarted = true;
            _ = LoadAsync(catalog);
        }

        if (visitor is EnumEditor editor)
        {
            editor.Header = _property.DisplayName;
            editor.Items = _items;
            editor.Bind(EnumEditor.SelectedIndexProperty, _selectedIndex.ToBinding())
                .DisposeWith(_disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(_disposables);
            _editorRef = new WeakReference<EnumEditor>(editor);
        }
    }

    private int _loadVersion;
    private bool _rebuilding;

    private async Task LoadAsync(IGenerativeModelCatalog catalog)
    {
        if (_catalog is null)
        {
            _catalog = catalog;
            _choice.Node.CatalogOperationChanged += OnCatalogOperationChanged;
        }

        // Only the latest load may apply: a slower answer for the previous task would
        // otherwise replace the models of the one now chosen.
        int version = ++_loadVersion;
        try
        {
            IReadOnlyList<GenerativeModelInfo> models =
                await catalog.GetModelsAsync(_choice.Node.CatalogOperationId, CancellationToken.None);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || version != _loadVersion)
                    return;
                bool reloaded = _catalogLoaded;
                _models = models;
                _catalogLoaded = true;
                // A reload follows a change of task, whose models are a different list.
                Rebuild(modelChanged: reloaded);
            });
        }
        catch (Exception ex)
        {
            // The defaults stay on offer; the executor checks the request again anyway.
            s_logger.LogWarning(ex, "Failed to load the AI model catalog for a node editor.");
        }
    }

    private void OnCatalogOperationChanged(object? sender, EventArgs e)
    {
        if (_catalog is { } catalog)
            Dispatcher.UIThread.Post(() => _ = LoadAsync(catalog));
    }

    private void Rebuild() => Rebuild(modelChanged: false);

    private void Rebuild(bool modelChanged)
    {
        if (_disposed)
            return;

        object current = _property.GetValue() ?? (_choice.Kind == GenerativeChoiceKind.Duration ? 0 : string.Empty);
        GenerativeModelInfo? model = CurrentModel();
        GenerativeVideoCapabilities? video = _choice.Node.Operation is GenerativeOperation.VideoGeneration or GenerativeOperation.VideoEdit
            ? model?.Video ?? GenerativeVideoCapabilities.Unrestricted
            : null;
        GenerativeImageCapabilities image = model?.Image ?? new GenerativeImageCapabilities(null, null, true, int.MaxValue);
        List<(object Value, string Label)> options = _choice.Kind switch
        {
            // As in the AI tab, there is no "default" entry: an input left empty shows and
            // runs on the model the picker would start on.
            GenerativeChoiceKind.Model => _models.Select(m => ((object)m.Id, m.Label)).ToList(),
            GenerativeChoiceKind.AspectRatio =>
                (video?.AspectRatioChoices ?? image.AspectRatioChoices).Select(v => ((object)v, v)).ToList(),
            GenerativeChoiceKind.Resolution =>
                (video ?? GenerativeVideoCapabilities.Unrestricted).ResolutionChoices.Select(v => ((object)v, v)).ToList(),
            GenerativeChoiceKind.Duration =>
                (video ?? GenerativeVideoCapabilities.Unrestricted).DurationChoices
                    .Select(v => ((object)v, $"{v} {Strings.AiVideoSeconds}")).ToList(),
            _ => image.BackgroundChoices.Select(v => ((object)v, BackgroundLabel(v))).ToList(),
        };

        bool offered = options.Any(option => Equals(option.Value, current));
        if (_choice.Kind == GenerativeChoiceKind.Model)
        {
            if (modelChanged && _catalogLoaded && current is string { Length: > 0 } && !offered)
            {
                // Another task's model would be refused; fall back to this task's default.
                _property.SetValue(string.Empty);
                return;
            }
        }
        else if (modelChanged && _catalogLoaded && options.Count > 0 && !offered)
        {
            // As the dialog does: a value the new model does not take would only be
            // refused, so fall back to one it does — the nearest length, or the first shape.
            _property.SetValue(current is int seconds
                ? options.Select(option => (int)option.Value).MinBy(value => Math.Abs(value - seconds))
                : options[0].Value);
            return;
        }

        object shown = current;
        if (_choice.Kind == GenerativeChoiceKind.Model && current is string { Length: 0 })
            shown = DefaultModel()?.Id ?? string.Empty;

        // A saved value the list no longer holds is still shown, so it is never silently lost.
        if (shown is not string { Length: 0 } && !options.Any(option => Equals(option.Value, shown)))
            options.Add((shown, shown is int s2 ? $"{s2} {Strings.AiVideoSeconds}" : shown.ToString() ?? string.Empty));

        _values = options.Select(option => option.Value).ToArray();
        _items = options.Select(option => new EnumItem(option.Label, string.Empty, option.Value)).ToArray();
        // The combo box confirms the index it is given; that must not store the shown default
        // model into an input left empty.
        _rebuilding = true;
        try
        {
            if (_editorRef?.TryGetTarget(out EnumEditor? editor) == true)
                editor.Items = _items;
            _selectedIndex.Value = -1;
            _selectedIndex.Value = Array.IndexOf(_values, shown);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private GenerativeModelInfo? CurrentModel()
    {
        string? modelId = _choice.Node.ModelProperty?.GetValue();
        return string.IsNullOrEmpty(modelId)
            ? DefaultModel()
            : _models.FirstOrDefault(m => m.Id == modelId);
    }

    // The model the AI tab's picker starts on, which the executor also runs an empty input on.
    private GenerativeModelInfo? DefaultModel()
        => _models.FirstOrDefault(m => m.IsAvailable && m.IsDefault) ?? _models.FirstOrDefault(m => m.IsAvailable);

    private static string BackgroundLabel(string value) => value switch
    {
        "auto" => Strings.AiBackgroundAuto,
        "opaque" => Strings.AiBackgroundOpaque,
        "transparent" => Strings.AiBackgroundTransparent,
        _ => value,
    };

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (!_rebuilding
            && e is PropertyEditorValueChangedEventArgs<int> args
            && args.NewValue >= 0
            && args.NewValue < _values.Length)
        {
            _property.SetValue(_values[args.NewValue]);
        }
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        _disposed = true;
        _choice.Node.CatalogOperationChanged -= OnCatalogOperationChanged;
        _disposables.Dispose();
        _editorRef = null;
    }
}
