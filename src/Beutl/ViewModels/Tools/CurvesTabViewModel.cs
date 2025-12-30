using System.Text.Json.Nodes;

using Beutl.Controls.Curves;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Avalonia.Media.Imaging;
using Beutl.Editor;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

public enum CurveGroup
{
    Custom,
    HueVsHue,
    HueVsSaturation,
    HueVsLuminance,
    LuminanceVsSaturation,
    SaturationVsSaturation,
}

public enum CustomCurveChannel
{
    Master,
    Red,
    Green,
    Blue,
}

public record CurveGroupItem(CurveGroup Group, string DisplayName);

public record CustomCurveChannelItem(CustomCurveChannel Channel, string DisplayName);

public sealed class CurvesTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _effectDisposables = [];
    private readonly EditViewModel _editViewModel;

    public CurvesTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        SourceBitmap.Value = editViewModel.Player.PreviewImage.Value as WriteableBitmap;

        Effect.Subscribe(SetEditors)
            .DisposeWith(_disposables);

        editViewModel.Player.AfterRendered.CombineLatest(IsSelected)
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (!IsSelected.Value) return;

                var bitmap = editViewModel.Player.PreviewImage.Value as WriteableBitmap;
                if (ReferenceEquals(SourceBitmap.Value, bitmap))
                {
                    SourceBitmap.Value = null;
                }

                SourceBitmap.Value = bitmap;
                UpdateHistogramForCurrentGroup();
            })
            .DisposeWith(_disposables);

        HasCurves = Effect
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        IsCurvesMissing = HasCurves
            .Select(v => !v)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        CurveGroups =
        [
            new CurveGroupItem(CurveGroup.Custom, Strings.CustomCurve),
            new CurveGroupItem(CurveGroup.HueVsHue, Strings.HueVsHue),
            new CurveGroupItem(CurveGroup.HueVsSaturation, Strings.HueVsSaturation),
            new CurveGroupItem(CurveGroup.HueVsLuminance, Strings.HueVsLuminance),
            new CurveGroupItem(CurveGroup.LuminanceVsSaturation, Strings.LuminanceVsSaturation),
            new CurveGroupItem(CurveGroup.SaturationVsSaturation, Strings.SaturationVsSaturation),
        ];

        CustomCurveChannels =
        [
            new CustomCurveChannelItem(CustomCurveChannel.Master, Strings.Master),
            new CustomCurveChannelItem(CustomCurveChannel.Red, Strings.Red),
            new CustomCurveChannelItem(CustomCurveChannel.Green, Strings.Green),
            new CustomCurveChannelItem(CustomCurveChannel.Blue, Strings.Blue),
        ];

        SelectedGroupItem = new ReactivePropertySlim<CurveGroupItem?>(CurveGroups.FirstOrDefault());
        SelectedChannelItem = new ReactivePropertySlim<CustomCurveChannelItem?>(CustomCurveChannels.FirstOrDefault());

        SelectedGroupItem
            .Where(v => v != null)
            .Select(v => v!.Group)
            .Subscribe(group => SelectedGroup.Value = group)
            .DisposeWith(_disposables);

        SelectedGroup
            .Subscribe(group =>
            {
                CurveGroupItem? item = CurveGroups.FirstOrDefault(v => v.Group == group);
                if (!EqualityComparer<CurveGroupItem?>.Default.Equals(SelectedGroupItem.Value, item))
                {
                    SelectedGroupItem.Value = item;
                }

                UpdateHistogramForCurrentGroup();
            })
            .DisposeWith(_disposables);

        ShowCustom = SelectedGroup
            .Select(x => x == CurveGroup.Custom)
            .ToReadOnlyReactivePropertySlim(initialValue: true)
            .DisposeWith(_disposables)!;

        SelectedChannelItem
            .Where(v => v != null)
            .Select(v => v!.Channel)
            .Subscribe(channel => SelectedChannel.Value = channel)
            .DisposeWith(_disposables);

        SelectedChannel
            .Subscribe(channel =>
            {
                CustomCurveChannelItem? item = CustomCurveChannels.FirstOrDefault(v => v.Channel == channel);
                if (!EqualityComparer<CustomCurveChannelItem?>.Default.Equals(SelectedChannelItem.Value, item))
                {
                    SelectedChannelItem.Value = item;
                }
            })
            .DisposeWith(_disposables);

        ShowMasterCurve = ShowCustom
            .CombineLatest(SelectedChannel, (showCustom, channel) => showCustom && channel == CustomCurveChannel.Master)
            .ToReadOnlyReactivePropertySlim(initialValue: true)
            .DisposeWith(_disposables)!;

        ShowRedCurve = ShowCustom
            .CombineLatest(SelectedChannel, (showCustom, channel) => showCustom && channel == CustomCurveChannel.Red)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowGreenCurve = ShowCustom
            .CombineLatest(SelectedChannel, (showCustom, channel) => showCustom && channel == CustomCurveChannel.Green)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowBlueCurve = ShowCustom
            .CombineLatest(SelectedChannel, (showCustom, channel) => showCustom && channel == CustomCurveChannel.Blue)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowHueVsHue = SelectedGroup
            .Select(x => x == CurveGroup.HueVsHue)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowHueVsSaturation = SelectedGroup
            .Select(x => x == CurveGroup.HueVsSaturation)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowHueVsLuminance = SelectedGroup
            .Select(x => x == CurveGroup.HueVsLuminance)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowLuminanceVsSaturation = SelectedGroup
            .Select(x => x == CurveGroup.LuminanceVsSaturation)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        ShowSaturationVsSaturation = SelectedGroup
            .Select(x => x == CurveGroup.SaturationVsSaturation)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
    }

    public string Header => Strings.Curves;

    public ToolTabExtension Extension => CurvesTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public ReadOnlyReactivePropertySlim<bool> HasCurves { get; }

    public ReadOnlyReactivePropertySlim<bool> IsCurvesMissing { get; }

    public IReadOnlyList<CurveGroupItem> CurveGroups { get; }

    public IReadOnlyList<CustomCurveChannelItem> CustomCurveChannels { get; }

    public ReactivePropertySlim<WriteableBitmap?> SourceBitmap { get; } = new();

    public CurveVisualizationRenderer Renderer { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> MasterCurve { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> RedCurve { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> GreenCurve { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> BlueCurve { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> HueVsHue { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> HueVsSaturation { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> HueVsLuminance { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> LuminanceVsSaturation { get; } = new();

    public ReactivePropertySlim<CurvePresenterViewModel?> SaturationVsSaturation { get; } = new();

    public ReactivePropertySlim<CurveGroupItem?> SelectedGroupItem { get; }

    public ReactivePropertySlim<CurveGroup> SelectedGroup { get; } = new(CurveGroup.Custom);

    public ReactivePropertySlim<CustomCurveChannelItem?> SelectedChannelItem { get; }

    public ReactivePropertySlim<CustomCurveChannel> SelectedChannel { get; } = new(CustomCurveChannel.Master);

    public ReadOnlyReactivePropertySlim<bool> ShowCustom { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowMasterCurve { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowRedCurve { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowGreenCurve { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowBlueCurve { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowHueVsHue { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowHueVsSaturation { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowHueVsLuminance { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowLuminanceVsSaturation { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowSaturationVsSaturation { get; }

    public ReactivePropertySlim<Curves?> Effect { get; } = new();

    public void Dispose()
    {
        ClearEditors();
        _disposables.Dispose();
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("selectedGroup", out var selectedGroupNode)
            && selectedGroupNode is JsonValue selectedGroupValue
            && selectedGroupValue.TryGetValue(out int selectedGroup))
        {
            SelectedGroup.Value = (CurveGroup)selectedGroup;
        }

        if (json.TryGetPropertyValue("selectedChannel", out var selectedChannelNode)
            && selectedChannelNode is JsonValue selectedChannelValue
            && selectedChannelValue.TryGetValue(out int selectedChannel))
        {
            SelectedChannel.Value = (CustomCurveChannel)selectedChannel;
        }

        if (json.TryGetPropertyValue("effectId", out var effectIdNode)
            && effectIdNode is JsonValue effectIdValue
            && effectIdValue.TryGetValue(out string? effectIdStr)
            && Guid.TryParse(effectIdStr, out Guid effectId))
        {
            var colorGrading = _editViewModel.Scene.FindById(effectId) as Curves;
            Effect.Value = colorGrading;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json["selectedGroup"] = (int)SelectedGroup.Value;
        json["selectedChannel"] = (int)SelectedChannel.Value;
        json["effectId"] = Effect.Value?.Id;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(EditViewModel))
            return _editViewModel;

        if (serviceType == typeof(HistoryManager))
            return _editViewModel.HistoryManager;

        if (serviceType == typeof(Element))
            return Effect.Value?.FindHierarchicalParent<Element>();

        if (serviceType == typeof(Curves))
            return Effect.Value;

        if (serviceType == typeof(Scene))
            return _editViewModel.Scene;

        return _editViewModel.GetService(serviceType);
    }

    public void SelectCurveByPropertyName(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(Curves.MasterCurve):
                SelectedGroup.Value = CurveGroup.Custom;
                SelectedChannel.Value = CustomCurveChannel.Master;
                break;
            case nameof(Curves.RedCurve):
                SelectedGroup.Value = CurveGroup.Custom;
                SelectedChannel.Value = CustomCurveChannel.Red;
                break;
            case nameof(Curves.GreenCurve):
                SelectedGroup.Value = CurveGroup.Custom;
                SelectedChannel.Value = CustomCurveChannel.Green;
                break;
            case nameof(Curves.BlueCurve):
                SelectedGroup.Value = CurveGroup.Custom;
                SelectedChannel.Value = CustomCurveChannel.Blue;
                break;
            case nameof(Curves.HueVsHue):
                SelectedGroup.Value = CurveGroup.HueVsHue;
                break;
            case nameof(Curves.HueVsSaturation):
                SelectedGroup.Value = CurveGroup.HueVsSaturation;
                break;
            case nameof(Curves.HueVsLuminance):
                SelectedGroup.Value = CurveGroup.HueVsLuminance;
                break;
            case nameof(Curves.LuminanceVsSaturation):
                SelectedGroup.Value = CurveGroup.LuminanceVsSaturation;
                break;
            case nameof(Curves.SaturationVsSaturation):
                SelectedGroup.Value = CurveGroup.SaturationVsSaturation;
                break;
        }
    }

    private void SetEditors(Curves? effect)
    {
        ClearEditors();

        if (effect == null)
            return;

        MasterCurve.Value = CreateCurve(effect.MasterCurve);
        RedCurve.Value = CreateCurve(effect.RedCurve);
        GreenCurve.Value = CreateCurve(effect.GreenCurve);
        BlueCurve.Value = CreateCurve(effect.BlueCurve);
        HueVsHue.Value = CreateCurve(effect.HueVsHue);
        HueVsSaturation.Value = CreateCurve(effect.HueVsSaturation);
        HueVsLuminance.Value = CreateCurve(effect.HueVsLuminance);
        LuminanceVsSaturation.Value = CreateCurve(effect.LuminanceVsSaturation);
        SaturationVsSaturation.Value = CreateCurve(effect.SaturationVsSaturation);

        effect.DetachedFromHierarchy += OnEffectDetached;
        _effectDisposables.Add(Disposable.Create(() => effect.DetachedFromHierarchy -= OnEffectDetached));
    }

    private void OnEffectDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        Effect.Value = null;
    }

    private CurvePresenterViewModel CreateCurve(IProperty<CurveMap> property)
    {
        var vm = new CurvePresenterViewModel(property.Name, Effect.Value!, property, _editViewModel.HistoryManager);
        _effectDisposables.Add(vm);
        return vm;
    }

    private void ClearEditors()
    {
        _effectDisposables.Clear();

        MasterCurve.Value = null;
        RedCurve.Value = null;
        GreenCurve.Value = null;
        BlueCurve.Value = null;
        HueVsHue.Value = null;
        HueVsSaturation.Value = null;
        HueVsLuminance.Value = null;
        LuminanceVsSaturation.Value = null;
        SaturationVsSaturation.Value = null;
    }

    private void UpdateHistogramForCurrentGroup()
    {
        HistogramCategory requiredCategory = GetRequiredCategory(SelectedGroup.Value);
        Renderer.UpdateHistogram(SourceBitmap.Value, requiredCategory);
    }

    private static HistogramCategory GetRequiredCategory(CurveGroup group)
    {
        return group switch
        {
            CurveGroup.Custom => HistogramCategory.Rgb,
            CurveGroup.HueVsHue => HistogramCategory.Hue,
            CurveGroup.HueVsSaturation => HistogramCategory.Hue,
            CurveGroup.HueVsLuminance => HistogramCategory.Hue,
            CurveGroup.LuminanceVsSaturation => HistogramCategory.Luminance,
            CurveGroup.SaturationVsSaturation => HistogramCategory.Saturation,
            _ => HistogramCategory.None
        };
    }
}
