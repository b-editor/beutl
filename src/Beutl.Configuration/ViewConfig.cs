using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

// ShowStorageServices is a setting this section no longer has: the file browser always shows the registered storage
// services, so a stored value is dropped rather than kept as an unknown member.
[DroppedMembers("ShowStorageServices")]
public sealed class ViewConfig : ConfigurationBase
{
    private readonly CoreList<string> _recentFiles = [];
    private readonly CoreList<string> _recentProjects = [];
    private readonly CoreList<string> _pinnedToolTabs = [];

    // Extension ids must remain outside BuiltinThemeIds, whose normalization defines reserved ids.
    private const string DefaultThemeId = FirstPartyThemeIds.DarkBorder;

    public ViewConfig()
    {
        // The lists are values rather than defaults, as their CoreProperty forms had no default.
        RecentFilesProperty.Value = _recentFiles;
        RecentProjectsProperty.Value = _recentProjects;
        PinnedToolTabsProperty.Value = _pinnedToolTabs;
        ScanProperties<ViewConfig>();
        _recentFiles.CollectionChanged += (_, _) => OnChanged();
        _recentProjects.CollectionChanged += (_, _) => OnChanged();
        _pinnedToolTabs.CollectionChanged += (_, _) => OnChanged();
    }

    // Serialize and Deserialize write and read the theme, the window bounds and the lists themselves.
    [NotAutoSerialized]
    public IProperty<string> ThemeProperty { get; } = new ValueOnlyProperty<string>(DefaultThemeId);

    public IProperty<CultureInfo> UICultureProperty { get; } = new ValueOnlyProperty<CultureInfo>(CultureInfo.InstalledUICulture);

    [NotAutoSerialized]
    public IProperty<(int X, int Y)?> WindowPositionProperty { get; } = new ValueOnlyProperty<(int X, int Y)?>(null);

    [NotAutoSerialized]
    public IProperty<(int Width, int Height)?> WindowSizeProperty { get; } = new ValueOnlyProperty<(int Width, int Height)?>(null);

