using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ColorGradingTab.ViewModels;

public record ColorGradingWheelMode(string Name, int Value)
{
    public static readonly ColorGradingWheelMode ShadowsMidtonesHighlights =
        new($"{GraphicsStrings.ColorGrading_Shadows} / {GraphicsStrings.ColorGrading_Midtones} / {GraphicsStrings.ColorGrading_Highlights}", 0);

    public static readonly ColorGradingWheelMode LiftGammaGainOffset =
        new($"{GraphicsStrings.ColorGrading_Lift} / {GraphicsStrings.ColorGrading_Gamma} / {GraphicsStrings.ColorGrading_Gain} / {GraphicsStrings.ColorGrading_Offset}", 1);
}

public sealed class ColorGradingTabViewModel : EffectToolTabViewModel<ColorGrading>, IPropertyEditorContextVisitor
{
    public ColorGradingTabViewModel(IEditorContext editorContext)
        : base(editorContext, Strings.ColorGrading)
    {
        IsLogMode = WheelMode
            .Select(v => v == ColorGradingWheelMode.ShadowsMidtonesHighlights)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;

        IsLiftGammaGainMode = WheelMode
            .Select(v => v == ColorGradingWheelMode.LiftGammaGainOffset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;
    }

    public override ToolTabExtension Extension => ColorGradingTabExtension.Instance;

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

    public override void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue("wheelMode", out int mode))
        {
            WheelMode.Value = AvailableWheelModes.FirstOrDefault(i => i.Value == mode) ?? WheelMode.Value;
        }

        ReadEffectFromJson(json);

        if (json.TryGetPropertyValueAsJsonValue("isNumberEditorsVisible", out bool isVisible))
        {
            IsNumberEditorsVisible.Value = isVisible;
        }

        ReadPinFromJson(json);
    }

    public override void WriteToJson(JsonObject json)
    {
        json["wheelMode"] = WheelMode.Value.Value;
        WriteEffectToJson(json);
        json["isNumberEditorsVisible"] = IsNumberEditorsVisible.Value;
        WritePinToJson(json);
    }

    private protected override void SetEditors(ColorGrading? effect)
    {
        ClearEditors();

        if (effect == null)
            return;

        var factory = EditorContext.GetService<IPropertyEditorFactory>();
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

        CloseWhenDetached(effect);
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
            EffectDisposables.Add(ctx);
        }
        return ctx;
    }

    private protected override void ClearEditors()
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

        EffectDisposables.Clear();
    }
}
