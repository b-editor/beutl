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

    public IProperty<string?> LastMediaDirectory { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<bool> AutoAdjustSceneDuration { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> EnablePointerLockInProperty { get; } = new ValueOnlyProperty<bool>(true);

    [Display(Name = nameof(SettingsStrings.EnableFrameCache), ResourceType = typeof(SettingsStrings))]
    public IProperty<bool> IsFrameCacheEnabled { get; } = new ValueOnlyProperty<bool>(false);

    [Range(0.0, double.MaxValue)]
    [Display(Name = nameof(SettingsStrings.FrameCacheMaxSize), ResourceType = typeof(SettingsStrings))]
    public IProperty<double> FrameCacheMaxSize { get; } = new ValueOnlyProperty<double>(s_defaultFrameCacheMaxSize);

    [Display(Name = nameof(SettingsStrings.FrameCacheScale), ResourceType = typeof(SettingsStrings))]
    public IProperty<FrameCacheConfigScale> FrameCacheScale { get; } = new ValueOnlyProperty<FrameCacheConfigScale>(FrameCacheConfigScale.FitToPreviewer);

    [Display(Name = nameof(SettingsStrings.FrameCacheColorType), ResourceType = typeof(SettingsStrings))]
    public IProperty<FrameCacheConfigColorType> FrameCacheColorType { get; } = new ValueOnlyProperty<FrameCacheConfigColorType>(FrameCacheConfigColorType.RGBA);

    [Display(Name = nameof(SettingsStrings.EnableNodeCache), ResourceType = typeof(SettingsStrings))]
    public IProperty<bool> IsNodeCacheEnabled { get; } = new ValueOnlyProperty<bool>(false);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(SettingsStrings.NodeCacheMaxPixels), ResourceType = typeof(SettingsStrings))]
    public IProperty<int> NodeCacheMaxPixels { get; } = new ValueOnlyProperty<int>(1000 * 1000);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(SettingsStrings.NodeCacheMinPixels), ResourceType = typeof(SettingsStrings))]
    public IProperty<int> NodeCacheMinPixels { get; } = new ValueOnlyProperty<int>(1);

    public IProperty<bool> SwapTimelineScrollDirection { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<bool> ClampResizeToOriginalLength { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> IsTimelineSnapEnabled { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> IsRippleEnabled { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<TimelineAutoScrollMode> TimelineAutoScrollMode { get; } = new ValueOnlyProperty<TimelineAutoScrollMode>(Configuration.TimelineAutoScrollMode.PageScroll);

    public IProperty<UIToneMappingOperator> ToneMappingMode { get; } = new ValueOnlyProperty<UIToneMappingOperator>(UIToneMappingOperator.None);

    public IProperty<float> ToneMappingExposure { get; } = new ValueOnlyProperty<float>(0f);

    public IProperty<bool> UseHdrPreview { get; } = new ValueOnlyProperty<bool>(false);

    [Display(Name = nameof(Strings.OnionSkin), ResourceType = typeof(Strings))]
    public IProperty<bool> IsOnionSkinEnabled { get; } = new ValueOnlyProperty<bool>(false);

    [Range(0, 10)]
    [Display(Name = nameof(Strings.OnionSkinPrevCount), ResourceType = typeof(Strings))]
    public IProperty<int> OnionSkinPrevCount { get; } = new ValueOnlyProperty<int>(1);

    [Range(0, 10)]
    [Display(Name = nameof(Strings.OnionSkinNextCount), ResourceType = typeof(Strings))]
    public IProperty<int> OnionSkinNextCount { get; } = new ValueOnlyProperty<int>(0);

    [Range(0.0, 1.0)]
    [Display(Name = nameof(Strings.OnionSkinPrevOpacity), ResourceType = typeof(Strings))]
    public IProperty<float> OnionSkinPrevOpacity { get; } = new ValueOnlyProperty<float>(0.35f);

    [Range(0.0, 1.0)]
    [Display(Name = nameof(Strings.OnionSkinNextOpacity), ResourceType = typeof(Strings))]
    public IProperty<float> OnionSkinNextOpacity { get; } = new ValueOnlyProperty<float>(0.35f);

    [Display(Name = nameof(Strings.PreviewSource), ResourceType = typeof(Strings))]
    public IProperty<PreviewSourceMode> PreviewSourceMode { get; } = new ValueOnlyProperty<PreviewSourceMode>(Configuration.PreviewSourceMode.PreferProxy);

    /// <summary>Sets <see cref="PreviewSourceMode"/> from a <c>ComboBox.SelectedIndex</c>.</summary>
    /// <remarks>Out-of-range indices, such as the <c>-1</c> of a transient no-selection, are ignored.</remarks>
    public void SetPreviewSourceModeFromIndex(int index)
    {
        if (Enum.IsDefined(typeof(PreviewSourceMode), index))
            PreviewSourceMode.Value = (PreviewSourceMode)index;
    }

    public CoreDictionary<string, LibraryTabDisplayMode> LibraryTabDisplayModes { get; } = new()
    {
        ["Search"] = LibraryTabDisplayMode.Show,
        ["Easings"] = LibraryTabDisplayMode.Show,
        ["Library"] = LibraryTabDisplayMode.Show,
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
