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

public enum UIToneMappingOperator
{
    None,
    Reinhard,
    ACES,
    Hable
}

public sealed class EditorConfig : ConfigurationBase
{
    // The memory the GC may use: the physical memory, or a container's or job's limit when one is set. Half of it.
    private static readonly double s_defaultFrameCacheMaxSize = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024d) / 2;

    public EditorConfig()
    {
        ScanProperties<EditorConfig>();
        LibraryTabDisplayModes.CollectionChanged += (_, _) => OnChanged();
    }

    public IProperty<string?> LastMediaDirectoryProperty { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<bool> AutoAdjustSceneDurationProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> EnablePointerLockInPropertyProperty { get; } = new ValueOnlyProperty<bool>(true);

    [Display(Name = nameof(SettingsStrings.EnableFrameCache), ResourceType = typeof(SettingsStrings))]
    public IProperty<bool> IsFrameCacheEnabledProperty { get; } = new ValueOnlyProperty<bool>(false);

    [Range(0.0, double.MaxValue)]
    [Display(Name = nameof(SettingsStrings.FrameCacheMaxSize), ResourceType = typeof(SettingsStrings))]
    public IProperty<double> FrameCacheMaxSizeProperty { get; } = new ValueOnlyProperty<double>(s_defaultFrameCacheMaxSize);

    [Display(Name = nameof(SettingsStrings.FrameCacheScale), ResourceType = typeof(SettingsStrings))]
    public IProperty<FrameCacheConfigScale> FrameCacheScaleProperty { get; } = new ValueOnlyProperty<FrameCacheConfigScale>(FrameCacheConfigScale.FitToPreviewer);

    [Display(Name = nameof(SettingsStrings.FrameCacheColorType), ResourceType = typeof(SettingsStrings))]
    public IProperty<FrameCacheConfigColorType> FrameCacheColorTypeProperty { get; } = new ValueOnlyProperty<FrameCacheConfigColorType>(FrameCacheConfigColorType.RGBA);

    [Display(Name = nameof(SettingsStrings.EnableNodeCache), ResourceType = typeof(SettingsStrings))]
    public IProperty<bool> IsNodeCacheEnabledProperty { get; } = new ValueOnlyProperty<bool>(false);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(SettingsStrings.NodeCacheMaxPixels), ResourceType = typeof(SettingsStrings))]
    public IProperty<int> NodeCacheMaxPixelsProperty { get; } = new ValueOnlyProperty<int>(1000 * 1000);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(SettingsStrings.NodeCacheMinPixels), ResourceType = typeof(SettingsStrings))]
    public IProperty<int> NodeCacheMinPixelsProperty { get; } = new ValueOnlyProperty<int>(1);

    public IProperty<bool> SwapTimelineScrollDirectionProperty { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<bool> ClampResizeToOriginalLengthProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> IsTimelineSnapEnabledProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> IsRippleEnabledProperty { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<TimelineAutoScrollMode> TimelineAutoScrollModeProperty { get; } = new ValueOnlyProperty<TimelineAutoScrollMode>(TimelineAutoScrollMode.PageScroll);

    public IProperty<UIToneMappingOperator> ToneMappingModeProperty { get; } = new ValueOnlyProperty<UIToneMappingOperator>(UIToneMappingOperator.None);

    public IProperty<float> ToneMappingExposureProperty { get; } = new ValueOnlyProperty<float>(0f);

    public IProperty<bool> UseHdrPreviewProperty { get; } = new ValueOnlyProperty<bool>(false);

    [Display(Name = nameof(Strings.OnionSkin), ResourceType = typeof(Strings))]
    public IProperty<bool> IsOnionSkinEnabledProperty { get; } = new ValueOnlyProperty<bool>(false);

    [Range(0, 10)]
    [Display(Name = nameof(Strings.OnionSkinPrevCount), ResourceType = typeof(Strings))]
    public IProperty<int> OnionSkinPrevCountProperty { get; } = new ValueOnlyProperty<int>(1);

    [Range(0, 10)]
    [Display(Name = nameof(Strings.OnionSkinNextCount), ResourceType = typeof(Strings))]
    public IProperty<int> OnionSkinNextCountProperty { get; } = new ValueOnlyProperty<int>(0);

    [Range(0.0, 1.0)]
    [Display(Name = nameof(Strings.OnionSkinPrevOpacity), ResourceType = typeof(Strings))]
    public IProperty<float> OnionSkinPrevOpacityProperty { get; } = new ValueOnlyProperty<float>(0.35f);

    [Range(0.0, 1.0)]
    [Display(Name = nameof(Strings.OnionSkinNextOpacity), ResourceType = typeof(Strings))]
    public IProperty<float> OnionSkinNextOpacityProperty { get; } = new ValueOnlyProperty<float>(0.35f);

    [Display(Name = nameof(Strings.PreviewSource), ResourceType = typeof(Strings))]
    public IProperty<PreviewSourceMode> PreviewSourceModeProperty { get; } = new ValueOnlyProperty<PreviewSourceMode>(PreviewSourceMode.PreferProxy);

    public bool AutoAdjustSceneDuration
    {
        get => AutoAdjustSceneDurationProperty.Value;
        set => AutoAdjustSceneDurationProperty.Value = value;
    }

    public bool EnablePointerLockInProperty
    {
        get => EnablePointerLockInPropertyProperty.Value;
        set => EnablePointerLockInPropertyProperty.Value = value;
    }

    public bool IsFrameCacheEnabled
    {
        get => IsFrameCacheEnabledProperty.Value;
        set => IsFrameCacheEnabledProperty.Value = value;
    }

    public double FrameCacheMaxSize
    {
        get => FrameCacheMaxSizeProperty.Value;
        set => FrameCacheMaxSizeProperty.Value = value;
    }

    public FrameCacheConfigScale FrameCacheScale
    {
        get => FrameCacheScaleProperty.Value;
        set => FrameCacheScaleProperty.Value = value;
    }

    public FrameCacheConfigColorType FrameCacheColorType
    {
        get => FrameCacheColorTypeProperty.Value;
        set => FrameCacheColorTypeProperty.Value = value;
    }

    public bool IsNodeCacheEnabled
    {
        get => IsNodeCacheEnabledProperty.Value;
        set => IsNodeCacheEnabledProperty.Value = value;
    }

    public int NodeCacheMaxPixels
    {
        get => NodeCacheMaxPixelsProperty.Value;
        set => NodeCacheMaxPixelsProperty.Value = value;
    }

    public int NodeCacheMinPixels
    {
        get => NodeCacheMinPixelsProperty.Value;
        set => NodeCacheMinPixelsProperty.Value = value;
    }

    public bool SwapTimelineScrollDirection
    {
        get => SwapTimelineScrollDirectionProperty.Value;
        set => SwapTimelineScrollDirectionProperty.Value = value;
    }

    public bool ClampResizeToOriginalLength
    {
        get => ClampResizeToOriginalLengthProperty.Value;
        set => ClampResizeToOriginalLengthProperty.Value = value;
    }

    public bool IsTimelineSnapEnabled
    {
        get => IsTimelineSnapEnabledProperty.Value;
        set => IsTimelineSnapEnabledProperty.Value = value;
    }

    public bool IsRippleEnabled
    {
        get => IsRippleEnabledProperty.Value;
        set => IsRippleEnabledProperty.Value = value;
    }

    public TimelineAutoScrollMode TimelineAutoScrollMode
    {
        get => TimelineAutoScrollModeProperty.Value;
        set => TimelineAutoScrollModeProperty.Value = value;
    }

    public UIToneMappingOperator ToneMappingMode
    {
        get => ToneMappingModeProperty.Value;
        set => ToneMappingModeProperty.Value = value;
    }

    public float ToneMappingExposure
    {
        get => ToneMappingExposureProperty.Value;
        set => ToneMappingExposureProperty.Value = value;
    }

    public bool UseHdrPreview
    {
        get => UseHdrPreviewProperty.Value;
        set => UseHdrPreviewProperty.Value = value;
    }

    public bool IsOnionSkinEnabled
    {
        get => IsOnionSkinEnabledProperty.Value;
        set => IsOnionSkinEnabledProperty.Value = value;
    }

    public int OnionSkinPrevCount
    {
        get => OnionSkinPrevCountProperty.Value;
        set => OnionSkinPrevCountProperty.Value = value;
    }

    public int OnionSkinNextCount
    {
        get => OnionSkinNextCountProperty.Value;
        set => OnionSkinNextCountProperty.Value = value;
    }

    public float OnionSkinPrevOpacity
    {
        get => OnionSkinPrevOpacityProperty.Value;
        set => OnionSkinPrevOpacityProperty.Value = value;
    }

    public float OnionSkinNextOpacity
    {
        get => OnionSkinNextOpacityProperty.Value;
        set => OnionSkinNextOpacityProperty.Value = value;
    }

    public PreviewSourceMode PreviewSourceMode
    {
        get => PreviewSourceModeProperty.Value;
        set => PreviewSourceModeProperty.Value = value;
    }

    /// <summary>Sets <see cref="PreviewSourceMode"/> from a <c>ComboBox.SelectedIndex</c>.</summary>
    /// <remarks>Out-of-range indices, such as the <c>-1</c> of a transient no-selection, are ignored.</remarks>
    public void SetPreviewSourceModeFromIndex(int index)
    {
        if (Enum.IsDefined(typeof(PreviewSourceMode), index))
            PreviewSourceMode = (PreviewSourceMode)index;
    }

    public CoreDictionary<string, LibraryTabDisplayMode> LibraryTabDisplayModes { get; } = new()
    {
        ["Search"] = LibraryTabDisplayMode.Show,
        ["Easings"] = LibraryTabDisplayMode.Show,
        ["Library"] = LibraryTabDisplayMode.Show,
        ["Nodes"] = LibraryTabDisplayMode.Hide,
    };

    public string? LastMediaDirectory
    {
        get => LastMediaDirectoryProperty.Value;
        set => LastMediaDirectoryProperty.Value = value;
    }

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
