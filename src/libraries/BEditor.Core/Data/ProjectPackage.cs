// ProjectPackage.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using BEditor.Packaging;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Provides for the manipulation of project packages.
    /// </summary>
    public static class ProjectPackage
    {
        /// <summary>
        /// Get information about dependent plugins from the project package.
        /// </summary>
        /// <param name="file">The project package file.</param>
        /// <returns>Information on dependent plugins.</returns>
        public static PluginInfo[] GetPluginInfo(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = archive.GetEntry("plugins/plugins.json");
            if (entry is null) return Array.Empty<PluginInfo>();

            using var jsonStream = entry.Open();
            using var reader = new StreamReader(jsonStream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<PluginInfo[]>(json, PackageFile._serializerOptions) ?? Array.Empty<PluginInfo>();
        }

        /// <summary>
        /// Gets the readme from the project package.
        /// </summary>
        /// <param name="file">The project package file.</param>
        /// <returns>The README.</returns>
        public static string GetReadMe(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = archive.GetEntry("README");
            if (entry is null) return string.Empty;

            using var jsonStream = entry.Open();
            using var reader = new StreamReader(jsonStream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Open the project package.
        /// </summary>
        /// <param name="file">The project package file.</param>
        /// <param name="directry">The destination directory.</param>
        /// <returns>Returns the opened project.</returns>
        public static Project? OpenFile(string file, string directry)
        {
            if (!File.Exists(file)) throw new FileNotFoundException(null, file);
            var projName = Path.GetFileNameWithoutExtension(GetProjectName(file));
            var projDir = Path.Combine(directry, projName);
            if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
            Directory.CreateDirectory(projDir);

            var app = ServicesLocator.Current.Provider.GetRequiredService<IApplication>();

            // 展開
            ZipFile.ExtractToDirectory(file, projDir);

            // プロジェクトを読み込む
            var proj = Project.FromFile(Path.Combine(projDir, projName + ".bedit"), app);
            if (proj is null) return null;

            proj.DirectoryName = projDir;
            proj.Name = projName;

            return proj;
        }

        // パスをエスケープ
        internal static string PathEscape(string path)
        {
            return path.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }

        private static string GetProjectName(string zipFile)
        {
            using var stream = new FileStream(zipFile, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            return archive.Entries.First(i => Path.GetExtension(i.FullName) is ".bedit").Name;
        }

        /// <summary>
        /// The plugin info.
        /// </summary>
        public sealed class PluginInfo : IEquatable<PluginInfo?>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="PluginInfo"/> class.
            /// </summary>
            /// <param name="plugin">The plugin.</param>
            public PluginInfo(PluginObject plugin)
            {
                Id = plugin.Id;
                Version = plugin.GetType().Assembly.GetName().Version!.ToString(3);
                Name = plugin.PluginName;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="PluginInfo"/> class.
            /// </summary>
            public PluginInfo()
            {
            }

            /// <summary>
            /// Gets or sets the id of the plugin.
            /// </summary>
            [JsonPropertyName("id")]
            public Guid Id { get; set; }

            /// <summary>
            /// Gets or sets the version of the plugin.
            /// </summary>
            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the name of the plugin.
            /// </summary>
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return Equals(obj as PluginInfo);
            }

            /// <inheritdoc/>
            public bool Equals(PluginInfo? other)
            {
                return other != null &&
                       Id.Equals(other.Id) &&
                       Version == other.Version &&
                       Name == other.Name;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCode.Combine(Id, Version, Name);
            }
        }
    }
}