using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Configuration;

public enum LibraryTabDisplayMode
{
    Show,
    Hide
}

public enum FrameCacheConfigScale
{
    Original,

    FitToPreviewer,

    Half,

    Quarter,
}

public enum FrameCacheConfigColorType
{
    RGBA,

    YUV
}

public enum TimelineAutoScrollMode
{
    None,

    AlwaysFollow,

    PageScroll
}

// The numbers are what settings files hold. Auto took 0 from None, the default before Auto existed, so a preview left at
// that default follows its display.
public enum UIToneMappingOperator
{
    // FitToDisplay on a preview that shows HDR, None on any other.
    Auto = 0,
    Reinhard = 1,
    ACES = 2,
    Hable = 3,

    // Rolls off only what the display cannot show, keeping hue, instead of clipping each channel.
    FitToDisplay = 4,

    // Clips each channel at what the display shows.
    None = 5
}

public static class UIToneMappingOperatorExtensions
{
    /// <summary>
    /// The operator a preview applies for <paramref name="mode"/>: <see cref="UIToneMappingOperator.Auto"/> is
    /// <see cref="UIToneMappingOperator.FitToDisplay"/> where the preview shows HDR and
    /// <see cref="UIToneMappingOperator.None"/> where it does not, and any other mode is itself.
    /// </summary>
    public static UIToneMappingOperator Resolve(this UIToneMappingOperator mode, bool showsHdr)
    {
        if (mode != UIToneMappingOperator.Auto)
            return mode;

        return showsHdr ? UIToneMappingOperator.FitToDisplay : UIToneMappingOperator.None;
    }
}

