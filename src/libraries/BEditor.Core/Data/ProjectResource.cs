// ProjectResource.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Data.Property;

namespace BEditor.Data
{
    public class ProjectPackage
    {
        private ProjectPackage(Project project, string fontDir, string rsrcDir, string otherDir)
        {
            BaseProject = project;
            FontsDirectory = fontDir;
            ResourcesDirectory = rsrcDir;
            OthersDirectory = otherDir;
        }

        /// <summary>
        /// Gets the projects contained in this project package.
        /// </summary>
        public Project BaseProject { get; }

        /// <summary>
        /// Gets the directory where the resource will be saved.
        /// </summary>
        public string ResourcesDirectory { get; }

        /// <summary>
        /// Gets the directory where the font will be saved.
        /// </summary>
        public string FontsDirectory { get; }

        /// <summary>
        /// Gets the directory where the arbitrary file will be saved.
        /// </summary>
        public string OthersDirectory { get; }

        /// <summary>
        /// Create a project package from a project.
        /// </summary>
        /// <param name="project">Projects to include in the project package.</param>
        /// <returns>Returns the project package created by this method.</returns>
        public static ProjectPackage? FromProject(Project project)
        {
            var proj = project.DeepClone();
            if (proj is null) return null;

            var workDir = Path.Combine(project.DirectoryName, ".app", project.Name);
            var fontDir = Path.Combine(workDir, "fonts");
            var rsrcDir = Path.Combine(workDir, "resources");
            var otherDir = Path.Combine(workDir, "others");

            // ディレクトリをクリーンにする
            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);

            // ディレクトリを作成
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(fontDir);
            Directory.CreateDirectory(rsrcDir);
            Directory.CreateDirectory(otherDir);

            // JSONに保存されないプロパティの値を設定
            proj.DirectoryName = workDir;
            proj.Name = project.Name;
            proj.Parent = project.Parent;

            // 依存しているフォントをコピー
            CopyFonts(proj, fontDir);

            // 依存しているファイルをコピー
            CopyFiles(proj, rsrcDir);

            // Todo: 依存しているプラグインを保存する処理を追加
            // ディレクトリはすべてエスケープする
            foreach (var prop in proj.GetAllChildren<FolderProperty>())
            {
                prop.Value = Path.Combine(otherDir, PathEscape(prop.Value));
                prop.Mode = FilePathType.FromProject;
            }

            proj.Save();

            return new ProjectPackage(proj, fontDir, rsrcDir, otherDir);
        }

        /// <summary>
        /// Compress this project package.
        /// </summary>
        /// <param name="file">The name of the file to be saved.</param>
        public void Compress(string file)
        {
            BaseProject.Save();

            var appDir = Path.Combine(BaseProject.DirectoryName, ".app");
            var thumbnail = Path.Combine(BaseProject.DirectoryName, "thumbnail.png");

            if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
            if (File.Exists(thumbnail)) File.Delete(thumbnail);
            if (File.Exists(file)) File.Delete(file);

            ZipFile.CreateFromDirectory(BaseProject.DirectoryName, file, CompressionLevel.Optimal, true);
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
    }
}
