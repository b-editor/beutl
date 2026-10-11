using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Testing.Headless;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class GlobalConfigurationAutoSaveTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("global-config-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private static GlobalConfiguration CreateConfiguration()
        => (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;

    // Set by the editor window as it closes, and written by the save on exit that follows.
    private static readonly HashSet<string> s_savedOnExit =
    [
        $"{nameof(ViewConfig)}.{nameof(ViewConfig.WindowPosition)}",
        $"{nameof(ViewConfig)}.{nameof(ViewConfig.WindowSize)}",
        $"{nameof(ViewConfig)}.{nameof(ViewConfig.IsWindowMaximized)}",
    ];

    [Test]
    public void Every_setting_saves_the_file_when_it_changes()
    {
        // A setting that does not raise ConfigurationChanged is written only when something else saves
        // the file, and is lost if Beutl is killed or crashes natively first.
        string path = Path.Combine(_directory, "settings.json");
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(path);
        var changed = new List<ConfigurationBase>();
        config.ConfigurationChanged += (_, section) => changed.Add(section);

        var unsaved = new List<string>();
        foreach (ConfigurationBase section in typeof(GlobalConfiguration).GetProperties()
            .Where(p => typeof(ConfigurationBase).IsAssignableFrom(p.PropertyType))
            .Select(p => (ConfigurationBase)p.GetValue(config)!))
        {
            foreach (CoreProperty property in PropertyRegistry.GetRegistered(section.GetType()))
            {
                string name = $"{section.GetType().Name}.{property.Name}";
                // Lists save through their CollectionChanged handlers.
                if (property.Name is nameof(CoreObject.Id) or nameof(CoreObject.Name)
                    || s_savedOnExit.Contains(name)
                    || property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(CoreList<>))
                {
                    continue;
                }

                changed.Clear();
                section.SetValue(property, Different(property.PropertyType, section.GetValue(property)));
                if (!changed.Contains(section))
                    unsaved.Add(name);
            }
        }

        Assert.That(unsaved, Is.Empty);
    }

    // Each changes one setting only, since any save writes every section.
    [Test]
    public void Gpu_choice_is_saved_as_soon_as_it_changes()
    {
        string path = Path.Combine(_directory, "settings.json");
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(path);

        config.GraphicsConfig.SelectedGpuName = "Restart GPU";

        GlobalConfiguration next = CreateConfiguration();
        next.Restore(path);
        Assert.That(next.GraphicsConfig.SelectedGpuName, Is.EqualTo("Restart GPU"));
    }

    [Test]
    public void Proxy_store_folder_is_saved_as_soon_as_it_changes()
    {
        string path = Path.Combine(_directory, "settings.json");
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(path);

        config.ProxyStoreConfig.StoreRootPath = Path.Combine(_directory, "proxies");

        GlobalConfiguration next = CreateConfiguration();
        next.Restore(path);
        Assert.That(next.ProxyStoreConfig.StoreRootPath, Is.EqualTo(Path.Combine(_directory, "proxies")));
    }

    private static object Different(Type type, object? current)
    {
        Type valueType = Nullable.GetUnderlyingType(type) ?? type;
        return valueType switch
        {
            _ when valueType == typeof(bool) => current is not true,
            _ when valueType == typeof(string) => $"{current}-changed",
            _ when valueType == typeof(int) => (int)(current ?? 0) + 1,
            _ when valueType == typeof(long) => (long)(current ?? 0L) + 1,
            _ when valueType == typeof(float) => (float)(current ?? 0f) + 0.5f,
            _ when valueType == typeof(double) => (double)(current ?? 0d) + 0.5,
            _ when valueType == typeof(CultureInfo) => CultureInfo.GetCultureInfo(
                (current as CultureInfo)?.Name == "ja-JP" ? "en-US" : "ja-JP"),
            { IsEnum: true } => Enum.GetValues(valueType).Cast<object>().First(value => !value.Equals(current)),
            _ => throw new NotSupportedException($"Pick a changed value for {type} to cover this setting."),
        };
    }

    [Test]
    public void Change_before_restore_does_not_write_the_default_settings_file()
    {
        // A process that never loaded the user's settings holds defaults; auto-saving them would
        // replace the user's settings.json with a reset copy.
        string path = GlobalConfiguration.DefaultFilePath;
        Assert.That(
            path,
            Is.EqualTo(Path.Combine(BeutlHomeIsolation.CurrentHome!, "settings.json")),
            "AssemblySetUp must isolate BEUTL_HOME so a regression cannot touch the real settings.");
        const string Sentinel = "{\"sentinel\":true}";
        File.WriteAllText(path, Sentinel);
        try
        {
            GlobalConfiguration config = CreateConfiguration();

            config.FontConfig.FontDirectories.Clear();

            Assert.That(File.ReadAllText(path), Is.EqualTo(Sentinel));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Change_after_restore_saves_to_the_restored_file()
    {
        string path = Path.Combine(_directory, "settings.json");
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(path);

        config.FontConfig.FontDirectories.Add("/restored-font-directory");

        Assert.That(File.ReadAllText(path), Does.Contain("/restored-font-directory"));
    }

    [Test]
    public void Save_before_endpoint_start_keeps_the_legacy_token_for_the_next_launch()
    {
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, new JsonObject
        {
            ["AiAgent"] = new JsonObject { ["LiveMcpToken"] = "pre-startup-token" }
        }.ToJsonString());
        GlobalConfiguration first = CreateConfiguration();
        first.Restore(path);
        first.Save(path); // Startup failure recovery can save before the endpoint starts.
        first.AiAgentConfig.AgentId = "codex"; // Ordinary auto-save must also retain it.

        GlobalConfiguration next = CreateConfiguration();
        next.Restore(path);
        Assert.That(next.AiAgentConfig.LiveMcpToken, Is.EqualTo("pre-startup-token"));
        string token = LiveMcpTokenStore.GetOrCreate(_directory, next.AiAgentConfig.LiveMcpToken);
        next.AiAgentConfig.LiveMcpToken = "";
        Assert.That(token, Is.EqualTo("pre-startup-token"));
        Assert.That(File.ReadAllText(path), Does.Not.Contain("pre-startup-token"));
        Assert.That(LiveMcpTokenStore.GetOrCreate(_directory), Is.EqualTo(token));
    }
}
