using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BEditor;
using BEditor.Data;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Properties;
using BEditor.Primitive.Effects;
using BEditor.Primitive.Objects;
using BEditor.Primitive;

using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor
{
    public class App : IApplication
    {
        public static readonly App Current = new();

        public Status AppStatus { get; set; }
        public IServiceCollection Services { get; } = new ServiceCollection();
        public ILoggerFactory LoggingFactory { get; } = new LoggerFactory();
    }

    public class CLISynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            d?.Invoke(state);
        }
        public override void Send(SendOrPostCallback d, object? state)
        {
            d?.Invoke(state);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                Name = "bedit",
            };
            SynchronizationContext.SetSynchronizationContext(new CLISynchronizationContext());

            SetKnownTypes();
            Task.Run(async () => await CheckFFmpeg()).Wait();

            // ヘルプ出力のトリガーとなるオプションを指定
            app.HelpOption("-?|-h|--help");

            app.OnExecute(() =>
            {
                return 0;
            });

            app.Command("json", command =>
            {
                command.Description = CommandLineResources.OutputProjectToJson;

                command.HelpOption("-?|-h|--help");

                var input = command.Argument("file", CommandLineResources.ProjectFile);
                var output = command.Option("-o|--out", CommandLineResources.OutputDestinationFile, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    using Stream stream = output.HasValue() ? new FileStream(output.Value(), FileMode.Create) : new MemoryStream();
                    using var project = Project.FromFile(input.Value, App.Current);

                    project?.Save(stream, SerializeMode.Json);

                    if (project is null)
                    {
                        Console.Error.WriteLine(CommandLineResources.FailedToLoadProject);
                        return 1;
                    }

                    if (!output.HasValue()) Console.Out.WriteLine(Encoding.UTF8.GetString(((MemoryStream)stream).ToArray()));

                    return 0;
                });
            });

            app.Command("encode_img", command =>
            {
                command.Description = CommandLineResources.SaveFrameToImageFile;

                command.HelpOption("-?|-h|--help");

                var input = command.Argument("file", CommandLineResources.ProjectFile);
                var output = command.Argument("out", CommandLineResources.OutputDestinationFile);
                var frame = command.Argument("frame", CommandLineResources.FrameToOutput);
                var sc = command.Option("-s|--scene", CommandLineResources.SceneToOutput, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    using var project = Project.FromFile(input.Value, App.Current);

                    if (project is null)
                    {
                        Console.Error.WriteLine(CommandLineResources.FailedToLoadProject);
                        return 1;
                    }

                    project.Load();
                    var scene = !sc.HasValue() ? project.PreviewScene
                        : int.TryParse(sc.Value(), out var index) ? project.SceneList[index] : project.SceneList.ToList().Find(s => s.Name == sc.Value())!;

                    using var image = scene.Render(int.Parse(frame.Value)).Image;
                    image.Encode(output.Value);

                    return 0;
                });
            });

            app.Command("encode", command =>
            {
                command.Description = CommandLineResources.OutputVideo;

                command.HelpOption("-?|-h|--help");

                var input = command.Argument("file", CommandLineResources.ProjectFile);
                var output = command.Argument("out", CommandLineResources.OutputDestinationFile);
                var sc = command.Option("-s|--scene", CommandLineResources.SceneToOutput, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    using var project = Project.FromFile(input.Value, App.Current);

                    if (project is null)
                    {
                        Console.Error.WriteLine(CommandLineResources.FailedToLoadProject);
                        return 1;
                    }

                    project.Load();
                    var scene = !sc.HasValue() ? project.PreviewScene
                        : int.TryParse(sc.Value(), out var index) ? project.SceneList[index] : project.SceneList.ToList().Find(s => s.Name == sc.Value())!;

                    using var encoder = new FFmpegEncoder(scene.Width, scene.Height, project.Framerate, VideoCodec.Default, output.Value);
                    var progress = new ProgressBar();
                    var total = scene.TotalFrame + 1;

                    for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                    {
                        using var img = scene.Render(frame, RenderType.VideoOutput).Image;

                        encoder.Write(img);

                        progress.Report((double)frame / total);
                    }
                    progress.Dispose();

                    project.Unload();

                    return 0;
                });
            });

            app.Command("new", command =>
            {
                command.Description = CommandLineResources.CreateNewProject;

                command.HelpOption("-?|-h|--help");

                var width_Arg = command.Argument("width", Resources.Width);
                var height_Arg = command.Argument("height", Resources.Height);
                var framerate_Arg = command.Argument("framerate", Resources.Framerate);
                var samplingrate_Arg = command.Argument("samplingrate", Resources.Samplingrate);
                var name_Opt = command.Option("-n|--name", CommandLineResources.NameOfProject, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    var width = int.Parse(width_Arg.Value);
                    var height = int.Parse(height_Arg.Value);
                    var framerate = int.Parse(framerate_Arg.Value);
                    var samplingrate = int.Parse(samplingrate_Arg.Value);
                    var dir = new DirectoryInfo(name_Opt.HasValue() ? name_Opt.Value() : Environment.CurrentDirectory);

                    using var proj = new Project(width, height, framerate, samplingrate, App.Current);

                    return proj.Save(Path.Combine(dir.FullName, dir.Name + ".bedit")) ? 0 : 1;
                });
            });

            app.Command("open", command =>
            {
                command.Description = CommandLineResources.CreateNewProject;

                command.HelpOption("-?|-h|--help");

                var file = command.Argument("project", CommandLineResources.ProjectFile);

                command.OnExecute(() =>
                {
                    ConsoleEditor? editor = null;
                    try
                    {
                        editor = new ConsoleEditor(file.Value);

                        editor.Execute();

                        return 0;
                    }
                    catch
                    {
                        return 1;
                    }
                    finally
                    {
                        editor?.Project.Dispose();
                    }
                });
            });

            app.Execute(args);
        }

        private static void SetKnownTypes()
        {
            Serialize.SerializeKnownTypes.AddRange(new Type[]
            {
                typeof(AudioObject),
                typeof(CameraObject),
                typeof(GL3DObject),
                typeof(Figure),
                typeof(ImageFile),
                typeof(Text),
                typeof(VideoFile),
                typeof(SceneObject),
                typeof(RoundRect),
                typeof(Polygon),

                typeof(Blur),
                typeof(Border),
                typeof(ColorKey),
                typeof(Dilate),
                typeof(Erode),
                typeof(Monoc),
                typeof(Shadow),
                typeof(Clipping),
                typeof(AreaExpansion),
                typeof(LinearGradient),
                typeof(CircularGradient),
                typeof(Mask),
                typeof(PointLightDiffuse),
                typeof(ChromaKey),
                typeof(ImageSplit),
                typeof(MultipleControls),
                typeof(DepthTest),
                typeof(DirectionalLightSource),
                typeof(PointLightSource),
                typeof(SpotLight),
            });

            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.VideoMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.ImageMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.FigureMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.PolygonMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.RoundRectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.TextMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.CameraMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.GL3DObjectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.SceneMetadata);

            EffectMetadata.LoadedEffects.Add(new(Resources.Effects)
            {
                Children = new EffectMetadata[]
                {
                    new(Resources.Border, () => new Border()),
                    new(Resources.ColorKey, () => new ColorKey()),
                    new(Resources.DropShadow, () => new Shadow()),
                    new(Resources.Blur, () => new Blur()),
                    new(Resources.Monoc, () => new Monoc()),
                    new(Resources.Dilate, () => new Dilate()),
                    new(Resources.Erode, () => new Erode()),
                    new(Resources.Clipping, () => new Clipping()),
                    new(Resources.AreaExpansion, () => new AreaExpansion()),
                    new(Resources.LinearGradient, () => new LinearGradient()),
                    new(Resources.CircularGradient, () => new CircularGradient()),
                    new(Resources.Mask, () => new Mask()),
                    new(Resources.PointLightDiffuse, () => new PointLightDiffuse()),
                    new(Resources.ChromaKey, () => new ChromaKey()),
                    new(Resources.ImageSplit, () => new ImageSplit()),
                    new(Resources.MultipleImageControls, () => new MultipleControls()),
                }
            });
            EffectMetadata.LoadedEffects.Add(new(Resources.Camera)
            {
                Children = new EffectMetadata[]
                {
                    new(Resources.DepthTest, () => new DepthTest()),
                    new(Resources.DirectionalLightSource, () => new DirectionalLightSource()),
                    new(Resources.PointLightSource, () => new PointLightSource()),
                    new(Resources.SpotLight, () => new SpotLight()),
                }
            });
