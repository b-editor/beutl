using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Extensions.ViewCommand;

using Reactive.Bindings;

namespace BEditor.ViewModels.ToolControl
{
    public class ObjectViewerViewModel
    {
        public ObjectViewerViewModel()
        {
            GetPathCommand.Subscribe(x =>
            {
                if (x is IBindable bindable)
                {
                    var path = bindable.GetString();
                    Clipboard.SetText(path);
                }
                else
                {
                    Message.Snackbar("IBindableでない");
                }
            });
        }

        public ReactiveCommand<object> GetPathCommand { get; } = new();
    }
}
