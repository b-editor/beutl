using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

// ShowStorageServices is a setting this section no longer has: the file browser always shows the registered storage
// services, so a stored value is dropped rather than kept as an unknown member.
[DroppedMembers("ShowStorageServices")]
public sealed partial class ViewConfig : ConfigurationBase
{
    // Extension ids must remain outside BuiltinThemeIds, whose normalization defines reserved ids.
    private const string DefaultThemeId = FirstPartyThemeIds.DarkBorder;

    public ViewConfig()
    {
        ScanProperties<ViewConfig>();
        RecentFiles.CollectionChanged += (_, _) => OnChanged();
        RecentProjects.CollectionChanged += (_, _) => OnChanged();
        PinnedToolTabs.CollectionChanged += (_, _) => OnChanged();
    }

    // Serialize and Deserialize write and read the theme, the window bounds and the lists themselves.
    [NotAutoSerialized]
    public IProperty<string> Theme { get; } = Property.CreateValue(DefaultThemeId);

    public IProperty<CultureInfo> UICulture { get; } = Property.CreateValue(CultureInfo.InstalledUICulture);

    [NotAutoSerialized]
    public IProperty<(int X, int Y)?> WindowPosition { get; } = Property.CreateValue<(int X, int Y)?>(null);

    [NotAutoSerialized]
    public IProperty<(int Width, int Height)?> WindowSize { get; } = Property.CreateValue<(int Width, int Height)?>(null);

    public IProperty<bool?> IsWindowMaximized { get; } = Property.CreateValue<bool?>(null);

    public IProperty<bool> UseCustomAccentColor { get; } = Property.CreateValue(false);

    public IProperty<string?> CustomAccentColor { get; } = Property.CreateValue<string?>(null);

    public IProperty<bool> ShowExactBoundaries { get; } = Property.CreateValue(false);

    [NotAutoSerialized]
    public IListProperty<string> RecentFiles { get; } = new ListProperty<string> { AffectsRender = false };

    [NotAutoSerialized]
    public IListProperty<string> RecentProjects { get; } = new ListProperty<string> { AffectsRender = false };

    // The tool tabs pinned to the top of the new-tab page, as extension type names in the order they were pinned.
    [NotAutoSerialized]
    public IListProperty<string> PinnedToolTabs { get; } = new ListProperty<string> { AffectsRender = false };

    public IProperty<string?> LastOpenedProjectFile { get; } = Property.CreateValue<string?>(null);

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        Theme.Value = context is IJsonSerializationContext jsonContext
            ? NormalizeThemeId(jsonContext.GetNode(nameof(Theme)))
            : DefaultThemeId;

        // A missing or null key keeps the existing empty list (Replace(null) throws).
        if (TryReadValue(context, nameof(RecentFiles), out CoreList<string>? recentFiles) && recentFiles is not null)
        {
            RecentFiles.Replace(recentFiles);
        }

        if (TryReadValue(context, nameof(RecentProjects), out CoreList<string>? recentProjects) && recentProjects is not null)
        {
            RecentProjects.Replace(recentProjects);
        }

        if (TryReadValue(context, nameof(PinnedToolTabs), out CoreList<string>? pinnedToolTabs) && pinnedToolTabs is not null)
        {
            PinnedToolTabs.Replace(pinnedToolTabs);
        }

        WindowPosition.Value = null;
        if (TryReadValue(context, nameof(WindowPosition), out WindowPositionRecord? pos) && pos is not null)
        {
            WindowPosition.Value = (pos.X, pos.Y);
        }

        WindowSize.Value = null;
        if (TryReadValue(context, nameof(WindowSize), out WindowSizeRecord? size) && size is not null)
        {
            WindowSize.Value = (size.Width, size.Height);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Theme), Theme.Value);
        context.SetValue(nameof(RecentFiles), RecentFiles);
        context.SetValue(nameof(RecentProjects), RecentProjects);
        context.SetValue(nameof(PinnedToolTabs), PinnedToolTabs);

        if (WindowPosition.Value.HasValue)
        {
            (int x, int y) = WindowPosition.Value.Value;
            context.SetValue(nameof(WindowPosition), new WindowPositionRecord(x, y));
        }

        if (WindowSize.Value.HasValue)
        {
            (int width, int height) = WindowSize.Value.Value;
            context.SetValue(nameof(WindowSize), new WindowSizeRecord(width, height));
        }
    }

    public void UpdateRecentFile(string filename)
    {
        RecentFiles.Remove(filename);
        RecentFiles.Insert(0, filename);
    }

    public void UpdateRecentProject(string filename)
    {
        RecentProjects.Remove(filename);
        RecentProjects.Insert(0, filename);
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
