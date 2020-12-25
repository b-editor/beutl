using System;
using System.IO;

using BEditor.Core.Data;

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
                    using var project = new Project(input.Value);

                    //project.Save()

                    return 0;
                });
            });

            app.Execute(args);

            Console.Read();
            //new ArguentsParser(args).Command.Execute();
        }
    }
}
