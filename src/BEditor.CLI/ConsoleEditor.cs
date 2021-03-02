using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.CommandLineUtils;
using Microsoft.CodeAnalysis.CSharp.Scripting;

using BEditor.Data;
using BEditor.Properties;
using System.Text.RegularExpressions;
using System.IO;
using BEditor.Data.Property;
using Microsoft.CodeAnalysis.Scripting;

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
        private readonly ScriptContext _context;

        public ConsoleEditor(string file)
        {
            Project = Project.FromFile(file, App.Current) ?? throw new Exception();
            Project.Load();

            _context = new(Project);
        }

        public Project Project { get; }

        public async Task Execute()
        {
            while (true)
            {
                var line = Console.ReadLine();

                if (line is not null)
                {
                    if (line is "exit") return;

                    try
                    {
                        var stete = await CSharpScript.RunAsync(line, globals: _context);
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("スクリプトを実行出来ませんでした");

                        Console.ResetColor();
                    }
                }
            }
        }
    }
}
