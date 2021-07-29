// ProjectResource.cs
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

using BEditor.Data.Property;
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
        /// Create a project package from a project.
        /// </summary>
        /// <param name="project">Projects to include in the project package.</param>
        /// <param name="file">The name of the file to be saved.</param>
        /// <returns>Returns the project package created by this method.</returns>
        public static bool CreateFromProject(Project project, string file)
        {
            if (project is null) throw new ArgumentNullException(nameof(project));

            var proj = project.DeepClone();
            if (proj is null) return false;

            // ディレクトリをつなげる
            var workDir = Path.Combine(project.DirectoryName, ".app", project.Name);
            var fontDir = Path.Combine(workDir, "fonts");
            var rsrcDir = Path.Combine(workDir, "resources");
            var otherDir = Path.Combine(workDir, "others");
            var pluginDir = Path.Combine(workDir, "plugins");

            // ディレクトリをクリーンにする
            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);

            // ディレクトリを作成
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(fontDir);
            Directory.CreateDirectory(rsrcDir);
            Directory.CreateDirectory(otherDir);
            Directory.CreateDirectory(pluginDir);

            // JSONに保存されないプロパティの値を設定
            proj.DirectoryName = workDir;
            proj.Name = project.Name;
            proj.Parent = project.Parent;

            // 依存しているフォントをコピー
            CopyFonts(proj, fontDir);

            // 依存しているファイルをコピー
            CopyFiles(proj, rsrcDir);

            // 依存しているプラグインを保存
            SavePlugins(proj, pluginDir);

            // ディレクトリはすべてエスケープする
            foreach (var prop in proj.GetAllChildren<FolderProperty>())
            {
                prop.Value = Path.Combine(otherDir, PathEscape(prop.Value));
                prop.Mode = FilePathType.FromProject;
            }

            proj.Save();

            var appDir = Path.Combine(proj.DirectoryName, ".app");
            var thumbnail = Path.Combine(proj.DirectoryName, "thumbnail.png");

            if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
            if (File.Exists(thumbnail)) File.Delete(thumbnail);
            if (File.Exists(file)) File.Delete(file);

            // Zip圧縮
            ZipFile.CreateFromDirectory(proj.DirectoryName, file, CompressionLevel.Optimal, false);

            return true;
        }

        public static PluginInfo[] GetPluginInfo(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = archive.GetEntry("/plugins/plugins.json");
            if (entry is null) return Array.Empty<PluginInfo>();

            using var jsonStream = entry.Open();
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<PluginInfo[]>(reader.ReadToEnd(), PackageFile._serializerOptions) ?? Array.Empty<PluginInfo>();
        }

        public static Project? OpenFile(string file, string directry)
        {
            if (!File.Exists(file)) throw new FileNotFoundException(null, file);
            var projName = GetProjectName(file);
            var projDir = Path.Combine(directry, projName);
            if (Directory.Exists(projDir)) Directory.Delete(projDir, true);
            Directory.CreateDirectory(projDir);

            var app = ServicesLocator.Current.Provider.GetRequiredService<IApplication>();

            // 展開
            ZipFile.ExtractToDirectory(file, projDir);

            // プロジェクトを読み込む
            var proj = Project.FromFile(Directory.EnumerateFiles(projDir, ".bedit").First(), app);
            if (proj is null) return null;

            proj.DirectoryName = projDir;
            proj.Name = projName;

            return proj;
        }

        private static string GetProjectName(string zipFile)
        {
            using var stream = new FileStream(zipFile, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            return archive.Entries.First(i => Path.GetExtension(i.FullName) is ".bedit").Name;
        }

        // 依存しているフォントをコピー
        private static void CopyFonts(Project project, string directry)
        {
            foreach (var font in project.GetAllChildren<FontProperty>())
            {
                var dstFilename = Path.Combine(directry, Path.GetFileName(font.Value.Filename));

                if (!File.Exists(dstFilename))
                {
                    // 存在していない場合
                    File.Copy(font.Value.Filename, dstFilename);
                }
                else if (!FileCompare(font.Value.Filename, dstFilename))
                {
                    // 存在していて内部が違う場合、名前を変更
                    var num = 1;
                    dstFilename += num;
                    while (!File.Exists(dstFilename))
                    {
                        num++;
                    }
                }

                font.Value = new(dstFilename);
                font.Mode = FilePathType.FromProject;
            }
        }

        // 依存しているファイルをコピー
        private static void CopyFiles(Project project, string directry)
        {
            foreach (var prop in project.GetAllChildren<FileProperty>())
            {
                var dstFilename = Path.Combine(directry, Path.GetFileName(prop.Value));

                if ((!File.Exists(dstFilename)) && File.Exists(prop.Value))
                {
                    // 宛先が存在していなくて、ソースが存在している場合
                    File.Copy(prop.Value, dstFilename);
                }
                else if (!FileCompare(prop.Value, dstFilename))
                {
                    // 存在していて内部が違う場合、名前を変更
                    var num = 1;
                    dstFilename += num;
                    while (!File.Exists(dstFilename))
                    {
                        num++;
                    }
                }

                prop.Value = dstFilename;
                prop.Mode = FilePathType.FromProject;
            }
        }

        // 依存しているプラグインを保存
        private static void SavePlugins(Project project, string directry)
        {
            var plugins = project.FindDependentPlugins().ToArray();

            // 依存しているプラグインを書き込む
            using var writer = new StreamWriter(Path.Combine(directry, "plugins.json"));
            var json = JsonSerializer.Serialize(plugins.Select(i => new PluginInfo(i)).ToArray(), PackageFile._serializerOptions);
            writer.Write(json);

            // 設定を保存
            foreach (var plugin in plugins)
            {
                plugin.Settings.Save(Path.Combine(directry, plugin.PluginName + plugin.Id.ToString()) + ".json");
            }
        }

        // パスをエスケープ
        private static string PathEscape(string path)
        {
            return path.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }

        // ファイルを比較
        private static bool FileCompare(string file1, string file2)
        {
            // 絶対パスが同じ
            if (file1 == file2)
            {
                return true;
            }

            using var fs1 = new FileStream(file1, FileMode.Open, FileAccess.Read);
            using var fs2 = new FileStream(file2, FileMode.Open, FileAccess.Read);

            // ファイルサイズを確認
            if (fs1.Length != fs2.Length)
            {
                return false;
            }

            int file1byte;
            int file2byte;

            // 違うバイトが出たら終了
            do
            {
                file1byte = fs1.ReadByte();
                file2byte = fs2.ReadByte();
            }
            while ((file1byte == file2byte) && (file1byte != -1));

            return (file1byte - file2byte) == 0;
        }

        /// <summary>
        /// The plugin info.
        /// </summary>
        public class PluginInfo
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="PluginInfo"/> class.
            /// </summary>
            /// <param name="plugin">The plugin.</param>
            public PluginInfo(PluginObject plugin)
            {
                Id = plugin.Id;
                Version = plugin.GetType().Assembly.GetName().Version!.ToString(3);
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
        }
    }
}
