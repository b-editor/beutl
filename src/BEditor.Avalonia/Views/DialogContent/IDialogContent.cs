using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using static BEditor.IMessage;

namespace BEditor.Views.DialogContent
{
    public interface IDialogContent : IContentControl
    {
        public ButtonType DialogResult { get; }

        public event EventHandler? ButtonClicked;
    }
}