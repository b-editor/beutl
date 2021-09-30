// PackageFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Linq.Expressions;
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
        /// <summary>
        /// Files to exclude by default.
        /// </summary>
        public static readonly string[] DefaultExculudeFiles =
        {
            "Avalonia.Animation.dll",
            "Avalonia.Base.dll",
            "Avalonia.Controls.DataGrid.dll",
            "Avalonia.Controls.dll",
            "Avalonia.Controls.PanAndZoom.dll",
            "Avalonia.DesignerSupport.dll",
            "Avalonia.Desktop.dll",
            "Avalonia.DesktopRuntime.dll",
            "Avalonia.Diagnostics.dll",
            "Avalonia.Dialogs.dll",
            "Avalonia.dll",
            "Avalonia.FreeDesktop.dll",
            "Avalonia.Input.dll",
            "Avalonia.Interactivity.dll",
            "Avalonia.Layout.dll",
            "Avalonia.Markup.dll",
            "Avalonia.Markup.Xaml.dll",
            "Avalonia.MicroCom.dll",
            "Avalonia.Native.dll",
            "Avalonia.OpenGL.dll",
            "Avalonia.Remote.Protocol.dll",
            "Avalonia.Skia.dll",
            "Avalonia.Styling.dll",
            "Avalonia.Themes.Default.dll",
            "Avalonia.Themes.Fluent.dll",
            "Avalonia.Visuals.dll",
            "Avalonia.Win32.dll",
            "Avalonia.X11.dll",
            "BEditor.Audio.dll",
            "BEditor.Audio.pdb",
            "BEditor.Base.dll",
            "BEditor.Base.pdb",
            "BEditor.Base.xml",
            "BEditor.Compute.dll",
            "BEditor.Compute.pdb",
            "BEditor.Compute.xml",
            "BEditor.Core.dll",
            "BEditor.Core.pdb",
            "BEditor.Core.xml",
            "beditor.deps.json",
            "beditor.dll",
            "BEditor.Drawing.dll",
            "BEditor.Drawing.pdb",
            "BEditor.Drawing.xml",
            "BEditor.Graphics.dll",
            "BEditor.Graphics.OpenGL.dll",
            "BEditor.Graphics.OpenGL.pdb",
            "BEditor.Graphics.pdb",
            "BEditor.Graphics.Skia.dll",
            "BEditor.Graphics.Skia.pdb",
            "BEditor.Graphics.Veldrid.dll",
            "BEditor.Graphics.Veldrid.pdb",
            "BEditor.Media.dll",
            "BEditor.Media.pdb",
            "BEditor.Media.xml",
            "BEditor.PackageInstaller.dll",
            "BEditor.PackageInstaller.pdb",
            "BEditor.Packaging.dll",
            "BEditor.Packaging.pdb",
            "BEditor.Packaging.xml",
            "beditor.pdb",
            "BEditor.Primitive.dll",
            "BEditor.Primitive.pdb",
            "BEditor.Primitive.xml",
            "beditor.runtimeconfig.dev.json",
            "beditor.runtimeconfig.json",
            "BEditor.Settings.dll",
            "BEditor.Settings.pdb",
            "FluentAvalonia.dll",
            "HarfBuzzSharp.dll",
            "JetBrains.Annotations.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "Microsoft.CodeAnalysis.CSharp.Scripting.dll",
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.Scripting.dll",
            "Microsoft.CodeAnalysis.VisualBasic.dll",
            "Microsoft.DotNet.PlatformAbstractions.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.DependencyInjection.dll",
            "Microsoft.Extensions.DependencyModel.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.Logging.dll",
            "Microsoft.Extensions.Options.dll",
            "Microsoft.Extensions.Primitives.dll",
            "Microsoft.VisualBasic.dll",
            "Microsoft.Win32.SystemEvents.dll",
            "NativeLibraryLoader.dll",
            "Newtonsoft.Json.dll",
            "OpenCvSharp.dll",
            "OpenCvSharp.Extensions.dll",
            "OpenTK.Core.dll",
            "OpenTK.Graphics.dll",
            "OpenTK.Mathematics.dll",
            "OpenTK.OpenAL.dll",
            "OpenTK.Windowing.GraphicsLibraryFramework.dll",
            "ReactiveProperty.Core.dll",
            "ReactiveProperty.dll",
            "Serilog.dll",
            "Serilog.Extensions.Logging.dll",
            "Serilog.Sinks.File.dll",
            "SharpDX.D3DCompiler.dll",
            "SharpDX.Direct3D11.dll",
            "SharpDX.dll",
            "SharpDX.DXGI.dll",
            "SharpGen.Runtime.COM.dll",
            "SharpGen.Runtime.dll",
            "SkiaSharp.dll",
            "System.Drawing.Common.dll",
            "System.Reactive.dll",
            "Tmds.DBus.dll",
            "Veldrid.dll",
            "Veldrid.MetalBindings.dll",
            "Veldrid.OpenGLBindings.dll",
            "Veldrid.SDL2.dll",
            "Veldrid.SPIRV.dll",
            "Veldrid.StartupUtilities.dll",
            "Veldrid.Utilities.dll",
            "vk.dll",
            "Vortice.Multimedia.dll",
            "Vortice.XAudio2.dll",
            "cs\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "cs\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "cs\\Microsoft.CodeAnalysis.resources.dll",
            "cs\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "de\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "de\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "de\\Microsoft.CodeAnalysis.resources.dll",
            "de\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "es\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "es\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "es\\Microsoft.CodeAnalysis.resources.dll",
            "es\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "fr\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "fr\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "fr\\Microsoft.CodeAnalysis.resources.dll",
            "fr\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "it\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "it\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "it\\Microsoft.CodeAnalysis.resources.dll",
            "it\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "ja\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "ja\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "ja\\Microsoft.CodeAnalysis.resources.dll",
            "ja\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "ja-JP\\BEditor.Audio.resources.dll",
            "ja-JP\\BEditor.Base.resources.dll",
            "ja-JP\\BEditor.Core.resources.dll",
            "ja-JP\\BEditor.Drawing.resources.dll",
            "ja-JP\\BEditor.Graphics.OpenGL.resources.dll",
            "ja-JP\\BEditor.Graphics.resources.dll",
            "ja-JP\\BEditor.Media.resources.dll",
            "ja-JP\\BEditor.PackageInstaller.resources.dll",
            "ja-JP\\BEditor.Primitive.resources.dll",
            "ja-JP\\beditor.resources.dll",
            "ko\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "ko\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "ko\\Microsoft.CodeAnalysis.resources.dll",
            "ko\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "pl\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "pl\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "pl\\Microsoft.CodeAnalysis.resources.dll",
            "pl\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "pt-BR\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "pt-BR\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "pt-BR\\Microsoft.CodeAnalysis.resources.dll",
            "pt-BR\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "ru\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "ru\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "ru\\Microsoft.CodeAnalysis.resources.dll",
            "ru\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "tr\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "tr\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "tr\\Microsoft.CodeAnalysis.resources.dll",
            "tr\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "zh-Hans\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "zh-Hans\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "zh-Hans\\Microsoft.CodeAnalysis.resources.dll",
            "zh-Hans\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "zh-Hant\\Microsoft.CodeAnalysis.CSharp.resources.dll",
            "zh-Hant\\Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll",
            "zh-Hant\\Microsoft.CodeAnalysis.resources.dll",
            "zh-Hant\\Microsoft.CodeAnalysis.Scripting.resources.dll",
            "runtimes\\centos7-x64\\native\\libOpenCvSharpExtern.so",
            "runtimes\\debian-x64\\native\\libuv.so",
            "runtimes\\fedora-x64\\native\\libuv.so",
            "runtimes\\linux-arm\\native\\libHarfBuzzSharp.so",
            "runtimes\\linux-arm\\native\\libSkiaSharp.so",
            "runtimes\\linux-arm64\\native\\libHarfBuzzSharp.so",
            "runtimes\\linux-arm64\\native\\libSkiaSharp.so",
            "runtimes\\linux-musl-x64\\native\\libHarfBuzzSharp.so",
            "runtimes\\linux-musl-x64\\native\\libSkiaSharp.so",
            "runtimes\\linux-x64\\native\\libglfw.so.3.3",
            "runtimes\\linux-x64\\native\\libHarfBuzzSharp.so",
            "runtimes\\linux-x64\\native\\libSkiaSharp.so",
            "runtimes\\linux-x64\\native\\libveldrid-spirv.so",
            "runtimes\\opensuse-x64\\native\\libuv.so",
            "runtimes\\osx\\native\\libAvaloniaNative.dylib",
            "runtimes\\osx\\native\\libHarfBuzzSharp.dylib",
            "runtimes\\osx\\native\\libSkiaSharp.dylib",
            "runtimes\\osx\\native\\libuv.dylib",
            "runtimes\\osx-x64\\native\\libglfw.3.dylib",
            "runtimes\\osx-x64\\native\\libOpenCvSharpExtern.dylib",
            "runtimes\\osx-x64\\native\\libsdl2.dylib",
            "runtimes\\osx-x64\\native\\libveldrid-spirv.dylib",
            "runtimes\\rhel-x64\\native\\libuv.so",
            "runtimes\\ubuntu.18.04-x64\\native\\libOpenCvSharpExtern.so",
            "runtimes\\win-arm64\\native\\av_libglesv2.dll",
            "runtimes\\win-arm64\\native\\libHarfBuzzSharp.dll",
            "runtimes\\win-arm64\\native\\libSkiaSharp.dll",
            "runtimes\\win-x64\\native\\glfw3.dll",
            "runtimes\\win-x64\\native\\libHarfBuzzSharp.dll",
            "runtimes\\win-x64\\native\\libSkiaSharp.dll",
            "runtimes\\win-x64\\native\\libveldrid-spirv.dll",
            "runtimes\\win-x64\\native\\OpenCvSharpExtern.dll",
            "runtimes\\win-x64\\native\\opencv_videoio_ffmpeg453_64.dll",
            "runtimes\\win-x64\\native\\SDL2.dll",
            "runtimes\\win-x86\\native\\glfw3.dll",
            "runtimes\\win-x86\\native\\libHarfBuzzSharp.dll",
            "runtimes\\win-x86\\native\\libSkiaSharp.dll",
            "runtimes\\win-x86\\native\\libveldrid-spirv.dll",
            "runtimes\\win-x86\\native\\OpenCvSharpExtern.dll",
            "runtimes\\win-x86\\native\\opencv_videoio_ffmpeg453.dll",
            "runtimes\\win-x86\\native\\SDL2.dll",
            "runtimes\\win7-arm\\native\\libuv.dll",
            "runtimes\\win7-x64\\native\\av_libglesv2.dll",
            "runtimes\\win7-x64\\native\\libuv.dll",
            "runtimes\\win7-x86\\native\\av_libglesv2.dll",
            "runtimes\\win7-x86\\native\\libuv.dll",
            "runtimes\\unix\\lib\\netcoreapp3.0\\System.Drawing.Common.dll",
            "runtimes\\win\\lib\\netcoreapp3.0\\Microsoft.Win32.SystemEvents.dll",
            "runtimes\\win\\lib\\netcoreapp3.0\\System.Drawing.Common.dll",
        };

        internal static readonly JsonSerializerOptions _serializerOptions = new()
        {
            // すべての言語セットをエスケープせずにシリアル化させる
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true,
        };

        static PackageFile()
        {
            for (var i = 0; i < DefaultExculudeFiles.Length; i++)
            {
                ref var item = ref DefaultExculudeFiles[i];
                item = item.Replace('\\', Path.DirectorySeparatorChar);
            }
        }

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

            Compress(dir, packagefile, info, DefaultExculudeFiles, progress);
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

            await CompressAsync(dir, packagefile, info, DefaultExculudeFiles, progress);
        }

        /// <summary>
        /// Creates the package file.
        /// </summary>
        /// <param name="mainfile">The assembly file for the plugin.</param>
        /// <param name="packagefile">The destination package file.</param>
        /// <param name="info">The package information.</param>
        /// <param name="excludeFiles">Files to exclude.</param>
        /// <param name="progress">The progress of creating the file.</param>
        public static void CreatePackage(string mainfile, string packagefile, Package info, string[] excludeFiles, IProgress<int>? progress = null)
        {
            if (!File.Exists(mainfile))
            {
                throw new FileNotFoundException(null, mainfile);
            }

            var dirinfo = Directory.GetParent(mainfile)!;
            var dir = dirinfo.FullName;

            Compress(dir, packagefile, info, excludeFiles, progress);
        }

        /// <summary>
        /// Creates the package file.
        /// </summary>
        /// <param name="mainfile">The assembly file for the plugin.</param>
        /// <param name="packagefile">The destination package file.</param>
        /// <param name="info">The package information.</param>
        /// <param name="excludeFiles">Files to exclude.</param>
        /// <param name="progress">The progress of creating the file.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static async Task CreatePackageAsync(string mainfile, string packagefile, Package info, string[] excludeFiles, IProgress<int>? progress = null)
        {
            if (!File.Exists(mainfile))
            {
                throw new FileNotFoundException(null, mainfile);
            }

            var dirinfo = Directory.GetParent(mainfile)!;
            var dir = dirinfo.FullName;

            await CompressAsync(dir, packagefile, info, excludeFiles, progress);
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

        private static void Compress(string directory, string packagefile, Package info, string[] excludeDlls, IProgress<int>? progress = null)
        {
            using var stream = new FileStream(packagefile, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var array = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            progress?.Report(0);
            for (var i = 0; i < array.Length; i++)
            {
                var item = array[i];
                var entryName = Path.GetRelativePath(directory, item);
                if (excludeDlls.Any(i => i == entryName))
                {
                    continue;
                }

                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

                using var entryStream = entry.Open();
                using var itemStream = new FileStream(item, FileMode.Open);

                itemStream.CopyTo(entryStream);
                progress?.Report(i / array.Length);
            }

            using var infoStream = zip.CreateEntry("PACKAGEINFO").Open();
            WriteInfo(infoStream, info);
        }

        private static async Task CompressAsync(string directory, string packagefile, Package info, string[] excludeDlls, IProgress<int>? progress = null)
        {
            await using var stream = new FileStream(packagefile, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var array = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            progress?.Report(0);
            for (var i = 0; i < array.Length; i++)
            {
                var item = array[i];
                var entryName = Path.GetRelativePath(directory, item);
                if (excludeDlls.Any(i => i == entryName))
                {
                    continue;
                }

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