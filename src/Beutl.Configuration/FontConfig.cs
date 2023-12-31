using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class FontConfig : ConfigurationBase
{
    public FontConfig()
    {
        FontDirectories.CollectionChanged += (_, _) => OnChanged();
    }

    public ObservableCollection<string> FontDirectories { get; } = CreateDefaultFontDirectories();

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        JsonNode? GetNode(string name1, string name2)
        {
            if (json[name1] is JsonNode node1)
                return node1;
            else if (json[name2] is JsonNode node2)
                return node2;
            else
                return null;
        }

        if (GetNode("directories", nameof(FontDirectories)) is JsonArray dirsArray)
        {
            string[] array = dirsArray.Select(i => (string?)i).Where(i => i != null).ToArray()!;
            string[] fontDirs = [.. FontDirectories];

            foreach (string item in array.Except(fontDirs))
            {
                FontDirectories.Add(item);
            }

            foreach (string item in fontDirs.Except(array))
            {
                FontDirectories.Remove(item);
            }
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        json[nameof(FontDirectories)] = new JsonArray(FontDirectories.Select(i => JsonValue.Create(i)).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        string[] array = context.GetValue<string[]>(nameof(FontDirectories)) ?? [];
        string[] fontDirs = [.. FontDirectories];

        foreach (string item in array.Except(fontDirs))
        {
            FontDirectories.Add(item);
        }

        foreach (string item in fontDirs.Except(array))
        {
            FontDirectories.Remove(item);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(FontDirectories), FontDirectories);
    }

    private static ObservableCollection<string> CreateDefaultFontDirectories()
    {
        static IEnumerable<string> Windows()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return $"{user}\\AppData\\Local\\Microsoft\\Windows\\Fonts";
            yield return "C:\\Windows\\Fonts";
        }

        static IEnumerable<string> Linux()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return "/usr/local/share/fonts";
            yield return "/usr/share/fonts";
            yield return $"{user}/.local/share/fonts/";
        }

        static IEnumerable<string> MacOS()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            yield return $"{user}/Library/Fonts";
        }

        IEnumerable<string>? e = null;
        if (OperatingSystem.IsWindows())
        {
            e = Windows();
        }
        else if (OperatingSystem.IsLinux())
        {
            e = Linux();
        }
        else if (OperatingSystem.IsMacOS())
        {
            e = MacOS();
        }

        return e != null ? [.. e] : [];
    }
}
