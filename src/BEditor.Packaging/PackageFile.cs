// PackageFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Provides the ability to manipulate package files.
    /// </summary>
    public static class PackageFile
    {
        internal static readonly JsonSerializerOptions _serializerOptions = new()
        {
            // すべての言語セットをエスケープせずにシリアル化させる
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true,
        };

        private static readonly string[] _ignoreDlls =
        {
            "BEditor.Audio",
            "BEditor.Base",
            "BEditor.Compute",
            "BEditor.Core",
            "BEditor.Drawing",
            "BEditor.Graphics",
            "BEditor.Media",
            "BEditor.Packaging",
            "BEditor.Primitive",
            "BEditor.Settings",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Primitive",
            "Microsoft.Win32.SystemEvents",
            "OpenCvSharp",
            "OpenCvSharp.Extensions",
            "OpenTK.Core",
            "OpenTK.Graphics",
            "OpenTK.Mathematics",
            "OpenTK.OpenAL",
            "OpenTK.Windowing.GraphicsLibraryFramework",
            "SkiaSharp",
            "System.Drawing.Common",
            "System.Reactive",
            "glfw",
            "opencv_videoio_ffmpeg452",
        };

        /// <summary>
        /// Creates the package file.
        /// </summary>
        /// <param name="mainfile">The assembly file for the plugin.</param>
        /// <param name="packagefile">The destination package file.</param>
        /// <param name="info">The package information.</param>
        /// <param name="progress">The progress of creating the file.</param>
        public static void CreatePackage(string mainfile, string packagefile, Package info, IProgress<int>? progress = null)
        {
            if (!File.Exists(mainfile))
            {
                throw new FileNotFoundException(null, mainfile);
            }

            var dirinfo = Directory.GetParent(mainfile)!;
            var dir = dirinfo.FullName;

            Compress(dir, packagefile, info, progress);
        }

        /// <summary>
        /// Creates the package file.
        /// </summary>
        /// <param name="mainfile">The assembly file for the plugin.</param>
        /// <param name="packagefile">The destination package file.</param>
        /// <param name="info">The package information.</param>
        /// <param name="progress">The progress of creating the file.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static async Task CreatePackageAsync(string mainfile, string packagefile, Package info, IProgress<int>? progress = null)
        {
            if (!File.Exists(mainfile))
            {
                throw new FileNotFoundException(null, mainfile);
            }

            var dirinfo = Directory.GetParent(mainfile)!;
            var dir = dirinfo.FullName;

            await CompressAsync(dir, packagefile, info, progress);
        }

        /// <summary>
        /// Open the package file.
        /// </summary>
        /// <param name="packagefile">The source package file.</param>
        /// <param name="destDirectory">The destination directory.</param>
        /// <param name="progress">The progress of opening the file.</param>
        /// <returns>Returns information about the opened package on success, or null on failure.</returns>
        public static Package? OpenPackage(string packagefile, string destDirectory, IProgress<int>? progress = null)
        {
            using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Package? info = null;
            Directory.CreateDirectory(destDirectory);
            var entries = zip.Entries;

            progress?.Report(0);
            for (var i = 0; i < entries.Count; i++)
            {
                var item = entries[i];
                if (item.FullName is "PACKAGEINFO")
                {
                    using var itemStream = item.Open();
                    info = ReadInfo(itemStream);
                }
                else
                {
                    var dstPath = Path.Combine(destDirectory, item.FullName);
                    var dirInfo = Directory.GetParent(dstPath)!;
                    if (!dirInfo.Exists)
                    {
                        dirInfo.Create();
                    }

                    using var dstStream = new FileStream(dstPath, FileMode.Create);
                    using var srcStream = item.Open();

                    srcStream.CopyTo(dstStream);
                }

                progress?.Report(i / entries.Count);
            }

            return info;
        }

        /// <summary>
        /// Open the package file.
        /// </summary>
        /// <param name="packagefile">The source package file.</param>
        /// <param name="destDirectory">The destination directory.</param>
        /// <param name="progress">The progress of opening the file.</param>
        /// <returns>Returns information about the opened package on success, or null on failure.</returns>
        public static async Task<Package?> OpenPackageAsync(string packagefile, string destDirectory, IProgress<int>? progress = null)
        {
            await using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Package? info = null;
            Directory.CreateDirectory(destDirectory);
            var entries = zip.Entries;

            progress?.Report(0);
            for (var i = 0; i < entries.Count; i++)
            {
                var item = entries[i];
                if (item.FullName is "PACKAGEINFO")
                {
                    await using var itemStream = item.Open();
                    info = await ReadInfoAsync(itemStream);
                }
                else
                {
                    var dstPath = Path.Combine(destDirectory, item.FullName);
                    var dirInfo = Directory.GetParent(dstPath)!;
                    if (!dirInfo.Exists)
                    {
                        dirInfo.Create();
                    }

                    await using var dstStream = new FileStream(dstPath, FileMode.Create);
                    await using var srcStream = item.Open();

                    await srcStream.CopyToAsync(dstStream);
                }

                progress?.Report(i / entries.Count);
            }

            return info;
        }

        /// <summary>
        /// Gets information from the package file.
        /// </summary>
        /// <param name="packagefile">The package file to retrieve the information.</param>
        /// <returns>The package info.</returns>
        public static Package GetPackageInfo(string packagefile)
        {
            if (!File.Exists(packagefile))
            {
                throw new FileNotFoundException(null, packagefile);
            }

            using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = zip.GetEntry("PACKAGEINFO") ?? throw new InvalidOperationException("このファイルは BEditor Package ではありません。");

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            return JsonSerializer.Deserialize<Package>(reader.ReadToEnd(), _serializerOptions) ?? throw new NotSupportedException("サポートしていないパッケージ情報です。");
        }

        /// <summary>
        /// Gets information from the package file.
        /// </summary>
        /// <param name="packagefile">The package file to retrieve the information.</param>
        /// <returns>The package info.</returns>
        public static async Task<Package> GetPackageInfoAsync(string packagefile)
        {
            if (!File.Exists(packagefile))
            {
                throw new FileNotFoundException(null, packagefile);
            }

            await using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = zip.GetEntry("PACKAGEINFO") ?? throw new InvalidOperationException("このファイルは BEditor Package ではありません。");

            await using var entryStream = entry.Open();
            return await JsonSerializer.DeserializeAsync<Package>(entryStream, _serializerOptions) ?? throw new NotSupportedException("サポートしていないパッケージ情報です。");
        }

        private static void Compress(string directory, string packagefile, Package info, IProgress<int>? progress = null)
        {
            using var stream = new FileStream(packagefile, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var array = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            progress?.Report(0);
            for (var i = 0; i < array.Length; i++)
            {
                var item = array[i];
                if (_ignoreDlls.Any(i => item.Contains(i)))
                {
                    continue;
                }

                var entryName = Path.GetRelativePath(directory, item);
                var entry = zip.CreateEntry(entryName);

                using var entryStream = entry.Open();
                using var itemStream = new FileStream(item, FileMode.Open);

                itemStream.CopyTo(entryStream);
                progress?.Report(i / array.Length);
            }

            using var infoStream = zip.CreateEntry("PACKAGEINFO").Open();
            WriteInfo(infoStream, info);
        }

        private static async Task CompressAsync(string directory, string packagefile, Package info, IProgress<int>? progress = null)
        {
            await using var stream = new FileStream(packagefile, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var array = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            progress?.Report(0);
            for (var i = 0; i < array.Length; i++)
            {
                var item = array[i];
                if (_ignoreDlls.Any(i => item.Contains(i)))
                {
                    continue;
                }

                var entryName = Path.GetRelativePath(directory, item);
                var entry = zip.CreateEntry(entryName);

                await using var entryStream = entry.Open();
                await using var itemStream = new FileStream(item, FileMode.Open);

                await itemStream.CopyToAsync(entryStream);
                progress?.Report(i / array.Length);
            }

            await using var infoStream = zip.CreateEntry("PACKAGEINFO").Open();
            await WriteInfoAsync(infoStream, info);
        }

        private static void WriteInfo(Stream stream, Package package)
        {
            var json = JsonSerializer.Serialize(package, _serializerOptions);
            using var writer = new StreamWriter(stream);
            writer.NewLine = "\n";

            writer.Write(json);
        }

        private static async Task WriteInfoAsync(Stream stream, Package package)
        {
            await JsonSerializer.SerializeAsync(stream, package, _serializerOptions);
        }

        private static Package ReadInfo(Stream stream)
        {
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Package>(reader.ReadToEnd(), _serializerOptions)!;
        }

        private static ValueTask<Package> ReadInfoAsync(Stream stream)
        {
            return JsonSerializer.DeserializeAsync<Package>(stream, _serializerOptions)!;
        }
    }
}