#if DEBUG
            EffectMetadata.LoadedEffects.Add(new("TestEffect", () => new TestEffect()));
#endif
        }
        private static async Task CheckFFmpeg()
        {
            static bool Exists()
            {
                if (OperatingSystem.IsWindows())
                {
                    var dlls = new string[]
                    {
                        "avcodec-58.dll",
                        "avdevice-58.dll",
                        "avfilter-7.dll",
                        "avformat-58.dll",
                        "avutil-56.dll",
                        "postproc-55.dll",
                        "swresample-3.dll",
                        "swscale-5.dll",
                    };

                    var dir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

                    foreach (var dll in dlls)
                    {
                        if (!File.Exists(Path.Combine(dir, dll)))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                else
                {
                    //avcodec-58.dll
                    var lib = OperatingSystem.IsLinux() ? "libavutil.so.56" : "libavutil.56.dylib";


                    if (NativeLibrary.TryLoad(lib, out var ptr))
                    {
                        NativeLibrary.Free(ptr);

                        return true;
                    }

                    return false;
                }
            }

            if (!Exists())
            {
                if (OperatingSystem.IsWindows())
                {
                    Console.WriteLine(CommandLineResources.FFmpegNotFound);

                    if (char.ToUpperInvariant(Console.ReadKey().KeyChar) is 'Y')
                    {
                        await FFmpegWindows();
                    }
                }
                else
                {
                    Console.WriteLine("FFmpeg was not found.");

                    if (OperatingSystem.IsMacOS())
                    {
                        Console.WriteLine("You need to");

                        Console.WriteLine("\n$ brew install ffmpeg");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        Console.WriteLine($"You need to");

                        Console.WriteLine(@"
                            $ sudo apt update
                            $ sudo apt -y upgrade
                            $ sudo apt install ffmpeg");
                    }

                    Environment.Exit(1);
                }
            }
        }
        private static async Task FFmpegWindows()
        {
            const string url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2021-02-20-12-31/ffmpeg-N-101185-g029e3c1c70-win64-gpl-shared.zip";
            using var client = new WebClient();
            var progress = new ProgressBar();

            var tmp = Path.GetTempFileName();
            client.DownloadFileCompleted += (s, e) =>
            {
                progress.Dispose();
            };
            client.DownloadProgressChanged += (s, e) =>
            {
                progress.Report(e.ProgressPercentage / 100d);
            };

            Console.WriteLine(string.Format(CommandLineResources.IsDownloading, "FFmpeg"));

            await client.DownloadFileTaskAsync(url, tmp);

            Console.WriteLine(string.Format(CommandLineResources.IsExtractedAndPlaced, "FFmpeg"));

            using (var stream = new FileStream(tmp, FileMode.Open))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                const string ziproot = "ffmpeg-N-101185-g029e3c1c70-win64-gpl-shared";
                var dir = Path.Combine(ziproot, "bin");
                var destdir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

                if (!Directory.Exists(destdir))
                {
                    Directory.CreateDirectory(destdir);
                }

                foreach (var entry in zip.Entries
                    .Where(i => i.FullName.Contains("bin"))
                    .Where(i => Path.GetExtension(i.Name) is ".dll"))
                {
                    var file = Path.GetFileName(entry.FullName);
                    using var deststream = new FileStream(Path.Combine(destdir, file), FileMode.Create);
                    using var srcstream = entry.Open();

                    await srcstream.CopyToAsync(deststream);
                }
            }

            File.Delete(tmp);
        }
    }
}