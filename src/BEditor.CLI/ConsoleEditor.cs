using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.CommandLineUtils;

using BEditor.Data;
using BEditor.Properties;
using System.Text.RegularExpressions;
using System.IO;
using BEditor.Data.Property;

namespace BEditor
{
    public class EditorCommand
    {
        private readonly Action<CommandArgument[], CommandOption[]> _action;

        public EditorCommand(ConsoleEditor editor, Action<CommandArgument[], CommandOption[]> action)
        {
            Editor = editor;
            _action = action;
        }

        public ConsoleEditor Editor { get; }

        public void Execute(CommandArgument[] arguments, CommandOption[] options)
        {
            _action(arguments, options);
        }
    }

    public class ConsoleEditor
    {
        private readonly CommandLineApplication _app = new();

        public ConsoleEditor(string file)
        {
            Project = Project.FromFile(file, App.Current) ?? throw new Exception();
            Project.Load();

            _app.HelpOption("-?|-h|--help");

            _app.Command("save", command =>
            {
                command.Description = "プロジェクトを保存します。";

                command.HelpOption("-?|-h|--help");

                var file = command.Option("-f|--file", Resources.ProjectFile, CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    static void Check(bool result)
                    {
                        if (!result)
                        {
                            Console.WriteLine("    保存できませんでした。");
                        }
                        else
                        {
                            Console.WriteLine("    保存しました。");
                        }
                    }

                    if (file.HasValue())
                    {
                        Check(Project.Save(file.Value()));
                    }
                    else
                    {
                        Check(Project.Save());
                    }

                    return 0;
                });
            });
            _app.Command("list", command =>
            {
                command.Description = "シーンまたはクリップをリスト表示します。";

                command.HelpOption("-?|-h|--help");

                var isscene = command.Option("-s|--scene", "シーンを表示する場合 true", CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    if (isscene.HasValue())
                    {
                        foreach (var scene in Project.SceneList)
                        {
                            Console.Write("* " + scene.SceneName);

                            if (scene == Project.PreviewScene)
                            {
                                Console.Write(" <= Selected");
                            }

                            Console.Write('\n');
                        }
                    }
                    else
                    {
                        Console.WriteLine("管理名 | 名前");
                        Console.WriteLine("=============");

                        foreach (var clip in Project.PreviewScene.Datas)
                        {
                            Console.Write("* " + clip.Name + " | " + clip.LabelText);

                            if (clip == Project.PreviewScene.SelectItem)
                            {
                                Console.Write(" <= Selected");
                            }

                            Console.Write('\n');
                        }
                    }

                    return 0;
                });
            });
            _app.Command("add", command =>
            {
                command.Description = "クリップを現在のシーンに追加します。";

                command.HelpOption("-?|-h|--help");
                //Todo: 未実装 WPFプロジェクトのInRangeが必要
            });
            _app.Command("prop", command =>
            {
                command.Description = "現在のクリップの指定したエフェクトのプロパティをJson形式で表示します。";

                command.HelpOption("-?|-h|--help");
                var path = command.Argument("path", "プロパティへのパス (E: [0][5])");

                //path.Valueがキャッシュされてしまう
                command.OnExecute(() =>
                {
                    // [Effect][Property]の場合
                    var regex1 = new Regex(@"^\[([\d]+)\]\[([\d]+)\]\z");
                    // [Effect][Group][Property]の場合
                    var regex2 = new Regex(@"^\[([\d]+)\]\[([\d]+)\]\[([\d]+)\]\z");
                    PropertyElement? prop = null;

                    if (regex1.IsMatch(path.Value))
                    {
                        var match = regex1.Match(path.Value);

                        var effect = int.TryParse(match.Groups[1].Value, out var id) ? Project.PreviewScene.SelectItem?.Find(id) : null;
                        prop = int.TryParse(match.Groups[2].Value, out var id1) ? effect?.Find(id1) : null;
                    }
                    else if (regex2.IsMatch(path.Value))
                    {
                        var match = regex2.Match(path.Value);

                        var effect = int.TryParse(match.Groups[1].Value, out var id) ? Project.PreviewScene.SelectItem?.Find(id) : null;
                        var parent = int.TryParse(match.Groups[2].Value, out var id1) ? effect?.Find(id1) as IParent<PropertyElement> : null;
                        prop = int.TryParse(match.Groups[3].Value, out var id2) ? parent?.Find(id2) : null;
                    }

                    // Convert to json.
                    if (prop is not null)
                    {
                        using var memory = new MemoryStream();
                        if (!Serialize.SaveToStream(prop, memory, SerializeMode.Json))
                        {
                            Console.WriteLine("プロパティをJsonに変換できませんでした。");

                            return 1;
                        }

                        Console.WriteLine(Encoding.UTF8.GetString(memory.ToArray()));
                    }
                    else
                    {
                        Console.WriteLine("プロパティが見つかりませんでした。");

                        return 1;
                    }

                    return 0;
                });
            });
        }

        public Project Project { get; }

        public void Execute()
        {
            while (true)
            {
                var line = Console.ReadLine();

                if (line is not null)
                {
                    var strs = line.Split(' ');

                    if (strs.Length is 0 || strs[0] is "\n" || strs[0] is "")
                    {
                        continue;
                    }
                    else if (strs.FirstOrDefault() is "exit")
                    {
                        return;
                    }

                    _app.Execute(strs);

                    Console.WriteLine();
                }
            }
        }
    }
}
