using System;
using System.IO;
using System.Linq;
using System.Text;

using BEditor;
using BEditor.Data;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;

using Microsoft.Extensions.CommandLineUtils;

namespace BEditor
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                // アプリケーション名（ヘルプの出力で使用される）
                Name = "bedit",
            };

            // ヘルプ出力のトリガーとなるオプションを指定
            app.HelpOption("-?|-h|--help");

            app.OnExecute(() =>
            {
                Console.WriteLine("Hello World!");
                return 0;
            });

            app.Command("json", command =>
            {
                // 説明（ヘルプの出力で使用される）
                command.Description = "プロジェクトをJson形式で出力します";

                // コマンドについてのヘルプ出力のトリガーとなるオプションを指定
                command.HelpOption("-?|-h|--help");

                // コマンドの引数（名前と説明を引数で渡しているが、これはヘルプ出力で使用される）
                var input = command.Argument("file", "プロジェクトファイル");
                var output = command.Option("-o|--out", "出力するファイル", CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    using Stream stream = output.HasValue() ? new FileStream(output.Value(), FileMode.Create) : new MemoryStream();
                    using var project = Project.FromFile(input.Value);

                    project?.Save(stream, SerializeMode.Json);

                    if (project is null)
                    {
                        Console.Error.WriteLine("プロジェクトファイルの読み込みに失敗しました");
                        return 1;
                    }

                    if (!output.HasValue()) Console.Out.WriteLine(Encoding.UTF8.GetString(((MemoryStream)stream).ToArray()));

                    return 0;
                });
            });

            app.Command("output", command =>
            {
                command.Description = "プロジェクトを出力します";

                command.HelpOption("-?|-h|--help");

                var input = command.Argument("file", "プロジェクトファイル");
                var output = command.Argument("out", "保存するファイル");
                var frame = command.Argument("frame", "出力するフレーム");
                var sc = command.Option("-s|--scene", "出力するシーン", CommandOptionType.SingleValue);

                command.OnExecute(async () =>
                {
                    using var project = Project.FromFile(input.Value);

                    if (project is null)
                    {
                        Console.Error.WriteLine("プロジェクトファイルの読み込みに失敗しました");
                        return 1;
                    }

                    project.Load();
                    var scene = sc.HasValue() ? project.PreviewScene
                        : int.TryParse(sc.Value(), out var index) ? project.SceneList[index] : project.SceneList.ToList().Find(s => s.Name == sc.Value())!;

                    await using var image = scene.Render(int.Parse(frame.Value)).Image;
                    image.Encode(output.Value);

                    return 0;
                });
            });

            app.Command("encode", command =>
            {
                command.Description = "プロジェクトを出力します";

                command.HelpOption("-?|-h|--help");

                var input = command.Argument("file", "プロジェクトファイル");
                var output = command.Argument("out", "保存するファイル");
                var sc = command.Option("-s|--scene", "出力するシーン", CommandOptionType.SingleValue);

                command.OnExecute(async () =>
                {
                    using var project = Project.FromFile(input.Value);

                    if (project is null)
                    {
                        Console.Error.WriteLine("プロジェクトファイルの読み込みに失敗しました");
                        return 1;
                    }

                    project.Load();
                    var scene = !sc.HasValue() ? project.PreviewScene
                        : int.TryParse(sc.Value(), out var index) ? project.SceneList[index] : project.SceneList.ToList().Find(s => s.Name == sc.Value())!;

                    using var encoder = new FFmpegEncoder(scene.Width, scene.Height, project.Framerate, VideoCodec.Default, output.Value);
                    var progress = new ProgressColor(55, scene.TotalFrame);

                    for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                    {
                        await using var img = scene.Render(frame, RenderType.VideoOutput).Image;

                        encoder.Write(img);

                        progress.Update($"{frame.Value} Frame");
                    }
                    progress.Done("Done!");

                    return 0;
                });
            });

            app.Execute(args);
        }
    }
}