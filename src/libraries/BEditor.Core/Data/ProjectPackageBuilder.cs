// ProjectPackageBuilder.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Packaging;
using BEditor.Plugin;

namespace BEditor.Data
{
    /// <summary>
    /// Create a project package.
    /// </summary>
    public sealed class ProjectPackageBuilder
    {
        private readonly Project _project;

        private ProjectPackageBuilder(Project project)
        {
            _project = project;
            Fonts = _project.GetAllChildren<FontProperty>()
                .Select(i => i.Value)
                .ToHashSet();
            Files = _project.GetAllChildren<FileProperty>()
                .Where(i => !string.IsNullOrWhiteSpace(i.Value))
                .Select(i => i.Value)
                .ToHashSet();
            Plugins = _project.FindDependentPlugins().ToHashSet();
        }

        /// <summary>
        /// Gets the fonts.
        /// </summary>
        public HashSet<Font> Fonts { get; }

        /// <summary>
        /// Gets the files.
        /// </summary>
        public HashSet<string> Files { get; }

        /// <summary>
        /// Gets the plugins.
        /// </summary>
        public HashSet<PluginObject> Plugins { get; }

        /// <summary>
        /// Get files that are not used for the project.
        /// </summary>
        public HashSet<string> OtherFiles { get; } = new();

        /// <summary>
        /// Gets or sets the README.
        /// </summary>
        public string ReadMe { get; set; } = string.Empty;

        /// <summary>
        /// Begin configuring an project package.
        /// </summary>
        /// <param name="project">Projects to include in the project package.</param>
        /// <returns>The same instance of the <see cref="ProjectPackageBuilder"/> for chaining.</returns>
        public static ProjectPackageBuilder Configure(Project project)
        {
            return new(project);
        }

        /// <summary>
        /// Exclude the specified font.
        /// </summary>
        /// <param name="font">The font to be excluded.</param>
        /// <returns>The same instance of the <see cref="ProjectPackageBuilder"/> for chaining.</returns>
        public ProjectPackageBuilder ExcludeFont(Font font)
        {
            Fonts.Remove(font);
            return this;
        }

        /// <summary>
        /// Exclude the specified fonts.
        /// </summary>
        /// <param name="fonts">The fonts to be excluded.</param>
        /// <returns>The same instance of the <see cref="ProjectPackageBuilder"/> for chaining.</returns>
        public ProjectPackageBuilder ExcludeFonts(IEnumerable<Font> fonts)
        {
            Fonts.ExceptWith(fonts);
            return this;
        }

        /// <summary>
        /// Include the specified font.
        /// </summary>
        /// <param name="font">The font to be include.</param>
        /// <returns>The same instance of the <see cref="ProjectPackageBuilder"/> for chaining.</returns>
        public ProjectPackageBuilder IncludeFont(Font font)
        {
            Fonts.Add(font);
            return this;
        }

        /// <summary>
        /// Include the specified fonts.
        /// </summary>
        /// <param name="fonts">The fonts to be include.</param>
        /// <returns>The same instance of the <see cref="ProjectPackageBuilder"/> for chaining.</returns>
        public ProjectPackageBuilder IncludeFonts(IEnumerable<Font> fonts)
        {
            Fonts.UnionWith(fonts);
            return this;
        }

        /// <summary>
        /// Create a project package.
        /// </summary>
        /// <param name="file">The name of the file to be saved.</param>
        /// <returns>Returns true on success, false otherwise.</returns>
        public bool Create(string file)
        {
            var proj = _project.DeepClone();
            if (proj is null) return false;

            // ディレクトリをつなげる
            var workDir = Path.Combine(_project.DirectoryName, ".app", _project.Name);
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

            // 依存しているフォントをコピー
            CopyFonts(proj, fontDir);

            // 依存しているファイルをコピー
            CopyFiles(proj, rsrcDir);

            // 依存しているプラグインを保存
            SavePlugins(pluginDir);

            // その他のファイルをコピー
            CopyOtherFiles(otherDir);

            // REAMEを書き込む
            WriteReadMe(workDir);

            // ディレクトリはすべてエスケープする
            foreach (var prop in proj.GetAllChildren<FolderProperty>())
            {
                prop.Value = Path.Combine(otherDir, ProjectPackage.PathEscape(prop.Value));
                prop.Mode = FilePathType.FromProject;
            }

            proj.DirectoryName = workDir;

            proj.Save();

            var appDir = Path.Combine(proj.DirectoryName, ".app");
            var thumbnail = Path.Combine(proj.DirectoryName, "thumbnail.png");

            if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
            if (File.Exists(thumbnail)) File.Delete(thumbnail);
            if (File.Exists(file)) File.Delete(file);

            // Zip圧縮
            ZipFile.CreateFromDirectory(proj.DirectoryName, file, CompressionLevel.Optimal, false);

            Directory.Delete(workDir, true);

            return true;
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

        private void WriteReadMe(string directry)
        {
            var file = Path.Combine(directry, "README");
            using var writer = new StreamWriter(file);
            writer.Write(ReadMe);
        }

        // 依存しているフォントをコピー
        private void CopyFonts(Project project, string directry)
        {
            foreach (var font in project.GetAllChildren<FontProperty>())
            {
                if (Fonts.Contains(font.Value))
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

                        File.Copy(font.Value.Filename, dstFilename);
                    }

                    font.Value = new(dstFilename);
                    font.Mode = FontProperty.FontSaveMode.FromProject;
                }
                else
                {
                    font.Mode = FontProperty.FontSaveMode.FamilyName;
                }
            }
        }

        // 依存しているファイルをコピー
        private void CopyFiles(Project project, string directry)
        {
            foreach (var prop in project.GetAllChildren<FileProperty>())
            {
                if (string.IsNullOrWhiteSpace(prop.Value)) continue;
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

                    File.Copy(prop.Value, dstFilename);
                }

                prop.Value = dstFilename;
                prop.Mode = FilePathType.FromProject;
            }
        }

        // 依存しているプラグインを保存
        private void SavePlugins(string directry)
        {
            // 依存しているプラグインを書き込む
            using var writer = new StreamWriter(Path.Combine(directry, "plugins.json"));
            var json = JsonSerializer.Serialize(Plugins.Select(i => new ProjectPackage.PluginInfo(i)).ToArray(), PackageFile._serializerOptions);
            writer.Write(json);

            // 設定を保存
            foreach (var plugin in Plugins)
            {
                plugin.Settings.Save(Path.Combine(directry, plugin.PluginName + plugin.Id.ToString()) + ".json");
            }
        }

        private void CopyOtherFiles(string directry)
        {
            foreach (var file in OtherFiles)
            {
                if (!File.Exists(file)) continue;
                var dstFilename = Path.Combine(directry, Path.GetFileName(file));

                if (!File.Exists(dstFilename))
                {
                    // 宛先が存在していない場合
                    File.Copy(file, dstFilename);
                }
                else if (!FileCompare(file, dstFilename))
                {
                    // 存在していて内部が違う場合、名前を変更
                    var num = 1;
                    dstFilename += num;
                    while (!File.Exists(dstFilename))
                    {
                        num++;
                    }

                    File.Copy(file, dstFilename);
                }
            }
        }
    }
}
