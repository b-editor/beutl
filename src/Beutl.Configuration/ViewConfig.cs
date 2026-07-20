using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class ViewConfig : ConfigurationBase
{
    public static readonly CoreProperty<string> ThemeProperty;
    public static readonly CoreProperty<CultureInfo> UICultureProperty;
    public static readonly CoreProperty<(int X, int Y)?> WindowPositionProperty;
    public static readonly CoreProperty<(int Width, int Height)?> WindowSizeProperty;
    public static readonly CoreProperty<bool?> IsWindowMaximizedProperty;
    public static readonly CoreProperty<bool> UseCustomAccentColorProperty;
    public static readonly CoreProperty<string?> CustomAccentColorProperty;
    public static readonly CoreProperty<bool> ShowExactBoundariesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentFilesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentProjectsProperty;
    public static readonly CoreProperty<string?> LastOpenedProjectFileProperty;
    private readonly CoreList<string> _recentFiles = [];
    private readonly CoreList<string> _recentProjects = [];
    private bool _showExactBoundaries = false;

    // The default first-party theme id (DarkBorderThemeExtension.ThemeId, which lives in the app layer
    // this project cannot reference). It resolves to built-in Dark until the extension registers
    // during startup. Kept in sync by DarkBorderThemeExtensionTests.ViewConfigDefault_MatchesThemeId.
    private const string DefaultThemeId = "beutl.dark.border";

    static ViewConfig()
    {
        ThemeProperty = ConfigureProperty<string, ViewConfig>(nameof(Theme))
            .DefaultValue(DefaultThemeId)
            .Register();

        UICultureProperty = ConfigureProperty<CultureInfo, ViewConfig>(nameof(UICulture))
            .DefaultValue(CultureInfo.InstalledUICulture)
            .Register();

        WindowPositionProperty = ConfigureProperty<(int X, int Y)?, ViewConfig>(nameof(WindowPosition))
            .DefaultValue(null)
            .Register();

        WindowSizeProperty = ConfigureProperty<(int Width, int Height)?, ViewConfig>(nameof(WindowSize))
            .DefaultValue(null)
            .Register();

        IsWindowMaximizedProperty = ConfigureProperty<bool?, ViewConfig>(nameof(IsWindowMaximized))
            .DefaultValue(null)
            .Register();

        UseCustomAccentColorProperty = ConfigureProperty<bool, ViewConfig>(nameof(UseCustomAccentColor))
            .DefaultValue(false)
            .Register();

        CustomAccentColorProperty = ConfigureProperty<string?, ViewConfig>(nameof(CustomAccentColor))
            .DefaultValue(null)
            .Register();

        ShowExactBoundariesProperty = ConfigureProperty<bool, ViewConfig>(nameof(ShowExactBoundaries))
            .Accessor(o => o.ShowExactBoundaries, (o, v) => o.ShowExactBoundaries = v)
            .DefaultValue(false)
            .Register();

        RecentFilesProperty = ConfigureProperty<CoreList<string>, ViewConfig>(nameof(RecentFiles))
            .Accessor(o => o.RecentFiles, (o, v) => o.RecentFiles = v)
            .Register();

        RecentProjectsProperty = ConfigureProperty<CoreList<string>, ViewConfig>(nameof(RecentProjects))
            .Accessor(o => o.RecentProjects, (o, v) => o.RecentProjects = v)
            .Register();

        LastOpenedProjectFileProperty = ConfigureProperty<string?, ViewConfig>(nameof(LastOpenedProjectFile))
            .DefaultValue(null)
            .Register();
    }

    public ViewConfig()
    {
        _recentFiles.CollectionChanged += (_, _) => OnChanged();
        _recentProjects.CollectionChanged += (_, _) => OnChanged();
    }

    [NotAutoSerialized]
    public string Theme
    {
        get => GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public CultureInfo UICulture
    {
        get => GetValue(UICultureProperty);
        set => SetValue(UICultureProperty, value);
    }

    [NotAutoSerialized]
    public (int X, int Y)? WindowPosition
    {
        get => GetValue(WindowPositionProperty);
        set => SetValue(WindowPositionProperty, value);
    }

    [NotAutoSerialized]
    public (int Width, int Height)? WindowSize
    {
        get => GetValue(WindowSizeProperty);
        set => SetValue(WindowSizeProperty, value);
    }

    public bool? IsWindowMaximized
    {
        get => GetValue(IsWindowMaximizedProperty);
        set => SetValue(IsWindowMaximizedProperty, value);
    }

    public bool UseCustomAccentColor
    {
        get => GetValue(UseCustomAccentColorProperty);
        set => SetValue(UseCustomAccentColorProperty, value);
    }

    public string? CustomAccentColor
    {
        get => GetValue(CustomAccentColorProperty);
        set => SetValue(CustomAccentColorProperty, value);
    }

    public bool ShowExactBoundaries
    {
        get => _showExactBoundaries;
        set => SetAndRaise(ShowExactBoundariesProperty, ref _showExactBoundaries, value);
    }

    [NotAutoSerialized]
    public CoreList<string> RecentFiles
    {
        get => _recentFiles;
        set => _recentFiles.Replace(value);
    }

    [NotAutoSerialized]
    public CoreList<string> RecentProjects
    {
        get => _recentProjects;
        set => _recentProjects.Replace(value);
    }

    public string? LastOpenedProjectFile
    {
        get => GetValue(LastOpenedProjectFileProperty);
        set => SetValue(LastOpenedProjectFileProperty, value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        // The raw node is needed only to tell a legacy JSON number from a string id; any other
        // context can only carry the string form.
        if (context is IJsonSerializationContext jsonContext)
        {
            Theme = NormalizeThemeId(jsonContext.GetNode(nameof(Theme)));
        }
        else
        {
            Theme = NormalizeThemeId(context.GetValue<string>(nameof(Theme)));
        }

        // 古い settings.json や手動編集後のファイルでこれらのキーが欠落していると
        // GetValue が null を返す。null! を素通りさせると _recentFiles.Replace(null) で
        // ArgumentNullException が起き、後続の Editor/Graphics/Tutorial 設定の
        // Deserialize もまとめて中断してしまう。null の場合は既存の空リストを保持する。
        if (context.GetValue<CoreList<string>>(nameof(RecentFiles)) is { } recentFiles)
        {
            RecentFiles = recentFiles;
        }

        if (context.GetValue<CoreList<string>>(nameof(RecentProjects)) is { } recentProjects)
        {
            RecentProjects = recentProjects;
        }

        WindowPosition = null;
        if (context.GetValue<WindowPositionRecord?>(nameof(WindowPosition)) is { } pos)
        {
            WindowPosition = (pos.X, pos.Y);
        }

        WindowSize = null;
        if (context.GetValue<WindowSizeRecord?>(nameof(WindowSize)) is { } size)
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
    // lowercase id. Name-form ids resolve through BuiltinThemeIds, which ThemeRegistry validates
    // extension ids against — the two must not drift. The legacy-enum mapping stays here because it
    // resolves to DefaultThemeId, an app-layer id that BuiltinThemeIds must never return: its
    // Normalize backs ThemeRegistry's reserved-id check.
    private static string NormalizeThemeId(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return DefaultThemeId;
        }

        // A JSON number is only ever the legacy enum; ids are persisted as strings.
        if (value.TryGetValue(out int legacyEnum))
        {
            return FromLegacyEnum(legacyEnum);
        }

        return value.TryGetValue(out string? raw) ? NormalizeThemeId(raw) : DefaultThemeId;
    }

    private static string NormalizeThemeId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultThemeId;
        }

        raw = raw.Trim();

        // A hand-edited settings.json may quote the legacy enum; it must migrate like the number form.
        // An id that merely looks numeric ("2026") is custom and is left to BuiltinThemeIds.Normalize.
        if (int.TryParse(raw, CultureInfo.InvariantCulture, out int legacyEnum)
            && legacyEnum is >= 0 and <= 3)
        {
            return FromLegacyEnum(legacyEnum);
        }

        return BuiltinThemeIds.Normalize(raw);
    }

    // Legacy Dark (1) was the pre-2.0 default, so it marks a user who never chose a theme rather than
    // one who wants the classic look: they get DefaultThemeId, which is now the product's dark look.
    // Classic dark remains reachable by picking "Dark (Classic)", which persists the "dark" id and so
    // takes the name path above. Out-of-range values are corrupt rather than a choice, so they too
    // fall to the default — a persisted theme and an absent one must not diverge.
    private static string FromLegacyEnum(int legacyEnum) => legacyEnum switch
    {
        0 => BuiltinThemeIds.Light,
        2 => BuiltinThemeIds.HighContrast,
        3 => BuiltinThemeIds.System,
        _ => DefaultThemeId,
    };

    private record WindowPositionRecord(int X, int Y);

    private record WindowSizeRecord(int Width, int Height);
}
