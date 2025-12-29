using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Editors;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

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
    private readonly EditViewModel _editViewModel;
    private readonly List<BaseEditorViewModel> _editorContexts = [];

    public ColorGradingTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

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

    public ColorGradingWheelMode[] AvailableWheelModes { get; } =
    [
        ColorGradingWheelMode.ShadowsMidtonesHighlights,
        ColorGradingWheelMode.LiftGammaGainOffset
    ];

    public ReadOnlyReactivePropertySlim<bool> IsLogMode { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLiftGammaGainMode { get; }

    public ReadOnlyReactivePropertySlim<bool> IsColorGradingMissing { get; }

    public ReactivePropertySlim<NumberEditorViewModel<float>?> TemperatureEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> TintEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> ExposureEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> ContrastEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> ContrastPivotEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> SaturationEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> VibranceEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> HueEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> LowRangeEditor { get; } = new();

    public ReactivePropertySlim<NumberEditorViewModel<float>?> HighRangeEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> ShadowsEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> MidtonesEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> HighlightsEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> LiftEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> GammaEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> GainEditor { get; } = new();

    public ReactivePropertySlim<GradingColorEditorViewModel?> OffsetEditor { get; } = new();

    public void Visit(IPropertyEditorContext context)
    {
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(EditViewModel))
            return _editViewModel;

        if (serviceType == typeof(HistoryManager))
            return _editViewModel.HistoryManager;

        if (serviceType == typeof(Element))
            return Effect.Value?.FindHierarchicalParent<Element>();

        if (serviceType == typeof(ColorGrading))
            return Effect.Value;

        if (serviceType == typeof(Scene))
            return _editViewModel.Scene;

        return _editViewModel.GetService(serviceType);
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
    }

    public void WriteToJson(JsonObject json)
    {
        json["wheelMode"] = WheelMode.Value.Value;
    }

    private void SetEditors(ColorGrading? effect)
    {
        ClearEditors();

        if (effect == null)
            return;

        TemperatureEditor.Value = CreateNumberEditor(effect.Temperature);
        TintEditor.Value = CreateNumberEditor(effect.Tint);
        ExposureEditor.Value = CreateNumberEditor(effect.Exposure);
        ContrastEditor.Value = CreateNumberEditor(effect.Contrast);
        ContrastPivotEditor.Value = CreateNumberEditor(effect.ContrastPivot);
        SaturationEditor.Value = CreateNumberEditor(effect.Saturation);
        VibranceEditor.Value = CreateNumberEditor(effect.Vibrance);
        HueEditor.Value = CreateNumberEditor(effect.Hue);
        LowRangeEditor.Value = CreateNumberEditor(effect.LowRange);
        HighRangeEditor.Value = CreateNumberEditor(effect.HighRange);

        ShadowsEditor.Value = CreateColorEditor(effect.Shadows);
        MidtonesEditor.Value = CreateColorEditor(effect.Midtones);
        HighlightsEditor.Value = CreateColorEditor(effect.Highlights);
        LiftEditor.Value = CreateColorEditor(effect.Lift);
        GammaEditor.Value = CreateColorEditor(effect.Gamma);
        GainEditor.Value = CreateColorEditor(effect.Gain);
        OffsetEditor.Value = CreateColorEditor(effect.Offset);
    }

    private NumberEditorViewModel<float>? CreateNumberEditor(IProperty<float> property)
    {
        if (property is not AnimatableProperty<float> anim)
            return null;

        var adapter = new AnimatablePropertyAdapter<float>(anim, Effect.Value!);
        var vm = new NumberEditorViewModel<float>(adapter);
        vm.Accept(this);
        _editorContexts.Add(vm);
        return vm;
    }

    private GradingColorEditorViewModel? CreateColorEditor(IProperty<GradingColor> property)
    {
        if (property is not AnimatableProperty<GradingColor> anim)
            return null;

        var adapter = new AnimatablePropertyAdapter<GradingColor>(anim, Effect.Value!);
        var vm = new GradingColorEditorViewModel(adapter);
        vm.Accept(this);
        _editorContexts.Add(vm);
        return vm;
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

        foreach (BaseEditorViewModel item in _editorContexts)
        {
            item.Dispose();
        }

        _editorContexts.Clear();
    }
}
