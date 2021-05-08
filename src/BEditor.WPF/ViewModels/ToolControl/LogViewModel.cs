using System;

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