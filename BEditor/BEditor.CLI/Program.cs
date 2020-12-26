using System;
using System.Linq;
using System.IO;
using System.Text;

using BEditor.Core;
using BEditor.Core.Data;

using Microsoft.Extensions.CommandLineUtils;
using BEditor.Drawing;

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
                    using var project = new Project(input.Value);

                    project.Save(stream, SerializeMode.Json);

                    if (!output.HasValue()) Console.Out.WriteLine(Encoding.UTF8.GetString(((MemoryStream)stream).ToArray()));

                    return 0;
                });
            });

            app.Command("output", command =>
            {
                // 説明（ヘルプの出力で使用される）
                command.Description = "プロジェクトを出力します";

                // コマンドについてのヘルプ出力のトリガーとなるオプションを指定
                command.HelpOption("-?|-h|--help");

                // コマンドの引数（名前と説明を引数で渡しているが、これはヘルプ出力で使用される）
                var input = command.Argument("file", "プロジェクトファイル");
                var output = command.Argument("out", "保存するファイル");
                var frame = command.Argument("frame", "出力するフレーム");
                var sc = command.Option("-s|--scene", "出力するシーン", CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    using var project = new Project(input.Value);
                    var scene = sc.HasValue() ? project.PreviewScene
                        : int.TryParse(sc.Value(), out var index) ? project.SceneList[index] : project.SceneList.ToList().Find(s => s.Name == sc.Value());

                    using var image = scene.Render(int.Parse(frame.Value)).Image;
                    image.Encode(output.Value);

                    return 0;
                });
            });

            app.Execute(args);
        }
    }
}