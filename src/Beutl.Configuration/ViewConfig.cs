using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Collections;

namespace Beutl.Configuration;

public sealed class ViewConfig : ConfigurationBase
{
    public static readonly CoreProperty<ViewTheme> ThemeProperty;
    public static readonly CoreProperty<CultureInfo> UICultureProperty;
    public static readonly CoreProperty<bool> HidePrimaryPropertiesProperty;
    public static readonly CoreProperty<CoreList<string>> PrimaryPropertiesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentFilesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentProjectsProperty;
    private readonly CoreList<string> _primaryProperties = new()
    {
        "AlignmentX", "AlignmentY", "TransformOrigin", "BlendMode"
    };
    private readonly CoreList<string> _recentFiles = new();
    private readonly CoreList<string> _recentProjects = new();

    static ViewConfig()
    {
        ThemeProperty = ConfigureProperty<ViewTheme, ViewConfig>("Theme")
            .DefaultValue(ViewTheme.Dark)
            .Register();

        UICultureProperty = ConfigureProperty<CultureInfo, ViewConfig>("UICulture")
            .DefaultValue(CultureInfo.InstalledUICulture)
            .Register();

        HidePrimaryPropertiesProperty = ConfigureProperty<bool, ViewConfig>("HidePrimaryProperties")
            .DefaultValue(false)
            .Register();

        PrimaryPropertiesProperty = ConfigureProperty<CoreList<string>, ViewConfig>("PrimaryProperties")
            .Accessor(o => o.PrimaryProperties, (o, v) => o.PrimaryProperties = v)
            .Register();
        
        RecentFilesProperty = ConfigureProperty<CoreList<string>, ViewConfig>("RecentFiles")
            .Accessor(o => o.RecentFiles, (o, v) => o.RecentFiles = v)
            .Register();

        RecentProjectsProperty = ConfigureProperty<CoreList<string>, ViewConfig>("RecentProjects")
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

    [NotAutoSerialized()]
    public CoreList<string> PrimaryProperties
    {
        get => _primaryProperties;
        set => _primaryProperties.Replace(value);
    }
    
    [NotAutoSerialized()]
    public CoreList<string> RecentFiles
    {
        get => _recentFiles;
        set => _recentFiles.Replace(value);
    }

    [NotAutoSerialized()]
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

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json["primary-properties"] is JsonArray primaryProperties)
        {
            _primaryProperties.Replace(primaryProperties.Select(i => (string?)i).Where(i => i != null).ToArray()!);
        }
        
        if (json["recent-files"] is JsonArray recentFiles)
        {
            _recentFiles.Replace(recentFiles.Select(i => (string?)i).Where(i => i != null && File.Exists(i)).ToArray()!);
        }

        if (json["recent-projects"] is JsonArray recentProjects)
        {
            _recentProjects.Replace(recentProjects.Select(i => (string?)i).Where(i => i != null && File.Exists(i)).ToArray()!);
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        json["primary-properties"] = JsonSerializer.SerializeToNode(_primaryProperties, JsonHelper.SerializerOptions);
        json["recent-files"] = JsonSerializer.SerializeToNode(_recentFiles, JsonHelper.SerializerOptions);
        json["recent-projects"] = JsonSerializer.SerializeToNode(_recentProjects, JsonHelper.SerializerOptions);
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
        PrimaryProperties.Replace(new[] { "AlignmentX", "AlignmentY", "TransformOrigin", "BlendMode" });
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is "Theme" or "UICulture" or "HidePrimaryProperties")
        {
            OnChanged();
        }
    }
}
