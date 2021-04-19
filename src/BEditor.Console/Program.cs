using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Primitive;
using BEditor.Primitive.Effects;
using BEditor.Primitive.Objects;
using BEditor.Properties;

using Microsoft.Extensions.CommandLineUtils;

namespace BEditor
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                Name = "bedit",
            };
            SynchronizationContext.SetSynchronizationContext(new CustomSynchronizationContext());
            App.Current.UIThread = SynchronizationContext.Current;

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
                command.Description = Strings.OutputProjectToJson;

                command.HelpOption("-?|-h|--help");

                var input = command.Option("--file|-f", Strings.ProjectFile, CommandOptionType.SingleValue);
                var output = command.Option("-o|--out", Strings.OutputDestinationFile, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    using Stream stream = output.HasValue() ? new FileStream(output.Value(), FileMode.Create) : new MemoryStream();

                    var project = LoadProject(input);

                    if (project is null)
                    {
                        return 1;
                    }

                    project.Save(stream, SerializeMode.Json);

                    if (!output.HasValue()) Console.Out.WriteLine(Encoding.UTF8.GetString(((MemoryStream)stream).ToArray()));

                    project.Unload();

                    return 0;
                });
            });

            app.Command("encode_img", command =>
            {
                command.Description = Strings.SaveFrameToImageFile;

                command.HelpOption("-?|-h|--help");

                var output = command.Argument("out", Strings.OutputDestinationFile);
                var frame = command.Argument("frame", Strings.FrameToOutput);
                var input = command.Option("--file|-f", Strings.ProjectFile, CommandOptionType.SingleValue);
                var sc = command.Option("-s|--scene", Strings.SceneToOutput, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    var project = LoadProject(input);

                    if (project is null)
                    {
                        return 1;
                    }

                    project.Load();
                    var scene = FindScene(sc, project);

                    using var image = scene.Render(int.Parse(frame.Value));
                    image.Encode(output.Value);

                    project.Unload();

                    Console.WriteLine(Strings.SavedTo, output.Value);

                    return 0;
                });
            });

            app.Command("encode", command =>
            {
                command.Description = Strings.OutputVideo;

                command.HelpOption("-?|-h|--help");

                var output = command.Argument("out", Strings.OutputDestinationFile);
                var input = command.Option("--file|-f", Strings.ProjectFile, CommandOptionType.SingleValue);
                var sc = command.Option("-s|--scene", Strings.SceneToOutput, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    var project = LoadProject(input);

                    if (project is null)
                    {
                        return 1;
                    }

                    project.Load();
                    var scene = FindScene(sc, project);

                    using var encoder = new FFmpegEncoder(scene.Width, scene.Height, project.Framerate, VideoCodec.Default, output.Value);
                    var progress = new ProgressBar();
                    var total = scene.TotalFrame + 1;

                    for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                    {
                        using var img = scene.Render(frame, RenderType.VideoOutput);

                        encoder.Write(img);

                        progress.Report((double)frame / total);
                    }
                    progress.Dispose();

                    project.Unload();

                    Console.WriteLine(Strings.SavedTo, output.Value);

                    return 0;
                });
            });

            app.Command("new", command =>
            {
                command.Description = Strings.CreateNewProject;

                command.HelpOption("-?|-h|--help");

                var width_Arg = command.Argument("width", Strings.Width);
                var height_Arg = command.Argument("height", Strings.Height);
                var framerate_Arg = command.Argument("framerate", Strings.Framerate);
                var samplingrate_Arg = command.Argument("samplingrate", Strings.Samplingrate);
                var name_Opt = command.Option("-n|--name", Strings.NameOfProject, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    var width = int.Parse(width_Arg.Value);
                    var height = int.Parse(height_Arg.Value);
                    var framerate = int.Parse(framerate_Arg.Value);
                    var samplingrate = int.Parse(samplingrate_Arg.Value);
                    string? filename = null;

                    // ファイルに保存
                    if (name_Opt.HasValue() || Path.HasExtension(name_Opt.Value()))
                    {
                        filename = Path.Combine(Environment.CurrentDirectory, name_Opt.Value());
                    }
                    // ディレクトリーを作成する
                    else if (name_Opt.HasValue())
                    {
                        var dir = new DirectoryInfo(name_Opt.Value());
                        filename = Path.Combine(dir.FullName, dir.Name + ".bedit");
                    }
                    else
                    {
                        var dir = new DirectoryInfo(Environment.CurrentDirectory);
                        filename = Path.Combine(dir.FullName, dir.Name + ".bedit");
                    }

                    var proj = new Project(width, height, framerate, samplingrate, App.Current, filename);
                    var result = proj.Save(filename) ? 0 : 1;

                    proj.Unload();

                    Console.WriteLine(Strings.SavedTo, filename);

                    return result;
                });
            });

            app.Command("open", command =>
            {
                command.Description = Strings.CreateNewProject;

                command.HelpOption("-?|-h|--help");

                var input = command.Option("--file|-f", Strings.ProjectFile, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    ConsoleEditor? editor = null;
                    try
                    {
                        var file = input.HasValue() ? input.Value() : FindProjectFile();
                        if (!File.Exists(file))
                        {
                            Console.Error.WriteLine(Strings.ProjectFileNotFound);
                            return 1;
                        }

                        editor = new ConsoleEditor(file!);

                        editor.Execute();

                        return 0;
                    }
                    catch
                    {
                        return 1;
                    }
                    finally
                    {
                        editor?.Project.Unload();
                    }
                });
            });

            app.Command("fonts", command =>
            {
                command.Description = Strings.EnumerateFonts;

                command.HelpOption("-?|-h|--help");

                command.OnExecute(() =>
                {
                    Console.WriteLine("FamilyName | Weight | Width");
                    Console.WriteLine("===========================");

                    foreach (var font in FontManager.Default.LoadedFonts)
                    {
                        Console.WriteLine($"{font.FamilyName} | {font.Weight:g} | {font.Width:g}");
                    }

                    return 0;
                });
            });

            app.Command("add_clip", command =>
            {
                command.Description = Strings.AddClip;

                command.HelpOption("-?|-h|--help");

                var start = command.Argument("start", Strings.Start);
                var layer = command.Argument("layer", Strings.Layer);
                var type = command.Argument("type", Strings.ClipType);
                var input = command.Option("--file|-f", Strings.ProjectFile, CommandOptionType.SingleValue);
                var sc = command.Option("-s|--scene", Strings.Scene, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    var project = LoadProject(input);

                    if (project is null)
                    {
                        return 1;
                    }

                    project.Load();
                    var scene = FindScene(sc, project);
                    Frame st_frame = default;
                    int layer_num = default;
                    ObjectMetadata? metadata = null;

                    try
                    {
                        st_frame = start.TryParse() ?? throw new Exception(Strings.InvalidValue + "\n  start:\n    " + start.Value);
                        layer_num = layer.TryParse() ?? throw new Exception(Strings.InvalidValue + "\n  layer:\n    " + layer.Value);
                        metadata = ObjectMetadata.LoadedObjects.FirstOrDefault(i => i.Name == type.Value) ?? throw new Exception(string.Format(Strings.NotFound, type.Value));
                    }
                    catch (Exception e)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine(e.Message);

                        Console.ResetColor();

                        return 1;
                    }

                    if (!scene.InRange(st_frame, st_frame + 180, layer_num))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine(Strings.ClipExistsInTheSpecifiedLocation);

                        Console.ResetColor();

                        return 1;
                    }


                    scene.AddClip(st_frame, layer_num, metadata, out var clip).Execute();
                    Console.WriteLine(Strings.AddedClip, clip.Start.Value, clip.End.Value, clip.Layer);

                    project.Save();

                    project.Unload();

                    return 0;
                });
            });

            app.Command("test", command =>
            {
                command.OnExecute(() =>
                {
                    var proj = new Project(1920, 1080, 30, 44100, App.Current, "./TestProject.bedit");
                    proj.Load();
                    proj.PreviewScene.PreviewFrame = 75;
                    proj.PreviewScene.AddClip(50, 5, PrimitiveTypes.ShapeMetadata, out _).Execute();

                    proj.Save();

                    proj.Unload();

                    return 1;
                });
            });

            app.Execute(args);

            Settings.Default.Save();
        }

        private static Project? LoadProject(CommandOption input)
        {
            var file = input.HasValue() ? input.Value() : FindProjectFile();
            if (!File.Exists(file))
            {
                Console.Error.WriteLine(Strings.ProjectFileNotFound);
                return null;
            }

            var proj = Project.FromFile(file!, App.Current);

            if (proj is null)
            {
                Console.Error.WriteLine(Strings.FailedToLoadProject);
                return null;
            }

            return proj;
        }
        private static Scene FindScene(CommandOption sc, Project project)
        {
            return !sc.HasValue() ? project.PreviewScene
                : int.TryParse(sc.Value(), out var index) ? project.SceneList[index] : project.SceneList.FirstOrDefault(s => s.SceneName == sc.Value()) ?? project.PreviewScene;
        }
        private static string? FindProjectFile()
        {
            var files = Directory.EnumerateFiles(Environment.CurrentDirectory)
                .Where(f => Path.GetExtension(f) is ".bedit")
                .ToArray();

            if (files.Length is 0 or < 1)
            {
                return null;
            }
            else
            {
                return files[0];
            }
        }
        private static void SetKnownTypes()
        {
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.VideoMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.ImageMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.ShapeMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.PolygonMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.RoundRectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.TextMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.CameraMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.GL3DObjectMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.SceneMetadata);
            ObjectMetadata.LoadedObjects.Add(PrimitiveTypes.FramebufferMetadata);

            foreach (var effect in PrimitiveTypes.EnumerateAllEffectMetadata())
            {
                EffectMetadata.LoadedEffects.Add(effect);
            }
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
                    Console.WriteLine(Strings.FFmpegNotFound);
                    Console.WriteLine(Strings.InstallIt);

                    if (char.ToUpperInvariant(Console.ReadKey().KeyChar) is 'Y')
                    {
                        Console.WriteLine();
                        await FFmpegWindows();
                    }
                }
                else
                {
                    Console.WriteLine(Strings.FFmpegNotFound);
                    Console.WriteLine(Strings.ExecuteTheCommand);

                    if (OperatingSystem.IsMacOS())
                    {
                        Console.WriteLine("$ brew install ffmpeg");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
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
            var installer = new FFmpegInstaller(Path.Combine(AppContext.BaseDirectory, "ffmpeg"));
            var progress = new ProgressBar();

            var tmp = Path.GetTempFileName();
            installer.StartInstall += (s, e) => Console.WriteLine(string.Format(Strings.IsDownloading, "FFmpeg"));
            installer.Installed += (s, e) => Console.WriteLine(string.Format(Strings.IsExtractedAndPlaced, "FFmpeg"));
            installer.DownloadCompleted += (s, e) => progress.Dispose();
            installer.DownloadProgressChanged += (s, e) => progress.Report(e.ProgressPercentage / 100d);

            await installer.Install();
        }
    }
}