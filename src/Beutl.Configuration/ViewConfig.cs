using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class ViewConfig : ConfigurationBase
{
    public static readonly CoreProperty<ViewTheme> ThemeProperty;
    public static readonly CoreProperty<CultureInfo> UICultureProperty;
    public static readonly CoreProperty<bool> HidePrimaryPropertiesProperty;
    public static readonly CoreProperty<(int X, int Y)?> WindowPositionProperty;
    public static readonly CoreProperty<(int Width, int Height)?> WindowSizeProperty;
    public static readonly CoreProperty<bool?> IsWindowMaximizedProperty;
    public static readonly CoreProperty<bool> UseCustomAccentColorProperty;
    public static readonly CoreProperty<string?> CustomAccentColorProperty;
    public static readonly CoreProperty<bool> ShowExactBoundariesProperty;
    public static readonly CoreProperty<CoreList<string>> PrimaryPropertiesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentFilesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentProjectsProperty;
    private readonly CoreList<string> _primaryProperties =
    [
        "AlignmentX", "AlignmentY", "TransformOrigin", "BlendMode"
    ];
    private readonly CoreList<string> _recentFiles = [];
    private readonly CoreList<string> _recentProjects = [];
    private bool _showExactBoundaries = false;

    static ViewConfig()
    {
        ThemeProperty = ConfigureProperty<ViewTheme, ViewConfig>(nameof(Theme))
            .DefaultValue(ViewTheme.Dark)
            .Register();

        UICultureProperty = ConfigureProperty<CultureInfo, ViewConfig>(nameof(UICulture))
            .DefaultValue(CultureInfo.InstalledUICulture)
            .Register();

        HidePrimaryPropertiesProperty = ConfigureProperty<bool, ViewConfig>(nameof(HidePrimaryProperties))
            .DefaultValue(false)
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

        PrimaryPropertiesProperty = ConfigureProperty<CoreList<string>, ViewConfig>(nameof(PrimaryProperties))
            .Accessor(o => o.PrimaryProperties, (o, v) => o.PrimaryProperties = v)
            .Register();

        RecentFilesProperty = ConfigureProperty<CoreList<string>, ViewConfig>(nameof(RecentFiles))
            .Accessor(o => o.RecentFiles, (o, v) => o.RecentFiles = v)
            .Register();

        RecentProjectsProperty = ConfigureProperty<CoreList<string>, ViewConfig>(nameof(RecentProjects))
            .Accessor(o => o.RecentProjects, (o, v) => o.RecentProjects = v)
            .Register();
    }

    public ViewConfig()
    {
        _primaryProperties.CollectionChanged += (_, _) => OnChanged();
        _recentFiles.CollectionChanged += (_, _) => OnChanged();
        _recentProjects.CollectionChanged += (_, _) => OnChanged();
    }

    public ViewTheme Theme
    {
        get => GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public CultureInfo UICulture
    {
        get => GetValue(UICultureProperty);
        set => SetValue(UICultureProperty, value);
    }

    public bool HidePrimaryProperties
    {
        get => GetValue(HidePrimaryPropertiesProperty);
        set => SetValue(HidePrimaryPropertiesProperty, value);
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
    public CoreList<string> PrimaryProperties
    {
        get => _primaryProperties;
        set => _primaryProperties.Replace(value);
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

    public enum ViewTheme
    {
        Light,
        Dark,
        HighContrast,
        System
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        PrimaryProperties = context.GetValue<CoreList<string>>(nameof(PrimaryProperties))!;
        RecentFiles = context.GetValue<CoreList<string>>(nameof(RecentFiles))!;
        RecentProjects = context.GetValue<CoreList<string>>(nameof(RecentProjects))!;

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
        context.SetValue(nameof(PrimaryProperties), PrimaryProperties);
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

    public void ResetPrimaryProperties()
    {
        PrimaryProperties.Replace(["AlignmentX", "AlignmentY", "TransformOrigin", "BlendMode"]);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(Theme) or nameof(UICulture) or nameof(HidePrimaryProperties) or nameof(UseCustomAccentColor) or nameof(CustomAccentColor) or nameof(ShowExactBoundaries))
        {
            OnChanged();
        }
    }

    private record WindowPositionRecord(int X, int Y);

    private record WindowSizeRecord(int Width, int Height);
}
