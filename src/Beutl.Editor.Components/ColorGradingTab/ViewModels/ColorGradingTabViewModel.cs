using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ColorGradingTab.ViewModels;

public record ColorGradingWheelMode(string Name, int Value)
{
    public static readonly ColorGradingWheelMode ShadowsMidtonesHighlights =
        new($"{Strings.Shadows} / {Strings.Midtones} / {Strings.Highlights}", 0);

    public static readonly ColorGradingWheelMode LiftGammaGainOffset =
        new($"{Strings.Lift} / {Strings.Gamma} / {Strings.Gain} / {Strings.Offset}", 1);
}

public sealed class ColorGradingTabViewModel : IToolContext, IPropertyEditorContextVisitor
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _effectDisposables = [];

    public ColorGradingTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;

        Effect.Subscribe(SetEditors)
            .DisposeWith(_disposables);

        HasColorGrading = Effect
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        IsLogMode = WheelMode
            .Select(v => v == ColorGradingWheelMode.ShadowsMidtonesHighlights)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        IsLiftGammaGainMode = WheelMode
            .Select(v => v == ColorGradingWheelMode.LiftGammaGainOffset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        IsColorGradingMissing = HasColorGrading
            .Select(v => !v)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
    }

    public string Header => Strings.ColorGrading;

    public ToolTabExtension Extension => ColorGradingTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public ReadOnlyReactivePropertySlim<bool> HasColorGrading { get; }

    public ReactivePropertySlim<ColorGrading?> Effect { get; } = new();

    public ReactivePropertySlim<ColorGradingWheelMode> WheelMode { get; } =
        new(ColorGradingWheelMode.ShadowsMidtonesHighlights);

    public ReactivePropertySlim<bool> IsNumberEditorsVisible { get; } = new(true);

    public ColorGradingWheelMode[] AvailableWheelModes { get; } =
    [
        ColorGradingWheelMode.ShadowsMidtonesHighlights,
        ColorGradingWheelMode.LiftGammaGainOffset
    ];

    public ReadOnlyReactivePropertySlim<bool> IsLogMode { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLiftGammaGainMode { get; }

    public ReadOnlyReactivePropertySlim<bool> IsColorGradingMissing { get; }

    public ReactivePropertySlim<IPropertyEditorContext?> TemperatureEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> TintEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> ExposureEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> ContrastEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> ContrastPivotEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> SaturationEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> VibranceEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> HueEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> LowRangeEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> HighRangeEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> ShadowsEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> MidtonesEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> HighlightsEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> LiftEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> GammaEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> GainEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> OffsetEditor { get; } = new();

    public void Visit(IPropertyEditorContext context)
    {
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Effect.Value?.FindHierarchicalParent<Element>();

        if (serviceType == typeof(ColorGrading))
            return Effect.Value;

        return _editorContext.GetService(serviceType);
    }

    public void Dispose()
    {
        ClearEditors();
        _disposables.Dispose();
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("wheelMode", out var wheelModeNode)
            && wheelModeNode is JsonValue wheelModeValue
            && wheelModeValue.TryGetValue(out int mode))
        {
            WheelMode.Value = AvailableWheelModes.FirstOrDefault(i => i.Value == mode) ?? WheelMode.Value;
        }

        if (json.TryGetPropertyValue("effectId", out var effectIdNode)
            && effectIdNode is JsonValue effectIdValue
            && effectIdValue.TryGetValue(out string? effectIdStr)
            && Guid.TryParse(effectIdStr, out Guid effectId))
        {
            var scene = _editorContext.GetService<Scene>();
            var colorGrading = scene?.FindById(effectId) as ColorGrading;
            Effect.Value = colorGrading;
        }

        if (json.TryGetPropertyValue("isNumberEditorsVisible", out var isNumberEditorsVisibleNode)
            && isNumberEditorsVisibleNode is JsonValue isNumberEditorsVisibleValue
            && isNumberEditorsVisibleValue.TryGetValue(out bool isVisible))
        {
            IsNumberEditorsVisible.Value = isVisible;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json["wheelMode"] = WheelMode.Value.Value;
        json["effectId"] = Effect.Value?.Id;
        json["isNumberEditorsVisible"] = IsNumberEditorsVisible.Value;
    }

    private void SetEditors(ColorGrading? effect)
    {
        ClearEditors();

        if (effect == null)
            return;

        var factory = _editorContext.GetService<IPropertyEditorFactory>();
        if (factory == null)
            return;

        TemperatureEditor.Value = CreateEditor(factory, effect.Temperature, effect);
        TintEditor.Value = CreateEditor(factory, effect.Tint, effect);
        ExposureEditor.Value = CreateEditor(factory, effect.Exposure, effect);
        ContrastEditor.Value = CreateEditor(factory, effect.Contrast, effect);
        ContrastPivotEditor.Value = CreateEditor(factory, effect.ContrastPivot, effect);
        SaturationEditor.Value = CreateEditor(factory, effect.Saturation, effect);
        VibranceEditor.Value = CreateEditor(factory, effect.Vibrance, effect);
        HueEditor.Value = CreateEditor(factory, effect.Hue, effect);
        LowRangeEditor.Value = CreateEditor(factory, effect.LowRange, effect);
        HighRangeEditor.Value = CreateEditor(factory, effect.HighRange, effect);

        ShadowsEditor.Value = CreateEditor(factory, effect.Shadows, effect);
        MidtonesEditor.Value = CreateEditor(factory, effect.Midtones, effect);
        HighlightsEditor.Value = CreateEditor(factory, effect.Highlights, effect);
        LiftEditor.Value = CreateEditor(factory, effect.Lift, effect);
        GammaEditor.Value = CreateEditor(factory, effect.Gamma, effect);
        GainEditor.Value = CreateEditor(factory, effect.Gain, effect);
        OffsetEditor.Value = CreateEditor(factory, effect.Offset, effect);

        effect.DetachedFromHierarchy += OnEffectDetached;
        _effectDisposables.Add(Disposable.Create(() => effect.DetachedFromHierarchy -= OnEffectDetached));
    }

    private IPropertyEditorContext? CreateEditor<T>(IPropertyEditorFactory factory, IProperty<T> property, EngineObject owner)
    {
        if (property is not AnimatableProperty<T> anim)
            return null;

        var adapter = new AnimatablePropertyAdapter<T>(anim, owner);
        var ctx = factory.CreateEditor(adapter);
        if (ctx != null)
        {
            ctx.Accept(this);
            _effectDisposables.Add(ctx);
        }
        return ctx;
    }

    private void OnEffectDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        Effect.Value = null;
    }

    private void ClearEditors()
    {
        TemperatureEditor.Value = null;
        TintEditor.Value = null;
        ExposureEditor.Value = null;
        ContrastEditor.Value = null;
        ContrastPivotEditor.Value = null;
        SaturationEditor.Value = null;
        VibranceEditor.Value = null;
        HueEditor.Value = null;
        LowRangeEditor.Value = null;
        HighRangeEditor.Value = null;

        ShadowsEditor.Value = null;
        MidtonesEditor.Value = null;
        HighlightsEditor.Value = null;
        LiftEditor.Value = null;
        GammaEditor.Value = null;
        GainEditor.Value = null;
        OffsetEditor.Value = null;

        _effectDisposables.Clear();
    }
}
