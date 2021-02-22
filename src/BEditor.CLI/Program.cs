using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using BEditor;
using BEditor.Data;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Properties;

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
                // アプリケーション名（ヘルプの出力で使用される）
                Name = "bedit",
            };
            SynchronizationContext.SetSynchronizationContext(new CLISynchronizationContext());

            // ヘルプ出力のトリガーとなるオプションを指定
            app.HelpOption("-?|-h|--help");

            app.OnExecute(() =>
            {
                return 0;
            });

            app.Command("json", command =>
            {
                // 説明（ヘルプの出力で使用される）
                command.Description = CommandLineResources.OutputProjectToJson;

                // コマンドについてのヘルプ出力のトリガーとなるオプションを指定
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

            app.Command("output", command =>
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
                    var progress = new ProgressColor(55, scene.TotalFrame + 1);

                    for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                    {
                        using var img = scene.Render(frame, RenderType.VideoOutput).Image;

                        encoder.Write(img);

                        progress.Update($"{frame.Value} Frame");
                    }
                    progress.Done("Done!");

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

            app.Execute(args);
        }
    }
}