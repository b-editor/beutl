using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels.ToolControl
{
    public class LogViewModel
    {
        public static readonly LogViewModel Current = new();

        public LogViewModel()
        {
            Remove.Subscribe(log =>
            {
                if (log is null) return;

                log.Text.Value = "";
            });
        }


        public ReactiveCommand<BEditorLogger> Remove { get; } = new();
        public ReactiveCollection<BEditorLogger> Loggers { get; } = new();
    }
}