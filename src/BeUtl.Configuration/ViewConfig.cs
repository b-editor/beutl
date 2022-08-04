using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Collections;

namespace BeUtl.Configuration;

public sealed class ViewConfig : ConfigurationBase
{
    public static readonly CoreProperty<ViewTheme> ThemeProperty;
    public static readonly CoreProperty<CultureInfo> UICultureProperty;
    public static readonly CoreProperty<bool> IsMicaEffectEnabledProperty;
    public static readonly CoreProperty<CoreList<string>> RecentFilesProperty;
    public static readonly CoreProperty<CoreList<string>> RecentProjectsProperty;
    private readonly CoreList<string> _recentFiles = new();
    private readonly CoreList<string> _recentProjects = new();

    static ViewConfig()
    {
        ThemeProperty = ConfigureProperty<ViewTheme, ViewConfig>("Theme")
            .SerializeName("theme")
            .DefaultValue(ViewTheme.Dark)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        UICultureProperty = ConfigureProperty<CultureInfo, ViewConfig>("UICulture")
            .SerializeName("ui-culture")
            .DefaultValue(CultureInfo.InstalledUICulture)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        IsMicaEffectEnabledProperty = ConfigureProperty<bool, ViewConfig>("IsMicaEffectEnabled")
            .SerializeName("is-mica-enabled")
            .DefaultValue(false)
            .PropertyFlags(PropertyFlags.NotifyChanged)
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

    public bool IsMicaEffectEnabled
    {
        get => GetValue(IsMicaEffectEnabledProperty);
        set => SetValue(IsMicaEffectEnabledProperty, value);
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

    public enum ViewTheme
    {
        Light,
        Dark,
        HighContrast,
        System
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);

        if (json is JsonObject jsonObject)
        {
            if (jsonObject["recent-files"] is JsonArray recentFiles)
            {
                _recentFiles.Replace(recentFiles.Select(i => (string?)i).Where(i => i != null && File.Exists(i)).ToArray()!);
            }

            if (jsonObject["recent-projects"] is JsonArray recentProjects)
            {
                _recentProjects.Replace(recentProjects.Select(i => (string?)i).Where(i => i != null && File.Exists(i)).ToArray()!);
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
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

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is "Theme" or "UICulture" or "IsMicaEffectEnabled")
        {
            OnChanged();
        }
    }
}