public sealed partial class EditorConfig : ConfigurationBase
{
    // The memory the GC may use: the physical memory, or a container's or job's limit when one is set. Half of it.
    private static readonly double s_defaultFrameCacheMaxSize = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024d) / 2;

    public EditorConfig()
    {
        ScanProperties<EditorConfig>();
        LibraryTabDisplayModes.CollectionChanged += (_, _) => OnChanged();
    }

    public IProperty<string?> LastMediaDirectory { get; } = Property.CreateValue<string?>(null);

    public IProperty<bool> AutoAdjustSceneDuration { get; } = Property.CreateValue(true);

    public IProperty<bool> EnablePointerLockInProperty { get; } = Property.CreateValue(true);

    [Display(Name = nameof(SettingsStrings.EnableFrameCache), ResourceType = typeof(SettingsStrings))]
    public IProperty<bool> IsFrameCacheEnabled { get; } = Property.CreateValue(false);

    [Range(0.0, double.MaxValue)]
    [Display(Name = nameof(SettingsStrings.FrameCacheMaxSize), ResourceType = typeof(SettingsStrings))]
    public IProperty<double> FrameCacheMaxSize { get; } = Property.CreateValue(s_defaultFrameCacheMaxSize);

    [Display(Name = nameof(SettingsStrings.FrameCacheScale), ResourceType = typeof(SettingsStrings))]
    public IProperty<FrameCacheConfigScale> FrameCacheScale { get; } = Property.CreateValue(FrameCacheConfigScale.FitToPreviewer);

    [Display(Name = nameof(SettingsStrings.FrameCacheColorType), ResourceType = typeof(SettingsStrings))]
    public IProperty<FrameCacheConfigColorType> FrameCacheColorType { get; } = Property.CreateValue(FrameCacheConfigColorType.RGBA);

    [Display(Name = nameof(SettingsStrings.EnableNodeCache), ResourceType = typeof(SettingsStrings))]
    public IProperty<bool> IsNodeCacheEnabled { get; } = Property.CreateValue(false);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(SettingsStrings.NodeCacheMaxPixels), ResourceType = typeof(SettingsStrings))]
    public IProperty<int> NodeCacheMaxPixels { get; } = Property.CreateValue(1000 * 1000);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(SettingsStrings.NodeCacheMinPixels), ResourceType = typeof(SettingsStrings))]
    public IProperty<int> NodeCacheMinPixels { get; } = Property.CreateValue(1);

    public IProperty<bool> SwapTimelineScrollDirection { get; } = Property.CreateValue(false);

    public IProperty<bool> SeekOnTimelineClick { get; } = Property.CreateValue(true);

    public IProperty<bool> ClampResizeToOriginalLength { get; } = Property.CreateValue(true);

    // Off leaves clips without a filmstrip or waveform unless one is turned on for that clip, which keeps
    // zooming a timeline full of media from decoding thumbnails.
    public IProperty<bool> ShowTimelineThumbnailsByDefault { get; } = Property.CreateValue(true);

    public IProperty<bool> IsTimelineSnapEnabled { get; } = Property.CreateValue(true);

    public IProperty<bool> IsRippleEnabled { get; } = Property.CreateValue(false);

    public IProperty<TimelineAutoScrollMode> TimelineAutoScrollMode { get; } = Property.CreateValue(Configuration.TimelineAutoScrollMode.PageScroll);

    public IProperty<UIToneMappingOperator> ToneMappingMode { get; } = Property.CreateValue(UIToneMappingOperator.Auto);

    public IProperty<float> ToneMappingExposure { get; } = Property.CreateValue(0f);

    public IProperty<bool> UseHdrPreview { get; } = Property.CreateValue(false);

    // The peak in nit that FitToDisplay rolls highlights off into on a Windows HDR display, in place of the one DXGI
    // reports, which mostly comes from the EDID and can be far from the panel's or missing. 0 uses the reported one.
    [Range(0.0, 10000.0)]
    public IProperty<float> DisplayPeakLuminance { get; } = Property.CreateValue(0f);

    [Display(Name = nameof(Strings.OnionSkin), ResourceType = typeof(Strings))]
    public IProperty<bool> IsOnionSkinEnabled { get; } = Property.CreateValue(false);

    [Range(0, 10)]
    [Display(Name = nameof(Strings.OnionSkinPrevCount), ResourceType = typeof(Strings))]
    public IProperty<int> OnionSkinPrevCount { get; } = Property.CreateValue(1);

    [Range(0, 10)]
    [Display(Name = nameof(Strings.OnionSkinNextCount), ResourceType = typeof(Strings))]
    public IProperty<int> OnionSkinNextCount { get; } = Property.CreateValue(0);

    [Range(0.0, 1.0)]
    [Display(Name = nameof(Strings.OnionSkinPrevOpacity), ResourceType = typeof(Strings))]
    public IProperty<float> OnionSkinPrevOpacity { get; } = Property.CreateValue(0.35f);

    [Range(0.0, 1.0)]
    [Display(Name = nameof(Strings.OnionSkinNextOpacity), ResourceType = typeof(Strings))]
    public IProperty<float> OnionSkinNextOpacity { get; } = Property.CreateValue(0.35f);

    [Display(Name = nameof(Strings.PreviewSource), ResourceType = typeof(Strings))]
    public IProperty<PreviewSourceMode> PreviewSourceMode { get; } = Property.CreateValue(Configuration.PreviewSourceMode.PreferProxy);

    // Shows or hides every guide in PreviewGuideKinds at once, which is what the editor's guide toggle flips.
    public IProperty<bool> IsPreviewGuideEnabled { get; } = Property.CreateValue(false);

    public IProperty<PreviewGuideKinds> PreviewGuideKinds { get; } = Property.CreateValue(
        Configuration.PreviewGuideKinds.TitleSafe | Configuration.PreviewGuideKinds.ActionSafe | Configuration.PreviewGuideKinds.CenterCross);

    // #AARRGGBB, read with Avalonia's Color.TryParse; one that does not parse draws the guides white.
    public IProperty<string> PreviewGuideColor { get; } = Property.CreateValue("#FFFFFFFF");

    [Range(0.0, 1.0)]
    public IProperty<float> PreviewGuideOpacity { get; } = Property.CreateValue(0.8f);

    // The most columns or rows the custom grid divides the frame into.
    public const int PreviewGuideGridMaxCells = 64;

    [Range(1, PreviewGuideGridMaxCells)]
    public IProperty<int> PreviewGuideGridColumns { get; } = Property.CreateValue(4);

    [Range(1, PreviewGuideGridMaxCells)]
    public IProperty<int> PreviewGuideGridRows { get; } = Property.CreateValue(4);

    // A list PreviewGuideAspectRatio.TryParseList reads, such as "9:16, 1:1".
    public IProperty<string> PreviewGuideAspectRatios { get; } = Property.CreateValue("9:16, 1:1, 4:5, 2.39:1");

    // Shows the preview's transparent parts over a checkerboard instead of black. Only the editor's preview draws it: the
    // presentation window, exports and saved images keep what the frame holds (#4066).
    public IProperty<bool> IsPreviewCheckerboardEnabled { get; } = Property.CreateValue(false);

    /// <summary>Sets <see cref="PreviewSourceMode"/> from a <c>ComboBox.SelectedIndex</c>.</summary>
    /// <remarks>Out-of-range indices, such as the <c>-1</c> of a transient no-selection, are ignored.</remarks>
    public void SetPreviewSourceModeFromIndex(int index)
    {
        if (Enum.IsDefined(typeof(PreviewSourceMode), index))
            PreviewSourceMode.Value = (PreviewSourceMode)index;
    }

    // The order the settings page lists the tone mapping modes in: Auto first, then the others as they were listed before
    // Auto existed. It differs from the modes' numbers, which settings files hold.
    private static readonly UIToneMappingOperator[] s_toneMappingModeOrder =
    [
        UIToneMappingOperator.Auto,
        UIToneMappingOperator.None,
        UIToneMappingOperator.Reinhard,
        UIToneMappingOperator.ACES,
        UIToneMappingOperator.Hable,
        UIToneMappingOperator.FitToDisplay
    ];

    /// <summary>The <c>ComboBox.SelectedIndex</c> that shows <paramref name="mode"/>, or -1 for none.</summary>
    public static int GetToneMappingModeIndex(UIToneMappingOperator mode)
    {
        return Array.IndexOf(s_toneMappingModeOrder, mode);
    }

    /// <summary>Sets <see cref="ToneMappingMode"/> from a <c>ComboBox.SelectedIndex</c>.</summary>
    /// <remarks>Out-of-range indices, such as the <c>-1</c> of a transient no-selection, are ignored.</remarks>
    public void SetToneMappingModeFromIndex(int index)
    {
        if ((uint)index < (uint)s_toneMappingModeOrder.Length)
            ToneMappingMode.Value = s_toneMappingModeOrder[index];
    }

    public CoreDictionary<string, LibraryTabDisplayMode> LibraryTabDisplayModes { get; } = new()
    {
        ["Search"] = LibraryTabDisplayMode.Show,
        ["Easings"] = LibraryTabDisplayMode.Show,
        ["Library"] = LibraryTabDisplayMode.Show,
        ["Effects"] = LibraryTabDisplayMode.Show,
        ["Transitions"] = LibraryTabDisplayMode.Show,
        ["Nodes"] = LibraryTabDisplayMode.Hide,
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue(nameof(LibraryTabDisplayModes), LibraryTabDisplayModes);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (TryReadValue(context, nameof(LibraryTabDisplayModes), out Dictionary<string, LibraryTabDisplayMode>? items)
            && items != null)
        {
            // A tab introduced after this config was written has no saved entry, and the
            // view binds per tab id — without the default carried over it resolves nothing.
            var defaults = new Dictionary<string, LibraryTabDisplayMode>(LibraryTabDisplayModes);

            LibraryTabDisplayModes.Clear();
            foreach (KeyValuePair<string, LibraryTabDisplayMode> item in items)
            {
                LibraryTabDisplayModes.TryAdd(item.Key, item.Value);
            }

            foreach (KeyValuePair<string, LibraryTabDisplayMode> item in defaults)
            {
                LibraryTabDisplayModes.TryAdd(item.Key, item.Value);
            }
        }
    }
}
