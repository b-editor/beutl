using System.Text.Json.Nodes;
using Beutl.Controls.Curves;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.CurvesTab.ViewModels;

public sealed class CurvesTabViewModel : EffectToolTabViewModel<Curves>
{
    private readonly IPreviewPlayer _player;

    public CurvesTabViewModel(IEditorContext editorContext)
        : base(editorContext, Strings.Curves)
    {
        _player = editorContext.GetService<IPreviewPlayer>()!;

        SourceBitmap.Value = _player.PreviewImage.Value;

        _player.AfterRendered.CombineLatest(IsSelected)
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (!IsSelected.Value) return;

                SourceBitmap.Value = _player.PreviewImage.Value;
                UpdateHistogramForCurrentGroup();
            })
            .DisposeWith(Disposables);

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
            .DisposeWith(Disposables);

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
            .DisposeWith(Disposables);

        ShowCustom = ObserveGroupVisibility(CurveGroup.Custom, initialValue: true);

        SelectedChannelItem
            .Where(v => v != null)
            .Select(v => v!.Channel)
            .Subscribe(channel => SelectedChannel.Value = channel)
            .DisposeWith(Disposables);

        SelectedChannel
            .Subscribe(channel =>
            {
                CustomCurveChannelItem? item = CustomCurveChannels.FirstOrDefault(v => v.Channel == channel);
                if (!EqualityComparer<CustomCurveChannelItem?>.Default.Equals(SelectedChannelItem.Value, item))
                {
                    SelectedChannelItem.Value = item;
                }
            })
            .DisposeWith(Disposables);

        ShowMasterCurve = ObserveChannelVisibility(CustomCurveChannel.Master, initialValue: true);
        ShowRedCurve = ObserveChannelVisibility(CustomCurveChannel.Red);
        ShowGreenCurve = ObserveChannelVisibility(CustomCurveChannel.Green);
        ShowBlueCurve = ObserveChannelVisibility(CustomCurveChannel.Blue);

        ShowHueVsHue = ObserveGroupVisibility(CurveGroup.HueVsHue);
        ShowHueVsSaturation = ObserveGroupVisibility(CurveGroup.HueVsSaturation);
        ShowHueVsLuminance = ObserveGroupVisibility(CurveGroup.HueVsLuminance);
        ShowLuminanceVsSaturation = ObserveGroupVisibility(CurveGroup.LuminanceVsSaturation);
        ShowSaturationVsSaturation = ObserveGroupVisibility(CurveGroup.SaturationVsSaturation);
    }

    private ReadOnlyReactivePropertySlim<bool> ObserveGroupVisibility(CurveGroup group, bool initialValue = false)
    {
        return SelectedGroup
            .Select(x => x == group)
            .ToReadOnlyReactivePropertySlim(initialValue)
            .DisposeWith(Disposables)!;
    }

    // The custom curves share one panel; each channel shows only while that panel does.
    private ReadOnlyReactivePropertySlim<bool> ObserveChannelVisibility(CustomCurveChannel channel, bool initialValue = false)
    {
        return ShowCustom
            .CombineLatest(SelectedChannel, (showCustom, selected) => showCustom && selected == channel)
            .ToReadOnlyReactivePropertySlim(initialValue)
            .DisposeWith(Disposables)!;
    }

    public override ToolTabExtension Extension => CurvesTabExtension.Instance;

    public IReadOnlyList<CurveGroupItem> CurveGroups { get; }

    public IReadOnlyList<CustomCurveChannelItem> CustomCurveChannels { get; }

    public ReactivePropertySlim<Ref<Bitmap>?> SourceBitmap { get; } = new();

    internal CurveVisualizationRenderer Renderer { get; } = new();

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

    public override void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue("selectedGroup", out int selectedGroup))
        {
            SelectedGroup.Value = (CurveGroup)selectedGroup;
        }

        if (json.TryGetPropertyValueAsJsonValue("selectedChannel", out int selectedChannel))
        {
            SelectedChannel.Value = (CustomCurveChannel)selectedChannel;
        }

        ReadEffectFromJson(json);
        ReadPinFromJson(json);
    }

    public override void WriteToJson(JsonObject json)
    {
        json["selectedGroup"] = (int)SelectedGroup.Value;
        json["selectedChannel"] = (int)SelectedChannel.Value;
        WriteEffectToJson(json);
        WritePinToJson(json);
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

    private protected override void SetEditors(Curves? effect)
    {
        ClearEditors();

        if (effect == null)
            return;

        var history = EditorContext.GetService<HistoryManager>()!;

        MasterCurve.Value = CreateCurve(effect.MasterCurve, history);
        RedCurve.Value = CreateCurve(effect.RedCurve, history);
        GreenCurve.Value = CreateCurve(effect.GreenCurve, history);
        BlueCurve.Value = CreateCurve(effect.BlueCurve, history);
        HueVsHue.Value = CreateCurve(effect.HueVsHue, history);
        HueVsSaturation.Value = CreateCurve(effect.HueVsSaturation, history);
        HueVsLuminance.Value = CreateCurve(effect.HueVsLuminance, history);
        LuminanceVsSaturation.Value = CreateCurve(effect.LuminanceVsSaturation, history);
        SaturationVsSaturation.Value = CreateCurve(effect.SaturationVsSaturation, history);

        CloseWhenDetached(effect);
    }

    private CurvePresenterViewModel CreateCurve(IProperty<CurveMap> property, HistoryManager history)
    {
        var vm = new CurvePresenterViewModel(property, history);
        EffectDisposables.Add(vm);
        return vm;
    }

    private protected override void ClearEditors()
    {
        EffectDisposables.Clear();

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
