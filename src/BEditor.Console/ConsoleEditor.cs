using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Properties;

using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.CommandLineUtils;

namespace BEditor
{
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

        public void Execute()
        {
            while (true)
            {
                var line = Console.ReadLine();

                if (line is not null)
                {
                    if (line is "exit") return;

                    try
                    {
                        CSharpScript.RunAsync(line, globals: _context).Wait();
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("スクリプトを実行出来ませんでした");

                        Console.ResetColor();

                        throw;
                    }
                }
            }
        }
    }
}