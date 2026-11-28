using System.Text.Json.Nodes;
using Avalonia.Controls;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.Editor.Components.CompressorProperties.ViewModels;

// The compressor's rows with its transfer curve above them. The curve edits the same number editors
// the rows show, so ranges, keyframes and one-drag-one-undo follow the number boxes.
public sealed class CompressorPropertiesViewModel : IPropertyEditorContext, IServiceProvider
{
    private readonly IReadOnlyList<IPropertyAdapter> _props;
    private readonly CompositeDisposable _subscriptions = [];
    private IServiceProvider? _parentServices;
    private bool _editorsCreated;
    private DragSession? _drag;

    public CompressorPropertiesViewModel(IReadOnlyList<IPropertyAdapter> props)
    {
        _props = props;
    }

    public PropertyEditorExtension Extension => CompressorPropertiesExtension.Instance;

    public CoreList<IPropertyEditorContext> Properties { get; } = [];

    public NumberEditorViewModel<float>? ThresholdEditor { get; private set; }

    public NumberEditorViewModel<float>? RatioEditor { get; private set; }

    // What the number rows show, which the curve draws. The curve reads these instead of the
    // properties so an animated value follows the playhead exactly as its row does.
    public ReactivePropertySlim<float> Threshold { get; } = new(DefaultThresholdDb);

    public ReactivePropertySlim<float> Ratio { get; } = new(DefaultRatio);

    public ReactivePropertySlim<float> Knee { get; } = new(DefaultKneeDb);

    public ReactivePropertySlim<float> MakeupGain { get; } = new(DefaultMakeupGainDb);

    public object? GetService(Type serviceType) => _parentServices?.GetService(serviceType);

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is not IServiceProvider services) return;

        _parentServices = services;
        CreateEditors();
        var childVisitor = new ChildVisitor(this);
        foreach (IPropertyEditorContext item in Properties)
        {
            item.Accept(childVisitor);
        }
    }

    public void ReadFromJson(JsonObject json)
        => NestedEditorContextHelper.ReadEditorArray(json, nameof(Properties), Properties);

    public void WriteToJson(JsonObject json)
        => NestedEditorContextHelper.WriteEditorArray(json, nameof(Properties), Properties);

    // A drag of the curve's handle or of the curve itself is one history session, as a number
    // editor's drag is: previews write the value, and the session's completion makes one undo step.
    public bool BeginDrag(Control owner, bool fromThreshold)
    {
        EndDrag();

        NumberEditorViewModel<float>? editor = fromThreshold ? ThresholdEditor : RatioEditor;
        // An expression decides the value, so the curve only shows that result; the number row keeps
        // editing the value under it.
        if (editor is null || !editor.CanEdit.Value || editor.HasExpression.Value) return false;

        _drag = DragSession.Begin(
            this, owner, CommandNames.EditProperty,
            confirm: () => _drag = null,
            cancel: () => _drag = null);
        return _drag != null;
    }

    public void SetThreshold(float thresholdDb)
    {
        if (_drag is null || ThresholdEditor is null) return;

        using (_drag.Enter())
        {
            ThresholdEditor.PreviewEdit(
                Math.Clamp(thresholdDb, MinThresholdDb, MaxThresholdDb), relative: false);
        }
    }

    public void SetRatio(float ratio)
    {
        if (_drag is null || RatioEditor is null) return;

        using (_drag.Enter())
        {
            RatioEditor.PreviewEdit(Math.Clamp(ratio, MinRatio, MaxRatio), relative: false);
        }
    }

    public void EndDrag()
    {
        _drag?.Confirm();
        _drag = null;
    }

    public void Dispose()
    {
        _drag?.Cancel();
        _drag = null;
        _subscriptions.Dispose();
        foreach (IPropertyEditorContext item in Properties)
        {
            item.Dispose();
        }

        Threshold.Dispose();
        Ratio.Dispose();
        Knee.Dispose();
        MakeupGain.Dispose();
    }

    private void CreateEditors()
    {
        if (_editorsCreated) return;
        _editorsCreated = true;

        if (_parentServices?.GetService<IPropertyEditorFactory>() is not { } factory) return;

        Properties.EnsureCapacity(_props.Count);
        foreach (IPropertyAdapter prop in _props)
        {
            if (factory.CreateEditor(prop) is { } editor)
            {
                Properties.Add(editor);
            }
        }

        if (_props.FirstOrDefault()?.GetEngineProperty()?.GetOwnerObject() is not CompressorEffect compressor)
            return;

        ThresholdEditor = FindNumberEditor(compressor.Threshold);
        RatioEditor = FindNumberEditor(compressor.Ratio);
        NumberEditorViewModel<float>? kneeEditor = FindNumberEditor(compressor.Knee);
        NumberEditorViewModel<float>? makeupEditor = FindNumberEditor(compressor.MakeupGain);

        Subscribe(ThresholdEditor, FindAdapter(compressor.Threshold), Threshold, DefaultThresholdDb);
        Subscribe(RatioEditor, FindAdapter(compressor.Ratio), Ratio, DefaultRatio);
        Subscribe(kneeEditor, FindAdapter(compressor.Knee), Knee, DefaultKneeDb);
        Subscribe(makeupEditor, FindAdapter(compressor.MakeupGain), MakeupGain, DefaultMakeupGainDb);
    }

    private NumberEditorViewModel<float>? FindNumberEditor(IProperty property)
        => Properties.OfType<NumberEditorViewModel<float>>()
            .FirstOrDefault(editor => ReferenceEquals(editor.PropertyAdapter.GetEngineProperty(), property));

    private IPropertyAdapter<float>? FindAdapter(IProperty property)
        => _props.OfType<IPropertyAdapter<float>>()
            .FirstOrDefault(adapter => ReferenceEquals(adapter.GetEngineProperty(), property));

    // A non-finite animated value would break the curve's geometry; draw its fallback instead, as the
    // node falls back to the declared defaults. An extension can replace a number's editor, so fall
    // back to the adapter's own value there: the curve still reads even if only the rows can edit it.
    private void Subscribe(
        NumberEditorViewModel<float>? editor, IPropertyAdapter<float>? adapter,
        ReactivePropertySlim<float> target, float fallback)
    {
        if (editor is not null)
        {
            editor.Value
                .Subscribe(value => target.Value = FiniteOr(value, fallback))
                .AddTo(_subscriptions);
        }
        else if (adapter is not null)
        {
            adapter.GetObservable()
                .Subscribe(value => target.Value = FiniteOr(value, fallback))
                .AddTo(_subscriptions);
        }
        else
        {
            target.Value = fallback;
        }
    }

    private static float FiniteOr(float? value, float fallback)
        => value is { } v && float.IsFinite(v) ? v : fallback;
}