    public IProperty<bool?> IsWindowMaximizedProperty { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool> UseCustomAccentColorProperty { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<string?> CustomAccentColorProperty { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<bool> ShowExactBoundariesProperty { get; } = new ValueOnlyProperty<bool>(false);

    [NotAutoSerialized]
    public IProperty<CoreList<string>> RecentFilesProperty { get; } = new ValueOnlyProperty<CoreList<string>>(null!) { KeepsList = true };

    [NotAutoSerialized]
    public IProperty<CoreList<string>> RecentProjectsProperty { get; } = new ValueOnlyProperty<CoreList<string>>(null!) { KeepsList = true };

    [NotAutoSerialized]
    public IProperty<CoreList<string>> PinnedToolTabsProperty { get; } = new ValueOnlyProperty<CoreList<string>>(null!) { KeepsList = true };

    public IProperty<string?> LastOpenedProjectFileProperty { get; } = new ValueOnlyProperty<string?>(null);

    public string Theme
    {
        get => ThemeProperty.Value;
        set => ThemeProperty.Value = value;
    }

    public CultureInfo UICulture
    {
        get => UICultureProperty.Value;
        set => UICultureProperty.Value = value;
    }

    public (int X, int Y)? WindowPosition
    {
        get => WindowPositionProperty.Value;
        set => WindowPositionProperty.Value = value;
    }

    public (int Width, int Height)? WindowSize
    {
        get => WindowSizeProperty.Value;
        set => WindowSizeProperty.Value = value;
    }

    public bool? IsWindowMaximized
    {
        get => IsWindowMaximizedProperty.Value;
        set => IsWindowMaximizedProperty.Value = value;
    }

    public bool UseCustomAccentColor
    {
        get => UseCustomAccentColorProperty.Value;
        set => UseCustomAccentColorProperty.Value = value;
    }

    public string? CustomAccentColor
    {
        get => CustomAccentColorProperty.Value;
        set => CustomAccentColorProperty.Value = value;
    }

    public bool ShowExactBoundaries
    {
        get => ShowExactBoundariesProperty.Value;
        set => ShowExactBoundariesProperty.Value = value;
    }

    public CoreList<string> RecentFiles
    {
        get => _recentFiles;
        set => _recentFiles.Replace(value);
    }

    public CoreList<string> RecentProjects
    {
        get => _recentProjects;
        set => _recentProjects.Replace(value);
    }

    /// <summary>
    /// Gets or sets the tool tabs pinned to the top of the new-tab page, as extension type names in
    /// the order they were pinned.
    /// </summary>
    public CoreList<string> PinnedToolTabs
    {
        get => _pinnedToolTabs;
        set => _pinnedToolTabs.Replace(value);
    }

    public string? LastOpenedProjectFile
    {
        get => LastOpenedProjectFileProperty.Value;
        set => LastOpenedProjectFileProperty.Value = value;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        Theme = context is IJsonSerializationContext jsonContext
            ? NormalizeThemeId(jsonContext.GetNode(nameof(Theme)))
            : DefaultThemeId;

        // A missing or null key keeps the existing empty list (Replace(null) throws).
        if (TryReadValue(context, nameof(RecentFiles), out CoreList<string>? recentFiles) && recentFiles is not null)
        {
            RecentFiles = recentFiles;
        }

        if (TryReadValue(context, nameof(RecentProjects), out CoreList<string>? recentProjects) && recentProjects is not null)
        {
            RecentProjects = recentProjects;
        }

        if (TryReadValue(context, nameof(PinnedToolTabs), out CoreList<string>? pinnedToolTabs) && pinnedToolTabs is not null)
        {
            PinnedToolTabs = pinnedToolTabs;
        }

        WindowPosition = null;
        if (TryReadValue(context, nameof(WindowPosition), out WindowPositionRecord? pos) && pos is not null)
        {
            WindowPosition = (pos.X, pos.Y);
        }

        WindowSize = null;
        if (TryReadValue(context, nameof(WindowSize), out WindowSizeRecord? size) && size is not null)
        {
            WindowSize = (size.Width, size.Height);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Theme), Theme);
        context.SetValue(nameof(RecentFiles), RecentFiles);
        context.SetValue(nameof(RecentProjects), RecentProjects);
        context.SetValue(nameof(PinnedToolTabs), PinnedToolTabs);

        if (WindowPosition.HasValue)
        {
            (int x, int y) = WindowPosition.Value;
            context.SetValue(nameof(WindowPosition), new WindowPositionRecord(x, y));
        }

        if (WindowSize.HasValue)
        {
            (int width, int height) = WindowSize.Value;
            context.SetValue(nameof(WindowSize), new WindowSizeRecord(width, height));
        }
    }

    public void UpdateRecentFile(string filename)
    {
        _recentFiles.Remove(filename);
        _recentFiles.Insert(0, filename);
    }

    public void UpdateRecentProject(string filename)
    {
        _recentProjects.Remove(filename);
        _recentProjects.Insert(0, filename);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(Theme) or nameof(UICulture) or nameof(UseCustomAccentColor) or nameof(CustomAccentColor) or nameof(ShowExactBoundaries) or nameof(LastOpenedProjectFile))
        {
            OnChanged();
        }
    }

    // Migrate legacy <2.0 ViewTheme enum values (a JSON number, or a PascalCase name) to the stable
    // lowercase id. Missing, corrupt, and pre-2.0-default values use the current default.
    private static string NormalizeThemeId(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return DefaultThemeId;
        }

        // A JSON number is only ever the legacy enum; ids are persisted as strings.
        if (value.TryGetValue(out int legacyEnum))
        {
            return BuiltinThemeIds.TryFromLegacyEnum(legacyEnum) ?? DefaultThemeId;
        }

        return BuiltinThemeIds.TryNormalize(value.TryGetValue(out string? raw) ? raw : null) ?? DefaultThemeId;
    }

    private record WindowPositionRecord(int X, int Y);

    private record WindowSizeRecord(int Width, int Height);
}
