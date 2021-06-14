using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

using BEditor.Models.ManagePlugins;
using BEditor.Plugin;

namespace BEditor.Models.ManagePlugins
{
    public static class PluginChangeSchedule
    {
        public static ObservableCollection<PluginUpdateOrInstall> UpdateOrInstall { get; } = new();

        public static ObservableCollection<PluginObject> Uninstall { get; } = new();

        public static void CreateJsonFile(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Create);
            using var writer = new Utf8JsonWriter(stream, new()
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                Indented = true
            });

            writer.WriteStartArray();
            foreach (var item in UpdateOrInstall)
            {
                var version = item.Version ?? item.Target.Versions[0];

                writer.WriteStartObject();
                writer.WriteString("main-assembly", item.Target.MainAssembly);
                writer.WriteString("name", item.Target.Name);
                writer.WriteString("author", item.Target.Author);
                writer.WriteString("version", version.Version);
                writer.WriteString("type", item.Type is PluginChangeType.Install ? "install" : "update");
                writer.WriteString("id", item.Target.Id);
                writer.WriteString("license", item.Target.License);
                writer.WriteString("url", version.DownloadUrl);
                writer.WriteEndObject();
            }

            foreach (var item in Uninstall)
            {
                var asm = item.GetType().Assembly;
                var asmName = asm.GetName()!;
                var company = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
                var license = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

                writer.WriteStartObject();
                writer.WriteString("main-assembly", asmName.Name + ".dll");
                writer.WriteString("name", item.PluginName);
                writer.WriteString("author", company);
                writer.WriteString("version", asmName.Version?.ToString(3) ?? string.Empty);
                writer.WriteString("type", "uninstall");
                writer.WriteString("id", item.Id);
                writer.WriteString("license", license);
                writer.WriteString("url", string.Empty);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.Flush();
        }
    }
}